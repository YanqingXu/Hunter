// 用无数据库后端验证存储线程边界、请求编码、错误透传及关闭排空。
#include "common/Types.h"
#include "../fixtures/StorageHost.h"

#include <nlohmann/json.hpp>

#include <atomic>
#include <iostream>
#include <mutex>

namespace
{
using namespace hunter::storage;
using namespace hunter::storage::test;
using Json = nlohmann::json;
constexpr u64 large_id = 9007199254740999ULL;
const Str saved_json = "\n{\"id\":9007199254740999, \"text\":\"\\u4e2d\"}\n";

struct Event
{
    Str name;
    std::thread::id thread;
};

struct Trace
{
    std::mutex mutex;
    Vec<Event> events;
    std::atomic<u32> delivered = 0;
    bool drain = false;
    bool fail_open = false;
    bool fail_close = false;

    // 将不同线程上的观察保存为有序事件，不借助数据库实现细节。
    void record(const Str& name)
    {
        std::scoped_lock lock(mutex);
        events.push_back({name, std::this_thread::get_id()});
    }

    // 返回同步快照，避免观察工作线程时产生数据竞争。
    Vec<Event> snapshot()
    {
        std::scoped_lock lock(mutex);
        return events;
    }
};

class Probe final : public Backend
{
public:
    // 记录创建线程，以检查工厂没有在逻辑线程提前创建资源。
    explicit Probe(Ptr<Trace> trace) : trace_(std::move(trace))
    {
        trace_->record("create");
    }

    // 记录销毁线程，包括打开失败和析构保底的路径。
    ~Probe() override
    {
        trace_->record("destroy");
    }

    // 接受空路径的测试后端，验证门面不施加 SQLite 路径限制。
    void open(const OpenCfg& cfg) override
    {
        trace_->record("open");
        check(cfg.backend == "probe" && cfg.path.empty(), "backend config preserved");
        if (trace_->fail_open)
        {
            throw Error{Code::Open, 701, false, "probe_open", "probe"};
        }
    }

    // 正常停止必须等到所有已受理回调返回之后才关闭后端。
    void close() override
    {
        trace_->record("close");
        if (trace_->drain)
        {
            check(trace_->delivered == 2, "close follows delivered completions");
        }
        if (trace_->fail_close)
        {
            throw Error{Code::Write, 702, false, "probe_close", "probe"};
        }
    }

    // 返回超过浮点精度的拥有型身份，验证门面不改写字段。
    PlayerSave load_player(u64 player_id, usize limit) override
    {
        trace_->record("load_player");
        check(player_id == 1 && limit == 4096, "player route and result budget");
        return {1, large_id, large_id, {{large_id + 1, 7, 2, large_id}}};
    }

    // 为所有分配路由返回一个精确的大整数。
    MatchId alloc_match() override
    {
        trace_->record("alloc_match");
        return {large_id};
    }

    // 校验门面提交完整整数编码，并返回带空白和转义的原始结果。
    MatchResult commit_match(const CommitMatch& req, const Str& json, usize limit) override
    {
        trace_->record("commit_match");
        const auto value = Json::parse(json);
        check(req.match_id == large_id && req.expected_revision == large_id && limit == 4096,
            "typed match request preserved");
        check(value["match_id"].get<u64>() == large_id
            && value["expected_revision"].get<u64>() == large_id,
            "match request integer encoding");
        return {large_id, large_id + 1, false, saved_json};
    }

    // 分别覆盖原文成功、驱动错误和未知异常映射。
    MatchResult find_match(u64 match_id, usize limit) override
    {
        trace_->record("find_match");
        check(limit == 4096, "match query result budget");
        if (match_id == 2)
        {
            throw Error{Code::Write, 703, true, "probe_commit_unknown", "probe"};
        }
        if (match_id == 3)
        {
            throw std::runtime_error("probe_unexpected");
        }
        check(match_id == large_id, "match query identity");
        return {large_id, large_id + 1, true, saved_json};
    }

    // 验证猎人采用独立的小预算，保留后端结果字节。
    HunterResult load_hunters(u64 player_id, usize limit) override
    {
        trace_->record("load_hunters");
        check(player_id == 1 && limit == 2048, "hunter route and result budget");
        return {large_id, large_id, 0, false, saved_json};
    }

    // 验证确定性猎人请求与原始意图都跨线程保留。
    HunterResult apply_hunter(const HunterReq& req, const Str& json, usize limit) override
    {
        trace_->record("apply_hunter");
        const auto value = Json::parse(json);
        check(req.hunter_id == large_id && req.expected_revision == large_id && limit == 2048,
            "typed hunter request preserved");
        check(value["hunter_id"].get<u64>() == large_id
            && value["intent"].get<Str>() == req.intent_json
            && value["payload"]["cost"].get<u64>() == large_id,
            "hunter request integer and intent encoding");
        return {large_id + 1, large_id, 0, false, saved_json};
    }

    // 校验幂等查询各字段独立传入，不进行 JSON 归一化。
    HunterResult find_hunter_op(u64 player_id, const Str& op_id,
        const Str& intent, usize limit) override
    {
        trace_->record("find_hunter_op");
        check(player_id == 1 && op_id == "op-1" && intent == " {\"cfg_id\":7} "
            && limit == 2048, "hunter query preserves identity and intent");
        return {large_id + 1, large_id, 0, true, saved_json};
    }

private:
    Ptr<Trace> trace_;
};

// 让工厂和实例共同记入同一观察对象，检查工厂的实际执行线程。
BackendFactory factory(const Ptr<Trace>& trace)
{
    return [trace](const OpenCfg& cfg) -> UPtr<Backend>
    {
        trace->record("factory");
        check(cfg.backend == "probe", "factory receives selected backend");
        return std::make_unique<Probe>(trace);
    };
}

// 使用不同业务预算，检查门面将正确预算传给每个后端入口。
StorageCfg limits()
{
    StorageCfg cfg;
    cfg.max_result_bytes = 4096;
    cfg.max_hunter_bytes = 2048;
    return cfg;
}

// 要求每个生命周期和业务事件都发生在同一非逻辑线程。
void worker_events(const Ptr<Trace>& trace, const Vec<Str>& expected)
{
    const auto events = trace->snapshot();
    check(events.size() == expected.size(), "backend event count");
    check(!events.empty() && events.front().thread != std::this_thread::get_id(),
        "factory runs on worker");
    for (usize index = 0; index < events.size(); ++index)
    {
        check(events[index].name == expected[index], "backend event order");
        check(events[index].thread == events.front().thread, "single backend worker thread");
    }
}

// 所有业务命令只依赖注入接口，拥有型返回值与后端诊断原样送回逻辑线程。
void routing()
{
    auto trace = std::make_shared<Trace>();
    Host host(limits(), 501, factory(trace));
    check(trace->snapshot().empty(), "backend creation waits for open");
    host.open(OpenCfg{"probe", ""});
    const auto player = host.load();
    check(player.revision == large_id && player.items[0].item_uid == large_id + 1,
        "typed response integer precision");
    check(host.alloc() == large_id, "allocated integer precision");
    const CommitMatch req{large_id, 1, large_id, "Extracted", "content", {{7, 2}}};
    const auto committed = host.commit(req);
    check(committed && std::get<MatchResult>(*committed).result_json == saved_json,
        "match result preserves exact bytes");
    const auto found = host.find(large_id);
    check(found && std::get<MatchResult>(*found).replayed
        && std::get<MatchResult>(*found).result_json == saved_json, "match query exact bytes");
    check(host.hunters().result_json == saved_json, "hunter profile exact bytes");
    const HunterReq command{1, large_id, large_id, "op-1", "buy_skill",
        "{\"cfg_id\":7,\"cost\":9007199254740999}", " {\"cfg_id\":7} "};
    const auto applied = host.apply(command);
    check(applied && std::get<HunterResult>(*applied).result_json == saved_json,
        "hunter operation exact bytes");
    const auto replayed = host.find_op(command);
    check(replayed && std::get<HunterResult>(*replayed).replayed
        && std::get<HunterResult>(*replayed).result_json == saved_json, "hunter query exact bytes");
    const auto failed = host.find(2);
    error_is(failed, Code::Write);
    check(failed.error().native_code == 703 && failed.error().backend == "probe"
        && failed.error().commit_unknown && failed.error().message == "probe_commit_unknown",
        "backend diagnostic fields preserved");
    error_is(host.find(3), Code::Internal);
    host.stop();
    worker_events(trace, {"factory", "create", "open", "load_player", "alloc_match",
        "commit_match", "find_match", "load_hunters", "apply_hunter", "find_hunter_op",
        "find_match", "find_match", "close", "destroy"});
}

// 饱和队列仍接受关闭，后端资源只能在两个完成回调返回之后释放。
void drain()
{
    auto trace = std::make_shared<Trace>();
    trace->drain = true;
    auto cfg = limits();
    cfg.max_ops = 2;
    Vec<u64> delivered;
    const auto owner = std::this_thread::get_id();
    bool closed = false;
    Host host(cfg, 502, factory(trace));
    host.open(OpenCfg{"probe", ""});
    const auto done = [&](Rsp rsp)
    {
        check(owner == std::this_thread::get_id() && rsp.result.has_value(),
            "drained completion stays on logic thread");
        delivered.push_back(rsp.key.op);
        ++trace->delivered;
    };
    const auto first = host.store->load_player(1, done);
    const auto second = host.store->alloc_match(done);
    const auto full = host.store->alloc_match(done);
    check(first && second && !full && full.error().code == Code::Capacity,
        "ordinary queue saturated");
    const auto stop = host.store->stop([&](Rsp rsp)
    {
        check(owner == std::this_thread::get_id() && rsp.result
            && std::holds_alternative<Closed>(*rsp.result), "closed completion on logic thread");
        closed = true;
    });
    check(stop.has_value() && delivered.empty() && !closed, "stop has reserved deferred slot");
    host.until([&] { return closed; });
    check(delivered == Vec<u64>{first->key.op, second->key.op}, "accepted work completes in order");
    worker_events(trace, {"factory", "create", "open", "load_player", "alloc_match",
        "close", "destroy"});
}

// 析构保底等待受理任务并在工作线程回收后端，不内联触发旧回调。
void destruction()
{
    auto trace = std::make_shared<Trace>();
    bool delivered = false;
    Host host(limits(), 503, factory(trace));
    host.open(OpenCfg{"probe", ""});
    check(host.store->alloc_match([&](Rsp rsp)
    {
        check(rsp.result.has_value(), "abandoned accepted work commits");
        delivered = true;
    }).has_value(), "accept work before destructor");
    host.store.reset();
    check(!delivered, "destructor never completes inline");
    worker_events(trace, {"factory", "create", "open", "alloc_match", "destroy"});
    host.until([&] { return delivered; });
}

// 打开失败必须保留诊断、拒绝业务调用并在原工作线程销毁候选后端。
void open_failure()
{
    auto trace = std::make_shared<Trace>();
    trace->fail_open = true;
    Host host(limits(), 504, factory(trace));
    const auto result = host.call([&](auto done)
    {
        return host.store->open(OpenCfg{"probe", ""}, std::move(done));
    });
    error_is(result, Code::Open);
    check(result.error().backend == "probe" && result.error().native_code == 701,
        "open failure diagnostic source");
    const auto blocked = host.store->alloc_match([](Rsp) {});
    check(!blocked && blocked.error().code == Code::NotReady, "open failure blocks work");
    host.stop();
    worker_events(trace, {"factory", "create", "open", "destroy"});
}

// 工厂抛错或返回空实例都必须异步失败，且禁止继续执行业务命令。
void factory_failure()
{
    for (const bool empty : {false, true})
    {
        auto trace = std::make_shared<Trace>();
        BackendFactory fail = [trace, empty](const OpenCfg&) -> UPtr<Backend>
        {
            trace->record("factory");
            if (empty)
            {
                return {};
            }
            throw Error{Code::Open, 704, false, "probe_factory"};
        };
        Host host(limits(), 505, std::move(fail));
        const auto result = host.call([&](auto done)
        {
            return host.store->open(OpenCfg{"probe", ""}, std::move(done));
        });
        error_is(result, empty ? Code::Internal : Code::Open);
        check(result.error().backend == "probe", "factory failure receives backend source");
        const auto blocked = host.store->alloc_match([](Rsp) {});
        check(!blocked && blocked.error().code == Code::NotReady, "factory failure blocks work");
        host.stop();
        worker_events(trace, {"factory"});
    }
}

// 关闭抛错仍必须先释放工作线程资源，再向逻辑线程交付结构化错误。
void close_failure()
{
    auto trace = std::make_shared<Trace>();
    trace->fail_close = true;
    Host host(limits(), 506, factory(trace));
    host.open(OpenCfg{"probe", ""});
    const auto result = host.call([&](auto done)
    {
        return host.store->stop(std::move(done));
    });
    error_is(result, Code::Write);
    check(result.error().native_code == 702 && result.error().backend == "probe",
        "close error diagnostic fields preserved");
    worker_events(trace, {"factory", "create", "open", "close", "destroy"});
}

// 不支持的名称直接报告 Unsupported，不创建或回退到 SQLite 文件。
void unsupported(const Temp& temp)
{
    for (const auto name : {"mysql", "unknown"})
    {
        Host host;
        const auto path = temp.file(Str(name) + ".db");
        const auto result = host.call([&](auto done)
        {
            return host.store->open(OpenCfg{name, path}, std::move(done));
        });
        error_is(result, Code::Unsupported);
        check(result.error().backend == name, "unsupported backend diagnostic source");
        host.stop();
        const auto file = std::filesystem::path(std::u8string(path.begin(), path.end()));
        check(!std::filesystem::exists(file), "no fallback database");
    }
}
}

// 运行不依赖数据库实现细节的后端边界契约。
i32 main()
{
    try
    {
        const Temp temp;
        routing();
        drain();
        destruction();
        open_failure();
        factory_failure();
        close_failure();
        unsupported(temp);
        std::cout << "storage backend contract passed\n";
        return 0;
    }
    catch (const Error& error)
    {
        std::cerr << error.message << '\n';
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
    }
    return 1;
}

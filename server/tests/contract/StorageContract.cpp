// 用真实文件、SQLite 和 Asio 验证持久事务、容量、线程与关闭契约。
#include "common/Types.h"
#include "storage/Storage.h"
#include "storage/Db.h"
#include "storage/Schema.h"

#include <nlohmann/json.hpp>

#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <optional>
#include <stdexcept>
#include <thread>

namespace
{

using namespace hunter::storage;
using Json = nlohmann::json;

// 检查公开行为，保留可定位的失败原因。
void check(bool ok, const Str& msg)
{
    if (!ok)
    {
        throw std::runtime_error(msg);
    }
}

// 将临时目录转换为 SQLite 接受的 UTF-8 文件路径。
Str utf8(const std::filesystem::path& path)
{
    const auto value = path.u8string();
    return Str(value.begin(), value.end());
}

struct Temp
{
    std::filesystem::path root;

    // 建立本次测试独占的临时目录，不使用用户存档。
    Temp()
    {
        const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
        root = std::filesystem::temp_directory_path() /
            ("hunter-storage-" + std::to_string(stamp));
        check(std::filesystem::create_directory(root), "unique temporary directory");
    }

    // 只移除由本对象创建的目录，失败时不覆盖原测试异常。
    ~Temp()
    {
        std::error_code error;
        std::filesystem::remove_all(root, error);
    }

    // 为独立用例返回同一测试目录内的数据库路径。
    Str file(const Str& name) const
    {
        return utf8(root / name);
    }
};

struct Host
{
    asio::io_context io;
    UPtr<Storage> store;

    // 在当前线程创建存档门面及独立关联实例。
    explicit Host(StorageCfg cfg = {}, u64 instance = 17)
        : store(std::make_unique<Storage>(io, cfg, instance))
    {
    }

    // 释放工作线程后交付剩余拥有型完成包。
    ~Host()
    {
        store.reset();
        io.restart();
        io.poll();
    }

    // 有界驱动真实事件循环，避免测试失败时无限等待。
    void until(const Func<bool()>& done)
    {
        const auto end = std::chrono::steady_clock::now() + std::chrono::seconds(10);

        while (!done() && std::chrono::steady_clock::now() < end)
        {
            io.restart();
            io.run_for(std::chrono::milliseconds(5));
        }

        check(done(), "completion deadline");
    }

    // 同步等待测试调用，生产接口仍然异步并验证操作关联。
    template<typename F>
    std::expected<Value, Error> call(F submit)
    {
        auto result = std::make_shared<std::optional<Rsp>>();
        const auto owner = std::this_thread::get_id();
        auto ticket = submit([result, owner](Rsp rsp)
        {
            check(owner == std::this_thread::get_id(), "callback owning thread");
            check(!result->has_value(), "one terminal completion");
            *result = std::move(rsp);
        });
        check(ticket.has_value(), ticket ? "" : ticket.error().message);
        check(!result->has_value(), "never complete inline");
        until([&] { return result->has_value(); });
        check((*result)->key.instance == ticket->key.instance &&
            (*result)->key.op == ticket->key.op, "correlated operation");
        return std::move((*result)->result);
    }

    // 打开测试文件并要求数据库已完成初始化。
    void open(const Str& path)
    {
        auto result = call([&](auto done) { return store->open(path, std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
        check(std::holds_alternative<Opened>(*result), "opened result");
    }

    // 分配已提交的稳定对局 ID。
    u64 alloc()
    {
        auto result = call([&](auto done) { return store->alloc_match(std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
        return std::get<MatchId>(*result).value;
    }

    // 读取单机永久玩家的完整存档。
    PlayerSave load()
    {
        auto result = call([&](auto done) { return store->load_player(1, std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
        return std::get<PlayerSave>(std::move(*result));
    }

    // 提交给定测试请求，错误也保留为可断言结果。
    std::expected<Value, Error> commit(const CommitMatch& req)
    {
        return call([&](auto done) { return store->commit_match(req, std::move(done)); });
    }

    // 查询已保存结果，不从当前内存推断结算。
    std::expected<Value, Error> find(u64 id)
    {
        return call([&](auto done) { return store->find_match(id, std::move(done)); });
    }

    // 驱动关闭直到数据库资源已释放。
    void stop()
    {
        auto result = call([&](auto done) { return store->stop(std::move(done)); });
        check(result && std::holds_alternative<Closed>(*result), "closed completion");
    }
};

// 构造确定的奖励输入；同配置的两行仍为两个独立永久物品。
CommitMatch req(u64 id, u64 revision = 1)
{
    return {id, 1, revision, "extracted", "content-v1", {{10003, 2}, {10003, 1}}};
}

// 验证错误分类，避免把任何失败都误认为预期行为。
void error_is(const std::expected<Value, Error>& result, Code code)
{
    check(!result && result.error().code == code, "expected error category");
}

// 验证永久物品、去重、冲突以及重启后的序列与结果精度。
void transaction_contract(const Temp& tmp)
{
    const Str path = tmp.file("事务.db");
    Str saved;
    u64 first_uid = 0;
    {
        Host host;
        host.open(path);
        auto player = host.load();
        check(player.player_id == 1 && player.revision == 1 && player.items.empty() &&
            player.last_match_id == 0, "initial player");
        check(host.alloc() == 1, "first allocated id");
        auto result = host.commit(req(1));
        check(result.has_value(), "first commit");
        const auto& match = std::get<MatchResult>(*result);
        check(!match.replayed && match.revision == 2, "committed revision");
        saved = match.result_json;
        auto json = Json::parse(saved);
        check(json["items"].size() == 2 && json["revision_after"] == 2, "stored result");
        first_uid = json["items"][0]["item_uid"].get<u64>();
        check(first_uid > 0, "permanent uid");
        result = host.commit(req(1));
        check(result && std::get<MatchResult>(*result).replayed &&
            std::get<MatchResult>(*result).result_json == saved, "exact replay");
        auto changed = req(1);
        changed.items[0].count++;
        error_is(host.commit(changed), Code::Conflict);
        changed = req(1);
        changed.expected_revision = 2;
        error_is(host.commit(changed), Code::Conflict);
        changed = req(1);
        std::swap(changed.items[0], changed.items[1]);
        error_is(host.commit(changed), Code::Conflict);
        check(host.alloc() == 2, "second id");
        error_is(host.commit(req(2)), Code::Revision);
        error_is(host.find(2), Code::NotFound);
        error_is(host.commit(req(500, 2)), Code::Invalid);
        player = host.load();
        check(player.revision == 2 && player.items.size() == 2 &&
            player.items[0].count == 2 && player.last_match_id == 1, "no duplicate award");
        host.stop();
    }

    {
        Host host({}, 18);
        host.open(path);
        check(host.alloc() == 3, "allocated gap survives restart");
        auto result = host.commit(req(1));
        check(result && std::get<MatchResult>(*result).result_json == saved, "restart replay");
        auto empty = req(3, 2);
        empty.items.clear();
        result = host.commit(empty);
        check(result && std::get<MatchResult>(*result).revision == 3, "empty reward commits");
        check(host.load().items[0].item_uid == first_uid, "uid survives restart");
        host.stop();
    }

    for (u64 i = 0; i < 10; ++i)
    {
        Host host;
        host.open(path);
        const auto player = host.load();
        const auto id = host.alloc();
        auto result = host.commit(req(id, player.revision));
        check(result.has_value(), "ten restart commits");
        check(host.load().items.size() == 2 + 2 * (i + 1), "cumulative items");
        host.stop();
    }
}

// 用直接连接建立异常文件，确保正式接口保留文件并拒绝错误版本和结构。
void schema_contract(const Temp& tmp)
{
    const Vec<std::pair<Str, Str>> cases = {
        {"future.db", "PRAGMA user_version=2"},
        {"foreign.db", "CREATE TABLE unrelated(x INTEGER)"},
        {"wrong.db", "PRAGMA user_version=1; CREATE TABLE player_save(x TEXT)"}
    };

    for (const auto& [name, sql] : cases)
    {
        const Str path = tmp.file(name);
        {
            Db db(path);
            db.exec(sql.c_str());
        }

        const auto size = std::filesystem::file_size(tmp.root / name);
        Host host;
        auto result = host.call([&](auto done)
        {
            return host.store->open(path, std::move(done));
        });
        check(!result && (result.error().code == Code::Version ||
            result.error().code == Code::Corrupt), "reject foreign schema");
        host.stop();
        check(std::filesystem::file_size(tmp.root / name) == size,
            "rejected database preserved");
    }

    const auto broken = tmp.root / "broken.db";
    const Str bytes(8192, 'X');
    {
        std::ofstream out(broken, std::ios::binary);
        out << bytes;
    }

    Host host;
    auto result = host.call([&](auto done)
    {
        return host.store->open(utf8(broken), std::move(done));
    });
    error_is(result, Code::Corrupt);
    host.stop();
    std::ifstream in(broken, std::ios::binary);
    const Str actual((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    check(actual == bytes, "corrupt bytes preserved");

    Db db(tmp.file("owner.db"));
    bool wrong_thread = false;
    std::jthread other([&]
    {
        try
        {
            db.exec("CREATE TABLE invalid(x INTEGER)");
        }
        catch (const Error& error)
        {
            wrong_thread = error.code == Code::WrongThread;
        }
    });
    other.join();
    check(wrong_thread, "connection thread ownership");

    const auto foreign_path = tmp.file("foreign-key.db");
    {
        Db valid(foreign_path);
        open_schema(valid);
        check(valid.scalar("PRAGMA foreign_keys") == 1 &&
            valid.scalar("PRAGMA synchronous") == 2 &&
            valid.scalar("PRAGMA busy_timeout") == 2000 &&
            valid.scalar("PRAGMA user_version") == 1, "required sqlite settings");
        Stmt mode(valid, "PRAGMA journal_mode");
        check(mode.step() && mode.text(0, 32) == "wal", "wal enabled");
        check(!mode.step(), "journal mode consumed");
        bool constraint = false;

        try
        {
            valid.exec("INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) "
                "VALUES(1,10003,0,1)");
        }
        catch (const Error& error)
        {
            constraint = error.code == Code::Invalid;
        }

        check(constraint, "item count database constraint");
        valid.exec("PRAGMA foreign_keys=OFF");
        valid.exec("INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) "
            "VALUES(1,10003,1,123)");
    }

    Host foreign;
    error_is(foreign.call([&](auto done)
    {
        return foreign.store->open(foreign_path, std::move(done));
    }), Code::Corrupt);
    foreign.stop();
    Host missing;
    error_is(missing.call([&](auto done)
    {
        return missing.store->open(tmp.file("absent/child.db"), std::move(done));
    }), Code::Open);
    missing.stop();

    const auto extra_path = tmp.file("extra-schema.db");
    {
        Db extra(extra_path);
        open_schema(extra);
        extra.exec("CREATE TABLE sqlitex_probe(value INTEGER)");
    }

    Host extra;
    error_is(extra.call([&](auto done)
    {
        return extra.store->open(extra_path, std::move(done));
    }), Code::Corrupt);
    extra.stop();
}

// 验证异步容量包括未交付的完成，关闭不会被饱和普通队列阻断。
void async_contract(const Temp& tmp)
{
    StorageCfg cfg;
    cfg.max_ops = 1;
    Host host(cfg);
    auto early = host.store->alloc_match([](Rsp) {});
    check(!early && early.error().code == Code::NotReady, "reject before opened");
    host.open(tmp.file("async.db"));
    bool wrong_thread = false;
    std::jthread other([&]
    {
        auto result = host.store->alloc_match([](Rsp) {});
        wrong_thread = !result && result.error().code == Code::WrongThread;
    });
    other.join();
    check(wrong_thread, "facade thread ownership");
    Vec<u64> order;
    auto first = host.store->alloc_match([&](Rsp rsp)
    {
        check(rsp.result.has_value(), "accepted transaction completes");
        order.push_back(rsp.key.op);
    });
    auto full = host.store->alloc_match([](Rsp) {});
    check(first && !full && full.error().code == Code::Capacity, "count capacity");
    auto stop = host.store->stop([&](Rsp rsp)
    {
        check(rsp.result && std::holds_alternative<Closed>(*rsp.result), "stop result");
        order.push_back(rsp.key.op);
    });
    check(stop.has_value(), "reserved stop slot");
    auto closed = host.store->alloc_match([](Rsp) {});
    check(!closed && closed.error().code == Code::Closed, "reject after stopping");
    host.until([&] { return order.size() == 2; });
    check(order[0] == first->key.op && order[1] == stop->key.op, "drain before closed");
    check(!host.store->stop([](Rsp) {}), "duplicate stop rejected");

    cfg = {};
    cfg.max_result_bytes = 1024;
    cfg.max_done_bytes = 1024;
    Host limited(cfg);
    limited.open(tmp.file("bytes.db"));
    bool done = false;
    check(limited.store->load_player(1, [&](Rsp) { done = true; }).has_value(),
        "reserve full read result");
    auto capacity = limited.store->alloc_match([](Rsp) {});
    check(!capacity && capacity.error().code == Code::Capacity, "completion byte capacity");
    limited.until([&] { return done; });
    auto large = req(limited.alloc());
    large.content_key = Str(2048, 'c');
    auto result = limited.commit(large);
    error_is(result, Code::TooLarge);
    check(limited.load().revision == 1, "oversized result rolls back");
    limited.stop();
}

// 验证真实锁超时、写满回滚及整数边界，不把错误转为成功。
void failure_contract(const Temp& tmp)
{
    const Str path = tmp.file("failure.db");
    Host host;
    host.open(path);
    check(host.alloc() == 1, "failure test id");
    {
        Db lock(path);
        Txn txn(lock);
        auto result = host.commit(req(1));
        error_is(result, Code::Busy);
    }

    check(host.load().revision == 1, "busy left unchanged");
    auto invalid = req(1);
    invalid.items[0].count = -1;
    auto reject = host.store->commit_match(invalid, [](Rsp) {});
    check(!reject && reject.error().code == Code::Invalid, "negative award rejected");
    invalid = req(1);
    invalid.match_id = std::numeric_limits<u64>::max();
    reject = host.store->commit_match(invalid, [](Rsp) {});
    check(!reject && reject.error().code == Code::Invalid, "uint64 rejected before cast");
    host.stop();
    {
        Db db(path);
        db.exec("UPDATE save_meta SET int_value=9223372036854775807 "
            "WHERE key='next_match_id'");
        db.exec("UPDATE player_save SET revision=9223372036854775807");
    }

    Host exhausted;
    exhausted.open(path);
    error_is(exhausted.call([&](auto done)
    {
        return exhausted.store->alloc_match(std::move(done));
    }), Code::Overflow);
    error_is(exhausted.commit(req(1, 9223372036854775807ULL)), Code::Overflow);
    exhausted.stop();
}

// 验证任务字节上限、完整存档超限及原结果重放超限都不会截断或改写存档。
void limits_contract(const Temp& tmp)
{
    StorageCfg cfg;
    cfg.max_req_bytes = 1024;
    cfg.max_result_bytes = 1024;
    cfg.max_done_bytes = 1024 * 1024;
    Host host(cfg);
    host.open(tmp.file("request-limit.db"));
    auto large = req(1);
    large.content_key = Str(2048, 'x');
    auto rejected = host.store->commit_match(large, [](Rsp) {});
    check(!rejected && rejected.error().code == Code::TooLarge, "request byte limit");
    usize accepted = 0;
    usize completed = 0;

    for (usize i = 0; i < cfg.max_ops; ++i)
    {
        auto ticket = host.store->alloc_match([&](Rsp rsp)
        {
            check(rsp.result.has_value(), "byte capacity accepted result");
            ++completed;
        });

        if (!ticket)
        {
            check(ticket.error().code == Code::Capacity, "aggregate request capacity");
            break;
        }

        ++accepted;
    }

    check(accepted > 0 && accepted < cfg.max_ops, "request bytes constrain before count");
    host.until([&] { return completed == accepted; });
    host.stop();

    const auto path = tmp.file("large-save.db");
    auto many = req(1);
    many.items.assign(100, {10003, 1});
    {
        Host big;
        big.open(path);
        check(big.alloc() == 1, "large save id");
        check(big.commit(many).has_value(), "large valid award");
        big.stop();
    }

    cfg = {};
    cfg.max_result_bytes = 1024;
    Host small(cfg);
    small.open(path);
    error_is(small.call([&](auto done)
    {
        return small.store->load_player(1, std::move(done));
    }), Code::TooLarge);
    error_is(small.find(1), Code::TooLarge);
    error_is(small.commit(many), Code::TooLarge);
    small.stop();
    Host reopened;
    reopened.open(path);
    check(reopened.load().items.size() == 100 && reopened.load().revision == 2,
        "read limits do not truncate persistent data");
    reopened.stop();
}

// 验证超过浮点精确整数范围的 ID 和 UID 始终按整数保存和返回。
void precision_contract(const Temp& tmp)
{
    const auto path = tmp.file("precision.db");
    {
        Host host;
        host.open(path);
        host.stop();
    }

    constexpr u64 id = 9007199254740999ULL;
    {
        Db db(path);
        db.exec("UPDATE save_meta SET int_value=9007199254740999 WHERE key='next_match_id'");
        db.exec("INSERT INTO sqlite_sequence(name,seq) VALUES('player_item',9007199254740999)");
    }

    Host host;
    host.open(path);
    check(host.alloc() == id, "large match id exact");
    auto result = host.commit(req(id));
    check(result.has_value(), "large id commit");
    const auto json = Json::parse(std::get<MatchResult>(*result).result_json);
    check(json["match_id"].get<u64>() == id &&
        json["items"][0]["item_uid"].get<u64>() == id + 1, "json keeps integer precision");
    check(host.load().items[0].item_uid == id + 1, "large uid read");
    auto invalid = req(id + 1, 2);
    invalid.outcome = Str(1, static_cast<char>(0xff));
    auto ticket = host.store->commit_match(invalid, [](Rsp) {});
    check(!ticket && ticket.error().code == Code::Invalid, "invalid utf8 rejected");
    auto missing = req(host.alloc(), 2);
    missing.player_id = 2;
    error_is(host.commit(missing), Code::NotFound);
    host.stop();
}

// 在旧对象已销毁时交付其完成，再创建新对象，确保回调与状态不串实例。
void lifetime_contract(const Temp& tmp)
{
    Host host({}, 101);
    host.open(tmp.file("old-instance.db"));
    u32 old_done = 0;
    auto old = host.store->alloc_match([&](Rsp rsp)
    {
        check(rsp.key.instance == 101 && rsp.result.has_value(), "old detached completion");
        ++old_done;
    });
    check(old.has_value(), "old accepted work");
    host.store.reset();
    check(old_done == 0, "destructor never calls completion inline");
    host.store = std::make_unique<Storage>(host.io, StorageCfg{}, 102);
    host.open(tmp.file("new-instance.db"));
    check(old_done == 1 && host.alloc() == 1, "new independent instance");
    host.stop();
    Host old_file;
    old_file.open(tmp.file("old-instance.db"));
    check(old_file.alloc() == 2, "destruction drained accepted transaction");
    old_file.stop();
}

}

// 运行独立存档契约，失败返回非零退出码。
int main()
{
    try
    {
        Temp tmp;
        transaction_contract(tmp);
        schema_contract(tmp);
        async_contract(tmp);
        failure_contract(tmp);
        limits_contract(tmp);
        precision_contract(tmp);
        lifetime_contract(tmp);
        std::cout << "storage contract passed\n";
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

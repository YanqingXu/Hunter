// 验证真实脚本桥接、输出事务和线程边界。
#include "common/Types.h"
#include "core/Cfg.h"
#include "script/Script.h"
#include "ContentSpec.h"

#include <filesystem>
#include <chrono>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <thread>

#include <nlohmann/json.hpp>

namespace
{

// 把失败条件转为能定位契约的异常。
void check(bool ok, const Str& msg)
{
    if (!ok)
    {
        throw std::runtime_error(msg);
    }
}

// 将预期成功的返回值提取，同时保留具体错误上下文。
template <typename T>
T take(std::expected<T, Str> result)
{
    if (!result)
    {
        throw std::runtime_error(result.error());
    }

    return std::move(*result);
}

#if !HUNTER_PRODUCTION
// 创建隔离测试源码，每个调用保留七个入口以验证实际执行边界。
Str fixture(const Str& init, const Str& event, const Str& shutdown = "return true")
{
    static const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
    const auto path = std::filesystem::temp_directory_path() /
        ("hunter-script-contract-" + std::to_string(stamp) + ".lua");
    std::ofstream out(path, std::ios::binary);
    out << "-- 提供桥接失败回归需要的七个受控脚本入口。\n"
        << "-- 执行用例指定的初始化逻辑。\n"
        << "function init(ctx)\n    " << init << "\nend\n\n"
        << "-- 执行用例指定的输入逻辑。\n"
        << "function on_event(id, payload)\n    " << event << "\nend\n\n"
        << "-- 保留可检测会话失效的 Tick 入口。\n"
        << "function tick(id, dt)\n    return true\nend\n\n"
        << "-- 返回无玩法状态的对象快照。\n"
        << "function export_state()\n    return '{}'\nend\n\n"
        << "-- 接受测试不使用的导入入口。\n"
        << "function import_state(value)\n    return true\nend\n\n"
        << "-- 保留同步状态检查入口。\n"
        << "function validate_state()\n    return true\nend\n\n"
        << "-- 执行用例指定的退出与诊断逻辑。\n"
        << "function shutdown(reason)\n    " << shutdown << "\nend\n\nreturn true\n";
    out.close();
    return path.string();
}

// 对脚本失败路径断言整批输出不提交且会话不可继续。
void failures(hunter::Cfg cfg)
{
    cfg.source_path = fixture("return true", "net.emit('ack', "
        "'{\"v\":3,\"seq\":\"1\",\"match_id\":\"1\",\"applied_tick\":\"1\"}'); error('failed')");
    hunter::Script txn;
    take(txn.open(cfg, "{\"v\":1}"));
    check(!txn.event(1, "{}"), "failed call must discard earlier net.emit");
    check(!txn.tick(1, 0.01), "failed session must stop");
    check(txn.shutdown("test").has_value(), "failed entry still permits native cleanup");

    cfg.source_path = fixture("net.emit = 1; return true", "return true");
    hunter::Script readonly;
    check(!readonly.open(cfg, "{}"), "host namespaces must be readonly");

    cfg.source_path = fixture("return true", "while true do end");
    cfg.instruction_budget = 1000;
    hunter::Script budget;
    take(budget.open(cfg, "{}"));
    check(!budget.event(1, "{}"), "unbounded loop must exhaust execution budget");

    cfg.source_path = fixture("return true", "net.emit('ack', {}); return true");
    hunter::Script table;
    take(table.open(cfg, "{}"));
    check(!table.event(1, "{}"), "table must not cross Host boundary");

    cfg.source_path = fixture("return true", "net.emit('ack', "
        "'{\"v\":3,\"seq\":\"1\",\"match_id\":\"1\",\"applied_tick\":\"1\"}'); return true");
    cfg.max_outputs = 0;
    hunter::Script full;
    take(full.open(cfg, "{}"));
    check(!full.event(1, "{}"), "output reservation must reject saturated call buffer");

    cfg.source_path = fixture("return true", "local ok = pcall(function() "
        "net.emit('ack', '{\"v\":3,\"seq\":\"1\",\"match_id\":\"1\","
        "\"applied_tick\":\"1\"}') end); return true");
    hunter::Script caught;
    take(caught.open(cfg, "{}"));
    check(!caught.event(1, "{}"), "caught Host rejection still aborts output transaction");

    cfg.max_outputs = 256;
    cfg.source_path = fixture("assert(cfg.get() == ctx); return true", "return true");
    hunter::Script ctx_probe;
    take(ctx_probe.open(cfg, "{\"v\":1}"));
    check(!ctx_probe.event(1, Str(cfg.max_json_bytes + 1, 'x')), "JSON boundary size limit");

    cfg.source_path = fixture("return true", "net.emit('ack', "
        "'{\"v\":3,\"seq\":\"1\",\"match_id\":\"1\",\"applied_tick\":\"1\"}'); return true");
    cfg.native_work_budget = 10;
    hunter::Script native;
    take(native.open(cfg, "{}"));
    check(!native.event(1, "{}"), "Host payload copy consumes native work budget");

    cfg.script_memory_bytes = 128;
    hunter::Script memory;
    check(!memory.open(cfg, "{}"), "VM allocations honor configured memory budget");

    std::filesystem::remove(cfg.source_path);
}

// 校验真实 Host 输出事务拒绝正式 schema 的边界违规，并接受极值。
void output_schema(hunter::Cfg cfg)
{
    const Vec<hunter::ScriptOut> bad{
        {"ack", R"({"v":3,"seq":"0","match_id":"1","applied_tick":"1"})"},
        {"ack", R"({"v":3,"seq":"01","match_id":"1","applied_tick":"1"})"},
        {"ack", R"({"v":3,"seq":1,"match_id":"1","applied_tick":"1"})"},
        {"ack", R"({"v":3,"seq":"1","match_id":"1","applied_tick":"9223372036854775808"})"},
        {"ack", R"({"v":3,"seq":"1","match_id":"1","applied_tick":"1","extra":0})"},
        {"ack", R"({"v":3.0,"seq":"1","match_id":"1","applied_tick":"1"})"},
        {"snapshot", R"({"v":3,"seq":"0","tick_id":"0","match_id":"1","phase":"Playing",)"
            R"("entities":[{}]})"},
        {"error", R"({"v":3,"code":"rejected","detail":"","req_id":"","seq":"0"})"}};

    for (const auto& item : bad)
    {
        cfg.source_path = fixture("return true",
            "net.emit('ack', '{\"v\":3,\"seq\":\"1\",\"match_id\":\"1\","
            "\"applied_tick\":\"1\"}'); net.emit('" + item.kind + "', payload); return true");
        hunter::Script script;
        take(script.open(cfg, "{}"));
        check(!script.event(1, item.payload), "invalid output aborts the whole script batch");
        check(!script.tick(1, 0.01), "invalid output terminates the script session");
    }

    const Vec<hunter::ScriptOut> good{
        {"ack", R"({"v":3,"seq":"18446744073709551615","match_id":"1",)"
            R"("applied_tick":"9223372036854775807"})"},
        {"snapshot", R"({"v":3,"seq":"0","tick_id":"0","match_id":"0","phase":"Lobby",)"
            R"("entities":[]})"},
        {"error", R"({"v":3,"code":"paused","detail":"","req_id":"","seq":"0","match_id":"0"})"}};

    for (const auto& item : good)
    {
        cfg.source_path = fixture("return true", "net.emit('" + item.kind +
            "', payload); return true");
        hunter::Script script;
        take(script.open(cfg, "{}"));
        const auto out = take(script.event(1, item.payload));
        check(out.size() == 1 && out[0].payload == item.payload, "valid schema bounds preserved");
    }

    std::filesystem::remove(cfg.source_path);
}

// 退出失败必须保留错误和日志，重复关闭不得重新执行脚本或阻止资源释放。
void shutdown_contract(hunter::Cfg cfg)
{
    for (const Str body : {"diagnostics.log('closing'); return false",
        "diagnostics.log('closing'); error('shutdown_probe_failure')"})
    {
        cfg.source_path = fixture("return true", "return true", body);
        hunter::Script script;
        take(script.open(cfg, "{}"));
        const auto result = script.shutdown("test");
        check(!result && result.error().size() <= 4096, "shutdown failure remains observable");
        const auto logs = script.take_logs();
        check(logs.size() == 1 && logs[0] == "closing", "shutdown diagnostics survive VM close");
        const auto repeated = script.shutdown("again");
        check(!repeated && repeated.error() == result.error(), "shutdown failure is idempotent");
        check(script.take_logs().empty(), "repeated close does not execute shutdown twice");
        check(!script.tick(1, 0.01), "shutdown failure still releases the session");
        cfg.source_path = fixture("return true", "return true");
        take(script.open(cfg, "{}"));
        check(script.shutdown("reopened").has_value(), "VM can reopen after failed shutdown");
    }

    cfg.source_path = fixture("return true", "return true");
    hunter::Script owned;
    take(owned.open(cfg, "{}"));
    bool rejected = false;
    std::thread foreign([&]
    {
        const auto result = owned.shutdown("foreign");
        rejected = !result && result.error() == "wrong_thread";
    });
    foreign.join();
    check(rejected, "wrong thread shutdown is explicitly rejected");
    take(owned.tick(1, 0.01));
    check(owned.shutdown("owner").has_value(), "owner can close after rejected foreign call");
    check(owned.shutdown("owner").has_value(), "successful shutdown is idempotent");
    std::filesystem::remove(cfg.source_path);
}
#endif

#if HUNTER_PRODUCTION
// 管理生产负例的临时文件，失败退出时也删除测试制品。
struct TmpFile
{
    std::filesystem::path path;

    // 为每次运行创建独立文件名，避免并发测试覆盖制品。
    explicit TmpFile(const Str& suffix)
    {
        const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
        path = std::filesystem::temp_directory_path() /
            ("hunter-bundle-contract-" + std::to_string(stamp) + suffix);
    }

    // 清理只属于当前测试的文件，删除失败不会掩盖原始断言。
    ~TmpFile()
    {
        std::error_code error;
        std::filesystem::remove(path, error);
    }
};

// 通过生产 Runtime 验证授权策略与 Bundle 篡改拒绝，不能只依赖制品 CLI。
void bundle_failures(hunter::Cfg cfg)
{
    std::ifstream policy_file(cfg.policy_path);
    const auto trusted = nlohmann::json::parse(policy_file);
    TmpFile changed_policy(".json");
    cfg.policy_path = changed_policy.path.string();
    auto key = trusted;
    key["public_key"] = Str(64, '0');
    {
        std::ofstream out(changed_policy.path);
        out << key.dump();
    }

    hunter::Script wrong_key;
    check(!wrong_key.open(cfg, "{\"v\":1,\"snapshot_every\":3}"),
        "production loader rejects wrong public key");

    auto identity = trusted;
    identity["identities"]["runtime"] = Str(64, '1');
    {
        std::ofstream out(changed_policy.path);
        out << identity.dump();
    }

    hunter::Script wrong_identity;
    check(!wrong_identity.open(cfg, "{\"v\":1,\"snapshot_every\":3}"),
        "production loader rejects incompatible runtime identity");

    auto malformed = trusted;
    malformed["v"] = "invalid";
    {
        std::ofstream out(changed_policy.path);
        out << malformed.dump();
    }

    hunter::Script invalid_policy;
    check(!invalid_policy.open(cfg, "{\"v\":1,\"snapshot_every\":3}"),
        "malformed policy must return an error");
    {
        std::ofstream out(changed_policy.path);
        out << trusted.dump();
    }

    TmpFile changed_bundle(".luxb");
    std::filesystem::copy_file(cfg.bundle_path, changed_bundle.path);
    {
        std::fstream bytes(changed_bundle.path, std::ios::binary | std::ios::in | std::ios::out);
        bytes.seekg(-1, std::ios::end);
        char last = 0;
        bytes.get(last);
        bytes.seekp(-1, std::ios::end);
        bytes.put(static_cast<char>(last ^ 1));
    }

    cfg.bundle_path = changed_bundle.path.string();
    hunter::Script tampered;
    check(!tampered.open(cfg, "{\"v\":1,\"snapshot_every\":3}"),
        "production loader rejects modified signed bytecode");
}
#endif

}

// 运行源码或生产 Bundle 的相同主流程，返回进程级契约结果。
int main(int argc, char** argv)
{
    try
    {
        check(argc >= 2, "expected script or Bundle path");
        hunter::Cfg cfg;
#if HUNTER_PRODUCTION
        check(argc >= 3, "expected production policy path");
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[2];
#else
        cfg.source_path = argv[1];
#endif
        hunter::Script script;
        const nlohmann::json ctx = {{"v", 3}, {"snapshot_every", 3},
            {"content", nlohmann::json::parse(hunter::content::json_text)}};
        take(script.open(cfg, ctx.dump()));
        const auto logs = script.take_logs();
        check(!logs.empty(), "script diagnostics are observable after the entry returns");
        check(script.take_logs().empty(), "diagnostic extraction drains the bounded buffer");
        auto out = take(script.event(2, R"({"v":3,"req_id":"login"})"));
        check(out.size() == 1 && out[0].kind == "login", "local session login");
        out = take(script.event(3, R"({"v":3,"req_id":"start","after_match_id":"0"})"));
        check(out.size() == 2 && out[0].kind == "start" && out[1].kind == "snapshot",
            "start produces response and initial snapshot");
        out = take(script.event(1,
            R"({"v":3,"seq":"1","match_id":"1","applied_tick":"1","move_x":1,)"
            R"("aim_x":1000,"aim_y":0,"jump":false,"fire":false,"reload":false})"));
        check(out.size() == 1 && out[0].kind == "ack", "input produces one ack");
        check(nlohmann::json::parse(out[0].payload)["seq"] == "1", "exact input sequence");
        take(script.tick(1, 1.0 / 60.0));
        take(script.tick(2, 1.0 / 60.0));
        out = take(script.tick(3, 1.0 / 60.0));
        check(out.size() == 1 && out[0].kind == "snapshot", "snapshot cadence");
        check(nlohmann::json::parse(out[0].payload)["tick_id"] == "3", "exact Tick ID");
        auto state = take(script.export_state());
        check(script.validate_state().has_value(), "validate existing state");
        check(script.import_state(state).has_value(), "state roundtrip");
        bool rejected = false;
        std::thread other([&]
        {
            const auto res = script.tick(4, 1.0 / 60.0);
            rejected = !res && res.error().find("wrong_thread") != Str::npos;
        });
        other.join();
        check(rejected, "foreign thread must be rejected before VM access");
        take(script.tick(4, 1.0 / 60.0));
        check(script.shutdown("test").has_value(), "normal shutdown releases VM resources");
        check(script.shutdown("test").has_value(), "normal shutdown is idempotent");
        check(!script.tick(5, 0.01), "stopped handle is stale");
#if !HUNTER_PRODUCTION
        failures(cfg);
        output_schema(cfg);
        shutdown_contract(cfg);
#else
        bundle_failures(cfg);
#endif
        std::cout << "script contract passed\n";
        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }
}

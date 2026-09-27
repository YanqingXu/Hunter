// 用真实 SQLite、异步门面和子进程退出验证猎人迁移、资产原子性及出战回退。
#include "common/Types.h"
#include "storage/HunterStore.h"
#include "storage/Storage.h"
#include "storage/Schema.h"
#include "storage/Store.h"

#include <nlohmann/json.hpp>
#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <thread>
#if defined(_WIN32)
#include <process.h>
#endif

namespace
{
using namespace hunter::storage;
using Json = nlohmann::json;
Str failure_stage;
bool crash = false;

// 检查行为及不变量，错误包含当前用例说明。
void check(bool ok, const Str& message)
{
    if (!ok)
    {
        throw std::runtime_error(message);
    }
}

// 将独占临时路径转换为 SQLite 接受的 UTF-8。
Str utf8(const std::filesystem::path& path)
{
    const auto value = path.u8string();
    return Str(value.begin(), value.end());
}

struct Temp
{
    std::filesystem::path path;

    // 创建只属于本次测试的目录，不访问用户存档。
    Temp()
    {
        path = std::filesystem::temp_directory_path() / ("hunter-v2-" + std::to_string(
            std::chrono::steady_clock::now().time_since_epoch().count()));
        check(std::filesystem::create_directory(path), "创建独占测试目录");
    }

    // 回收本对象创建的临时目录，清理错误不覆盖原始断言。
    ~Temp()
    {
        std::error_code error;
        std::filesystem::remove_all(path, error);
    }

    // 返回目录内的单个测试数据库路径。
    Str file(const Str& name) const
    {
        return utf8(path / name);
    }
};

// 以正式读取接口获取最新账号版本。
u64 rev(Db& db)
{
    return read_hunters(db, 1, 1048576).revision;
}

// 构造显式参数命令，不依赖隐式价额或经验默认值。
HunterReq request(Db& db, const Str& op, const Str& kind, u64 hunter_id, const Json& value)
{
    return {1, rev(db), hunter_id, op, kind, value.dump()};
}

// 执行生产事务并保留原始操作响应。
HunterResult apply(Db& db, const HunterReq& req, usize limit = 1048576)
{
    return write_hunter(db, req, encode_hunter(req), limit);
}

// 为不同场景创建含免费技能和技能点的猎人。
HunterResult recruit_one(Db& db, const Str& op)
{
    return apply(db, request(db, op, "recruit", 0, {{"cfg_id", 2}, {"level", 1},
        {"xp", 0}, {"points", 8}, {"currency_cost", 0}, {"skills", Json::array({1})}}));
}

// 从拥有型档案返回指定猎人，已死亡或退役猎人不在可用列表。
Json hunter(Db& db, u64 id)
{
    const auto profile = Json::parse(read_hunters(db, 1, 1048576).result_json);
    for (const auto& value : profile["hunters"])
    {
        if (value["hunter_id"] == id)
        {
            return value;
        }
    }
    return nullptr;
}

// 验证失败码并确保完整可见档案和版本都没有变化。
void rejects(Db& db, const HunterReq& req, Code code, usize limit = 1048576)
{
    const auto before = read_hunters(db, 1, 1048576).result_json;
    bool rejected = false;
    try
    {
        static_cast<void>(apply(db, req, limit));
    }
    catch (const Error& error)
    {
        rejected = error.code == code;
    }
    check(rejected, "命令返回预期业务失败");
    check(read_hunters(db, 1, 1048576).result_json == before, "失败不改变档案和版本");
}

// 通过旧结算入口创建历史物品，所有物品必须保持真实 UID 和取得局号。
MatchResult seed_items(Db& db)
{
    CommitMatch req{next_match(db).value, 1, rev(db), "Extracted", "demo-v3:old",
        {{100, 3}, {101, 1}}};
    return write_match(db, req, encode_req(req), 1048576);
}

// 为最终死亡或主动放弃提供显式零奖励载荷。
Json dead_result(u64 match, const Str& outcome = "Dead")
{
    return {{"match_id", match}, {"outcome", outcome}, {"content_key", "hunt-v5:test"},
        {"level", 0}, {"xp", 0}, {"points", 0}, {"account_xp", 0}, {"currency_gain", 0},
        {"skills", Json::array()}, {"equipment", Json::array()}, {"items", Json::array()}};
}

// 验证完整旧数据库迁移，不转换免费配装、不修改历史结果或资产身份。
void migration(const Temp& temp)
{
    const auto path = temp.file("migration.db");
    Str saved;
    u64 last = 0;
    {
        Db db(path);
        open_schema(db);
        const auto old = seed_items(db);
        saved = old.result_json;
        last = old.match_id;
        db.exec("DROP INDEX idx_hunter_active");
        db.exec("DROP TABLE hunter_op");
        db.exec("DROP TABLE hunter_equip");
        db.exec("DROP TABLE hunter_skill");
        db.exec("DROP TABLE hunter_raid");
        db.exec("DROP TABLE hunter_save");
        db.exec("DROP TABLE account_progress");
        db.exec("PRAGMA user_version=1");
    }
    Db db(path);
    open_schema(db);
    check(db.scalar("PRAGMA user_version") == 2, "旧库升级到V2");
    check(read_match(db, last, 1048576).result_json == saved, "旧结果原文不变");
    const auto profile = Json::parse(read_hunters(db, 1, 1048576).result_json);
    check(profile["hunters"].empty() && profile["account_xp"] == 0
        && profile["currency"] == 0 && profile["stash"].size() == 2,
        "不赠送猎人或免费装备资产");
    check(profile["stash"][0]["item_uid"] == Json::parse(saved)["items"][0]["item_uid"],
        "保留历史物品身份");
    check(next_match(db).value > last, "保留持久局号高水位");
    open_schema(db);
    check(read_match(db, last, 1048576).result_json == saved, "重复打开迁移幂等");
}

// 验证技能实际支付、操作持久幂等、完整配装原子性和退休归仓。
void economy(const Temp& temp)
{
    Db db(temp.file("economy.db"));
    open_schema(db);
    const auto old = seed_items(db);
    const auto item = Json::parse(old.result_json)["items"][0]["item_uid"];
    auto recruit = request(db, "recruit", "recruit", 0,
        {{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 8},
            {"currency_cost", 0}, {"skills", Json::array({1})}});
    recruit.intent_json = R"({"cfg_id":2})";
    const auto created = apply(db, recruit);
    const auto again = apply(db, recruit);
    check(again.replayed && again.result_json == created.result_json, "招募同操作精确重放");
    auto changed = recruit;
    changed.payload_json = Json({{"cfg_id", 3}, {"level", 1}, {"xp", 0}, {"points", 8},
        {"currency_cost", 0}, {"skills", Json::array({1})}}).dump();
    rejects(db, changed, Code::Conflict);
    const auto id = created.hunter_id;
    apply(db, request(db, "buy", "buy_skill", id, {{"cfg_id", 2}, {"cost", 3}}));
    rejects(db, request(db, "double-buy", "buy_skill", id, {{"cfg_id", 2}, {"cost", 3}}),
        Code::Conflict);
    apply(db, request(db, "remove", "remove_skill", id, {{"cfg_id", 2}, {"refund", 1}}));
    check(hunter(db, id)["points"] == 6, "按实际支付退点");
    rejects(db, request(db, "free-refund", "remove_skill", id,
        {{"cfg_id", 1}, {"refund", 1}}), Code::Invalid);
    apply(db, request(db, "buy-one", "buy_skill", id, {{"cfg_id", 2}, {"cost", 1}}));
    apply(db, request(db, "refund-one", "remove_skill", id, {{"cfg_id", 2}, {"refund", 1}}));
    check(hunter(db, id)["points"] == 6, "支付一点可退一点");
    apply(db, request(db, "equip", "equip", id,
        {{"items", Json::array({{{"slot", 1}, {"item_uid", item}}})}}));
    check(read_player(db, 1, 1048576).items.size() == 1, "已装备物品不出现在可用仓库");
    rejects(db, request(db, "bad-equip", "equip", id, {{"items", Json::array({
        {{"slot", 1}, {"item_uid", item}}, {{"slot", 2}, {"item_uid", 9999}}})}}),
        Code::NotFound);
    const auto second = recruit_one(db, "second");
    rejects(db, request(db, "stolen", "equip", second.hunter_id,
        {{"items", Json::array({{{"slot", 1}, {"item_uid", item}}})}}), Code::Busy);
    rejects(db, request(db, "early-retire", "retire", id,
        {{"max_level", 2}, {"account_xp", 10}}), Code::Invalid);
    const auto retired = apply(db, request(db, "retire", "retire", id,
        {{"max_level", 1}, {"account_xp", 10}}));
    check(hunter(db, id).is_null() && read_player(db, 1, 1048576).items.size() == 2,
        "退役后猎人消失而装备归仓");
    check(Json::parse(retired.result_json)["profile"]["account_xp"] == 10,
        "退役经验同事务增加");
    check(find_hunter_op(db, 1, "recruit", recruit.intent_json, 1048576).result_json
        == created.result_json, "人物退役后先按原始意图返回成功响应");
    auto excessive = request(db, "oversized-intent", "retire", second.hunter_id,
        {{"max_level", 1}, {"account_xp", 0}});
    excessive.intent_json.assign(262145, 'x');
    rejects(db, excessive, Code::TooLarge);
    auto invalid = request(db, "missing-cost", "recruit", 0,
        {{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 8}, {"skills", Json::array()}});
    rejects(db, invalid, Code::Invalid);
    rejects(db, request(db, "result-budget", "buy_skill", second.hunter_id,
        {{"cfg_id", 2}, {"cost", 1}}), Code::TooLarge, 128);
}

// 验证出战副本结算、一次性技能回退和提交后结果优先。
void raids(const Temp& temp)
{
    const auto path = temp.file("raids.db");
    u64 id = 0;
    u64 interrupted = 0;
    {
        Db db(path);
        open_schema(db);
        id = recruit_one(db, "recruit").hunter_id;
        apply(db, request(db, "paid", "buy_skill", id, {{"cfg_id", 2}, {"cost", 3}}));
        interrupted = apply(db, request(db, "begin", "begin_raid", id,
            {{"content_key", "hunt-v5:test"}})).match_id;
        rejects(db, request(db, "busy", "remove_skill", id,
            {{"cfg_id", 2}, {"refund", 1}}), Code::Busy);
    }
    Db db(path);
    open_schema(db);
    recover_hunters(db);
    check(hunter(db, id)["state"] == "ready" && hunter(db, id)["skills"].size() == 2,
        "异常退出恢复出战前一次性技能");
    const auto recovered = rev(db);
    recover_hunters(db);
    check(rev(db) == recovered, "重复启动不重复回退或增长版本");
    const auto started = apply(db, request(db, "again", "begin_raid", id,
        {{"content_key", "hunt-v5:test"}}));
    check(started.match_id > interrupted, "回退不复用局号");
    auto final = dead_result(started.match_id, "Extracted");
    final["level"] = 2;
    final["xp"] = 10;
    final["points"] = 6;
    final["skills"] = Json::array({{{"cfg_id", 1}, {"paid_cost", 0}, {"source", "recruit"}},
        {{"cfg_id", 7}, {"paid_cost", 0}, {"source", "loot"}}});
    final["items"] = Json::array({{{"cfg_id", 100}, {"count", 2}}});
    final["currency_gain"] = 5;
    const auto req = request(db, "extract", "finish_raid", id, final);
    auto bad = req;
    auto forged = final;
    forged["skills"][1]["paid_cost"] = 4;
    bad.op_id = "forged-origin";
    bad.payload_json = forged.dump();
    rejects(db, bad, Code::Invalid);
    failure_stage = "in_txn";
    rejects(db, req, Code::Write);
    failure_stage.clear();
    failure_stage = "after_commit";
    bool lost = false;
    try
    {
        static_cast<void>(apply(db, req));
    }
    catch (const Error& error)
    {
        lost = error.commit_unknown;
    }
    failure_stage.clear();
    check(lost, "提交后模拟回包丢失");
    const auto saved = read_match(db, started.match_id, 1048576).result_json;
    recover_hunters(db);
    const auto replay = apply(db, req);
    check(replay.replayed && Json::parse(replay.result_json)["result"].dump() == saved,
        "已提交撤离保持原结果并精确重放");
    check(hunter(db, id)["skills"].size() == 2 && hunter(db, id)["level"] == 2,
        "撤离消费旧技能并保留拾取技能");
    rejects(db, request(db, "loot-refund", "remove_skill", id,
        {{"cfg_id", 7}, {"refund", 1}}), Code::Invalid);
    const auto death = apply(db, request(db, "final-begin", "begin_raid", id,
        {{"content_key", "hunt-v5:test"}}));
    const auto dead = request(db, "abandon", "finish_raid", id,
        dead_result(death.match_id, "Abandoned"));
    apply(db, dead);
    recover_hunters(db);
    check(hunter(db, id).is_null(), "主动放弃的已提交人物不能复原");
    check(apply(db, dead).replayed, "已删除人物的终局仍可幂等查询");
}

// 用异步公开门面验证新命令的完成线程、关联和启动回退。
void async_api(const Temp& temp)
{
    asio::io_context io;
    Storage store(io, {}, 71);
    const auto owner = std::this_thread::get_id();
    Opt<Rsp> result;
    const auto done = [&](Rsp value)
    {
        check(std::this_thread::get_id() == owner, "异步回调返回逻辑线程");
        result = std::move(value);
    };
    const auto wait = [&]
    {
        const auto end = std::chrono::steady_clock::now() + std::chrono::seconds(10);
        while (!result && std::chrono::steady_clock::now() < end)
        {
            io.restart();
            io.run_for(std::chrono::milliseconds(5));
        }
        check(result && result->result, "异步任务成功完成");
        check(result->key.instance == 71, "异步关联身份正确");
    };
    check(store.open(temp.file("async.db"), done).has_value() && !result, "打开不内联完成");
    wait();
    result.reset();
    check(store.load_hunters(1, done).has_value(), "提交档案读取");
    wait();
    const auto version = std::get<HunterResult>(*result->result).revision;
    result.reset();
    const HunterReq req{1, version, 0, "async-recruit", "recruit",
        Json({{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 0},
            {"currency_cost", 0}, {"skills", Json::array()}}).dump()};
    check(store.apply_hunter(req, done).has_value() && !result, "猎人命令不内联完成");
    wait();
    check(std::get<HunterResult>(*result->result).hunter_id > 0, "返回持久猎人身份");
    result.reset();
    check(store.stop(done).has_value(), "提交停止屏障");
    wait();
}

// 网络专用档案预算小于旧结果预算时，越界交易回滚且原档案仍可读取。
void async_budget(const Temp& temp)
{
    asio::io_context io;
    StorageCfg limits;
    limits.max_hunter_bytes = 2048;
    Storage store(io, limits, 72);
    Opt<Rsp> result;
    const auto done = [&](Rsp value) { result = std::move(value); };
    const auto wait = [&]
    {
        const auto end = std::chrono::steady_clock::now() + std::chrono::seconds(10);
        while (!result && std::chrono::steady_clock::now() < end)
        {
            io.restart();
            io.run_for(std::chrono::milliseconds(5));
        }
        check(result.has_value(), "小预算异步完成");
    };
    check(store.open(temp.file("budget.db"), done).has_value(), "小预算打开");
    wait();
    check(result->result.has_value(), "小预算打开成功");
    result.reset();
    check(store.load_hunters(1, done).has_value(), "小预算读取");
    wait();
    auto current = std::get<HunterResult>(*result->result);
    bool refused = false;
    Str rejected_op;
    for (u32 index = 0; index < 64; ++index)
    {
        rejected_op = "capacity-" + std::to_string(index);
        const HunterReq req{1, current.revision, 0, rejected_op, "recruit",
            Json({{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 0},
                {"currency_cost", 0}, {"skills", Json::array()}}).dump()};
        result.reset();
        check(store.apply_hunter(req, done).has_value(), "预算交易受理");
        wait();
        if (!result->result)
        {
            check(index > 0 && result->result.error().code == Code::TooLarge,
                "仅预算超限时拒绝而不沿用一兆旧结果预算");
            refused = true;
            break;
        }
        result.reset();
        check(store.load_hunters(1, done).has_value(), "成功交易后读取");
        wait();
        check(result->result.has_value(), "已提交档案始终可读");
        current = std::get<HunterResult>(*result->result);
    }
    check(refused, "有限次数招募达到显式预算");
    result.reset();
    check(store.load_hunters(1, done).has_value(), "超限后读取原档案");
    wait();
    check(result->result && std::get<HunterResult>(*result->result).result_json
        == current.result_json, "超限不修改人物、revision或原档案");
    result.reset();
    check(store.find_hunter_op(1, rejected_op, "", done).has_value(), "查询被拒操作");
    wait();
    check(!result->result && result->result.error().code == Code::NotFound,
        "超限不写永久成功记录");
    result.reset();
    check(store.stop(done).has_value(), "预算用例停止");
    wait();
    check(result->result.has_value(), "预算用例干净退出");
}

// 在独立进程中执行真实终局，并在指定事务边界立即退出而不运行析构。
void crash_child(const Str& path, const Str& stage)
{
    Db db(path);
    open_schema(db);
    Stmt stmt(db, "SELECT match_id,hunter_id FROM hunter_raid WHERE state='active'");
    check(stmt.step(), "子进程找到活动出战");
    const auto match = static_cast<u64>(stmt.integer(0));
    const auto id = static_cast<u64>(stmt.integer(1));
    const auto req = request(db, "crash-finish", "finish_raid", id, dead_result(match));
    failure_stage = stage;
    crash = true;
    static_cast<void>(apply(db, req));
    throw std::runtime_error("指定故障点没有退出");
}

// 实际启动子进程，在提交前、事务内及提交后验证强杀恢复边界。
void crashes(const Temp& temp, const Str& executable)
{
#if defined(_WIN32)
    for (const auto stage : {"before_txn", "in_txn", "after_commit"})
    {
        const auto path = temp.file(Str(stage) + ".db");
        u64 id = 0;
        u64 match = 0;
        {
            Db db(path);
            open_schema(db);
            const auto items = Json::parse(seed_items(db).result_json)["items"];
            id = recruit_one(db, "crash-recruit").hunter_id;
            apply(db, request(db, "crash-equip", "equip", id,
                {{"items", Json::array({{{"slot", 1}, {"item_uid", items[0]["item_uid"]}}})}}));
            match = apply(db, request(db, "crash-begin", "begin_raid", id,
                {{"content_key", "hunt-v5:test"}})).match_id;
        }
        const char* args[] = {executable.c_str(), "--crash", path.c_str(), stage, nullptr};
        check(_spawnv(_P_WAIT, executable.c_str(), args) == 42, "真实子进程在指定边界退出");
        Db db(path);
        open_schema(db);
        recover_hunters(db);
        if (Str(stage) == "after_commit")
        {
            check(hunter(db, id).is_null(), "提交后强杀不恢复死亡猎人");
            const auto saved = read_match(db, match, 1048576).result_json;
            check(Json::parse(saved)["outcome"] == "Dead", "提交后结果可查询");
            check(db.scalar("SELECT count(*) FROM player_item") == 1,
                "死亡携带装备整体删除而仓库保留");
        }
        else
        {
            check(hunter(db, id)["state"] == "ready"
                && hunter(db, id)["equipment"].size() == 1, "提交前强杀归还原人物装备");
            check(db.scalar("SELECT count(*) FROM match_result") == 1,
                "未提交终局不生成结果");
            check(db.scalar("SELECT count(*) FROM player_item") == 2, "资产无局部删除");
        }
    }
#else
    static_cast<void>(temp);
    static_cast<void>(executable);
#endif
}
}

namespace hunter::storage::test
{
// 测试专用真实事务钩子，生产库不链接此函数或故障开关。
void point(const char* stage, Db& db)
{
    static_cast<void>(db);
    if (failure_stage == stage)
    {
        if (crash)
        {
            std::_Exit(42);
        }
        throw Error{Code::Write, 0, failure_stage == "after_commit", "测试事务故障"};
    }
}
}

// 执行独立契约或子进程故障场景，所有数据库位于独占临时目录。
i32 main(i32 argc, char** argv)
{
    try
    {
        if (argc == 4 && Str(argv[1]) == "--crash")
        {
            crash_child(argv[2], argv[3]);
            return 1;
        }
        Temp temp;
        migration(temp);
        economy(temp);
        raids(temp);
        async_api(temp);
        async_budget(temp);
        crashes(temp, utf8(std::filesystem::absolute(argv[0])));
        std::cout << "猎人存档契约通过：迁移、经济、出战、回退、异步及三阶段强杀\n";
        return 0;
    }
    catch (const Error& error)
    {
        std::cerr << error.message << '\n';
        return 1;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

// 实现猎人永久资产事务；规则参数来自玩法，数据库保留出战前资产直到原子终局。
#include "common/Types.h"
#include "storage/HunterStore.h"
#include "storage/Schema.h"

#include <nlohmann/json.hpp>
#include <sqlite3.h>

#if HUNTER_STORAGE_TESTING
#include "StorageHooks.h"
#endif

namespace hunter::storage
{
namespace
{
using Json = nlohmann::json;
using req::ensure;
using req::identity;
using req::max_payload;
using req::max_value;
using req::number;
using req::text_value;

// 对已有计数执行不会溢出的非负增量。
i64 add(i64 current, i64 delta)
{
    ensure(current >= 0 && delta >= 0 && delta <= max_value - current,
        "hunter_counter_overflow", Code::Overflow);
    return current + delta;
}

// 绑定可信常量语句中的整数和文本参数。
void bind(Stmt& stmt, std::initializer_list<Json> values)
{
    i32 pos = 1;
    for (const auto& value : values)
    {
        if (value.is_string())
        {
            stmt.bind(pos++, value.get<Str>());
        }
        else
        {
            stmt.bind(pos++, number(value));
        }
    }
}

// 执行参数化写入，不允许请求拼接 SQL。
void run(Db& db, const char* sql, std::initializer_list<Json> values)
{
    Stmt stmt(db, sql);
    bind(stmt, values);
    stmt.run();
}

// 检查序列化结果预算，超限使外层事务整体回滚。
Str bounded(const Json& value, usize limit)
{
    auto result = value.dump();
    ensure(limit >= sizeof(Rsp) + sizeof(HunterResult)
        && result.capacity() <= limit - sizeof(Rsp) - sizeof(HunterResult),
        "hunter_result_limit", Code::TooLarge);
    return result;
}

// 返回给定账号的当前版本，不在读取时隐式创建账号。
i64 revision(Db& db, u64 player_id)
{
    Stmt stmt(db, "SELECT revision FROM player_save WHERE player_id=?");
    stmt.bind(1, identity(player_id));
    ensure(stmt.step(), "player_not_found", Code::NotFound);
    return stmt.integer(0);
}

// 返回猎人及其技能和装备的拥有型快照。
Json hunter_doc(Db& db, u64 player_id, u64 hunter_id)
{
    Stmt stmt(db, "SELECT cfg_id,level,xp,points,state FROM hunter_save "
        "WHERE hunter_id=? AND player_id=?");
    bind(stmt, {identity(hunter_id), identity(player_id)});
    ensure(stmt.step(), "hunter_not_found", Code::NotFound);
    Json result = {{"hunter_id", hunter_id}, {"cfg_id", stmt.integer(0)},
        {"level", stmt.integer(1)}, {"xp", stmt.integer(2)}, {"points", stmt.integer(3)},
        {"state", stmt.text(4, 16)}, {"skills", Json::array()}, {"equipment", Json::array()}};
    Stmt skills(db, "SELECT cfg_id,paid_cost,source FROM hunter_skill "
        "WHERE hunter_id=? ORDER BY cfg_id");
    skills.bind(1, identity(hunter_id));
    while (skills.step())
    {
        ensure(result["skills"].size() < 64, "hunter_skill_limit", Code::Corrupt);
        result["skills"].push_back({{"cfg_id", skills.integer(0)},
            {"paid_cost", skills.integer(1)}, {"source", skills.text(2, 16)}});
    }
    Stmt equipment(db, "SELECT e.slot,i.item_uid,i.cfg_id,i.count,i.acquired_match_id "
        "FROM hunter_equip e JOIN player_item i ON i.item_uid=e.item_uid "
        "WHERE e.hunter_id=? ORDER BY e.slot");
    equipment.bind(1, identity(hunter_id));
    while (equipment.step())
    {
        ensure(result["equipment"].size() < 16, "hunter_equipment_limit", Code::Corrupt);
        result["equipment"].push_back({{"slot", equipment.integer(0)},
            {"item_uid", equipment.integer(1)}, {"cfg_id", equipment.integer(2)},
            {"count", equipment.integer(3)}, {"acquired_match_id", equipment.integer(4)}});
    }
    return result;
}

// 读取完整档案，所有容器受完成预算限制。
Json profile_doc(Db& db, u64 player_id, usize limit)
{
    Json result = {{"player_id", player_id}, {"revision", revision(db, player_id)},
        {"hunters", Json::array()}, {"stash", Json::array()}, {"raids", Json::array()}};
    Stmt account(db, "SELECT xp,currency FROM account_progress WHERE player_id=?");
    account.bind(1, identity(player_id));
    ensure(account.step(), "account_progress_missing", Code::Corrupt);
    result["account_xp"] = account.integer(0);
    result["currency"] = account.integer(1);
    Stmt hunters(db, "SELECT hunter_id FROM hunter_save WHERE player_id=? "
        "AND state IN ('ready','in_raid') ORDER BY hunter_id");
    hunters.bind(1, identity(player_id));
    while (hunters.step())
    {
        result["hunters"].push_back(hunter_doc(db, player_id,
            static_cast<u64>(hunters.integer(0))));
        static_cast<void>(bounded(result, limit));
    }
    Stmt items(db, "SELECT item_uid,cfg_id,count,acquired_match_id FROM player_item i "
        "WHERE player_id=? AND NOT EXISTS(SELECT 1 FROM hunter_equip e "
        "WHERE e.item_uid=i.item_uid) ORDER BY item_uid");
    items.bind(1, identity(player_id));
    while (items.step())
    {
        result["stash"].push_back({{"item_uid", items.integer(0)}, {"cfg_id", items.integer(1)},
            {"count", items.integer(2)}, {"acquired_match_id", items.integer(3)}});
        static_cast<void>(bounded(result, limit));
    }
    Stmt raids(db, "SELECT match_id,hunter_id,content_key FROM hunter_raid "
        "WHERE player_id=? AND state='active' ORDER BY match_id");
    raids.bind(1, identity(player_id));
    while (raids.step())
    {
        result["raids"].push_back({{"match_id", raids.integer(0)},
            {"hunter_id", raids.integer(1)}, {"content_key", raids.text(2, 256)}});
    }
    return result;
}

// 对已存在猎人强制执行出战占用和终态门禁。
Json ready_hunter(Db& db, const HunterReq& req)
{
    auto result = hunter_doc(db, req.player_id, req.hunter_id);
    ensure(result["state"] == "ready", "hunter_not_ready", Code::Busy);
    return result;
}

// 读取经济账户，参数化读取避免把账号身份写进 SQL。
Json account_doc(Db& db, u64 player_id)
{
    Stmt stmt(db, "SELECT xp,currency FROM account_progress WHERE player_id=?");
    stmt.bind(1, identity(player_id));
    ensure(stmt.step(), "account_progress_missing", Code::Corrupt);
    return {{"xp", stmt.integer(0)}, {"currency", stmt.integer(1)}};
}

// 创建猎人和初始免费技能，不生成任何免费装备资产。
u64 recruit(Db& db, const HunterReq& req, const Json& value)
{
    const auto account = account_doc(db, req.player_id);
    const auto balance = number(account["currency"]);
    const auto cost = number(value["currency_cost"]);
    ensure(cost <= balance, "insufficient_currency");
    run(db, "UPDATE account_progress SET currency=? WHERE player_id=?",
        {balance - cost, req.player_id});
    run(db, "INSERT INTO hunter_save(player_id,cfg_id,level,xp,points,state) "
        "VALUES(?,?,?,?,?,'ready')", {req.player_id, value["cfg_id"], value["level"],
            value["xp"], value["points"]});
    const auto id = sqlite3_last_insert_rowid(db.handle());
    for (const auto& skill : value["skills"])
    {
        run(db, "INSERT INTO hunter_skill VALUES(?,?,0,'recruit')", {id, skill});
    }
    return static_cast<u64>(id);
}

// 替换完整配装，外层事务确保最后一项失败时保留原配装。
void equip(Db& db, const HunterReq& req, const Json& value)
{
    static_cast<void>(ready_hunter(db, req));
    run(db, "DELETE FROM hunter_equip WHERE hunter_id=?", {req.hunter_id});
    for (const auto& item : value["items"])
    {
        Stmt stmt(db, "SELECT player_id FROM player_item WHERE item_uid=?");
        bind(stmt, {item["item_uid"]});
        ensure(stmt.step() && stmt.integer(0) == identity(req.player_id),
            "item_not_owned", Code::NotFound);
        Stmt used(db, "SELECT hunter_id FROM hunter_equip WHERE item_uid=?");
        bind(used, {item["item_uid"]});
        ensure(!used.step(), "item_already_equipped", Code::Busy);
        run(db, "INSERT INTO hunter_equip VALUES(?,?,?)",
            {req.hunter_id, item["slot"], item["item_uid"]});
    }
}

// 根据实际取得记录扣点或退点，不把免费来源转成付费技能。
void trade_skill(Db& db, const HunterReq& req, const Json& value)
{
    const auto hunter = ready_hunter(db, req);
    auto points = number(hunter["points"]);
    Stmt prior(db, "SELECT paid_cost FROM hunter_skill WHERE hunter_id=? AND cfg_id=?");
    bind(prior, {req.hunter_id, value["cfg_id"]});
    const auto exists = prior.step();
    if (req.kind == "buy_skill")
    {
        ensure(!exists, "duplicate_skill", Code::Conflict);
        ensure(hunter["skills"].size() < 64, "hunter_skill_limit", Code::Capacity);
        const auto cost = number(value["cost"]);
        ensure(cost <= points, "insufficient_skill_points");
        points -= cost;
        run(db, "INSERT INTO hunter_skill VALUES(?,?,?,'purchased')",
            {req.hunter_id, value["cfg_id"], cost});
    }
    else
    {
        ensure(exists, "skill_not_owned", Code::NotFound);
        const auto refund = number(value["refund"]);
        ensure(refund <= prior.integer(0), "refund_exceeds_paid_cost");
        points = add(points, refund);
        run(db, "DELETE FROM hunter_skill WHERE hunter_id=? AND cfg_id=?",
            {req.hunter_id, value["cfg_id"]});
    }
    run(db, "UPDATE hunter_save SET points=? WHERE hunter_id=?", {points, req.hunter_id});
}

// 满级退役只解除装备归属并增加显式账号经验，不销毁仓库物品。
void retire(Db& db, const HunterReq& req, const Json& value)
{
    const auto hunter = ready_hunter(db, req);
    ensure(number(hunter["level"]) == number(value["max_level"]), "hunter_not_max_level");
    const auto account = account_doc(db, req.player_id);
    run(db, "UPDATE account_progress SET xp=? WHERE player_id=?",
        {add(number(account["xp"]), number(value["account_xp"])), req.player_id});
    run(db, "DELETE FROM hunter_equip WHERE hunter_id=?", {req.hunter_id});
    run(db, "DELETE FROM hunter_skill WHERE hunter_id=?", {req.hunter_id});
    run(db, "UPDATE hunter_save SET state='retired',points=0 WHERE hunter_id=?", {req.hunter_id});
}

// 在资产保持不变时登记出战基线，并在同一事务分配持久局号。
u64 begin_raid(Db& db, const HunterReq& req, const Json& value)
{
    const auto hunter = ready_hunter(db, req);
    Stmt active(db, "SELECT 1 FROM hunter_raid WHERE player_id=? AND state='active'");
    active.bind(1, identity(req.player_id));
    ensure(!active.step(), "player_already_deployed", Code::Busy);
    const auto match = db.scalar("SELECT int_value FROM save_meta WHERE key='next_match_id'");
    ensure(match > 0 && match < max_value, "match_sequence_exhausted", Code::Overflow);
    db.exec("UPDATE save_meta SET int_value=int_value+1 WHERE key='next_match_id'");
    run(db, "INSERT INTO hunter_raid VALUES(?,?,?,'active',?,?)",
        {match, req.player_id, req.hunter_id, value["content_key"], hunter.dump()});
    run(db, "UPDATE hunter_save SET state='in_raid' WHERE hunter_id=?", {req.hunter_id});
    return static_cast<u64>(match);
}

// 查找仍活动且归属正确的出战，保留基线用于终局校验。
Json raid_doc(Db& db, const HunterReq& req, i64 match_id)
{
    Stmt stmt(db, "SELECT state,content_key,baseline_json FROM hunter_raid "
        "WHERE match_id=? AND player_id=? AND hunter_id=?");
    bind(stmt, {match_id, req.player_id, req.hunter_id});
    ensure(stmt.step(), "raid_not_found", Code::NotFound);
    ensure(stmt.text(0, 16) == "active", "raid_already_closed", Code::Conflict);
    auto result = Json::parse(stmt.text(2, max_payload));
    result["content_key"] = stmt.text(1, 256);
    return result;
}

// 应用最终技能集，旧技能取得信息不可改写，新拾取必须免费。
void finish_skills(Db& db, const HunterReq& req, const Json& value, const Json& baseline)
{
    for (const auto& skill : value["skills"])
    {
        bool existing = false;
        for (const auto& prior : baseline["skills"])
        {
            if (prior["cfg_id"] == skill["cfg_id"])
            {
                ensure(prior == skill, "skill_origin_changed");
                existing = true;
            }
        }
        ensure(existing || (skill["paid_cost"] == 0 && skill["source"] == "loot"),
            "new_skill_must_be_loot");
    }
    run(db, "DELETE FROM hunter_skill WHERE hunter_id=?", {req.hunter_id});
    for (const auto& skill : value["skills"])
    {
        run(db, "INSERT INTO hunter_skill VALUES(?,?,?,?)",
            {req.hunter_id, skill["cfg_id"], skill["paid_cost"], skill["source"]});
    }
}

// 应用携带物剩余数量，拒绝局内伪造已有 UID 或增加出战物资。
void finish_equipment(Db& db, const HunterReq& req, const Json& value, const Json& baseline)
{
    Map<i64, i64> remaining;
    for (const auto& item : value["equipment"])
    {
        remaining.emplace(number(item["item_uid"], 1), number(item["count"], 1));
    }
    for (const auto& item : baseline["equipment"])
    {
        const auto uid = number(item["item_uid"], 1);
        const auto found = remaining.find(uid);
        if (found == remaining.end())
        {
            run(db, "DELETE FROM hunter_equip WHERE hunter_id=? AND item_uid=?",
                {req.hunter_id, uid});
            run(db, "DELETE FROM player_item WHERE item_uid=?", {uid});
        }
        else
        {
            ensure(found->second <= number(item["count"], 1), "equipment_count_increased");
            run(db, "UPDATE player_item SET count=? WHERE item_uid=?", {found->second, uid});
            remaining.erase(found);
        }
    }
    ensure(remaining.empty(), "equipment_not_deployed");
}

// 生成与旧查询兼容的终局结果，人物和物品变更尚在同一外层事务内。
Json finish_raid(Db& db, const HunterReq& req, const Json& value, const Str& request_json)
{
    const auto match = number(value["match_id"], 1);
    const auto baseline = raid_doc(db, req, match);
    ensure(value["content_key"] == baseline["content_key"], "raid_content_conflict");
    const bool extracted = value["outcome"] == "Extracted";
    finish_skills(db, req, value, baseline);
    finish_equipment(db, req, value, baseline);
    if (extracted)
    {
        ensure(number(value["level"], 1) >= number(baseline["level"], 1),
            "hunter_level_regression");
        run(db, "UPDATE hunter_save SET level=?,xp=?,points=?,state='ready' WHERE hunter_id=?",
            {value["level"], value["xp"], value["points"], req.hunter_id});
    }
    else
    {
        run(db, "UPDATE hunter_save SET state='dead',points=0 WHERE hunter_id=?", {req.hunter_id});
    }
    const auto account = account_doc(db, req.player_id);
    run(db, "UPDATE account_progress SET xp=?,currency=? WHERE player_id=?",
        {add(number(account["xp"]), number(value["account_xp"])),
            add(number(account["currency"]), number(value["currency_gain"])), req.player_id});
    Json items = Json::array();
    for (const auto& item : value["items"])
    {
        run(db, "INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) VALUES(?,?,?,?)",
            {req.player_id, item["cfg_id"], item["count"], match});
        items.push_back({{"item_uid", sqlite3_last_insert_rowid(db.handle())},
            {"cfg_id", item["cfg_id"]}, {"count", item["count"]}, {"acquired_match_id", match}});
    }
    const auto now = wall_ms();
    Json result = {{"v", 2}, {"match_id", match}, {"player_id", req.player_id},
        {"hunter_id", req.hunter_id}, {"outcome", value["outcome"]},
        {"content_key", value["content_key"]}, {"revision_before", req.expected_revision},
        {"revision_after", req.expected_revision + 1}, {"committed_at_ms", now},
        {"items", std::move(items)}, {"level", value["level"]}, {"xp", value["xp"]},
        {"points", value["points"]}, {"skills", value["skills"]},
        {"equipment", value["equipment"]}, {"account_xp", value["account_xp"]},
        {"currency_gain", value["currency_gain"]}};
    run(db, "INSERT INTO match_result VALUES(?,?,?,?,?,?,?,?,?)",
        {match, req.player_id, value["outcome"], value["content_key"], request_json,
            result.dump(), req.expected_revision, req.expected_revision + 1, now});
    run(db, "UPDATE hunter_raid SET state='committed' WHERE match_id=?", {match});
    run(db, "UPDATE player_save SET last_match_id=? WHERE player_id=?", {match, req.player_id});
    return result;
}

// 只解除未完成出战的占用，永久技能和装备一直保存着出战前值。
void rollback_raid(Db& db, const HunterReq& req, i64 match)
{
    static_cast<void>(raid_doc(db, req, match));
    Stmt committed(db, "SELECT 1 FROM match_result WHERE match_id=?");
    committed.bind(1, match);
    ensure(!committed.step(), "committed_raid_cannot_rollback", Code::Conflict);
    run(db, "UPDATE hunter_save SET state='ready' WHERE hunter_id=? AND state='in_raid'",
        {req.hunter_id});
    run(db, "UPDATE hunter_raid SET state='rolled_back' WHERE match_id=?", {match});
}
}

const Map<Str, Str>& hunter_tables()
{
    static const Map<Str, Str> tables = {
        {"account_progress", R"(CREATE TABLE account_progress (
    player_id INTEGER PRIMARY KEY REFERENCES player_save(player_id),
    xp INTEGER NOT NULL CHECK(xp >= 0),
    currency INTEGER NOT NULL CHECK(currency >= 0)
) STRICT)"},
        {"hunter_save", R"(CREATE TABLE hunter_save (
    hunter_id INTEGER PRIMARY KEY AUTOINCREMENT CHECK(hunter_id > 0),
    player_id INTEGER NOT NULL REFERENCES player_save(player_id),
    cfg_id INTEGER NOT NULL CHECK(cfg_id > 0 AND cfg_id <= 2147483647),
    level INTEGER NOT NULL CHECK(level > 0 AND level <= 1000000),
    xp INTEGER NOT NULL CHECK(xp >= 0),
    points INTEGER NOT NULL CHECK(points >= 0),
    state TEXT NOT NULL CHECK(state IN ('ready','in_raid','dead','retired'))
) STRICT)"},
        {"hunter_skill", R"(CREATE TABLE hunter_skill (
    hunter_id INTEGER NOT NULL REFERENCES hunter_save(hunter_id),
    cfg_id INTEGER NOT NULL CHECK(cfg_id > 0 AND cfg_id <= 2147483647),
    paid_cost INTEGER NOT NULL CHECK(paid_cost >= 0),
    source TEXT NOT NULL CHECK(source IN ('recruit','purchased','loot')),
    PRIMARY KEY(hunter_id,cfg_id),
    CHECK(source = 'purchased' OR paid_cost = 0)
) STRICT)"},
        {"hunter_equip", R"(CREATE TABLE hunter_equip (
    hunter_id INTEGER NOT NULL REFERENCES hunter_save(hunter_id),
    slot INTEGER NOT NULL CHECK(slot > 0 AND slot <= 16),
    item_uid INTEGER NOT NULL UNIQUE REFERENCES player_item(item_uid),
    PRIMARY KEY(hunter_id,slot)
) STRICT)"},
        {"hunter_raid", R"(CREATE TABLE hunter_raid (
    match_id INTEGER PRIMARY KEY CHECK(match_id > 0),
    player_id INTEGER NOT NULL REFERENCES player_save(player_id),
    hunter_id INTEGER NOT NULL REFERENCES hunter_save(hunter_id),
    state TEXT NOT NULL CHECK(state IN ('active','committed','rolled_back')),
    content_key TEXT NOT NULL CHECK(length(content_key) > 0),
    baseline_json TEXT NOT NULL CHECK(json_valid(baseline_json))
) STRICT)"},
        {"hunter_op", R"(CREATE TABLE hunter_op (
    player_id INTEGER NOT NULL REFERENCES player_save(player_id),
    op_id TEXT NOT NULL CHECK(length(op_id) > 0),
    request_json TEXT NOT NULL CHECK(json_valid(request_json)),
    result_json TEXT NOT NULL CHECK(json_valid(result_json)),
    revision INTEGER NOT NULL CHECK(revision > 0),
    hunter_id INTEGER NOT NULL CHECK(hunter_id >= 0),
    match_id INTEGER NOT NULL CHECK(match_id >= 0),
    PRIMARY KEY(player_id,op_id)
) STRICT)"},
        {"idx_hunter_active", "CREATE UNIQUE INDEX idx_hunter_active ON hunter_raid(player_id) "
            "WHERE state='active'"}
    };
    return tables;
}

HunterResult find_hunter_op(Db& db, u64 player_id, const Str& op_id,
    const Str& intent_json, usize limit)
{
    identity(player_id);
    text_value(op_id, 128);
    ensure(intent_json.size() <= max_payload, "hunter_intent_limit", Code::TooLarge);
    Stmt stmt(db, "SELECT request_json,result_json,revision,hunter_id,match_id "
        "FROM hunter_op WHERE player_id=? AND op_id=?");
    bind(stmt, {player_id, op_id});
    ensure(stmt.step(), "hunter_operation_not_found", Code::NotFound);
    const auto request = Json::parse(stmt.text(0, max_payload * 2 + 1024));
    ensure(request.at("intent") == intent_json, "hunter_operation_conflict", Code::Conflict);
    HunterResult result{static_cast<u64>(stmt.integer(2)), static_cast<u64>(stmt.integer(3)),
        static_cast<u64>(stmt.integer(4)), true, stmt.text(1, limit)};
    ensure(result.result_json.capacity() + sizeof(Rsp) + sizeof(HunterResult) <= limit,
        "hunter_result_limit", Code::TooLarge);
    return result;
}

HunterResult read_hunters(Db& db, u64 player_id, usize limit)
{
    identity(player_id);
    Txn txn(db);
    const auto profile = profile_doc(db, player_id, limit);
    HunterResult result;
    result.revision = static_cast<u64>(number(profile["revision"], 1));
    result.result_json = bounded(profile, limit);
    txn.commit();
    return result;
}

HunterResult write_hunter(Db& db, const HunterReq& req, const Str& json, usize limit)
{
    ensure(json == encode_hunter(req), "hunter_request_encoding");
#if HUNTER_STORAGE_TESTING
    test::point("before_txn", db);
#endif
    Txn txn(db);
    Stmt prior(db, "SELECT request_json,result_json,revision,hunter_id,match_id "
        "FROM hunter_op WHERE player_id=? AND op_id=?");
    bind(prior, {req.player_id, req.op_id});
    if (prior.step())
    {
        ensure(prior.text(0, max_payload * 2 + 1024) == json,
            "hunter_operation_conflict", Code::Conflict);
        HunterResult result{static_cast<u64>(prior.integer(2)),
            static_cast<u64>(prior.integer(3)), static_cast<u64>(prior.integer(4)),
            true, prior.text(1, limit)};
        ensure(result.result_json.capacity() + sizeof(Rsp) + sizeof(HunterResult) <= limit,
            "hunter_result_limit", Code::TooLarge);
        return result;
    }
    ensure(revision(db, req.player_id) == identity(req.expected_revision),
        "player_revision_conflict", Code::Revision);
    ensure(req.expected_revision < static_cast<u64>(max_value),
        "player_revision_exhausted", Code::Overflow);
    const auto value = hunter_payload(req);
    HunterResult result{req.expected_revision + 1, req.hunter_id, 0, false, {}};
    Json terminal;
    if (req.kind == "recruit")
    {
        result.hunter_id = recruit(db, req, value);
    }
    else if (req.kind == "equip")
    {
        equip(db, req, value);
    }
    else if (req.kind == "buy_skill" || req.kind == "remove_skill")
    {
        trade_skill(db, req, value);
    }
    else if (req.kind == "retire")
    {
        retire(db, req, value);
    }
    else if (req.kind == "begin_raid")
    {
        result.match_id = begin_raid(db, req, value);
    }
    else if (req.kind == "finish_raid")
    {
        result.match_id = static_cast<u64>(number(value["match_id"], 1));
        terminal = finish_raid(db, req, value, json);
    }
    else
    {
        result.match_id = static_cast<u64>(number(value["match_id"], 1));
        rollback_raid(db, req, identity(result.match_id));
    }
    run(db, "UPDATE player_save SET revision=?,updated_at_ms=? WHERE player_id=?",
        {result.revision, wall_ms(), req.player_id});
    Json document = {{"v", 2}, {"player_id", req.player_id}, {"revision", result.revision},
        {"hunter_id", result.hunter_id}, {"match_id", result.match_id}, {"kind", req.kind},
        {"profile", profile_doc(db, req.player_id, limit)}};
    if (!terminal.is_null())
    {
        document["result"] = std::move(terminal);
    }
    result.result_json = bounded(document, limit);
    run(db, "INSERT INTO hunter_op VALUES(?,?,?,?,?,?,?)", {req.player_id, req.op_id,
        json, result.result_json, result.revision, result.hunter_id, result.match_id});
#if HUNTER_STORAGE_TESTING
    test::point("in_txn", db);
#endif
    txn.commit();
#if HUNTER_STORAGE_TESTING
    test::point("after_commit", db);
#endif
    return result;
}

void recover_hunters(Db& db)
{
    Txn txn(db);
    Stmt active(db, "SELECT match_id,player_id,hunter_id FROM hunter_raid WHERE state='active'");
    Vec<Arr<i64, 3>> raids;
    while (active.step())
    {
        raids.push_back({active.integer(0), active.integer(1), active.integer(2)});
    }
    for (const auto& raid : raids)
    {
        HunterReq req;
        req.player_id = static_cast<u64>(raid[1]);
        req.hunter_id = static_cast<u64>(raid[2]);
        rollback_raid(db, req, raid[0]);
        const auto current = revision(db, req.player_id);
        run(db, "UPDATE player_save SET revision=?,updated_at_ms=? WHERE player_id=?",
            {add(current, 1), wall_ms(), req.player_id});
    }
    txn.commit();
}
}

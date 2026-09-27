// 只通过 Storage 验证永久结算与猎人经济；各后端传入独立新库配置即可复用。
#pragma once

#include "common/Types.h"
#include "StorageHost.h"

#include <nlohmann/json.hpp>

namespace hunter::storage::test
{
using Json = nlohmann::json;

// 构造确定的奖励输入；同配置的两行仍为两个独立永久物品。
inline CommitMatch req(u64 id, u64 revision = 1)
{
    return {id, 1, revision, "extracted", "content-v1", {{10003, 2}, {10003, 1}}};
}

// 验证永久物品、去重、冲突以及重启后的序列与结果精度。
inline void transaction_contract(const OpenCfg& cfg)
{
    Str saved;
    u64 first_uid = 0;
    {
        Host host;
        host.open(cfg);
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
        host.open(cfg);
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
        host.open(cfg);
        const auto player = host.load();
        const auto id = host.alloc();
        auto result = host.commit(req(id, player.revision));
        check(result.has_value(), "ten restart commits");
        check(host.load().items.size() == 2 + 2 * (i + 1), "cumulative items");
        host.stop();
    }
}

// 按当前持久版本构造猎人操作，不依赖数据库私有表。
inline HunterReq hunter_request(Host& host, const Str& op, const Str& kind,
    u64 hunter_id, const Json& value)
{
    return {1, host.hunters().revision, hunter_id, op, kind, value.dump()};
}

// 提交完整猎人操作并保留已确认的原始结果。
inline HunterResult hunter_apply(Host& host, const HunterReq& req)
{
    auto result = host.apply(req);
    check(result.has_value(), result ? "" : result.error().message);
    return std::get<HunterResult>(std::move(*result));
}

// 创建含免费技能和技能点的基准猎人。
inline HunterResult recruit_one(Host& host, const Str& op)
{
    return hunter_apply(host, hunter_request(host, op, "recruit", 0,
        {{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 8},
            {"currency_cost", 0}, {"skills", Json::array({1})}}));
}

// 从公共档案中按永久身份查找猎人。
inline Json saved_hunter(Host& host, u64 id)
{
    const auto profile = Json::parse(host.hunters().result_json);
    for (const auto& value : profile["hunters"])
    {
        if (value["hunter_id"] == id)
        {
            return value;
        }
    }
    return nullptr;
}

// 验证操作拒绝后整个可见档案及版本都不发生改变。
inline void hunter_rejects(Host& host, const HunterReq& req, Code code)
{
    const auto before = host.hunters().result_json;
    error_is(host.apply(req), code);
    check(host.hunters().result_json == before, "失败不改变档案和版本");
}

// 通过公共结算入口创建经济测试需要的历史物品。
inline MatchResult seed_items(Host& host)
{
    const CommitMatch req{host.alloc(), 1, host.hunters().revision, "Extracted",
        "demo-v3:old", {{100, 3}, {101, 1}}};
    auto result = host.commit(req);
    check(result.has_value(), result ? "" : result.error().message);
    return std::get<MatchResult>(std::move(*result));
}

// 验证技能实际支付、操作持久幂等、完整配装原子性和退休归仓。
inline void economy_contract(const OpenCfg& cfg)
{
    Host host;
    host.open(cfg);
    const auto old = seed_items(host);
    const auto item = Json::parse(old.result_json)["items"][0]["item_uid"];
    auto recruit = hunter_request(host, "recruit", "recruit", 0,
        {{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 8},
            {"currency_cost", 0}, {"skills", Json::array({1})}});
    recruit.intent_json = R"({"cfg_id":2})";
    const auto created = hunter_apply(host, recruit);
    const auto again = hunter_apply(host, recruit);
    check(again.replayed && again.result_json == created.result_json, "招募同操作精确重放");
    auto changed = recruit;
    changed.payload_json = Json({{"cfg_id", 3}, {"level", 1}, {"xp", 0}, {"points", 8},
        {"currency_cost", 0}, {"skills", Json::array({1})}}).dump();
    hunter_rejects(host, changed, Code::Conflict);
    const auto id = created.hunter_id;
    hunter_apply(host, hunter_request(host, "buy", "buy_skill", id, {{"cfg_id", 2}, {"cost", 3}}));
    hunter_rejects(host, hunter_request(host, "double-buy", "buy_skill", id,
        {{"cfg_id", 2}, {"cost", 3}}), Code::Conflict);
    hunter_apply(host, hunter_request(host, "remove", "remove_skill", id,
        {{"cfg_id", 2}, {"refund", 1}}));
    check(saved_hunter(host, id)["points"] == 6, "按实际支付退点");
    hunter_rejects(host, hunter_request(host, "free-refund", "remove_skill", id,
        {{"cfg_id", 1}, {"refund", 1}}), Code::Invalid);
    hunter_apply(host, hunter_request(host, "buy-one", "buy_skill", id,
        {{"cfg_id", 2}, {"cost", 1}}));
    hunter_apply(host, hunter_request(host, "refund-one", "remove_skill", id,
        {{"cfg_id", 2}, {"refund", 1}}));
    check(saved_hunter(host, id)["points"] == 6, "支付一点可退一点");
    hunter_apply(host, hunter_request(host, "equip", "equip", id,
        {{"items", Json::array({{{"slot", 1}, {"item_uid", item}}})}}));
    check(host.load().items.size() == 1, "已装备物品不出现在可用仓库");
    hunter_rejects(host, hunter_request(host, "bad-equip", "equip", id, {{"items", Json::array({
        {{"slot", 1}, {"item_uid", item}}, {{"slot", 2}, {"item_uid", 9999}}})}}),
        Code::NotFound);
    const auto second = recruit_one(host, "second");
    hunter_rejects(host, hunter_request(host, "stolen", "equip", second.hunter_id,
        {{"items", Json::array({{{"slot", 1}, {"item_uid", item}}})}}), Code::Busy);
    hunter_rejects(host, hunter_request(host, "early-retire", "retire", id,
        {{"max_level", 2}, {"account_xp", 10}}), Code::Invalid);
    const auto retired = hunter_apply(host, hunter_request(host, "retire", "retire", id,
        {{"max_level", 1}, {"account_xp", 9007199254740999ULL}}));
    check(saved_hunter(host, id).is_null() && host.load().items.size() == 2,
        "退役后猎人消失而装备归仓");
    const auto account_xp = Json::parse(retired.result_json)["profile"]["account_xp"].get<u64>();
    check(account_xp == 9007199254740999ULL, "退役经验同事务精确增加");
    const auto found = host.find_op(recruit);
    check(found && std::get<HunterResult>(*found).result_json == created.result_json,
        "人物退役后先按原始意图返回成功响应");
    auto intent_conflict = recruit;
    intent_conflict.intent_json = R"({"cfg_id":3})";
    error_is(host.find_op(intent_conflict), Code::Conflict);
    auto excessive = hunter_request(host, "oversized-intent", "retire", second.hunter_id,
        {{"max_level", 1}, {"account_xp", 0}});
    excessive.intent_json.assign(262145, 'x');
    hunter_rejects(host, excessive, Code::TooLarge);
    auto invalid = hunter_request(host, "missing-cost", "recruit", 0,
        {{"cfg_id", 2}, {"level", 1}, {"xp", 0}, {"points", 8}, {"skills", Json::array()}});
    hunter_rejects(host, invalid, Code::Invalid);
    auto stale = hunter_request(host, "stale-revision", "buy_skill", second.hunter_id,
        {{"cfg_id", 2}, {"cost", 1}});
    --stale.expected_revision;
    hunter_rejects(host, stale, Code::Revision);
    const auto saved = host.hunters();
    host.stop();
    Host reopened;
    reopened.open(cfg);
    check(reopened.hunters().result_json == saved.result_json, "重启保留完整猎人档案原文");
    const auto replay = reopened.apply(recruit);
    check(replay && std::get<HunterResult>(*replay).replayed
        && std::get<HunterResult>(*replay).result_json == created.result_json,
        "重启后保留原始操作结果并且不重复招募");
    reopened.stop();
}

// 为死亡或放弃构造显式零奖励终局，撤离用例再覆盖所需字段。
inline Json raid_result(u64 match, const Str& outcome)
{
    return {{"match_id", match}, {"outcome", outcome}, {"content_key", "hunt-v5:test"},
        {"level", 0}, {"xp", 0}, {"points", 0}, {"account_xp", 0}, {"currency_gain", 0},
        {"skills", Json::array()}, {"equipment", Json::array()}, {"items", Json::array()}};
}

// 验证出战、显式回退和终局原子资产，不依赖 SQLite 的全库启动恢复策略。
inline void raid_contract(const OpenCfg& cfg)
{
    Host host;
    host.open(cfg);
    const auto old = seed_items(host);
    const auto item = Json::parse(old.result_json)["items"][0]["item_uid"].get<u64>();
    const auto id = recruit_one(host, "recruit").hunter_id;
    hunter_apply(host, hunter_request(host, "paid", "buy_skill", id,
        {{"cfg_id", 2}, {"cost", 3}}));
    hunter_apply(host, hunter_request(host, "equip", "equip", id,
        {{"items", Json::array({{{"slot", 1}, {"item_uid", item}}})}}));
    const auto before = saved_hunter(host, id);
    const auto begin = hunter_request(host, "begin", "begin_raid", id,
        {{"content_key", "hunt-v5:test"}});
    const auto first = hunter_apply(host, begin);
    const auto begin_replay = hunter_apply(host, begin);
    check(begin_replay.replayed && begin_replay.result_json == first.result_json,
        "出战重试不分配新局号");
    check(saved_hunter(host, id)["state"] == "in_raid", "出战占用猎人");
    hunter_rejects(host, hunter_request(host, "busy", "remove_skill", id,
        {{"cfg_id", 2}, {"refund", 1}}), Code::Busy);
    hunter_apply(host, hunter_request(host, "recover", "recover_raid", id,
        {{"match_id", first.match_id}}));
    check(saved_hunter(host, id) == before, "显式回退保留全部出战前资产");
    error_is(host.find(first.match_id), Code::NotFound);
    const auto started = hunter_apply(host, hunter_request(host, "again", "begin_raid", id,
        {{"content_key", "hunt-v5:test"}}));
    check(started.match_id > first.match_id, "回退不复用局号");
    auto final = raid_result(started.match_id, "Extracted");
    final["level"] = 2;
    final["xp"] = 10;
    final["points"] = 6;
    final["skills"] = Json::array({{{"cfg_id", 1}, {"paid_cost", 0}, {"source", "recruit"}},
        {{"cfg_id", 7}, {"paid_cost", 0}, {"source", "loot"}}});
    final["equipment"] = Json::array({{{"item_uid", item}, {"count", 2}}});
    final["items"] = Json::array({{{"cfg_id", 100}, {"count", 2}}});
    final["currency_gain"] = 5;
    const auto finish = hunter_request(host, "extract", "finish_raid", id, final);
    auto forged = finish;
    auto excess = final;
    excess["equipment"][0]["count"] = 4;
    forged.op_id = "forged-equipment";
    forged.payload_json = excess.dump();
    hunter_rejects(host, forged, Code::Invalid);
    error_is(host.find(started.match_id), Code::NotFound);
    const auto committed = hunter_apply(host, finish);
    const auto found = host.find(started.match_id);
    check(found && std::get<MatchResult>(*found).result_json
        == Json::parse(committed.result_json)["result"].dump(), "终局与旧结算查询结果一致");
    const auto saved = saved_hunter(host, id);
    check(saved["state"] == "ready" && saved["skills"].size() == 2 && saved["level"] == 2
        && saved["equipment"][0]["count"] == 2, "撤离同时保存成长、技能与消耗后装备");
    check(host.load().items.size() == 2
        && Json::parse(host.hunters().result_json)["currency"] == 5,
        "撤离奖励和仓库同时提交");
    hunter_rejects(host, hunter_request(host, "loot-refund", "remove_skill", id,
        {{"cfg_id", 7}, {"refund", 1}}), Code::Invalid);
    host.stop();

    Host reopened;
    reopened.open(cfg);
    const auto replay = hunter_apply(reopened, finish);
    check(replay.replayed && replay.result_json == committed.result_json
        && saved_hunter(reopened, id) == saved, "重启保持终局原文及已提交资产");
    const auto death = hunter_apply(reopened, hunter_request(reopened, "last", "begin_raid", id,
        {{"content_key", "hunt-v5:test"}}));
    const auto dead = hunter_request(reopened, "death", "finish_raid", id,
        raid_result(death.match_id, "Dead"));
    const auto ended = hunter_apply(reopened, dead);
    check(saved_hunter(reopened, id).is_null() && reopened.load().items.size() == 2,
        "死亡删除携带资产且保留仓库");
    const auto dead_replay = hunter_apply(reopened, dead);
    check(dead_replay.replayed && dead_replay.result_json == ended.result_json,
        "已删除猎人的终局仍可幂等重放");
    reopened.stop();
    Host after;
    after.open(cfg);
    check(saved_hunter(after, id).is_null(), "重启不恢复已提交死亡猎人");
    const auto dead_found = after.find_op(dead);
    check(dead_found && std::get<HunterResult>(*dead_found).result_json == ended.result_json,
        "重启可查询死亡终局原文");
    after.stop();
}

}

// 实现永久状态事务和规范化结算，数据库不裁决玩法奖励。
#include "common/Types.h"
#include "storage/Store.h"
#include "storage/Schema.h"

#include <nlohmann/json.hpp>
#include <sqlite3.h>

#include <limits>

#if HUNTER_STORAGE_TESTING
#include "StorageHooks.h"
#endif

namespace hunter::storage
{
namespace
{

using Json = nlohmann::json;
constexpr u64 max_id = static_cast<u64>(std::numeric_limits<i64>::max());

// 检查持久整数边界，避免转换到 SQLite 时符号回绕。
void valid_id(u64 value)
{
    if (value == 0 || value > max_id)
    {
        fail(Code::Invalid, "persistent_id_range");
    }
}

// 检查持久文本最小约束，UTF-8 由规范 JSON 编码器进一步验证。
void valid_text(const Str& value)
{
    if (value.empty() || value.find('\0') != Str::npos)
    {
        fail(Code::Invalid, "persistent_text_empty_or_nul");
    }
}

// 校验存档正数列，拒绝把损坏负数转换成巨大无符号值。
u64 positive(i64 value)
{
    if (value <= 0)
    {
        fail(Code::Corrupt, "invalid_positive_column");
    }

    return static_cast<u64>(value);
}

// 在产生副作用前或读取时检查完成数据总量。
void fit(usize bytes, usize limit)
{
    if (bytes > limit)
    {
        fail(Code::TooLarge, "storage_result_limit");
    }
}

// 从结果行读取固定列，JSON 中的 UID 等原始结果不重新计算。
MatchResult match_row(Stmt& stmt, usize limit)
{
    fit(sizeof(Rsp) + sizeof(MatchResult), limit);
    MatchResult result;
    result.match_id = positive(stmt.integer(0));
    result.revision = positive(stmt.integer(1));
    result.result_json = stmt.text(2, limit - sizeof(Rsp) - sizeof(MatchResult));
    fit(sizeof(Rsp) + sizeof(MatchResult) + result.result_json.capacity(), limit);
    return result;
}

// 插入永久物品并构造带 UID 的确认结果，容量不足时让外层事务回滚。
Str award(Db& db, const CommitMatch& req, i64 now, usize limit)
{
    Json items = Json::array();
    usize item_bytes = 2;

    for (const auto& item : req.items)
    {
        Stmt insert(db, "INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) "
            "VALUES(?,?,?,?)");
        insert.bind(1, static_cast<i64>(req.player_id));
        insert.bind(2, static_cast<i64>(item.cfg_id));
        insert.bind(3, item.count);
        insert.bind(4, static_cast<i64>(req.match_id));
        insert.run();
        const auto uid = positive(sqlite3_last_insert_rowid(db.handle()));
        Json value = {{"item_uid", uid}, {"cfg_id", item.cfg_id}, {"count", item.count},
            {"acquired_match_id", req.match_id}};
        item_bytes += value.dump().size() + 1;
        fit(item_bytes, limit);
        items.push_back(std::move(value));
    }

    Json result = {{"v", 1}, {"match_id", req.match_id}, {"player_id", req.player_id},
        {"outcome", req.outcome}, {"content_key", req.content_key},
        {"revision_before", req.expected_revision},
        {"revision_after", req.expected_revision + 1}, {"committed_at_ms", now},
        {"items", std::move(items)}};
    auto json = result.dump();
    fit(sizeof(Rsp) + sizeof(MatchResult) + json.capacity(), limit);
    return json;
}

}

Str encode_req(const CommitMatch& req)
{
    valid_id(req.match_id);
    valid_id(req.player_id);
    valid_id(req.expected_revision);
    valid_text(req.outcome);
    valid_text(req.content_key);
    Json items = Json::array();

    for (const auto& item : req.items)
    {
        if (item.cfg_id == 0 || item.count <= 0)
        {
            fail(Code::Invalid, "invalid_item_delta");
        }

        items.push_back({{"cfg_id", item.cfg_id}, {"count", item.count}});
    }

    try
    {
        return Json({{"v", 1}, {"match_id", req.match_id}, {"player_id", req.player_id},
            {"expected_revision", req.expected_revision}, {"outcome", req.outcome},
            {"content_key", req.content_key}, {"items", std::move(items)}}).dump();
    }
    catch (const Json::exception&)
    {
        fail(Code::Invalid, "invalid_request_utf8");
    }
}

PlayerSave read_player(Db& db, u64 player_id, usize limit)
{
    valid_id(player_id);
    fit(sizeof(Rsp) + sizeof(PlayerSave), limit);
    Txn txn(db);
    PlayerSave result;
    Stmt player(db, "SELECT player_id,revision,last_match_id FROM player_save WHERE player_id=?");
    player.bind(1, static_cast<i64>(player_id));

    if (!player.step())
    {
        fail(Code::NotFound, "player_not_found");
    }

    result.player_id = positive(player.integer(0));
    result.revision = positive(player.integer(1));
    const auto last = player.integer(2);
    if (last < 0)
    {
        fail(Code::Corrupt, "invalid_last_match");
    }

    result.last_match_id = static_cast<u64>(last);
    Stmt items(db, "SELECT item_uid,cfg_id,count,acquired_match_id FROM player_item "
        "WHERE player_id=? ORDER BY item_uid");
    items.bind(1, static_cast<i64>(player_id));
    const usize max_items = (limit - sizeof(Rsp) - sizeof(PlayerSave)) / sizeof(ItemSave);

    while (items.step())
    {
        if (result.items.size() == max_items)
        {
            fail(Code::TooLarge, "player_items_limit");
        }

        const auto cfg = positive(items.integer(1));
        const auto count = positive(items.integer(2));
        if (cfg > std::numeric_limits<u32>::max() || count > std::numeric_limits<i32>::max())
        {
            fail(Code::Corrupt, "invalid_item_column");
        }

        if (result.items.size() == result.items.capacity())
        {
            const auto capacity = std::min(max_items,
                std::max<usize>(1, result.items.capacity() * 2));
            result.items.reserve(capacity);
            fit(sizeof(Rsp) + sizeof(PlayerSave) +
                result.items.capacity() * sizeof(ItemSave), limit);
        }

        result.items.push_back({positive(items.integer(0)), static_cast<u32>(cfg),
            static_cast<i32>(count), positive(items.integer(3))});
    }

    txn.commit();
    return result;
}

MatchId next_match(Db& db)
{
    Txn txn(db);
    const auto value = db.scalar("SELECT int_value FROM save_meta WHERE key='next_match_id'");
    if (value <= 0)
    {
        fail(Code::Corrupt, "invalid_match_sequence");
    }

    if (value == std::numeric_limits<i64>::max())
    {
        fail(Code::Overflow, "match_sequence_exhausted");
    }

    db.exec("UPDATE save_meta SET int_value=int_value+1 WHERE key='next_match_id'");
    txn.commit();
    return {static_cast<u64>(value)};
}

MatchResult read_match(Db& db, u64 match_id, usize limit)
{
    valid_id(match_id);
    Stmt stmt(db, "SELECT match_id,revision_after,result_json FROM match_result WHERE match_id=?");
    stmt.bind(1, static_cast<i64>(match_id));

    if (!stmt.step())
    {
        fail(Code::NotFound, "match_not_found");
    }

    return match_row(stmt, limit);
}

MatchResult write_match(Db& db, const CommitMatch& req, const Str& json, usize limit)
{
#if HUNTER_STORAGE_TESTING
    test::point("before_txn", db);
#endif
    Txn txn(db);
    {
        Stmt prior(db, "SELECT request_json=? FROM match_result WHERE match_id=?");
        prior.bind(1, json);
        prior.bind(2, static_cast<i64>(req.match_id));

        if (prior.step())
        {
            if (prior.integer(0) != 1)
            {
                fail(Code::Conflict, "match_request_conflict");
            }

            auto result = read_match(db, req.match_id, limit);
            result.replayed = true;
            return result;
        }
    }

    const auto next = db.scalar("SELECT int_value FROM save_meta WHERE key='next_match_id'");
    if (next <= 0 || req.match_id >= static_cast<u64>(next))
    {
        fail(Code::Invalid, "match_id_not_allocated");
    }

    {
        Stmt player(db, "SELECT revision FROM player_save WHERE player_id=?");
        player.bind(1, static_cast<i64>(req.player_id));

        if (!player.step())
        {
            fail(Code::NotFound, "player_not_found");
        }

        if (positive(player.integer(0)) != req.expected_revision)
        {
            fail(Code::Revision, "player_revision_conflict");
        }
    }

    if (req.expected_revision == max_id)
    {
        fail(Code::Overflow, "player_revision_exhausted");
    }

    const auto now = wall_ms();
    MatchResult result{req.match_id, req.expected_revision + 1, false,
        award(db, req, now, limit)};
    Stmt player(db, "UPDATE player_save SET revision=?,last_match_id=?,updated_at_ms=? "
        "WHERE player_id=?");
    player.bind(1, static_cast<i64>(result.revision));
    player.bind(2, static_cast<i64>(req.match_id));
    player.bind(3, now);
    player.bind(4, static_cast<i64>(req.player_id));
    player.run();
    Stmt record(db, "INSERT INTO match_result VALUES(?,?,?,?,?,?,?,?,?)");
    record.bind(1, static_cast<i64>(req.match_id));
    record.bind(2, static_cast<i64>(req.player_id));
    record.bind(3, req.outcome);
    record.bind(4, req.content_key);
    record.bind(5, json);
    record.bind(6, result.result_json);
    record.bind(7, static_cast<i64>(req.expected_revision));
    record.bind(8, static_cast<i64>(result.revision));
    record.bind(9, now);
    record.run();
#if HUNTER_STORAGE_TESTING
    test::point("in_txn", db);
#endif
    txn.commit();
#if HUNTER_STORAGE_TESTING
    test::point("after_commit", db);
#endif
    return result;
}

}

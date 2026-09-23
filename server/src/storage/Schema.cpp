// 冻结 V1 表、约束和初始化事务，已有异常文件只诊断不重置。
#include "common/Types.h"
#include "storage/Schema.h"

#include <chrono>

namespace hunter::storage
{
namespace
{

const Map<Str, Str> tables = {
    {"save_meta", R"(CREATE TABLE save_meta (
    key TEXT PRIMARY KEY,
    int_value INTEGER,
    text_value TEXT
) STRICT)"},
    {"player_save", R"(CREATE TABLE player_save (
    player_id INTEGER PRIMARY KEY CHECK(player_id > 0),
    revision INTEGER NOT NULL CHECK(revision > 0),
    last_match_id INTEGER NOT NULL DEFAULT 0 CHECK(last_match_id >= 0),
    created_at_ms INTEGER NOT NULL CHECK(created_at_ms >= 0),
    updated_at_ms INTEGER NOT NULL CHECK(updated_at_ms >= 0)
) STRICT)"},
    {"player_item", R"(CREATE TABLE player_item (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT CHECK(item_uid > 0),
    player_id INTEGER NOT NULL REFERENCES player_save(player_id),
    cfg_id INTEGER NOT NULL CHECK(cfg_id > 0 AND cfg_id <= 4294967295),
    count INTEGER NOT NULL CHECK(count > 0 AND count <= 2147483647),
    acquired_match_id INTEGER NOT NULL REFERENCES match_result(match_id)
        DEFERRABLE INITIALLY DEFERRED
) STRICT)"},
    {"match_result", R"(CREATE TABLE match_result (
    match_id INTEGER PRIMARY KEY CHECK(match_id > 0),
    player_id INTEGER NOT NULL REFERENCES player_save(player_id),
    outcome TEXT NOT NULL CHECK(length(outcome) > 0),
    content_key TEXT NOT NULL CHECK(length(content_key) > 0),
    request_json TEXT NOT NULL CHECK(json_valid(request_json)),
    result_json TEXT NOT NULL CHECK(json_valid(result_json)),
    revision_before INTEGER NOT NULL CHECK(revision_before > 0),
    revision_after INTEGER NOT NULL CHECK(revision_after = revision_before + 1),
    committed_at_ms INTEGER NOT NULL CHECK(committed_at_ms >= 0)
) STRICT)"},
    {"idx_player_item_player",
        "CREATE INDEX idx_player_item_player ON player_item(player_id)"}
};

// 校验完整 DDL，避免同列名但缺少约束或额外触发器的库被当作 V1。
void check_schema(Db& db)
{
    Stmt schema(db, "SELECT name, sql FROM sqlite_schema "
        "WHERE name NOT GLOB 'sqlite_*' ORDER BY name");
    usize count = 0;

    while (schema.step())
    {
        const auto name = schema.text(0, 128);
        const auto found = tables.find(name);
        if (found == tables.end() || schema.text(1, 8192) != found->second)
        {
            fail(Code::Corrupt, "schema_v1_mismatch");
        }

        ++count;
    }

    if (count != tables.size())
    {
        fail(Code::Corrupt, "schema_v1_incomplete");
    }

    Stmt foreign(db, "PRAGMA foreign_key_check");
    if (foreign.step())
    {
        fail(Code::Corrupt, "foreign_key_check_failed");
    }

    const auto next = db.scalar("SELECT int_value FROM save_meta WHERE key='next_match_id'");
    if (next <= 0 || next <= db.scalar("SELECT coalesce(max(match_id),0) FROM match_result") ||
        db.scalar("SELECT count(*) FROM player_save WHERE player_id=1") != 1)
    {
        fail(Code::Corrupt, "invalid_save_metadata");
    }
}

// 初始化元数据及玩家和 schema 版本，任何一步失败都回滚。
void create_schema(Db& db)
{
    for (const auto& [name, sql] : tables)
    {
        if (name != "idx_player_item_player")
        {
            db.exec(sql.c_str());
        }
    }

    db.exec(tables.at("idx_player_item_player").c_str());
    db.exec("INSERT INTO save_meta(key,int_value) VALUES('next_match_id',1)");
    Stmt player(db, "INSERT INTO player_save VALUES(1,1,0,?,?)");
    const auto now = wall_ms();
    player.bind(1, now);
    player.bind(2, now);
    player.run();
    db.exec("PRAGMA user_version=1");
}

// 先检查既有文件，拒绝损坏或陌生 schema 时尚未切换持久 journal 模式。
void inspect(Db& db)
{
    const auto version = db.scalar("PRAGMA user_version");
    if (version != 0 && version != 1)
    {
        fail(Code::Version, "unsupported_save_version");
    }

    Stmt quick(db, "PRAGMA quick_check");
    if (!quick.step() || quick.text(0, 512) != "ok" || quick.step())
    {
        fail(Code::Corrupt, "quick_check_failed");
    }

    if (version == 0)
    {
        if (db.scalar("SELECT count(*) FROM sqlite_schema") != 0)
        {
            fail(Code::Version, "nonempty_unversioned_database");
        }
    }
    else
    {
        check_schema(db);
    }
}

}

i64 wall_ms()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
}

void open_schema(Db& db)
{
    inspect(db);
    db.exec("PRAGMA foreign_keys=ON");
    db.exec("PRAGMA synchronous=FULL");
    {
        Stmt wal(db, "PRAGMA journal_mode=WAL");
        if (!wal.step() || wal.text(0, 32) != "wal" || wal.step())
        {
            fail(Code::Open, "wal_unavailable");
        }
    }

    if (db.scalar("PRAGMA foreign_keys") != 1 || db.scalar("PRAGMA synchronous") != 2 ||
        db.scalar("PRAGMA busy_timeout") != 2000)
    {
        fail(Code::Open, "sqlite_settings_mismatch");
    }

    Txn txn(db);
    inspect(db);

    if (db.scalar("PRAGMA user_version") == 0)
    {
        create_schema(db);
    }

    check_schema(db);
    txn.commit();
}

}

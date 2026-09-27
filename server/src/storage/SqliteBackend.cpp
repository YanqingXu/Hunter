// 适配现有 SQLite 业务实现，迁移和恢复不进入公共门面。
#include "common/Types.h"
#include "storage/SqliteBackend.h"
#include "storage/Db.h"
#include "storage/Schema.h"
#include "storage/Store.h"
#include "storage/HunterStore.h"

namespace hunter::storage
{

SqliteBackend::SqliteBackend() = default;

SqliteBackend::~SqliteBackend() = default;

void SqliteBackend::open(const OpenCfg& cfg)
{
    if (cfg.backend != "sqlite")
    {
        fail(Code::Unsupported, "storage_backend_unsupported");
    }

    if (db_)
    {
        fail(Code::Invalid, "storage_already_opened");
    }

    if (cfg.path.empty() || cfg.path.find('\0') != Str::npos)
    {
        fail(Code::Invalid, "invalid_database_path");
    }

    auto candidate = std::make_unique<Db>(cfg.path);
    open_schema(*candidate);
    recover_hunters(*candidate);
    db_ = std::move(candidate);
}

void SqliteBackend::close()
{
    if (db_)
    {
        db_->close();
        db_.reset();
    }
}

Db& SqliteBackend::db()
{
    if (!db_)
    {
        fail(Code::NotReady, "storage_connection_unavailable");
    }

    return *db_;
}

PlayerSave SqliteBackend::load_player(u64 player_id, usize limit)
{
    return read_player(db(), player_id, limit);
}

MatchId SqliteBackend::alloc_match()
{
    return next_match(db());
}

MatchResult SqliteBackend::commit_match(const CommitMatch& req, const Str& json, usize limit)
{
    return write_match(db(), req, json, limit);
}

MatchResult SqliteBackend::find_match(u64 match_id, usize limit)
{
    return read_match(db(), match_id, limit);
}

HunterResult SqliteBackend::load_hunters(u64 player_id, usize limit)
{
    return read_hunters(db(), player_id, limit);
}

HunterResult SqliteBackend::apply_hunter(const HunterReq& req, const Str& json, usize limit)
{
    return write_hunter(db(), req, json, limit);
}

HunterResult SqliteBackend::find_hunter_op(u64 player_id, const Str& op_id,
    const Str& intent_json, usize limit)
{
    return storage::find_hunter_op(db(), player_id, op_id, intent_json, limit);
}

}

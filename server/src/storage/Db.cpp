// 实现工作线程内 SQLite 资源管理和明确的事务失败边界。
#include "common/Types.h"
#include "storage/Db.h"

#include <sqlite3.h>

#include <limits>

namespace hunter::storage
{

void fail(Code code, const Str& message)
{
    throw Error{code, 0, false, message.substr(0, 512)};
}

Db::Db(const Str& path)
{
    const auto rc = sqlite3_open_v2(path.c_str(), &db_,
        SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_NOMUTEX, nullptr);

    try
    {
        check(rc);
        check(sqlite3_extended_result_codes(db_, 1));
        check(sqlite3_busy_timeout(db_, 2000));
    }
    catch (...)
    {
        sqlite3_close_v2(db_);
        db_ = nullptr;
        throw;
    }
}

Db::~Db()
{
    if (db_)
    {
        sqlite3_close_v2(handle());
    }
}

sqlite3* Db::handle() const
{
    if (owner_ != std::this_thread::get_id())
    {
        fail(Code::WrongThread, "sqlite_owner_thread");
    }

    if (!db_)
    {
        fail(Code::Closed, "sqlite_connection_closed");
    }

    return db_;
}

void Db::check(i32 rc) const
{
    if (rc == SQLITE_OK)
    {
        return;
    }

    Code code = Code::Internal;

    switch (rc & 0xff)
    {
    case SQLITE_BUSY:
    case SQLITE_LOCKED:
        code = Code::Busy;
        break;
    case SQLITE_CORRUPT:
    case SQLITE_NOTADB:
        code = Code::Corrupt;
        break;
    case SQLITE_CANTOPEN:
        code = Code::Open;
        break;
    case SQLITE_FULL:
    case SQLITE_IOERR:
    case SQLITE_READONLY:
    case SQLITE_PERM:
        code = Code::Write;
        break;
    case SQLITE_TOOBIG:
        code = Code::TooLarge;
        break;
    case SQLITE_CONSTRAINT:
    case SQLITE_RANGE:
        code = Code::Invalid;
        break;
    default:
        break;
    }

    const Str message = db_ ? sqlite3_errmsg(db_) : sqlite3_errstr(rc);
    throw Error{code, rc, false, message.substr(0, 512)};
}

void Db::exec(const char* sql)
{
    check(sqlite3_exec(handle(), sql, nullptr, nullptr, nullptr));
}

i64 Db::scalar(const char* sql)
{
    Stmt stmt(*this, sql);
    if (!stmt.step())
    {
        fail(Code::Corrupt, "missing_scalar");
    }

    const auto value = stmt.integer(0);
    if (stmt.step())
    {
        fail(Code::Corrupt, "duplicate_scalar");
    }

    return value;
}

void Db::close()
{
    check(sqlite3_close(handle()));
    db_ = nullptr;
}

void Db::rollback() noexcept
{
    if (db_ && !sqlite3_get_autocommit(db_) &&
        sqlite3_exec(db_, "ROLLBACK", nullptr, nullptr, nullptr) != SQLITE_OK)
    {
        sqlite3_close_v2(db_);
        db_ = nullptr;
    }
}

Stmt::Stmt(Db& db, const char* sql) : db_(db)
{
    const auto rc = sqlite3_prepare_v2(db_.handle(), sql, -1, &stmt_, nullptr);
    if (rc != SQLITE_OK)
    {
        sqlite3_finalize(stmt_);
        stmt_ = nullptr;
        db_.check(rc);
    }
}

Stmt::~Stmt()
{
    static_cast<void>(db_.handle());
    sqlite3_finalize(stmt_);
}

void Stmt::bind(i32 pos, i64 value)
{
    static_cast<void>(db_.handle());
    db_.check(sqlite3_bind_int64(stmt_, pos, value));
}

void Stmt::bind(i32 pos, const Str& value)
{
    static_cast<void>(db_.handle());

    if (value.size() > static_cast<usize>(std::numeric_limits<i32>::max()))
    {
        fail(Code::TooLarge, "sqlite_text_limit");
    }

    db_.check(sqlite3_bind_text(stmt_, pos, value.data(),
        static_cast<i32>(value.size()), SQLITE_TRANSIENT));
}

bool Stmt::step()
{
    static_cast<void>(db_.handle());
    const auto rc = sqlite3_step(stmt_);
    if (rc == SQLITE_ROW)
    {
        return true;
    }

    if (rc == SQLITE_DONE)
    {
        return false;
    }

    db_.check(rc);
    return false;
}

void Stmt::run()
{
    if (step())
    {
        fail(Code::Internal, "unexpected_statement_row");
    }
}

i64 Stmt::integer(i32 col) const
{
    static_cast<void>(db_.handle());

    if (sqlite3_column_type(stmt_, col) != SQLITE_INTEGER)
    {
        fail(Code::Corrupt, "expected_integer_column");
    }

    return sqlite3_column_int64(stmt_, col);
}

Str Stmt::text(i32 col, usize max_bytes) const
{
    static_cast<void>(db_.handle());

    if (sqlite3_column_type(stmt_, col) != SQLITE_TEXT)
    {
        fail(Code::Corrupt, "expected_text_column");
    }

    const auto* value = sqlite3_column_text(stmt_, col);
    const auto bytes = sqlite3_column_bytes(stmt_, col);

    if (!value)
    {
        db_.check(SQLITE_NOMEM);
    }

    if (static_cast<usize>(bytes) > max_bytes)
    {
        fail(Code::TooLarge, "stored_text_limit");
    }

    return Str(reinterpret_cast<const char*>(value), static_cast<usize>(bytes));
}

Txn::Txn(Db& db) : db_(db)
{
    db_.exec("BEGIN IMMEDIATE");
}

Txn::~Txn()
{
    if (!committed_)
    {
        db_.rollback();
    }
}

void Txn::commit()
{
    try
    {
        db_.exec("COMMIT");
        committed_ = true;
    }
    catch (Error& error)
    {
        error.commit_unknown = error.code == Code::Write || error.code == Code::Internal;
        throw;
    }
}

}

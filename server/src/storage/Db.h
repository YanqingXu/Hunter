// 将 SQLite 连接、语句和事务限制在创建线程；所有失败转换为拥有型 Error。
#pragma once

#include "common/Types.h"
#include "storage/Model.h"

#include <thread>

struct sqlite3;
struct sqlite3_stmt;

namespace hunter::storage
{

// 抛出模块内部错误，由异步边界转换为完成结果。
[[noreturn]] void fail(Code code, const Str& message);

class Db
{
public:
    // 在当前工作线程打开文件；构造失败时也关闭已创建连接。
    explicit Db(const Str& path);

    // 在创建线程释放连接，不执行任何保存业务。
    ~Db();

    // 数据库连接不可复制。
    Db(const Db&) = delete;

    // 数据库连接不可复制赋值。
    Db& operator=(const Db&) = delete;

    // 检查拥有线程并返回仅用于当前调用的连接。
    sqlite3* handle() const;

    // 执行可信常量 SQL；业务参数必须通过 Stmt 绑定。
    void exec(const char* sql);

    // 查询恰好一个整数结果，拒绝非整数或缺少结果。
    i64 scalar(const char* sql);

    // 把 SQLite 错误保留为有限诊断及扩展错误码。
    void check(i32 rc) const;

    // 在拥有线程显式关闭连接并报告资源未释放等错误。
    void close();

    // 异常清理时回滚；回滚失败则关闭连接，禁止继续使用不确定事务。
    void rollback() noexcept;

private:
    sqlite3* db_ = nullptr;
    std::thread::id owner_ = std::this_thread::get_id();
};

class Stmt
{
public:
    // 在连接拥有线程准备单条可信 SQL。
    Stmt(Db& db, const char* sql);

    // 在拥有线程释放语句。
    ~Stmt();

    // 语句不可复制。
    Stmt(const Stmt&) = delete;

    // 语句不可复制赋值。
    Stmt& operator=(const Stmt&) = delete;

    // 绑定有符号整数；领域无符号值须预先检查范围。
    void bind(i32 pos, i64 value);

    // 复制字符串到 SQLite，不跨步进借用调用方内存。
    void bind(i32 pos, const Str& value);

    // 前进到下一行，结束返回 false，其他状态抛出 Error。
    bool step();

    // 执行无返回行的写语句。
    void run();

    // 读取当前行整数，类型不符视为数据库损坏。
    i64 integer(i32 col) const;

    // 在分配前限制文本长度，类型不符或超限明确失败。
    Str text(i32 col, usize max_bytes) const;

private:
    Db& db_;
    sqlite3_stmt* stmt_ = nullptr;
};

class Txn
{
public:
    // 使用 BEGIN IMMEDIATE 获得事务写锁。
    explicit Txn(Db& db);

    // 未提交事务在异常路径自动回滚。
    ~Txn();

    // 事务所有权不可复制。
    Txn(const Txn&) = delete;

    // 事务所有权不可复制赋值。
    Txn& operator=(const Txn&) = delete;

    // COMMIT 成功后才标记提交；异常标明提交状态可能未知。
    void commit();

private:
    Db& db_;
    bool committed_ = false;
};

}

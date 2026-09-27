// 提供不依赖具体数据库的异步存储测试宿主、错误断言及独占临时目录。
#pragma once

#include "common/Types.h"
#include "storage/Backend.h"
#include "storage/Storage.h"

#include <chrono>
#include <filesystem>
#include <stdexcept>
#include <thread>

namespace hunter::storage::test
{

// 检查公开行为，保留可定位的失败原因。
inline void check(bool ok, const Str& msg)
{
    if (!ok)
    {
        throw std::runtime_error(msg);
    }
}

// 将临时目录转换为 SQLite 接受的 UTF-8 文件路径。
inline Str utf8(const std::filesystem::path& path)
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
    explicit Host(StorageCfg cfg = {}, u64 instance = 17, BackendFactory factory = {})
        : store(factory ? std::make_unique<Storage>(io, cfg, instance, std::move(factory))
            : std::make_unique<Storage>(io, cfg, instance))
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
    Expect<Value, Error> call(F submit)
    {
        auto result = std::make_shared<Opt<Rsp>>();
        const auto owner = std::this_thread::get_id();
        auto ticket = submit([result, owner](Rsp rsp)
        {
            check(owner == std::this_thread::get_id(), "callback owning thread");
            check(!result->has_value(), "one terminal completion");
            *result = std::move(rsp);
        });
        check(!result->has_value(), "never complete inline");
        if (!ticket)
        {
            return Unexpect(ticket.error());
        }
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

    // 使用显式后端配置打开，供所有持久化实现复用业务契约。
    void open(const OpenCfg& cfg)
    {
        auto result = call([&](auto done) { return store->open(cfg, std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
        check(std::holds_alternative<Opened>(*result), "opened result");
    }

    // 读取永久猎人档案，保持后端返回的原始结果字节。
    HunterResult hunters()
    {
        auto result = call([&](auto done) { return store->load_hunters(1, std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
        return std::get<HunterResult>(std::move(*result));
    }

    // 提交猎人命令，保留同步校验拒绝与异步事务失败。
    Expect<Value, Error> apply(const HunterReq& req)
    {
        return call([&](auto done) { return store->apply_hunter(req, std::move(done)); });
    }

    // 查询永久操作并按原始意图验证幂等身份。
    Expect<Value, Error> find_op(const HunterReq& req)
    {
        return call([&](auto done)
        {
            return store->find_hunter_op(req.player_id, req.op_id, req.intent_json,
                std::move(done));
        });
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
    Expect<Value, Error> commit(const CommitMatch& req)
    {
        return call([&](auto done) { return store->commit_match(req, std::move(done)); });
    }

    // 查询已保存结果，不从当前内存推断结算。
    Expect<Value, Error> find(u64 id)
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

// 验证错误分类，避免把任何失败都误认为预期行为。
inline void error_is(const Expect<Value, Error>& result, Code code)
{
    check(!result && result.error().code == code, "expected error category");
}

}

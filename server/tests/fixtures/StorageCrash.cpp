// 提供受父进程控制的真实 SQLite 强杀与磁盘写满探针，不链接到正式服务端。
#include "common/Types.h"
#include "storage/Storage.h"
#include "StorageHooks.h"

#include <sqlite3.h>

#include <iostream>
#include <optional>
#include <stdexcept>

namespace
{

using namespace hunter::storage;
Str fault_stage;

// 将故障探针断言转为非零退出码。
void check(bool condition, const Str& message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

// 构造重启前后完全一致的结算输入。
CommitMatch reward()
{
    return {1, 1, 1, "extracted", "crash-content", {{10003, 2}, {10005, 1}}};
}

struct Host
{
    asio::io_context io;
    Storage storage{io, {}, 91};

    // 驱动真实存档操作；父进程为故意阻塞和异常情况提供超时边界。
    template<typename F>
    std::expected<Value, Error> call(F submit)
    {
        std::optional<Rsp> result;
        auto accepted = submit([&](Rsp value) { result = std::move(value); });
        check(accepted.has_value(), accepted ? "" : accepted.error().message);

        while (!result)
        {
            io.restart();
            io.run_one();
        }

        return std::move(result->result);
    }

    // 打开父进程提供的临时数据库。
    void open(const Str& path)
    {
        auto result = call([&](auto done) { return storage.open(path, std::move(done)); });
        check(result.has_value(), result ? "" : result.error().message);
    }

    // 请求关闭并等待所有完成交付。
    void stop()
    {
        auto result = call([&](auto done) { return storage.stop(std::move(done)); });
        check(result.has_value(), "stop failed");
    }

    // 提交可重复的完整结算请求。
    std::expected<Value, Error> commit(const CommitMatch& req)
    {
        return call([&](auto done) { return storage.commit_match(req, std::move(done)); });
    }

    // 读取单机玩家的永久状态。
    PlayerSave load()
    {
        auto result = call([&](auto done) { return storage.load_player(1, std::move(done)); });
        check(result.has_value(), "load failed");
        return std::get<PlayerSave>(std::move(*result));
    }

    // 查询对局一的实际提交记录。
    std::expected<Value, Error> find()
    {
        return call([&](auto done) { return storage.find_match(1, std::move(done)); });
    }
};

// 在空数据库永久分配 ID，强杀后的验证不复用分配逻辑。
void seed(const Str& path)
{
    Host host;
    host.open(path);
    auto result = host.call([&](auto done) { return host.storage.alloc_match(std::move(done)); });
    check(result && std::get<MatchId>(*result).value == 1, "seed id");
    host.stop();
}

// 验证崩溃后只有整体提交或整体不存在，随后原请求安全重试。
void verify(const Str& path, bool committed)
{
    Host host;
    host.open(path);
    const auto player = host.load();
    check(player.revision == (committed ? 2 : 1) &&
        player.items.size() == (committed ? 2 : 0) &&
        player.last_match_id == (committed ? 1 : 0), "atomic recovered player");
    auto found = host.find();
    check(committed ? found.has_value() : (!found && found.error().code == Code::NotFound),
        "atomic recovered result");
    auto result = host.commit(reward());
    check(result && std::get<MatchResult>(*result).replayed == committed, "restart retry");

    if (committed)
    {
        check(std::get<MatchResult>(*result).result_json ==
            std::get<MatchResult>(*found).result_json, "saved bytes survive kill");
    }

    check(host.load().items.size() == 2 && host.load().revision == 2, "exactly one award");
    auto next = host.call([&](auto done) { return host.storage.alloc_match(std::move(done)); });
    check(next && std::get<MatchId>(*next).value == 2, "id survives process kill");
    host.stop();
}

// 在真实 SQLite 达到页面上限时验证 SQLITE_FULL 分支和事务回滚。
void disk_full(const Str& path)
{
    Host host;
    host.open(path);
    fault_stage = "full";
    auto req = reward();
    req.content_key = Str(32768, 'c');
    auto result = host.commit(req);
    check(!result && result.error().code == Code::Write &&
        (result.error().sqlite_code & 0xff) == SQLITE_FULL, "real SQLITE_FULL");
    check(host.load().revision == 1 && host.load().items.empty(), "full rollback player");
    auto found = host.find();
    check(!found && found.error().code == Code::NotFound, "full rollback result");
    fault_stage = "restore";
    result = host.commit(reward());
    check(result && host.load().items.size() == 2, "connection usable after rollback");
    host.stop();
}

}

namespace hunter::storage::test
{

void point(const char* stage, Db& db)
{
    if (fault_stage == "full" && Str(stage) == "before_txn")
    {
        const auto pages = db.scalar("PRAGMA page_count");
        db.exec(("PRAGMA max_page_count=" + std::to_string(pages)).c_str());
        return;
    }

    if (fault_stage == "restore" && Str(stage) == "before_txn")
    {
        db.exec("PRAGMA max_page_count=1073741823");
        return;
    }

    if (fault_stage == stage)
    {
        std::cout << "checkpoint:" << stage << std::endl;
        char byte = 0;
        std::cin.get(byte);
        fail(Code::Internal, "kill_checkpoint_resumed_unexpectedly");
    }
}

}

// 运行单个进程探针，参数与文件均由集成测试提供。
int main(int argc, char** argv)
{
    try
    {
        check(argc >= 3, "usage: mode path [stage]");
        const Str mode = argv[1];
        const Str path = argv[2];

        if (mode == "seed")
        {
            seed(path);
        }
        else if (mode == "commit")
        {
            check(argc == 4, "commit stage required");
            Host host;
            host.open(path);
            fault_stage = argv[3];
            auto result = host.commit(reward());
            check(result.has_value(), "unexpected commit failure");
            host.stop();
        }
        else if (mode == "verify")
        {
            check(argc == 4, "expected state required");
            verify(path, Str(argv[3]) == "committed");
        }
        else if (mode == "full")
        {
            disk_full(path);
        }
        else
        {
            check(false, "unknown mode");
        }

        std::cout << "ok\n";
        return 0;
    }
    catch (const Error& error)
    {
        std::cerr << error.message << '\n';
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
    }

    return 1;
}

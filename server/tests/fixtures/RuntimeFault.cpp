// 仅测试宿主链接此文件，在真实 Runtime 保存事务处提供受父进程控制的检查点。
#include "common/Types.h"
#include "StorageHooks.h"
#include <windows.h>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <thread>

namespace hunter::storage::test
{
// 测试专用环境变量不出现在正式库或桌面宿主中。
Str variable(const char* name)
{
    char value[2048]{};
    const auto length = GetEnvironmentVariableA(name, value, sizeof(value));
    return length > 0 && length < sizeof(value) ? Str(value, length) : Str{};
}

void point(const char* stage, Db&)
{
    static bool injected = false;
    const auto mode = variable("HUNTER_TEST_FAULT");
    if (!injected && mode == "unknown" && Str(stage) == "after_commit")
    {
        injected = true;
        throw Error{Code::Internal, 0, true, "injected_commit_unknown"};
    }

    if (!injected && mode == "write" && Str(stage) == "before_txn")
    {
        injected = true;
        throw Error{Code::Write, 0, false, "injected_write_failure"};
    }

    if (mode != stage)
    {
        return;
    }

    const auto path = variable("HUNTER_TEST_MARKER");
    std::ofstream(path).put('1');

    while (!std::filesystem::exists(path + ".release"))
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
}
}

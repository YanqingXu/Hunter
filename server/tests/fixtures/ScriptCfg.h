// 将测试候选配置离线制作为独立 Lua 源码或签名制品，不增加生产配置注入入口。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"
#include <nlohmann/json.hpp>
#include <atomic>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#ifdef _WIN32
#include <process.h>
#else
#include <spawn.h>
#include <sys/wait.h>
#include <unistd.h>
extern char** environ;
#endif

namespace hunter
{
// 读取仅供测试断言和夹具派生使用的客户端内容制品。
inline nlohmann::json test_content()
{
    std::ifstream input(HUNTER_CONTENT_JSON);
    return nlohmann::json::parse(input);
}

// 在独立进程运行固定的离线工具，参数按数组传递而不经过命令解释器。
inline void test_cfg_process(const Vec<Str>& args)
{
    Vec<const char*> values;

    for (const auto& arg : args)
    {
        values.push_back(arg.c_str());
    }

    values.push_back(nullptr);
#ifdef _WIN32
    const auto result = _spawnv(_P_WAIT, values.front(), values.data());
    if (result != 0)
    {
        throw std::runtime_error("offline Lua fixture generation failed");
    }
#else
    pid_t process = 0;
    const auto started = posix_spawn(&process, values.front(), nullptr, nullptr,
        const_cast<char**>(values.data()), environ);
    i32 status = 0;
    if (started != 0 || waitpid(process, &status, 0) < 0
        || !WIFEXITED(status) || WEXITSTATUS(status) != 0)
    {
        throw std::runtime_error("offline Lua fixture generation failed");
    }
#endif
}

// 先生成经过相同 Lua 校验的隔离夹具，再把制品路径交给真实 Runtime。
inline Cfg test_cfg(const Cfg& original, const nlohmann::json& content)
{
    static std::atomic<u64> serial{0};
#ifdef _WIN32
    const auto process = _getpid();
#else
    const auto process = getpid();
#endif
    const auto root = std::filesystem::path(HUNTER_TEST_CFG_DIR) / "inputs";
    std::filesystem::create_directories(root);
    const Str identity = std::to_string(process) + "-" + std::to_string(++serial);
    const auto input = root / (identity + ".json");
    const auto output = root / (identity + ".result.json");
    {
        std::ofstream stream(input, std::ios::binary);
        stream << content.dump();
        stream.close();

        if (!stream)
        {
            throw std::runtime_error("cannot write isolated Lua fixture input");
        }
    }

    Vec<Str> args = {HUNTER_TEST_PYTHON, HUNTER_TEST_CFG_SCRIPT,
        "--tools", HUNTER_TEST_CFG_TOOLS, "--input", input.string(),
        "--result", output.string(), "--mode", HUNTER_PRODUCTION ? "bundle" : "source"};

    if (HUNTER_PRODUCTION)
    {
        args.push_back("--policy");
        args.push_back(original.policy_path);
    }

    test_cfg_process(args);
    std::ifstream stream(output);
    const auto artifact = nlohmann::json::parse(stream);
    stream.close();
    Cfg result = original;

    if (HUNTER_PRODUCTION)
    {
        result.bundle_path = artifact.at("path").get<Str>();
    }
    else
    {
        result.source_path = artifact.at("path").get<Str>();
    }

    std::filesystem::remove(input);
    std::filesystem::remove(output);
    return result;
}
}

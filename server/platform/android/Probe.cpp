// 在 ARM64 设备上验证签名 Bundle、双向 Host 调用和 Asio 调度，不包含 Android Service。
#include "common/Types.h"
#include "core/Cfg.h"
#include "script/Script.h"
#include "ContentSpec.h"

#include <asio.hpp>
#include <iostream>
#include <nlohmann/json.hpp>

namespace
{
// 检查每条退出路径的脚本关闭结果，并交付正常或失败入口积累的诊断。
bool close_probe(hunter::Script& script, const Str& reason)
{
    auto closed = script.shutdown(reason);

    for (const auto& log : script.take_logs())
    {
        std::cerr << log << '\n';
    }

    if (!closed)
    {
        std::cerr << closed.error() << '\n';
    }

    return closed.has_value();
}
}

// 加载设备端制品并执行三次 Tick；结果供 adb 探针脚本留证。
int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "usage: hunter_android_probe bundle policy\n";
        return 2;
    }

    hunter::Cfg cfg;
    cfg.bundle_path = argv[1];
    cfg.policy_path = argv[2];
    hunter::Script script;
    const nlohmann::json ctx = {{"v", 3}, {"snapshot_every", 3},
        {"content", nlohmann::json::parse(hunter::content::json_text)}};
    auto opened = script.open(cfg, ctx.dump());
    if (!opened)
    {
        std::cerr << opened.error() << '\n';
        static_cast<void>(close_probe(script, "probe_open_failed"));
        return 1;
    }

    auto login = script.event(2, R"({"v":3,"req_id":"login"})");
    auto start = login ? script.event(3,
        R"({"v":3,"req_id":"start","after_match_id":"0"})") : login;

    if (!login || !start)
    {
        static_cast<void>(close_probe(script, "probe_start_failed"));
        return 1;
    }

    const nlohmann::json move = {{"v", 3}, {"seq", "1"}, {"match_id", "1"},
        {"applied_tick", "1"}, {"move_x", 1}, {"aim_x", 1000}, {"aim_y", 0},
        {"jump", false}, {"fire", false}, {"reload", false}};
    auto input = script.event(1, move.dump());
    if (!input || input->size() != 1 || (*input)[0].kind != "ack")
    {
        std::cerr << "input probe failed\n";
        static_cast<void>(close_probe(script, "probe_input_failed"));
        return 1;
    }

    asio::io_context io;
    asio::steady_timer timer(io);
    bool passed = false;
    timer.expires_after(std::chrono::milliseconds(1));
    timer.async_wait([&](const asio::error_code& error)
    {
        if (error)
        {
            return;
        }

        for (u64 tick_id = 1; tick_id <= 3; ++tick_id)
        {
            auto out = script.tick(tick_id, 1.0 / 60.0);
            if (!out)
            {
                std::cerr << out.error() << '\n';
                return;
            }

            if (tick_id == 3)
            {
                passed = out->size() == 1 && (*out)[0].kind == "snapshot";
            }
        }
    });
    io.run();
    auto state = script.export_state();
    auto valid = script.validate_state();
    const bool closed = close_probe(script, "probe_complete");
    if (!passed || !state || !valid || !closed)
    {
        return 1;
    }

    const auto world = nlohmann::json::parse(*state);
    if (world.at("phase") != "Playing" || world.at("match_id") != "1"
        || world.at("tick_id") != "3" || world.at("seq") != "1")
    {
        return 1;
    }

    std::cout << R"({"probe":"hunter-android","passed":true,"tick_id":"3"})" << '\n';
    return 0;
}

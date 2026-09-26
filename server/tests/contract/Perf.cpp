// 测量默认及六十四实体的真实 Tick 与协议输出开销。
#include "common/Types.h"
#include "game/World.h"
#include "../fixtures/ScriptCfg.h"
#include "script/Script.h"
#include "net/Protocol.h"
#include <nlohmann/json.hpp>
#include <algorithm>
#include <chrono>
#include <fstream>
#include <iostream>
#include <stdexcept>

// 提取结果并保留失败详情。
template <typename T>
T take(std::expected<T, Str> value)
{
    if (!value)
    {
        throw std::runtime_error(value.error());
    }

    return std::move(*value);
}

// 用合法出生记录初始化额外动态对象，测量原生容量而不扩大源表场景上限。
void fill_capacity(hunter::Script& script, const nlohmann::json& content)
{
    const auto result = script.change([&](hunter::World& world)
    {
        const auto& spawns = content.at("map").at("enemies");
        usize index = 0;

        while (world.count() < 64)
        {
            const auto& spawn = spawns.at(index++ % spawns.size());
            const auto& cfg = content.at("monsters").at(spawn.at("cfg_id").get<Str>());
            const nlohmann::json spec = {{"cfg_id", spawn.at("cfg_id")},
                {"spawn_id", spawn.at("spawn_id")}, {"x", spawn.at("x")}, {"y", spawn.at("y")},
                {"hp", cfg.at("hp")}, {"width", cfg.at("width")}, {"height", cfg.at("height")},
                {"grounded", true}};
            const auto id = world.spawn("monster", spec.dump());

            if (id.empty() || id.front() == ':')
            {
                throw std::runtime_error("dynamic capacity setup failed: " + id);
            }
        }
    });

    if (!result)
    {
        throw std::runtime_error(result.error());
    }
}

// 运行相同场景并打印可归档的测量结果。
int main(int argc, char** argv)
{
    try
    {
        const i32 required = HUNTER_PRODUCTION ? 4 : 3;
        if (argc != required && argc != required + 1)
        {
            throw std::runtime_error("expected source, content and optional tick count");
        }

        const u64 ticks = argc == required + 1 ? std::stoull(argv[required]) : 600;
        if (ticks < 60 || ticks > 36000)
        {
            throw std::runtime_error("tick count must be 60..36000");
        }

        std::ifstream stream(argv[2]);
        const auto original = nlohmann::json::parse(stream);

        for (const bool full : {false, true})
        {
            auto content = original;
            if (full)
            {
                const auto spawn = content["map"]["enemies"][0];
                content["map"]["width"] = 100000;
                content["map"]["solids"] = nlohmann::json::array();
                content["map"]["enemies"] = nlohmann::json::array();

                for (i32 index = 0; index < 32; ++index)
                {
                    auto enemy = spawn;
                    enemy["spawn_id"] = std::to_string(index + 1);
                    const i32 x = 20000 + index * 1000;
                    enemy["x"] = x;
                    enemy["patrol_min"] = x - 100;
                    enemy["patrol_max"] = x + 100;
                    content["map"]["enemies"].push_back(enemy);
                }
            }

            hunter::Cfg cfg;
#if HUNTER_PRODUCTION
            cfg.bundle_path = argv[1];
            cfg.policy_path = argv[3];
#else
            cfg.source_path = argv[1];
#endif
            hunter::Script script;
            take(script.open(hunter::test_cfg(cfg, content),
                nlohmann::json{{"v", 7}, {"snapshot_every", 3}}.dump()));
            take(script.event(2, R"({"v":7,"req_id":"login","player_id":"1"})"));
            take(script.event(3, R"({"v":7,"req_id":"start","after_match_id":"0",)"
            R"("match_id":"1","world_id":"1"})"));
            if (full)
            {
                fill_capacity(script, content);
            }

            Vec<f64> times;
            usize bytes = 0;
            usize frames = 0;

            for (u64 tick = 1; tick <= ticks; ++tick)
            {
                const auto begin = std::chrono::steady_clock::now();
                auto result = script.tick(tick, 1.0 / 60.0);
                if (!result)
                {
                    throw std::runtime_error("tick " + std::to_string(tick) + ": "
                        + result.error());
                }

                auto output = std::move(*result);
                const auto encoded = take(hunter::script_frames(output, cfg));
                times.push_back(std::chrono::duration<f64, std::micro>(
                    std::chrono::steady_clock::now() - begin).count());
                frames += encoded.size();

                for (const auto& frame : encoded)
                {
                    bytes += frame.bytes.size();
                }
            }

            std::sort(times.begin(), times.end());
            const auto valid = script.validate_state();
            if (!valid)
            {
                throw std::runtime_error(valid.error());
            }

            const auto stats = take(script.stats());
            std::cout << nlohmann::json{{"entities", script.world().count()}, {"ticks", ticks},
                {"p50_us", times[times.size() / 2]}, {"p95_us", times[times.size() * 95 / 100]},
                {"max_us", times.back()}, {"frames", frames}, {"bytes", bytes},
                {"live_bytes", stats.live_bytes}, {"peak_bytes", stats.peak_bytes},
                {"gc_cycles", stats.gc_cycles}}.dump() << '\n';
            const auto closed = script.shutdown("benchmark");
            if (!closed)
            {
                throw std::runtime_error(closed.error());
            }
        }

        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

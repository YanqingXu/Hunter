// 在真实源码与签名 Runtime 中执行独立实体夹具，不扩展生产玩法入口。
#include "common/Types.h"
#include "core/Cfg.h"
#include "script/Script.h"

#include <nlohmann/json.hpp>

#include <fstream>
#include <iostream>
#include <stdexcept>
#include <utility>

namespace
{

// 提取真实脚本结果，保留脚本断言与预算失败详情。
template <typename T>
T take(std::expected<T, Str> result)
{
    if (!result)
    {
        throw std::runtime_error(result.error());
    }

    return std::move(*result);
}

}

// 运行正式模块的增删、身份、配置关联和容量边界场景。
int main(int argc, char** argv)
{
    try
    {
        if (argc < (HUNTER_PRODUCTION ? 4 : 3))
        {
            throw std::runtime_error("expected entity fixture, content and production policy");
        }

        hunter::Cfg cfg;
#if HUNTER_PRODUCTION
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[3];
#else
        cfg.source_path = argv[1];
#endif
        std::ifstream stream(argv[2]);
        const auto content = nlohmann::json::parse(stream);
        hunter::Script script;
        take(script.open(cfg, nlohmann::json{{"v", 4}, {"snapshot_every", 3},
            {"content", content}}.dump()));
        take(script.event(2, R"({"v":4,"req_id":"login"})"));
        take(script.event(3, R"({"v":4,"req_id":"start","after_match_id":"0"})"));
        take(script.event(5, "{}"));

        for (u32 batch = 0; batch < 8; ++batch)
        {
            const auto result = script.event(6, "{}");
            if (!result)
            {
                throw std::runtime_error("spawn batch " + std::to_string(batch) + ": "
                    + result.error());
            }
        }

        const auto boundary = script.event(7, "{}");
        if (!boundary)
        {
            throw std::runtime_error("capacity boundary: " + boundary.error());
        }
        const auto saved = take(script.export_state());
        auto valid = script.import_state(saved);
        if (!valid)
        {
            throw std::runtime_error(valid.error());
        }

        const auto state = nlohmann::json::parse(take(script.export_state()));
        if (state["entity_ids"].size() != 63 || state["entities"].size() != 63
            || state["last_entity_id"] != "18446744073709551615")
        {
            throw std::runtime_error("maximum allocator world roundtrip failed");
        }

        const auto failed = script.event(8, "{}");
        if (failed || failed.error().find("invalid_event_value") == Str::npos
            || script.tick(1, 1.0 / 60.0))
        {
            throw std::runtime_error("caught native output failure must discard batch and abort");
        }

        std::cout << "entity contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

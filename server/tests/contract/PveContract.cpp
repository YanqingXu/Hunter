// 在正式 Luax 玩法中验证 Boss、掉落、撤离取消和同 Tick 致命伤优先级。
#include "common/Types.h"
#include "core/Cfg.h"
#include "game/World.h"
#include "script/Script.h"
#include "ContentSpec.h"
#include <fstream>
#include <iostream>
#include <stdexcept>

namespace
{
// 保留入口错误上下文，失败时直接结束契约。
template <typename T>
T take(std::expected<T, Str> result)
{
    if (!result)
    {
        throw std::runtime_error(result.error());
    }

    if constexpr (!std::is_void_v<T>)
    {
        return std::move(*result);
    }
}

// 检查业务条件并给出确定的失败场景。
void check(bool valid, const char* message)
{
    if (!valid)
    {
        throw std::runtime_error(message);
    }
}
}

// 测试通过正常入口运行规则，仅在风险布置阶段改变原生状态。
int main(int argc, char** argv)
{
    try
    {
        check(argc == 2 || argc == 3, "source or bundle and policy required");
        hunter::Cfg cfg;
#if HUNTER_PRODUCTION
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[2];
#else
        cfg.source_path = argv[1];
#endif
        hunter::Script game;
        auto content = nlohmann::json::parse(hunter::content::json_text);
        take(game.open(cfg, nlohmann::json{{"v", 6}, {"snapshot_every", 3},
            {"content", content}}.dump()));
        take(game.event(2, R"({"v":6,"req_id":"login","player_id":"73"})"));
        take(game.event(3, R"({"v":6,"req_id":"start","after_match_id":"0",
            "match_id":"101","world_id":"9"})"));
        u64 tick = 0;
        const auto step = [&] { take(game.tick(++tick, 1.0 / 60)); };
        const auto setup = [&](const Func<void(hunter::World&)>& fn)
        {
            take(game.change(fn));
        };
        setup([](hunter::World& w)
        {
            auto& player = w.actors[w.slot(w.player_entity_id)]->unit();
            player.x = 21000;
            player.y = 0;

            for (auto& actor : w.actors)
            {
                if (actor && actor->unit().kind == "monster")
                {
                    auto& monster = std::get<hunter::Monster>(actor->value);
                    monster.x = monster.spawn_id == 14 ? 22000 : 10000;
                }
            }
        });
        step();
        check(std::get<hunter::Monster>(game.world().actors[3]->value).state == "windup",
            "boss telegraphs before damage");
        const auto hp = game.world().actors[0]->unit().hp;
        setup([](hunter::World& w) { w.actors[0]->unit().x = 18000; });

        for (i32 i = 0; i < 35; ++i)
        {
            step();
        }

        check(game.world().actors[0]->unit().hp == hp, "windup can be dodged");
        setup([](hunter::World& w)
        {
            for (auto& actor : w.actors)
            {
                if (actor && actor->unit().kind == "monster")
                {
                    actor->unit().hp = 0;
                    actor->unit().alive = false;
                    std::get<hunter::Monster>(actor->value).state = "dead";
                    std::get<hunter::Monster>(actor->value).attack_ticks = 0;
                }
            }
        });
        step();
        check(game.world().phase == "Playing", "Boss death only unlocks exit");
        check(game.world().raid.extract_reason == "outside", "exit unlocked");
        const auto count = game.world().last_item_id;
        check(count >= 2, "boss guaranteed loot generated");
        step();
        check(game.world().last_item_id == count, "death does not duplicate loot");
        setup([](hunter::World& w) { w.actors[0]->unit().x = 2000; });
        step();
        check(game.world().raid.extract_ticks == 1, "automatic extraction begins");
        take(game.event(4, R"({"v":6,"paused":true})"));
        step();
        check(game.world().raid.extract_ticks == 1, "pause freezes extraction");
        take(game.event(4, R"({"v":6,"paused":false})"));
        setup([&](hunter::World& w) { w.raid.damage_tick = tick + 1; });
        step();
        check(game.world().raid.extract_ticks == 0 && game.world().raid.extract_reason == "hurt",
            "positive damage cancels extraction");
        step();
        setup([](hunter::World& w) { w.actors[0]->unit().x = 4000; });
        step();
        check(game.world().raid.extract_ticks == 0, "leaving cancels extraction");
        setup([](hunter::World& w)
        {
            w.actors[0]->unit().x = 2000;
            w.raid.extract_ticks = 179;
            w.actors[0]->unit().hp = 0;
            w.actors[0]->unit().alive = false;
        });
        step();
        check(game.world().raid.player_state == "Dead", "death wins final extraction tick");
        check(game.world().phase == "Settling", "death requires persistence confirmation");
        const auto saved = take(game.export_state());
        take(game.import_state(saved));
        check(take(game.export_state()) == saved, "complete V5 state round trip");
        std::cout << "pve contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

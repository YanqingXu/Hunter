// 在正式 Luax 玩法中验证 Boss、掉落、撤离取消和同 Tick 致命伤优先级。
#include "common/Types.h"
#include "core/Cfg.h"
#include "game/World.h"
#include "script/Script.h"
#include "../fixtures/ScriptCfg.h"
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
        take(game.open(cfg, nlohmann::json{{"v", 8}, {"snapshot_every", 3}}.dump()));
        take(game.event(2, R"({"v":8,"req_id":"login","player_id":"73"})"));
        take(game.event(3, R"({"v":8,"req_id":"start","after_match_id":"0",
            "match_id":"101","world_id":"9"})"));
        const auto content = hunter::test_content();
        u64 boss_id = 0;
        u32 boss_spawn = 0;
        i32 guaranteed_drops = 0;
        for (const auto& actor : game.world().actors)
        {
            if (actor && actor->unit().kind == "monster")
            {
                const auto& monster = std::get<hunter::Monster>(actor->value);
                const auto& value = content.at("monsters").at(std::to_string(monster.cfg_id));
                if (value.at("rank") == 3)
                {
                    check(boss_id == 0, "production selects exactly one Boss instance");
                    boss_id = monster.id;
                    boss_spawn = monster.spawn_id;
                }
                for (const auto& drop : value.at("drops"))
                {
                    guaranteed_drops += drop.at("chance") == 10000 ? 1 : 0;
                }
            }
        }
        check(boss_id != 0 && (game.world().boss_spawn == 0
            || game.world().boss_spawn == boss_spawn), "runtime Boss matches selected spawn");
        const auto& exit = content.at("extracts").at(0);
        const auto exit_x = exit.at("x").get<i32>() + exit.at("w").get<i32>() / 2;
        const auto exit_outside = exit.at("x").get<i32>() + exit.at("w").get<i32>() + 1000;
        const auto hold_ticks = exit.at("hold_ticks").get<i32>();
        const auto combat_x = content.at("map").at("width").get<i32>() - 3000;
        const auto initial_items = game.world().last_item_id;
        u64 tick = 0;
        const auto step = [&] { take(game.tick(++tick, 1.0 / 60)); };
        const auto setup = [&](const Func<void(hunter::World&)>& fn)
        {
            take(game.change(fn));
        };
        setup([&](hunter::World& w)
        {
            auto& player = w.actors[w.slot(w.player_entity_id)]->unit();
            player.x = combat_x;
            player.y = 0;

            for (auto& actor : w.actors)
            {
                if (actor && actor->unit().kind == "monster")
                {
                    auto& monster = std::get<hunter::Monster>(actor->value);
                    monster.x = monster.id == boss_id ? combat_x + 1000 : combat_x - 11000;
                }
            }
        });
        step();
        const auto& boss = std::get<hunter::Monster>(
            game.world().actors[game.world().slot(boss_id)]->value);
        check(boss.state == "windup",
            "boss telegraphs before damage");
        const auto windup = boss.ability_phase == "windup" ? boss.ability_ticks
            : content.at("monsters").at(std::to_string(boss.cfg_id)).at("windup").get<i32>();
        const auto hp = game.world().actors[0]->unit().hp;
        setup([&](hunter::World& w) { w.actors[0]->unit().x = combat_x - 3000; });

        for (i32 i = 0; i < windup + 5; ++i)
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
                    auto& monster = std::get<hunter::Monster>(actor->value);
                    monster.set_hp(0);
                    monster.set_alive(false);
                    monster.set_state("dead");
                }
            }
        });
        step();
        check(game.world().phase == "Playing", "Boss death leaves the raid active");
        check(game.world().raid.extract_reason == "outside", "exit unlocked");
        const auto count = game.world().last_item_id;
        check(count >= initial_items + guaranteed_drops, "guaranteed death loot adds to spawn loot");
        step();
        check(game.world().last_item_id == count, "death does not duplicate loot");
        setup([&](hunter::World& w) { w.actors[0]->unit().x = exit_x; });
        step();
        check(game.world().raid.extract_ticks == 1, "automatic extraction begins");
        take(game.event(4, R"({"v":8,"paused":true})"));
        step();
        check(game.world().raid.extract_ticks == 1, "pause freezes extraction");
        take(game.event(4, R"({"v":8,"paused":false})"));
        setup([&](hunter::World& w) { w.raid.damage_tick = tick + 1; });
        step();
        check(game.world().raid.extract_ticks == 0 && game.world().raid.extract_reason == "hurt",
            "positive damage cancels extraction");
        step();
        setup([&](hunter::World& w) { w.actors[0]->unit().x = exit_outside; });
        step();
        check(game.world().raid.extract_ticks == 0, "leaving cancels extraction");
        setup([&](hunter::World& w)
        {
            w.actors[0]->unit().x = exit_x;
            w.raid.extract_ticks = hold_ticks - 1;
            w.actors[0]->unit().hp = 0;
            w.actors[0]->unit().alive = false;
        });
        step();
        check(game.world().raid.player_state == "Dead", "death wins final extraction tick");
        check(game.world().phase == "Settling", "death requires persistence confirmation");
        const auto saved = take(game.export_state());
        take(game.import_state(saved));
        check(take(game.export_state()) == saved, "complete V7 state round trip");
        std::cout << "pve contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

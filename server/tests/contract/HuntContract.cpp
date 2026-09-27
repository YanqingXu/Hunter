// 在真实脚本与原生状态之间验证复活选择、技能消费、有效属性和特殊视角限制。
#include "common/Types.h"
#include "core/Cfg.h"
#include "game/World.h"
#include "script/Script.h"
#include "../fixtures/ScriptCfg.h"
#include <iostream>
#include <stdexcept>

namespace
{
using Json = nlohmann::json;

// 明确报告不变量失败的位置。
void check(bool value, const Str& reason)
{
    if (!value)
    {
        throw std::runtime_error(reason);
    }
}

// 保留真实脚本错误，避免默认值掩盖失败。
template <typename T>
T take(Expect<T, Str> result)
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

// 只在隔离夹具填写机制验证值，生产空白不以测试数值补齐。
Json content()
{
    auto doc = hunter::test_content();
    doc["skills"] = Json::object();
    for (const auto& [id, kind, effects] : Vec<std::tuple<Str, Str, Json>>{
        {"1", "once", {{{"stat", "revive_hp"}, {"op", "add"}, {"value", 25}},
            {{"stat", "revive_stamina"}, {"op", "add"}, {"value", 40}}}},
        {"2", "death", {{{"stat", "revive_full_hp"}, {"op", "add"}, {"value", 1}}}},
        {"8", "passive", {{{"stat", "reserve"}, {"op", "add"}, {"value", 20}}}}})
    {
        doc["skills"][id] = {{"name", "fixture"}, {"target", "player"},
            {"category", id == "8" ? "numeric" : "mechanic"},
            {"kind", kind}, {"cost", 0}, {"effects", effects}};
    }
    doc["default_loadout"]["skills"] = {"1", "2", "8"};
    for (auto& monster : doc["monsters"])
    {
        monster["detect_range"] = 1;
        monster["attack_range"] = 1;
    }
    return doc;
}

struct Game
{
    hunter::Script script;
    u64 tick = 0;
    u64 action_seq = 0;

    // 启动真实 Lua 配装、登录和对局入口。
    Game(const hunter::Cfg& cfg, const Json& data = content())
    {
        take(script.open(hunter::test_cfg(cfg, data),
            R"({"v":8,"snapshot_every":3})"));
        take(script.event(2, R"({"v":8,"req_id":"login","player_id":"1"})"));
        const auto accepted = take(script.check_loadout({}));
        take(script.change([&](hunter::World& world) { world.prepare_loadout(accepted); }));
        take(script.event(3, R"({"v":8,"req_id":"start","after_match_id":"0",
            "match_id":"1","world_id":"1"})"));
    }

    // 只读借用当前玩家，引用不跨入口保存。
    const hunter::Player& player() const
    {
        return std::get<hunter::Player>(script.world().actors[0]->value);
    }

    // 发送与网络动作相同的严格参数，读取业务拒绝。
    Str action(const Str& kind, const Str& death = "0")
    {
        const auto out = take(script.event(5, Json{{"v", 8}, {"req_id", "action"},
            {"kind", kind}, {"slot", 0}, {"target_id", "0"},
            {"action_seq", std::to_string(++action_seq)}, {"death_seq", death}}.dump()));
        for (const auto& item : out)
        {
            if (item.message.has_error())
            {
                return item.message.error().code();
            }
        }
        return {};
    }

    // 精确推进活动计时。
    void steps(i32 count)
    {
        for (i32 index = 0; index < count; ++index)
        {
            take(script.tick(++tick, 1.0 / 60));
        }
    }

    // 原生布置致死后状态，复活决策和消费仍走正式 Lua 入口。
    void down()
    {
        take(script.change([](hunter::World& world)
        {
            auto& player = std::get<hunter::Player>(world.actors[0]->value);
            player.set_hp(0);
            player.set_alive(false);
            player.set_vx(0);
            player.set_vy(0);
            world.clear_input();
            player.enter_downed();
        }));
    }
};

// 等待复活不结算，迟到请求不生效，成功一次消费并移除死亡类型技能。
void revival(const hunter::Cfg& cfg)
{
    Game game(cfg);
    const auto doc = content();
    const auto gun = std::to_string(game.player().weapon.cfg_id);
    const auto reserve = doc["weapons"][gun]["reserve"].get<i32>() + 20;
    check(game.player().reserve == reserve, "reserve bonus applies once at spawn");
    game.down();
    game.steps(4);
    check(game.script.world().phase == "Playing" && game.player().downed,
        "downed player waits without final death settlement");
    const auto saved = take(game.script.export_state());
    check(!game.script.import_state("["), "malformed state rejected");
    check(!game.script.import_state("[]"), "non-object state rejected");
    check(!game.script.import_state(Str(70000, 'x')), "over-budget state rejected");
    check(take(game.script.export_state()) == saved, "invalid input preserves active session");
    take(game.script.import_state(saved));
    check(take(game.script.export_state()) == saved, "downed state roundtrip");
    check(game.action("revive", "0") == "stale_death", "old death sequence rejected");
    check(game.action("revive", "1").empty(), "manual revive succeeds");
    check(game.player().alive && game.player().hp == game.player().max_hp
        && game.player().stamina == 40 && game.player().quiet_ticks == 180,
        "full-health override and explicit stamina apply");
    check(game.player().skills[0].spent && game.player().skills[1].spent
        && !game.player().skills[2].spent, "revive consumes only correct lifetimes");
    check(game.action("revive", "1") == "stale_death", "duplicate revive cannot consume twice");
    check(game.player().reserve == reserve, "recalculation never awards reserve again");
    game.steps(180);
    check(game.player().quiet_ticks == 0, "acquisition suppression lasts 180 active ticks");
    const auto restored = take(game.script.export_state());
    take(game.script.import_state(restored));
    check(take(game.script.export_state()) == restored, "spent skills roundtrip");
}

// 特殊视角禁止角色输入和普通动作，退出后恢复；等待期间可主动放弃。
void vision(const hunter::Cfg& cfg)
{
    Game game(cfg);
    const auto x = game.player().x;
    check(game.action("vision_on").empty(), "vision starts");
    hunter::wire::FrameInput input;
    input.set_seq(1);
    input.set_world_id(1);
    input.set_match_id(1);
    input.set_move_x(1);
    input.set_aim_x(1000);
    input.set_fire(true);
    take(game.script.input(input, 1));
    const auto ammo = game.player().weapon.ammo;
    game.steps(3);
    check(game.player().x == x && game.player().weapon.ammo == ammo,
        "camera mode rejects character movement and fire");
    check(!game.action("pickup").empty(), "ordinary actions blocked in vision");
    check(game.action("vision_off").empty(), "vision can exit");
    game.down();
    check(game.action("abandon").empty()
        && game.script.world().raid.player_state == "Abandoned", "downed abandonment is final");
}

// 致死伤害实际进入等待；下一次死亡不能被上一轮迟到复活请求消费。
void lethal(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["skills"]["4"] = doc["skills"]["1"];
    doc["monsters"]["1002"]["damage"] = 200;
    doc["monsters"]["1002"]["attack_range"] = 1000;
    doc["monsters"]["1002"]["detect_range"] = 1000;
    Game game(cfg, doc);
    take(game.script.change([](hunter::World& world)
    {
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        player.x = 4000;
        auto& enemy = std::get<hunter::Monster>(world.actors[1]->value);
        enemy.x = 4500;
    }));
    game.steps(1);
    check(game.player().downed && game.player().death_seq == 1
        && game.script.world().raid.player_state == "Alive", "lethal hit waits before settlement");
    check(game.action("revive", "1").empty(), "first damage death revives");
    take(game.script.change([](hunter::World& world)
    {
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        check(player.add_skill("4"), "new independent revive acquired");
        player.quiet_ticks = 0;
        std::get<hunter::Monster>(world.actors[1]->value).attack_ticks = 0;
    }));
    game.steps(1);
    check(game.player().downed && game.player().death_seq == 2, "next death has fresh identity");
    check(game.action("revive", "1") == "stale_death", "late request cannot target next death");
    check(!game.player().skills.back().spent, "late request preserves new revive skill");
    check(game.action("revive", "2").empty() && game.player().hp == 25,
        "removed full-health skill does not affect next revive");
}
}

// 同一套机制契约在源码和签名 Bundle 下运行。
int main(int argc, char** argv)
{
    try
    {
        check(argc >= 2, "script argument required");
        hunter::Cfg cfg;
#if HUNTER_PRODUCTION
        check(argc >= 3, "policy argument required");
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[2];
#else
        cfg.source_path = argv[1];
#endif
        revival(cfg);
        vision(cfg);
        lethal(cfg);
        std::cout << "hunt contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

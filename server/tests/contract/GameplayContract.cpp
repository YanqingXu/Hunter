// 在真实 Luax Runtime 中验证姿态恢复、配装枪弹、工具中断和有界场景交互。
#include "common/Types.h"
#include "core/Cfg.h"
#include "game/World.h"
#include "script/Script.h"
#include "ContentSpec.h"
#include <iostream>
#include <stdexcept>

namespace
{
using Json = nlohmann::json;

// 把行为差异转换为可定位的契约失败。
void check(bool value, const Str& reason)
{
    if (!value)
    {
        throw std::runtime_error(reason);
    }
}

// 保留真实脚本入口返回的错误详情，不以缺省结果掩盖失败。
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

// 使用合法的最小感知和移动参数，隔离需要精确计时的远处怪物。
Json content()
{
    auto value = Json::parse(hunter::content::json_text);

    for (auto& monster : value["monsters"])
    {
        monster["speed"] = 1;
        monster["detect_range"] = 1;
        monster["attack_range"] = 1;
    }

    return value;
}

struct Game
{
    hunter::Script script;
    u64 tick = 0;
    u64 seq = 0;
    u64 action_seq = 0;

    // 使用正式脚本及内容契约建立单局，测试布置通过受控原生入口完成。
    Game(const hunter::Cfg& cfg, Json data = content(),
        const hunter::wire::Loadout& loadout = {})
    {
        take(script.open(cfg, Json{{"v", 6}, {"snapshot_every", 3},
            {"content", std::move(data)}}.dump()));
        take(script.event(2, R"({"v":6,"req_id":"login","player_id":"1"})"));
        take(script.change([&](hunter::World& world) { world.prepare_loadout(loadout); }));
        take(script.event(3, R"({"v":6,"req_id":"start","after_match_id":"0",
            "match_id":"1","world_id":"1"})"));
    }

    // 只读借用玩家状态，调用后不跨入口保存引用。
    const hunter::Player& player() const
    {
        const auto& world = script.world();
        return std::get<hunter::Player>(world.actors[world.slot(world.player_entity_id)]->value);
    }

    // 为特定风险布置玩家状态，所有写入仍发生在拥有线程的显式片段。
    void setup(const Func<void(hunter::Player&, hunter::World&)>& fn)
    {
        take(script.change([&](hunter::World& world)
        {
            auto& player = std::get<hunter::Player>(
                world.actors[world.slot(world.player_entity_id)]->value);
            fn(player, world);
        }));
    }

    // 发送正式输入意图，位置和伤害不能从网络输入指定。
    void input(i32 move = 0, bool run = false, bool prone = false, bool jump = false,
        bool fire = false, bool reload = false, i32 aim_x = 1000, i32 aim_y = 0,
        i32 move_y = 0)
    {
        hunter::wire::FrameInput req;
        req.set_seq(++seq);
        req.set_world_id(1);
        req.set_match_id(1);
        req.set_move_x(move);
        req.set_move_y(move_y);
        req.set_aim_x(aim_x);
        req.set_aim_y(aim_y);
        req.set_run(run);
        req.set_prone(prone);
        req.set_jump(jump);
        req.set_fire(fire);
        req.set_reload(reload);
        take(script.input(req, tick + 1));
    }

    // 推进精确数量的活动或暂停 Tick。
    void steps(i32 count = 1)
    {
        for (i32 index = 0; index < count; ++index)
        {
            take(script.tick(++tick, 1.0 / 60));
        }
    }

    // 调用与 Runtime 相同的动作桥接并返回明确业务拒绝。
    Str action(const Str& kind, i32 slot = 0, u32 target = 0)
    {
        const auto result = take(script.event(5, Json{{"v", 6}, {"req_id", "action"},
            {"kind", kind}, {"slot", slot}, {"target_id", std::to_string(target)},
            {"action_seq", std::to_string(++action_seq)}}.dump()));

        for (const auto& out : result)
        {
            if (out.message.has_error())
            {
                return out.message.error().code();
            }

            if (out.message.has_action_rsp())
            {
                return "";
            }
        }

        throw std::runtime_error("missing action response");
    }

    // 验证完整状态往返不会丢失槽位、场景、计时和随机状态。
    void roundtrip()
    {
        const auto saved = take(script.export_state());
        take(script.import_state(saved));
        check(take(script.export_state()) == saved, "v6 state roundtrip");
    }
};

// 验证配装拒绝、默认值及免费槽位与奖励背包的分离。
void loadouts(const hunter::Cfg& cfg)
{
    Game game(cfg);
    const auto accepted = game.script.world().get_loadout();
    check(accepted.player_cfg_id() == 1 && accepted.weapons_size() == 2,
        "default loadout expanded");
    auto heavy = accepted;
    heavy.mutable_weapons(1)->set_cfg_id(2);
    heavy.mutable_weapons(1)->set_ammo_cfg_id(2);
    bool rejected = false;

    try
    {
        static_cast<void>(game.script.world().check_loadout(heavy));
    }
    catch (const std::exception&)
    {
        rejected = true;
    }

    check(rejected, "overweight rejected before starting");
    auto second = accepted;
    second.set_player_cfg_id(2);
    Game other(cfg, content(), second);
    check(other.player().cfg_id == 2 && other.player().hp == 150, "second template selected");
    check(other.script.world().snapshot(1).snapshot().items_size() == 0,
        "free loadout is not reward inventory");
    other.roundtrip();
}

// 验证体型、瞄准、体力与混合生命段的边界和暂停冻结。
void vitals(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.input(1, true);
    game.steps();
    check(game.player().x == 2150 && game.player().stamina == 100, "running without stamina cost");
    game.input(-1, false, true, false, false, false, -1000, -1000);
    game.steps();
    check(game.player().prone && game.player().x == 2100 && game.player().aim_y >= 0,
        "prone speed and local upward aim");
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.stamina = 0;
        player.stamina_delay = 120;
    });
    game.input(0, false, false, true);
    game.steps();
    check(game.player().vy > 0 && game.player().stamina == 0, "zero stamina still jumps");
    game.input();
    game.steps(121);
    check(game.player().stamina == 0, "recovery waits full delay and integer accumulation");
    game.steps(3);
    check(game.player().stamina > 0, "stamina recovers after delay");
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.hp = 99;
        player.health_delay = 300;
        player.health_rem = 0;
    });
    game.steps(300);
    check(game.player().hp == 99, "health recovery delay");
    game.steps(24);
    check(game.player().hp == 100, "mixed segment recovery stops at 100");
    game.steps(120);
    check(game.player().hp == 100, "exact boundary cannot enter next segment");
    take(game.script.event(4, R"({"v":6,"paused":true})"));
    const auto before = game.player().stamina;
    game.steps(120);
    check(game.player().stamina == before, "pause freezes recovery");
    take(game.script.event(4, R"({"v":6,"paused":false})"));
    game.roundtrip();

    auto tunnel = content();
    tunnel["map"]["solids"].push_back(
        {{"id", "299"}, {"x", 1800}, {"y", 700}, {"w", 1600}, {"h", 200}});
    tunnel["map"]["spawn"]["x"] = 1000;
    Game crawl(cfg, tunnel);
    crawl.input(1, false, true);
    crawl.steps(30);
    crawl.input();
    crawl.steps();
    check(crawl.player().prone, "low ceiling blocks standing");
}

// 构造无遮挡的两目标射击场，生命值足够观察每次穿透的差值。
Json firing_range()
{
    auto value = content();
    value["map"]["solids"] = Json::array();
    value["scenes"] = Json::array();
    value["map"]["enemies"][0]["x"] = 4000;
    value["map"]["enemies"][0]["patrol_min"] = 3500;
    value["map"]["enemies"][0]["patrol_max"] = 4500;
    value["map"]["enemies"][1]["x"] = 5000;
    value["map"]["enemies"][1]["patrol_min"] = 4500;
    value["map"]["enemies"][1]["patrol_max"] = 5500;
    value["monsters"]["1002"]["hp"] = 1000;
    return value;
}

// 验证一发扣一弹、普通怪穿透减伤和切枪时的装填资源边界。
void weapons(const hunter::Cfg& cfg)
{
    Game game(cfg, firing_range());
    game.input(0, false, false, false, true);
    game.steps();
    const auto& world = game.script.world();
    check(world.actors[1]->unit().hp == 874 && world.actors[2]->unit().hp == 924,
        "rifle penetration applies fixed loss after first monster");
    check(game.player().weapon.ammo == 4, "one shot consumes one round");
    game.input(0, false, false, false, false, true);
    game.steps();
    check(game.player().weapon.reload_ticks > 0, "partial rifle begins per-round reload");
    const auto reserve = game.player().reserve;
    check(game.action("switch_weapon", 2).empty(), "switch accepted");
    check(game.player().get_weapon_reserve(1) == reserve
        && game.player().get_weapon_ammo(1) == 4
        && game.player().get_weapon_reload_ticks(1) == 0, "switch cancels only unfinished round");
    game.roundtrip();

    auto choice = game.script.world().get_loadout();
    choice.mutable_weapons(0)->set_cfg_id(2);
    choice.mutable_weapons(0)->set_ammo_cfg_id(2);
    Game shotgun(cfg, firing_range(), choice);
    shotgun.input(0, false, false, false, true);
    shotgun.steps();
    check(shotgun.player().weapon.ammo == 5, "five pellets consume one shell");
    check(shotgun.script.world().actors[1]->unit().hp == 840,
        "five pellets damage independently");
    check(shotgun.script.world().actors[2]->unit().hp == 1000,
        "shotgun cannot penetrate first target");
}

// 验证治疗前摇、同Tick受伤取消、次数和补给防止资源复制。
void tools(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.hp = 60;
        player.health_delay = 300;
        player.health_rem = 0;
    });
    check(game.action("use", 2).empty(), "medkit starts");
    game.steps(119);
    check(game.player().hp == 60 && game.player().get_tool_count(2) == 3,
        "windup does not heal or consume");
    game.setup([&](hunter::Player&, hunter::World& world)
    {
        world.raid.damage_tick = game.tick + 1;
    });
    game.steps();
    check(game.player().hp == 60 && game.player().get_tool_count(2) == 3
        && game.player().use_slot == 0, "damage on completion cancels without consumption");
    check(game.action("use", 2).empty(), "medkit restarts");
    game.steps(120);
    check(game.player().hp == 110 && game.player().get_tool_count(2) == 2,
        "healing crosses health segments and consumes once");
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.hp = 60;
        player.health_delay = 300;
        player.health_rem = 0;
    });
    check(game.action("use", 5).empty(), "needle starts");
    game.steps(90);
    check(game.player().hp == 150 && game.player().get_tool_cfg(5) == "0",
        "consumable empties its slot");
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.x = 3400;
        player.y = 0;
        player.vx = 0;
        player.vy = 0;
        player.grounded = true;
    });
    u32 supply = 0;

    for (const auto& scene : game.script.world().content.at("scenes"))
    {
        if (scene.at("kind") == "supply")
        {
            supply = static_cast<u32>(std::stoul(scene.at("id").get<Str>()));
        }
    }

    check(supply != 0 && game.action("interact", 0, supply).empty(), "supply accepted");
    check(game.player().get_tool_count(2) == 3, "supply restores one regular charge");
    check(!game.action("interact", 0, supply).empty(), "supply used only once");
    game.roundtrip();
}

// 验证梯上只处理移动、离梯不重放边沿动作以及近战体力门禁。
void ladders(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.x = 6000;
        player.y = 0;
        player.vx = 0;
        player.vy = 0;
        player.grounded = true;
    });
    check(game.action("interact", 0, 1).empty(), "ladder entry accepted");
    const auto ammo = game.player().weapon.ammo;
    check(game.action("use", 6) == "action_locked", "ladder blocks consumable actions");
    check(game.action("interact", 0, 1) == "action_locked", "ladder blocks interaction");
    game.input(1, true, true, true, true, true, 1000, 0, 1);
    game.steps();
    check(game.player().ladder_id == 1 && game.player().y == 100
        && game.player().weapon.ammo == ammo && !game.player().jump && !game.player().fire,
        "ladder consumes only vertical movement");
    game.roundtrip();
    game.steps(14);
    check(game.player().y == 1500 && game.player().ladder_id == 0 && game.player().grounded,
        "upward movement exits onto clear platform at ladder top");
    game.input();
    game.steps();
    check(game.player().ladder_id == 0 && game.player().grounded
        && game.player().weapon.ammo == ammo && game.player().weapon.reload_ticks == 0,
        "leaving ladder never replays discarded fire or reload");
    game.setup([](hunter::Player& player, hunter::World&) { player.stamina = 0; });
    check(game.action("melee") == "action_locked", "zero stamina rejects melee");
    game.setup([](hunter::Player& player, hunter::World&) { player.stamina = 20; });
    check(game.action("melee").empty(), "available stamina accepts melee edge");
    game.steps();
    check(game.player().stamina == 0 && game.player().melee_ticks == 30
        && game.player().stamina_delay == 120, "melee consumes stamina once and resets recovery");
    game.roundtrip();
}

// 验证炸药预留容量、前摇取消及飞行爆炸的唯一生命周期。
void explosives(const hunter::Cfg& cfg)
{
    Game game(cfg, firing_range());
    check(game.action("use", 6).empty(), "explosive windup starts");
    check(game.script.world().projectile_free() == 15, "windup reserves projectile capacity");
    check(game.action("switch_weapon", 2).empty(), "switch cancels explosive");
    check(game.script.world().projectile_free() == 16
        && game.player().get_tool_count(6) == 1, "cancel releases capacity without consuming");
    check(game.action("use", 6).empty(), "explosive restarts");
    game.steps(180);
    check(game.player().get_tool_cfg(6) == "0", "launch consumes explosive");
    check(game.script.world().projectile_free() == 15, "projectile remains active after launch");
    game.roundtrip();
    game.steps(17);
    check(game.script.world().projectile_free() == 16, "range expiry explodes and releases slot");
    check(game.player().hp == 150, "explosion never harms owner");
    const auto hp = game.script.world().actors[1]->unit().hp;
    check(hp < 1000, "explosion damages nearby monster");
    game.steps(20);
    check(game.script.world().actors[1]->unit().hp == hp, "explosion cannot repeat");
}
}

// 两种Runtime运行完全相同的玩法契约，签名制品只改变加载方式。
int main(int argc, char** argv)
{
    Str stage = "setup";
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
        stage = "loadouts";
        loadouts(cfg);
        stage = "vitals";
        vitals(cfg);
        stage = "weapons";
        weapons(cfg);
        stage = "tools";
        tools(cfg);
        stage = "ladders";
        ladders(cfg);
        stage = "explosives";
        explosives(cfg);
        std::cout << "gameplay contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << stage << ": " << error.what() << '\n';
        return 1;
    }
}

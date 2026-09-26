// 在真实 Luax Runtime 中验证梯子门禁、掩体穿透、致命伤取消和投射容量边界。
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

// 保留正式配置字段，只冻结远处怪物以隔离需要精确计时的场景。
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

// 保留正式出生身份并将两个普通怪放在射击线上，冻结移动便于核对伤害。
Json range_content()
{
    auto value = content();
    value["map"]["solids"] = Json::array();
    value["scenes"] = Json::array();
    value["map"]["enemies"][0]["x"] = 4000;
    value["map"]["enemies"][0]["patrol_min"] = 3999;
    value["map"]["enemies"][0]["patrol_max"] = 4001;
    value["map"]["enemies"][1]["x"] = 5000;
    value["map"]["enemies"][1]["patrol_min"] = 4999;
    value["map"]["enemies"][1]["patrol_max"] = 5001;
    value["monsters"]["1002"]["hp"] = 1000;
    return value;
}

// 梯子穿过平台时合法，读条被取消，非移动意图不能在离梯后回放。
void ladders(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.x = 4700;
        player.hp = 60;
    });
    check(game.action("use", 2).empty(), "ladder fixture begins medkit");
    check(game.action("interact", 0, 1).empty(), "ladder entry accepted");
    check(game.player().ladder_id == 1 && game.player().use_slot == 0
        && game.player().get_tool_count(2) == 3, "entry cancels without consuming");
    game.input(0, false, false, false, false, false, 1000, 0, -1);
    game.steps();
    check(game.player().ladder_id == 1 && game.player().y == 0 && !game.player().grounded,
        "blocked standing space at bottom keeps player on ladder");
    game.input(0, true, true, true, true, true, 1000, 0, 1);
    game.steps();
    check(game.player().y == 100 && !game.player().prone && !game.player().jump
        && !game.player().fire && !game.player().reload, "only ladder movement remains");
    check(game.action("melee") == "action_locked" && game.action("use", 2) == "action_locked"
        && game.action("switch_weapon", 2) == "action_locked"
        && game.action("select_tool", 1) == "action_locked"
        && game.action("interact", 0, 1) == "action_locked",
        "ladder rejects every gameplay action including interaction");
    game.roundtrip();
    game.steps(14);
    check(game.player().y == 1500 && game.player().ladder_id == 0 && game.player().grounded,
        "upward movement exits at supported top without passing endpoint");
    const auto ammo = game.player().weapon.ammo;
    game.steps();
    check(game.player().weapon.ammo == ammo && !game.player().jump,
        "exit does not replay old fire or jump");
    game.roundtrip();
    auto data = content();
    data["map"]["solids"] = Json::array({{{"id", "904"}, {"x", 6100}, {"y", 2000},
        {"w", 600}, {"h", 300}}});
    Game blocked(cfg, data);
    blocked.setup([](hunter::Player& player, hunter::World&) { player.x = 4700; });
    check(blocked.action("interact", 0, 1).empty(), "blocked top fixture enters ladder");
    blocked.steps();
    check(blocked.player().ladder_id == 1 && blocked.player().y == 0,
        "stationary endpoint does not trigger automatic exit");
    blocked.input(0, false, false, false, false, false, 1000, 0, 1);
    blocked.steps(15);
    check(blocked.player().ladder_id == 1 && blocked.player().y == 1500,
        "blocked standing space at top keeps player on ladder");
    check(blocked.action("interact", 0, 1) == "action_locked",
        "blocked endpoint cannot bypass geometry through interaction");
    blocked.roundtrip();
    blocked.input(0, false, false, false, false, false, 1000, 0, -1);
    blocked.steps(15);
    check(blocked.player().ladder_id == 0 && blocked.player().y == 0
        && blocked.player().grounded, "downward movement exits at clear bottom");
    blocked.roundtrip();
}

// 暂停会清除输入，但不能把左向匍匐的瞄准恢复到局部后方。
void pause_aim(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.input(-1, false, true, false, false, false, -1000, -1000);
    game.steps();
    take(game.script.event(4, R"({"v":6,"paused":true})"));
    check(game.player().prone && game.player().facing == -1
        && game.player().aim_x <= 0 && game.player().aim_y >= 0,
        "pause keeps prone aim in the local forward upper quadrant");
    game.roundtrip();
}

// 掩体只衰减不消耗穿怪次数，Boss 和不可穿掩体都会终止射线。
void barriers(const hunter::Cfg& cfg)
{
    auto data = range_content();
    data["scenes"].push_back({{"id", "31"}, {"kind", "cover"}, {"x", 3000},
        {"y", 0}, {"w", 100}, {"h", 1600}, {"penetrable", true}});
    Game soft(cfg, data);
    soft.input(0, false, false, false, true);
    soft.steps();
    check(soft.script.world().actors[1]->unit().hp == 924
        && soft.script.world().actors[2]->unit().hp == 974,
        "cover loss precedes first hit without spending monster penetration");
    data["scenes"][0]["penetrable"] = false;
    Game hard(cfg, data);
    hard.input(0, false, false, false, true);
    hard.steps();
    check(hard.script.world().actors[1]->unit().hp == 1000,
        "nonpenetrable cover blocks all downstream targets");
    data["scenes"][0]["x"] = 3700;
    data["scenes"][0]["penetrable"] = true;
    Game same_plane(cfg, data);
    same_plane.input(0, false, false, false, true);
    same_plane.steps();
    check(same_plane.script.world().actors[1]->unit().hp == 924,
        "equal distance cover is ordered before monster regardless of its config id");
    data = range_content();
    data["monsters"]["1002"]["rank"] = 3;
    Game boss(cfg, data);
    boss.input(0, false, false, false, true);
    boss.steps();
    check(boss.script.world().actors[1]->unit().hp == 874
        && boss.script.world().actors[2]->unit().hp == 1000, "boss ends penetration");
}

// 医疗完成 Tick 的致命攻击必须先取消，玩家先击杀的怪物不能再反击。
void lethal_order(const hunter::Cfg& cfg)
{
    auto data = range_content();
    data["monsters"]["1002"]["damage"] = 100;
    data["monsters"]["1002"]["windup"] = 0;
    data["monsters"]["1002"]["recover"] = 0;
    data["monsters"]["1002"]["attack_range"] = 900;
    data["monsters"]["1002"]["detect_range"] = 900;
    Game hurt(cfg, data);
    hurt.setup([](hunter::Player& player, hunter::World&) { player.hp = 60; });
    check(hurt.action("use", 2).empty(), "lethal fixture starts medkit");
    hurt.steps(119);
    hurt.setup([](hunter::Player&, hunter::World& world)
    {
        auto& enemy = std::get<hunter::Monster>(world.actors[1]->value);
        enemy.x = 2500;
        enemy.attack_ticks = 0;
    });
    hurt.steps();
    check(!hurt.player().alive && hurt.player().hp == 0 && hurt.player().use_slot == 0
        && hurt.player().get_tool_count(2) == 3, "lethal completion cancels without resurrection");
    check(hurt.script.world().phase == "Settling"
        && hurt.script.world().raid.player_state == "Dead", "death remains settlement outcome");
    Game kill(cfg, data);
    kill.setup([](hunter::Player&, hunter::World& world)
    {
        auto& enemy = std::get<hunter::Monster>(world.actors[1]->value);
        enemy.x = 2500;
        enemy.hp = 100;
        enemy.attack_ticks = 0;
    });
    kill.input(0, false, false, false, true);
    kill.steps();
    check(kill.player().hp == 150 && !kill.script.world().actors[1]->unit().alive,
        "player lethal shot precedes monster attack");
}

// 完全耗尽普通工具保留槽位，不能补给时不消耗箱子或随机状态。
void resource_edges(const hunter::Cfg& cfg)
{
    Game game(cfg);
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.x = 3400;
        player.hp = 40;
        player.health_delay = 300;
        player.change_tool(2, "30002", 1);
    });
    check(game.action("use", 2).empty(), "last tool charge starts");
    game.steps(120);
    check(game.player().get_tool_cfg(2) == "30002" && game.player().get_tool_count(2) == 0,
        "exhausted ordinary tool keeps identity and slot");
    check(!game.action("use", 2).empty(), "empty tool cannot start");
    check(game.action("interact", 0, 2).empty(), "supply restores exhausted ordinary tool");
    check(game.player().get_tool_count(2) == 1, "supply adds exactly one charge");
    Game full(cfg);
    full.setup([](hunter::Player& player, hunter::World&) { player.x = 3400; });
    const auto random = full.script.world().raid.random;
    check(!full.action("interact", 0, 2).empty(), "full equipment rejects supply");
    check(!full.script.world().scene_used(2) && full.script.world().raid.random == random,
        "failed supply preserves box and random state");
}

// 投射容量不足时没有资源变化，爆炸受实心遮挡且永不伤害投掷者。
void explosive_edges(const hunter::Cfg& cfg)
{
    Game full(cfg, range_content());
    full.setup([](hunter::Player&, hunter::World& world)
    {
        for (i32 index = 0; index < 16; ++index)
        {
            world.spawn_projectile("30006", 2000, 800, 100, 0, 1600);
        }
    });
    check(!full.action("use", 6).empty(), "full projectile collection rejects windup");
    check(full.player().use_slot == 0 && full.player().get_tool_count(6) == 1
        && full.script.world().projectile_free() == 0, "capacity rejection is atomic");
    auto data = range_content();
    data["map"]["solids"].push_back(
        {{"id", "288"}, {"x", 4500}, {"y", 0}, {"w", 100}, {"h", 2000}});
    Game blast(cfg, data);
    check(blast.action("use", 6).empty(), "occlusion fixture starts explosive");
    blast.steps(197);
    check(blast.script.world().actors[1]->unit().hp == 875
        && blast.script.world().actors[2]->unit().hp == 1000,
        "blast hurts visible target while solid shields equally nearby target");
    check(blast.player().hp == 150, "blast excludes its owner");
}
}

// 两种脚本加载模式执行同一组边界验收。
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
        ladders(cfg);
        pause_aim(cfg);
        barriers(cfg);
        lethal_order(cfg);
        resource_edges(cfg);
        explosive_edges(cfg);
        std::cout << "gameplay edges passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

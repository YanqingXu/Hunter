// 在真实 Luax Runtime 中验证交互读条中断、引信倒计时和连锁爆炸唯一结算。
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

// 使用合法的最小感知和移动参数，隔离需要精确计时的远处怪物。
Json content()
{
    auto value = hunter::test_content();

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
        take(script.open(hunter::test_cfg(cfg, data),
            Json{{"v", 8}, {"snapshot_every", 3}}.dump()));
        take(script.event(2, R"({"v":8,"req_id":"login","player_id":"1"})"));
        const auto accepted = take(script.check_loadout(loadout));
        take(script.change([&](hunter::World& world) { world.prepare_loadout(accepted); }));
        take(script.event(3, R"({"v":8,"req_id":"start","after_match_id":"0",
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
        const auto result = take(script.event(5, Json{{"v", 8}, {"req_id", "action"},
            {"kind", kind}, {"slot", slot}, {"target_id", std::to_string(target)},
            {"death_seq", "0"}, {"action_seq", std::to_string(++action_seq)}}.dump()));

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
        check(take(script.export_state()) == saved, "v7 state roundtrip");
    }
};


// 读条取消不消费补给，完成时才原子提交场景和工具状态。
void channels(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["scenes"][1]["interaction"] = {{"mode", "channel"}, {"hold_ticks", 3}};
    Game game(cfg, doc);
    game.setup([](hunter::Player& player, hunter::World&) { player.tools[1].count = 2; });
    check(game.action("interact", 0, 2).empty(), "channel starts");
    check(game.script.world().scene_index() == 2 && game.player().tools[1].count == 2,
        "starting channel does not consume supply");
    game.roundtrip();
    game.input(1);
    game.steps();
    check(game.script.world().scene_index() == 0 && !game.script.world().scene_used(2),
        "movement interrupts without consuming supply");
    game.input();
    check(game.action("interact", 0, 2).empty(), "channel restarts");
    game.steps(2);
    check(!game.script.world().scene_used(2), "channel cannot finish early");
    game.steps();
    check(game.script.world().scene_used(2) && game.player().tools[1].count == 3,
        "completed channel commits one charge");
    check(game.action("interact", 0, 2) == "already_used", "used scene cannot restart");
    game.roundtrip();
}

// 枪弹先点燃首桶，引信到期才爆炸，附近桶由爆炸立即引爆且都只伤害一次。
void barrels(const hunter::Cfg& cfg)
{
    auto doc = content();
    for (i32 index = 0; index < 2; ++index)
    {
        doc["scenes"].push_back({{"id", std::to_string(4 + index)}, {"kind", "barrel"},
            {"x", 2700 + index * 600}, {"y", 0}, {"w", 300}, {"h", 1000},
            {"penetrable", false}, {"interaction", false},
            {"barrel", {{"hp", 10}, {"fuse_ticks", 3}, {"radius", 1200}, {"damage", 10}}}});
    }

    Game game(cfg, doc);
    game.input(0, false, false, false, true);
    game.steps();
    check(game.script.world().barrel_fuse(4) == 3 && game.script.world().barrel_hp(4) == 0,
        "bullet destruction arms full fuse without immediate detonation");
    check(!game.script.world().barrel_exploded(4)
        && game.script.world().barrel_hp(5) == 10, "second barrel remains intact before fuse");
    game.roundtrip();
    game.input();
    game.setup([](hunter::Player& player, hunter::World&)
    {
        player.quiet_ticks = 180;
        player.vision = true;
    });
    game.steps(2);
    check(!game.script.world().barrel_exploded(4), "fuse does not finish early");
    game.steps();
    check(game.script.world().barrel_exploded(4) && game.script.world().barrel_exploded(5),
        "explosion immediately triggers the neighboring barrel");
    check(game.player().hp == 130, "both barrels damage the player once");
    check(game.player().quiet_ticks > 0 && !game.player().vision,
        "target suppression is not invulnerability and damage exits special vision");
    game.steps(3);
    check(game.player().hp == 130, "exploded barrels never deal damage again");
    game.roundtrip();
    const auto before = take(game.script.export_state());
    auto invalid = Json::parse(before);
    invalid["scenes_state"]["barrels"][3]["hp"] = 1;
    check(!game.script.import_state(invalid.dump()), "exploded barrel cannot have health");
    check(take(game.script.export_state()) == before,
        "invalid state leaves active world unchanged");
}
}

// 源码与签名模式复用相同的场景行为契约。
int main(int argc, char** argv)
{
    Str stage = "initialization";
    try
    {
        check(argc == 2 || argc == 3, "source or bundle and policy required");
        hunter::Cfg cfg;
        cfg.script_memory_bytes = 32 * 1024 * 1024;
#if HUNTER_PRODUCTION
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[2];
#else
        cfg.source_path = argv[1];
#endif
        stage = "channels";
        channels(cfg);
        stage = "barrels";
        barrels(cfg);
        std::cout << "scene contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << stage << ": " << error.what() << '\n';
        return 1;
    }
}

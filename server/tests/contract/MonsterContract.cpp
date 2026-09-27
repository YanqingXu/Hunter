// 验证波次原子发布、召唤归属，以及真实 Luax 中的狂暴阈值和召唤生命周期。
#include "common/Types.h"
#include "core/Cfg.h"
#include "game/World.h"
#include "script/Script.h"
#include "../fixtures/NativeWorld.h"
#include "../fixtures/ScriptCfg.h"
#include <iostream>
#include <stdexcept>

namespace
{
using Json = nlohmann::json;

// 将失败的不变量转换为清晰的测试诊断。
void check(bool value, const Str& reason)
{
    if (!value)
    {
        throw std::runtime_error(reason);
    }
}

// 保留真实脚本入口的失败原因，不把失败结果转为默认成功。
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

// 检查非法原生调用确实拒绝。
void rejects(const Func<void()>& action)
{
    bool rejected = false;

    try
    {
        action();
    }
    catch (const std::exception&)
    {
        rejected = true;
    }

    check(rejected, "invalid native operation was accepted");
}

// 建立仅用于原生边界验证的合法活动世界。
void begin(hunter::World& world)
{
    world.access.writable = true;
    hunter::test::configure(world);
    world.login("1");
    world.prepare_loadout(hunter::test::loadout());
    world.begin("begin", "0", "1", "1");
    hunter::test::spawn(world, "player");
}

// 失败波次不得移动赏金或消耗身份，成功波次只发布一次且保留旧怪。
void atomic_wave()
{
    hunter::World world;
    begin(world);
    const auto old_enemy = hunter::test::spawn(world, "monster", "2");
    const auto bounty = world.create_item("2800002", 1);
    world.items[world.item_slot(1)]->value.place = "Ground";
    const Json batch = {{"owner", "1"}, {"items", {{{"id", bounty},
        {"expected_count", 1}, {"count", 1}, {"place", "Bag"}}}},
        {"tools", Json::array()}};
    const auto specs = Json::array({hunter::test::monster_spec("2"),
        hunter::test::monster_spec("3")});
    auto invalid = specs;
    invalid[1]["width"] = 601;
    rejects([&] { world.commit_wave(batch.dump(), invalid.dump(), bounty); });
    check(world.last_entity_id == 2 && world.count() == 2 && world.wave == 1
        && world.items[world.item_slot(1)]->value.place == "Ground",
        "invalid final spawn must preserve bounty and every actor");
    world.bag_slots = 0;
    check(world.commit_wave(batch.dump(), specs.dump(), bounty) == "bag_full",
        "full bag rejects complete wave transaction");
    check(world.last_entity_id == 2 && world.count() == 2 && world.wave == 1,
        "bag rejection must preserve wave and identity");
    world.bag_slots = 8;
    check(world.commit_wave(batch.dump(), specs.dump(), bounty).empty(), "wave commits");
    check(world.wave == 2 && world.bounty_id == 1 && world.count() == 4
        && world.contains(old_enemy)
        && world.items[world.item_slot(1)]->value.place == "Bag",
        "wave adds actors without replacing living first-wave actors");
    check(world.commit_wave(batch.dump(), specs.dump(), bounty) == "wave_already_triggered"
        && world.last_entity_id == 4, "duplicate wave cannot publish a third wave");
}

// 容量不足和重复召唤都在实体身份分配之前失败。
void native_summon()
{
    hunter::World world;
    begin(world);
    const auto owner = hunter::test::spawn(world, "monster", "2");
    const auto spec = hunter::test::monster_spec("2").dump();
    const auto id = world.spawn_summon(owner, 1, spec);
    check(id == "3", "first summon receives a unique entity identity");
    const auto& summon = std::get<hunter::Monster>(world.actors[world.slot(3)]->value);
    check(summon.owner_id == 2 && summon.owner_ability == 1 && summon.wave == 1,
        "summon records immutable source and wave");
    check(world.spawn_summon(owner, 2, spec) == ":summon_exists"
        && world.last_entity_id == 3, "all abilities share one active summon limit");
    world.remove(id);
    world.flush();
    check(world.spawn_summon(owner, 1, spec) == "4", "removed summon identity is not reused");
    auto& monster = std::get<hunter::Monster>(world.actors[world.slot(2)]->value);
    monster.set_rage_mask(3);
    rejects([&] { monster.set_rage_mask(1); });
    monster.write_ability("101", "windup", 2);
    monster.set_hp(0);
    monster.set_alive(false);
    monster.set_state("dead");
    check(monster.active_ability == 0 && monster.ability_phase == "idle"
        && monster.ability_ticks == 0, "death clears every unfinished cast");
    check(world.spawn_summon(owner, 1, spec) == ":invalid_summoner",
        "dead owner cannot summon");

    hunter::World full;
    begin(full);
    for (i32 index = 0; index < 62; ++index)
    {
        hunter::test::spawn(full, "monster", "2");
    }

    const auto bounty = full.create_item("2800002", 1);
    full.items[full.item_slot(1)]->value.place = "Ground";
    const Json batch = {{"owner", "1"}, {"items", {{{"id", bounty},
        {"expected_count", 1}, {"count", 1}, {"place", "Bag"}}}},
        {"tools", Json::array()}};
    const auto specs = Json::array({hunter::test::monster_spec("2"),
        hunter::test::monster_spec("3")}).dump();
    check(full.commit_wave(batch.dump(), specs, bounty) == "entity_capacity"
        && full.last_entity_id == 63 && full.wave == 1
        && full.items[full.item_slot(1)]->value.place == "Ground",
        "wave capacity rejection cannot partially transfer bounty");
}

// 创建显式 AI 配置，数值仅属于测试夹具而非生产内容。
Json ai(bool basic, const Json& abilities = Json::array())
{
    return {{"alert_speed", 50}, {"disengage_ticks", 3},
        {"damage_reduction_bp", 0}, {"basic", basic}, {"abilities", abilities},
        {"rage_thresholds", Json::array()}, {"rage_ticks", 0}, {"rage_ability_id", "0"}};
}

// 派生可重现的战斗内容，不改变真实工作簿。
Json content()
{
    auto doc = hunter::test_content();
    doc["legacy_ai"] = false;
    doc["abilities"] = Json::object();
    doc["encounter"] = false;
    for (auto& monster : doc["monsters"])
    {
        monster["ai"] = false;
    }

    return doc;
}

struct Game
{
    hunter::Script script;
    u64 tick = 0;
    u64 action_seq = 0;

    // 用正式配置校验、脚本和宿主入口启动单局。
    Game(const hunter::Cfg& cfg, const Json& doc)
    {
        take(script.open(hunter::test_cfg(cfg, doc),
            Json{{"v", 8}, {"snapshot_every", 3}}.dump()));
        take(script.event(2, R"({"v":8,"req_id":"login","player_id":"1"})"));
        const auto loadout = take(script.check_loadout({}));
        take(script.change([&](hunter::World& world) { world.prepare_loadout(loadout); }));
        take(script.event(3, R"({"v":8,"req_id":"start","after_match_id":"0",
            "match_id":"1","world_id":"1"})"));
    }

    // 推进精确数量的模拟 Tick。
    void steps(i32 count = 1)
    {
        for (i32 index = 0; index < count; ++index)
        {
            take(script.tick(++tick, 1.0 / 60));
        }
    }

    // 在权威线程片段中设置测试初态。
    void setup(const Func<void(hunter::World&)>& fn)
    {
        take(script.change(fn));
    }

    // 通过正式动作入口执行场景或拾取请求，保留业务拒绝。
    Str action(const Str& kind, const Str& target, const Str& death = "0")
    {
        const auto result = take(script.event(5, Json{{"v", 8}, {"req_id", "action"},
            {"kind", kind}, {"slot", 0}, {"target_id", target},
            {"action_seq", std::to_string(++action_seq)}, {"death_seq", death}}.dump()));
        for (const auto& output : result)
        {
            if (output.message.has_error())
            {
                return output.message.error().code();
            }
        }

        return "";
    }

    // 借用当前怪物，只在下一次入口之前有效。
    const hunter::Monster& monster(u64 id) const
    {
        const auto& world = script.world();
        return std::get<hunter::Monster>(world.actors[world.slot(id)]->value);
    }

    // 借用当前玩家，只在下一次入口之前有效。
    const hunter::Player& player() const
    {
        return std::get<hunter::Player>(script.world().actors[0]->value);
    }

    // 验证全部技能、来源与计时可以往返且未丢失权威状态。
    void roundtrip()
    {
        const auto saved = take(script.export_state());
        take(script.import_state(saved));
        check(take(script.export_state()) == saved, "encounter state roundtrip");
        const auto stats = take(script.stats());
        std::cout << "monster runtime peak bytes=" << stats.peak_bytes << '\n';
    }
};

// 验证严格阈值、多跨越合并、前摇、回血不重置及致死优先。
void rage(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["abilities"]["101"] = {{"kind", "melee"}, {"range", 7000}, {"damage", 7},
        {"windup", 2}, {"recover", 1}, {"cooldown", 20},
        {"summon_cfg_id", "0"}, {"despawn_ticks", 0}};
    auto& boss = doc["monsters"]["1003"];
    boss["ai"] = ai(false, {{{"cfg_id", "101"}, {"phase", "rage"}, {"priority", 1}}});
    boss["ai"]["rage_thresholds"] = {180, 120, 60};
    boss["ai"]["rage_ticks"] = 5;
    boss["ai"]["rage_ability_id"] = "101";
    Game game(cfg, doc);
    const auto initial = game.monster(4).x;
    game.steps(4);
    check(game.monster(4).x == initial && !game.monster(4).aware,
        "unalerted Boss remains stationary");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Player>(world.actors[0]->value).x = 20000;
    });
    game.steps();
    check(game.monster(4).aware && game.monster(4).vx == -25,
        "alerted Boss uses patrol speed");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(4)]->value).hp = 180;
    });
    game.steps();
    check(game.monster(4).rage_mask == 0, "exact threshold does not trigger rage");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(4)]->value).hp = 59;
    });
    const auto hp = game.player().hp;
    game.steps();
    check(game.monster(4).rage_mask == 7 && game.monster(4).rage_ticks == 5
        && game.monster(4).ability_ticks == 2 && game.player().hp == hp,
        "multiple thresholds start exactly one cast with its configured windup");
    game.roundtrip();
    take(game.script.event(4, R"({"v":8,"paused":true})"));
    game.steps(3);
    check(game.monster(4).rage_ticks == 5 && game.monster(4).ability_ticks == 2
        && game.monster(4).ability_cds[0] == 20, "pause freezes all monster timers");
    take(game.script.event(4, R"({"v":8,"paused":false})"));
    game.steps(2);
    check(game.player().hp == hp - 7, "rage attack lands exactly once after windup");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(4)]->value).hp = 240;
    });
    game.steps();
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(4)]->value).hp = 59;
    });
    game.steps();
    check(game.monster(4).rage_mask == 7 && game.monster(4).ability_ticks == 0
        && game.monster(4).ability_cds[0] < 20, "healing never rearms consumed thresholds");
    game.setup([](hunter::World& world)
    {
        auto& boss = std::get<hunter::Monster>(world.actors[world.slot(4)]->value);
        boss.set_hp(0);
        boss.set_alive(false);
        boss.set_state("dead");
    });
    game.steps();
    check(game.monster(4).active_ability == 0, "dead Boss cannot trigger a final rage cast");
}

// 验证召唤在场不计 CD、双圈计时重置、消失后才进入冷却及本体死亡清理。
void summons(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["abilities"]["201"] = {{"kind", "summon"}, {"range", 3000}, {"damage", 0},
        {"windup", 0}, {"recover", 0}, {"cooldown", 10},
        {"summon_cfg_id", "1004"}, {"despawn_ticks", 3}};
    doc["monsters"]["1004"] = doc["monsters"]["1002"];
    doc["monsters"]["1004"]["rank"] = 4;
    doc["monsters"]["1004"]["drops"] = Json::array();
    doc["monsters"]["1004"]["detect_range"] = 1000;
    doc["monsters"]["1002"]["ai"] = ai(false,
        {{{"cfg_id", "201"}, {"phase", "alert"}, {"priority", 1}}});
    Game game(cfg, doc);
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(2)]->value).x = 4000;
    });
    game.steps();
    check(game.script.world().count() == 5 && game.monster(5).owner_id == 2
        && game.monster(2).ability_cds[0] == 0, "living summon holds its cooldown at zero");
    game.roundtrip();
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Player>(world.actors[0]->value).x = 23000;
    });
    game.steps(2);
    check(game.script.world().contains("5") && game.monster(5).outside_ticks == 2,
        "both circles must remain empty for the full delay");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Player>(world.actors[0]->value).x = 2000;
    });
    game.steps();
    check(game.monster(5).outside_ticks == 0, "reentering either circle resets despawn delay");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Player>(world.actors[0]->value).x = 23000;
    });
    game.steps(3);
    check(!game.script.world().contains("5") && game.monster(2).ability_cds[0] == 10,
        "despawn starts the complete summon cooldown");
    game.roundtrip();
}

// 减伤由统一伤害入口解释，测试数值不改变生产怪物配置。
void reduction(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["monsters"]["1002"]["hp"] = 1000;
    doc["monsters"]["1002"]["ai"] = ai(false);
    doc["monsters"]["1002"]["ai"]["damage_reduction_bp"] = 5000;
    Game game(cfg, doc);
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(2)]->value).x = 3000;
    });
    hunter::wire::FrameInput input;
    input.set_seq(1);
    input.set_world_id(1);
    input.set_match_id(1);
    input.set_aim_x(1000);
    input.set_fire(true);
    take(game.script.input(input, 1));
    game.steps();
    const auto amount = doc["weapons"]["1"]["damage"].get<i32>() / 2;
    check(game.monster(2).hp == 1000 - amount, "passive reduction applies once before damage");
    game.roundtrip();
}

// 真实拾取入口同时交付赏金和第二波，扩大警戒也作用于第一波存活怪。
void pickup_wave(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["encounter"] = {{"bounty_cfg_id", "2800002"}, {"alert_scale_bp", 15000},
        {"second_wave", {"12", "13"}}};
    Game game(cfg, doc);
    game.setup([](hunter::World& world)
    {
        const auto id = world.create_item("2800002", 1);
        check(id == "1", "first bounty identity");
        auto& item = world.items[world.item_slot(1)]->value;
        item.place = "Ground";
        item.x = 2000;
        std::get<hunter::Monster>(world.actors[world.slot(2)]->value).x = 10000;
    });
    check(game.action("pickup", "1").empty(), "bounty pickup is accepted");
    check(game.script.world().wave == 2 && game.script.world().count() == 6
        && game.monster(2).wave == 1 && game.monster(5).wave == 2,
        "accepted pickup retains first wave and appends a distinct second wave");
    check(game.action("pickup", "1") == "already_picked"
        && game.script.world().count() == 6, "duplicate bounty pickup does not respawn");
    game.steps();
    check(game.monster(2).aware, "expanded detection applies to first-wave survivor");
    game.roundtrip();
}

// 区域 Boss 只出生一个，线索排除非 Boss 区域且重放不消费随机数。
void exploration(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["map"]["enemies"].push_back({{"cfg_id", "1003"}, {"spawn_id", "15"},
        {"x", 8000}, {"y", 0}, {"patrol_min", 7800}, {"patrol_max", 8100}});
    for (const auto& [id, x] : {std::pair{"4", 2800}, std::pair{"5", 18000}})
    {
        doc["scenes"].push_back({{"id", id}, {"kind", "clue"}, {"x", x}, {"y", 0},
            {"w", 200}, {"h", 200}, {"penetrable", false}, {"barrel", false},
            {"interaction", {{"mode", "instant"}, {"hold_ticks", 0}}}});
    }

    doc["exploration"] = {{"regions", {
        {{"id", "11"}, {"x", 0}, {"y", 0}, {"w", 12000}, {"h", 10000},
            {"boss_spawns", {"15"}}, {"clues", {"4"}}},
        {{"id", "22"}, {"x", 12000}, {"y", 0}, {"w", 12000}, {"h", 10000},
            {"boss_spawns", {"14"}}, {"clues", {"5"}}}}}};
    Game game(cfg, doc);
    check(game.script.world().count() == 4 && game.script.world().boss_region != 0
        && game.monster(4).spawn_id == game.script.world().boss_spawn,
        "only the randomly selected regional Boss is created");
    check(game.action("interact", "4").empty(), "first clue is accepted");
    const auto expected = game.script.world().boss_region == 11 ? 2U : 1U;
    check(game.script.world().excluded_regions == expected
        && game.script.world().used_scenes[3], "clue excludes only a non-Boss region");
    const auto random = game.script.world().raid.random;
    check(game.action("interact", "4") == "already_used"
        && game.script.world().raid.random == random, "duplicate clue preserves random state");
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Player>(world.actors[0]->value).x = 18000;
    });
    check(game.action("interact", "5") == "no_region_left"
        && !game.script.world().used_scenes[4]
        && game.script.world().raid.random == random,
        "exhausted candidates do not consume clue or random state");
    game.roundtrip();
}

// 技能拾取立即影响有效属性，重复与已消费技能都不得重复取得或发放备弹。
void skill_pickup(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["skills"]["1"] = {{"name", "测试技能"}, {"target", "player"},
        {"category", "numeric"}, {"kind", "death"}, {"cost", 2}, {"effects", {
            {{"stat", "run_speed"}, {"op", "add"}, {"value", 25}},
            {{"stat", "reserve"}, {"op", "add"}, {"value", 5}}}}};
    doc["skills"]["4"] = {{"name", "测试复活"}, {"target", "player"},
        {"category", "mechanic"}, {"kind", "once"}, {"cost", 0}, {"effects", {
            {{"stat", "revive_hp"}, {"op", "add"}, {"value", 25}},
            {{"stat", "revive_stamina"}, {"op", "add"}, {"value", 40}}}}};
    doc["default_loadout"]["skills"] = {"4"};
    doc["items"]["2900001"] = {{"name", "测试技能拾取"}, {"max_stack", 1},
        {"kind", 5}, {"type", 0}, {"skill_cfg_id", "1"}};
    Game game(cfg, doc);
    game.setup([](hunter::World& world)
    {
        world.create_item("2900001", 1);
        auto& item = world.items[world.item_slot(1)]->value;
        item.place = "Ground";
        item.x = 2000;
    });
    const auto reserve = game.player().reserve;
    check(game.action("pickup", "1").empty() && game.player().skills.size() == 2
        && !game.script.world().has_item("1") && game.player().reserve == reserve,
        "skill pickup atomically consumes item without issuing initial ammunition again");
    hunter::wire::FrameInput input;
    input.set_seq(1);
    input.set_world_id(1);
    input.set_match_id(1);
    input.set_move_x(1);
    input.set_aim_x(1000);
    input.set_run(true);
    take(game.script.input(input, 1));
    game.steps();
    check(game.player().vx == 175, "picked skill changes movement in the next simulation tick");
    game.roundtrip();
    game.setup([reserve](hunter::World& world)
    {
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        player.set_reserve(reserve + 5);
        player.set_hp(0);
        player.set_alive(false);
        player.set_vx(0);
        player.set_vy(0);
        world.clear_input();
        player.enter_downed();
    });
    game.roundtrip();
    check(game.action("revive", "0", "1").empty() && game.player().skills[1].spent
        && game.player().reserve == reserve + 5,
        "death skill loss preserves ammunition issued under the previous effective capacity");
    game.roundtrip();
    game.setup([](hunter::World& world)
    {
        world.create_item("2900001", 1);
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        auto& item = world.items[world.item_slot(2)]->value;
        item.place = "Ground";
        item.x = player.x;
    });
    check(game.action("pickup", "2") == "already_known_skill"
        && game.script.world().has_item("2"), "spent skill cannot be reacquired");
    game.steps();
    game.roundtrip();
}

// 等待复活时旧施法和 Boss 阈值继续推进，死亡目标不能受伤或引发新起手。
void downed_cast(const hunter::Cfg& cfg)
{
    auto doc = content();
    doc["skills"]["4"] = {{"name", "测试复活"}, {"target", "player"},
        {"category", "mechanic"}, {"kind", "once"}, {"cost", 0}, {"effects", {
            {{"stat", "revive_hp"}, {"op", "add"}, {"value", 25}},
            {{"stat", "revive_stamina"}, {"op", "add"}, {"value", 40}}}}};
    doc["default_loadout"]["skills"] = {"4"};
    doc["abilities"]["101"] = {{"kind", "melee"}, {"range", 6000}, {"damage", 7},
        {"windup", 3}, {"recover", 2}, {"cooldown", 20},
        {"summon_cfg_id", "0"}, {"despawn_ticks", 0}};
    doc["monsters"]["1002"]["ai"] = ai(false,
        {{{"cfg_id", "101"}, {"phase", "alert"}, {"priority", 1}}});
    auto& boss = doc["monsters"]["1003"];
    boss["ai"] = ai(false, {{{"cfg_id", "101"}, {"phase", "rage"}, {"priority", 1}}});
    boss["ai"]["rage_thresholds"] = {180};
    boss["ai"]["rage_ticks"] = 5;
    boss["ai"]["rage_ability_id"] = "101";
    Game game(cfg, doc);
    game.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(2)]->value).x = 4000;
    });
    game.steps();
    check(game.monster(2).ability_ticks == 3, "ordinary cast begins before player death");
    game.setup([](hunter::World& world)
    {
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        player.set_hp(0);
        player.set_alive(false);
        player.set_vx(0);
        player.set_vy(0);
        world.clear_input();
        player.enter_downed();
        std::get<hunter::Monster>(world.actors[world.slot(4)]->value).hp = 179;
    });
    game.steps();
    check(game.monster(2).ability_ticks == 2 && game.monster(4).rage_mask == 1
        && game.monster(4).rage_ticks == 5 && game.monster(4).active_ability == 0,
        "downed state advances old windup and records rage without acquiring dead targets");
    game.roundtrip();
    game.steps(2);
    check(game.monster(2).ability_phase == "recover" && game.monster(2).ability_ticks == 2
        && game.player().hp == 0, "old cast completes harmlessly into recovery");
    game.steps(3);
    check(game.monster(2).active_ability == 0 && game.monster(4).rage_ticks == 0
        && game.player().downed && game.script.world().phase == "Playing",
        "recovery and rage expire while the player is waiting");
    game.roundtrip();

    Game revived(cfg, doc);
    revived.setup([](hunter::World& world)
    {
        std::get<hunter::Monster>(world.actors[world.slot(2)]->value).x = 4000;
    });
    revived.steps();
    revived.setup([](hunter::World& world)
    {
        auto& player = std::get<hunter::Player>(world.actors[0]->value);
        player.set_hp(0);
        player.set_alive(false);
        player.set_vx(0);
        player.set_vy(0);
        world.clear_input();
        player.enter_downed();
    });
    revived.steps();
    check(revived.action("revive", "0", "1").empty(), "revive precedes old cast completion");
    revived.steps(2);
    check(revived.player().quiet_ticks == 178 && revived.player().hp == 18
        && revived.monster(2).ability_phase == "recover",
        "acquisition suppression does not prevent damage from an already started cast");
    revived.roundtrip();
}
}

// 两种加载模式运行同一套原生和真实脚本契约。
int main(int argc, char** argv)
{
    Str stage = "native";
    try
    {
        atomic_wave();
        native_summon();
        check(argc == 2 || argc == 3, "source or bundle and policy required");
        hunter::Cfg cfg;
        cfg.script_memory_bytes = 32 * 1024 * 1024;
#if HUNTER_PRODUCTION
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[2];
#else
        cfg.source_path = argv[1];
#endif
        stage = "rage";
        rage(cfg);
        stage = "summons";
        summons(cfg);
        stage = "reduction";
        reduction(cfg);
        stage = "pickup wave";
        pickup_wave(cfg);
        stage = "exploration";
        exploration(cfg);
        stage = "skill pickup";
        skill_pickup(cfg);
        stage = "downed cast";
        downed_cast(cfg);
        std::cout << "monster contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << stage << ": " << error.what() << '\n';
        return 1;
    }
}

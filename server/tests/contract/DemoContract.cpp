// 验证免费配装、原生槽位资源、投掷预留及完整增量状态的原子边界。
#include "common/Types.h"
#include "game/World.h"
#include "script/Schema.h"
#include "../fixtures/NativeWorld.h"
#include <iostream>
#include <stdexcept>

namespace
{
// 断言每个业务边界的可观察结果。
void check(bool value, const char* reason)
{
    if (!value)
    {
        throw std::runtime_error(reason);
    }
}

// 确保非法候选被拒绝，不吞掉测试断言失败。
template <typename F>
void rejects(F&& operation, const char* reason)
{
    bool rejected = false;
    try
    {
        operation();
    }
    catch (const std::exception&)
    {
        rejected = true;
    }

    check(rejected, reason);
}

// 重新借用活动玩家，导入状态后禁止继续使用旧对象引用。
hunter::Player& player(hunter::World& world)
{
    return std::get<hunter::Player>(world.actors[world.slot(world.player_entity_id)]->value);
}

// 找出正式默认配装中指定类别的工具槽。
i32 tool_slot(const hunter::World& world, const Str& kind)
{
    const auto& value = std::get<hunter::Player>(
        world.actors[world.slot(world.player_entity_id)]->value);

    for (i32 index = 1; index <= 8; ++index)
    {
        const auto id = value.get_tool_cfg(index);

        if ((id == "30001" && kind == "knife") || (id == "30002" && kind == "medkit")
            || (id == "30004" && kind == "needle") || (id == "30006" && kind == "bomb"))
        {
            return index;
        }
    }

    throw std::runtime_error("missing default tool");
}
// 初始化拒绝不能留下半份结构；出生参数必须绑定已接受的完整配装。
void check_initialization()
{
    hunter::World world;
    rejects([&]
    {
        world.configure({{"map_width", 24000}, {"map_height", 10000},
            {"bag_slots", 8}, {"scene_ids", {"1", "1"}}}, "rejected");
    }, "duplicate scene identity rejected");
    check(!world.configured && world.content_key.empty() && world.map_width == 0
        && world.scene_ids.empty(), "rejected initialization publishes no structure");
    hunter::test::configure(world);
    rejects([&] { hunter::test::configure(world); }, "configuration installs exactly once");
    rejects([&] { world.prepare_loadout(hunter::test::loadout()); }, "loadout write gate");
    world.access.writable = true;
    world.login("73");
    world.prepare_loadout(hunter::test::loadout());
    world.begin("init", "0", "1", "1");
    const auto before = world.document();
    const auto revision = world.revision;
    const auto serial = world.serial;
    auto spec = hunter::test::player_spec();
    spec["weapons"][0]["ammo_cfg_id"] = "2";
    rejects([&] { world.spawn("player", spec.dump()); }, "spawn binds accepted ammunition");
    check(world.document() == before && world.revision == revision && world.serial == serial,
        "failed player initialization preserves world and allocators");
    hunter::test::spawn(world, "player");
    const auto saved = world.save();
    auto invalid = world.document();
    invalid["entities"]["1"]["body"]["width"] = 599;
    const auto active_serial = world.serial;
    const auto active_revision = world.revision;
    rejects([&] { world.load(invalid.dump()); }, "malformed body rejected");
    check(world.save() == saved && world.serial == active_serial
        && world.revision == active_revision, "failed body import preserves handles");
}

// 混合提交必须核对旧实例和场景水位，任一失败不能部分补给或消费物品。
void check_batch()
{
    hunter::World world;
    hunter::test::configure(world);
    world.access.writable = true;
    world.login("73");
    world.prepare_loadout(hunter::test::loadout());
    world.begin("batch", "0", "1", "1");
    hunter::test::spawn(world, "player");
    auto& value = player(world);
    value.change_tool(2, "30002", 0);
    world.drop("2800001", "[1]", value.x, value.y);
    using Json = nlohmann::json;
    Json batch = {{"owner", "73"}, {"items", {{{"id", "1"}, {"expected_count", 1},
        {"count", 0}, {"place", "Ground"}}}}, {"tools", {{{"slot", 2},
        {"expected_instance", "999"}, {"cfg_id", "30002"}, {"count", 1}}}},
        {"scene", {{"index", 2}, {"expected_used", false}, {"used", true}}}};
    const auto before = world.save();
    check(world.commit(batch.dump()) == "stale_tool" && world.save() == before,
        "stale tool rejects entire mixed batch");
    batch["tools"][0]["expected_instance"] = value.get_tool_instance(2);
    batch["tools"][0]["count"] = false;
    rejects([&] { world.commit(batch.dump()); }, "malformed later batch value rejected");
    check(world.save() == before, "malformed batch does not consume earlier item change");
    batch["tools"][0]["count"] = 1;
    check(world.commit(batch.dump()).empty() && !world.has_item("1")
        && world.scene_used(2) && value.get_tool_count(2) == 1,
        "valid mixed batch commits all resources");
    batch["items"] = Json::array();
    batch["tools"][0]["count"] = 2;
    const auto committed = world.save();
    check(world.commit(batch.dump()) == "stale_scene" && world.save() == committed,
        "scene watermark prevents duplicate supply");
    const auto serial = world.serial;
    rejects([&] { world.drop("1", "[1,false]", value.x, value.y); },
        "malformed later drop stack rejected");
    check(world.save() == committed && world.serial == serial,
        "drop validation has no partial publication");
}

}

// 在隔离原生夹具上验证拒绝不消费资源、快照投影与状态往返。
int main()
{
    try
    {
        check_initialization();
        check_batch();
        hunter::World world;
        hunter::test::configure(world);
        world.access.writable = true;
        auto accepted = hunter::test::loadout();
        check(accepted.weapons_size() == 2 && accepted.tools_size() == 2
            && accepted.consumables_size() == 2, "complete default loadout");
        auto invalid = accepted;
        invalid.set_player_cfg_id(0);
        rejects([&] { world.prepare_loadout(invalid); }, "zero player id rejected");
        invalid = accepted;
        invalid.add_weapons()->CopyFrom(accepted.weapons(0));
        rejects([&] { world.prepare_loadout(invalid); }, "weapon capacity rejected");
        world.access.writable = true;
        world.login("73");
        world.prepare_loadout(accepted);
        world.begin("demo", "0", "1", "1");
        check(hunter::test::spawn(world, "player") == "1", "spawn accepted loadout");

        for (const auto spawn : {"2", "3"})
        {
            hunter::test::spawn(world, "monster", spawn);
        }

        check(world.valid(), "new demo world valid");
        const auto original = world.save();
        world.load(original);
        check(world.save() == original, "demo exact state roundtrip");
        auto& value = player(world);
        value.facing = -1;
        value.prone = true;
        world.clear_input();
        check(value.aim_x == -1000, "cleared prone aim follows current facing");
        value.facing = 1;
        hunter::wire::FrameInput input;
        input.set_seq(1);
        input.set_match_id(1);
        input.set_world_id(1);
        input.set_move_x(-1);
        input.set_prone(true);
        check(world.input(input, 1).has_ack() && value.aim_x == -1000 && value.aim_y == 0,
            "zero aim resolves from current movement facing");
        world.clear_input();
        value.facing = 1;
        value.prone = false;
        value.want_prone = false;
        const auto first_cfg = value.get_weapon_cfg(1);
        const auto second_cfg = value.get_weapon_cfg(2);
        const auto first_ammo = value.get_weapon_ammo(1);
        value.set_weapon_ammo(1, first_ammo - 1);
        value.set_weapon_reload_ticks(1, 10);
        value.set_fire(true);
        check(value.switch_weapon(2) && value.get_weapon_cfg(1) == first_cfg
            && value.get_weapon_cfg(2) == second_cfg && value.get_active_weapon() == 2,
            "weapon slots keep stable identity");
        check(value.get_weapon_ammo(1) == first_ammo - 1
            && value.get_weapon_reload_ticks(1) == 0 && !value.fire,
            "switch preserves rounds and cancels reload intent");
        value.switch_weapon(1);
        const auto medkit = tool_slot(world, "medkit");
        const auto medkit_id = value.get_tool_cfg(medkit);
        value.change_tool(medkit, medkit_id, 1);
        check(value.start_use(medkit, 1, false), "begin medical use");
        value.set_use_ticks(0);
        check(value.finish_use(0, false) && value.get_tool_count(medkit) == 0
            && value.get_tool_cfg(medkit) == medkit_id, "empty regular tool keeps slot");
        check(!value.start_use(medkit, 1, false), "empty tool cannot start");
        const auto needle = tool_slot(world, "needle");
        value.change_tool(needle, value.get_tool_cfg(needle), 1);
        check(value.start_use(needle, 1, false), "begin consumable use");
        value.set_use_ticks(0);
        check(value.finish_use(0, true) && value.get_tool_cfg(needle) == "0",
            "empty consumable clears slot");
        const auto bomb = tool_slot(world, "bomb");
        const auto bomb_id = value.get_tool_cfg(bomb);
        const auto bomb_count = value.get_tool_count(bomb);
        check(value.start_use(bomb, 1, true) && world.projectile_free() == 15,
            "bomb reserves flight capacity before use");
        const auto reserved_state = world.save();
        world.load(reserved_state);
        check(world.save() == reserved_state, "reservation state roundtrip");
        player(world).cancel_use();
        check(world.projectile_free() == 16 && player(world).get_tool_count(bomb) == bomb_count,
            "cancel returns reservation without consumption");
        auto& active = player(world);
        check(active.start_use(bomb, 1, true), "restart bomb");
        active.set_use_ticks(0);
        const auto projectile = world.spawn_projectile(bomb_id, active.x, active.y, 100, 0, 1600);
        check(active.finish_use(0, true) && world.projectile_alive(projectile)
            && world.projectile_free() == 15, "completion consumes reservation once");

        for (i32 index = 1; index < 16; ++index)
        {
            world.spawn_projectile(bomb_id, active.x, active.y, 100, 0, 1600);
        }

        check(world.projectile_free() == 0, "projectile capacity bounded");
        active.change_tool(bomb, bomb_id, 1);
        check(!active.start_use(bomb, 1, true) && active.get_tool_count(bomb) == 1,
            "full projectile capacity rejects without consumption");
        world.remove_projectile(projectile);
        check(active.start_use(bomb, 1, true), "released capacity reusable");
        active.cancel_use();
        check(world.get_loadout().SerializeAsString() == accepted.SerializeAsString(),
            "accepted loadout stays frozen after consumption");
        check(world.valid(), "all resource mutations preserve state");
        auto snapshot = world.snapshot(73);
        check(snapshot.snapshot().entities(0).weapons_size() == 2
            && snapshot.snapshot().projectiles_size() == 15, "native nested snapshot projection");
        check(hunter::validate_output(hunter::ScriptOut(snapshot), 65536).has_value(),
            "expanded snapshot validates");
        snapshot.mutable_snapshot()->add_projectiles()->CopyFrom(
            snapshot.snapshot().projectiles(0));
        check(!hunter::validate_output(hunter::ScriptOut(snapshot), 65536),
            "duplicate projectile rejected by output boundary");
        auto damaged = world.document();
        damaged["entities"]["1"]["demo"]["health_segments"][0] = 49;
        const auto before = world.save();
        rejects([&] { world.load(damaged.dump()); }, "bad state segments rejected");
        check(world.save() == before, "failed import leaves active state untouched");
        damaged = world.document();
        damaged["v"] = 5;
        rejects([&] { world.load(damaged.dump()); }, "old state version rejected");
        world.finish("Abandoned");
        check(world.projectile_free() == 16 && player(world).get_use_slot() == 0
            && world.valid(), "terminal cleanup preserves valid world");
        std::cout << "demo native contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

// 验证免费配装、原生槽位资源、投掷预留及完整增量状态的原子边界。
#include "common/Types.h"
#include "game/World.h"
#include "script/Schema.h"
#include "ContentSpec.h"
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

        if (id != "0" && world.content.at("tools").at(id).at("kind") == kind)
        {
            return index;
        }
    }

    throw std::runtime_error("missing default tool");
}
// 梯上拾取不得绕过动作门禁，拒绝必须同时保留地面物品与装备资源。
void check_ladder_pickup()
{
    hunter::World world;
    world.configure(nlohmann::json::parse(hunter::content::json_text));
    world.access.writable = true;
    world.login("73");
    world.begin("ladder_pickup", "0", "1", "1");
    check(world.spawn("player", "") == "1", "spawn ladder pickup player");
    auto& value = player(world);
    const auto needle = tool_slot(world, "needle");
    const auto needle_id = value.get_tool_cfg(needle);
    value.change_tool(needle, "0", 0);
    value.x = 6400;
    value.y = 500;
    value.vx = 0;
    value.vy = 0;
    value.grounded = false;
    value.ladder_id = 1;
    world.drop("2800001", 1, value.x, value.y);
    world.drop(needle_id, 1, value.x, value.y);
    const auto before = world.save();
    check(world.pickup("73", "1") == "action_locked" && world.save() == before,
        "ladder rejects nearby reward pickup without changing resources");
    check(world.pickup("73", "2") == "action_locked" && world.save() == before,
        "ladder rejects nearby consumable pickup without changing resources");
    value.ladder_id = 0;
    value.y = 1500;
    value.grounded = true;
    check(world.pickup("73", "1").empty(), "reward pickup resumes after leaving ladder");
    check(world.pickup("73", "2").empty() && value.get_tool_cfg(needle) == needle_id,
        "consumable pickup resumes after leaving ladder");
    check(world.valid(), "ladder pickup boundaries preserve valid native state");
}

}

// 在正式内容上验证拒绝不消费资源、快照投影与状态往返。
int main()
{
    try
    {
        check_ladder_pickup();
        hunter::World world;
        world.configure(nlohmann::json::parse(hunter::content::json_text));
        auto accepted = world.check_loadout({});
        check(accepted.weapons_size() == 2 && accepted.tools_size() == 2
            && accepted.consumables_size() == 2, "complete default loadout");
        auto invalid = accepted;
        invalid.mutable_weapons(0)->set_ammo_cfg_id(2147483647);
        rejects([&] { world.check_loadout(invalid); }, "mismatched ammo rejected");
        invalid = accepted;
        invalid.set_health_segments(0, 49);
        rejects([&] { world.check_loadout(invalid); }, "invalid segment rejected");
        invalid = accepted;
        invalid.add_tools(accepted.tools(0));
        rejects([&] { world.check_loadout(invalid); }, "duplicate tool rejected");
        world.access.writable = true;
        world.login("73");
        world.prepare_loadout(accepted);
        world.begin("demo", "0", "1", "1");
        check(world.spawn("player", "") == "1", "spawn accepted loadout");

        for (const auto& spawn : world.content.at("map").at("enemies"))
        {
            world.spawn("monster", spawn.at("spawn_id"));
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
        check(value.start_use(medkit, 1), "begin medical use");
        value.set_use_ticks(0);
        check(value.finish_use() && value.get_tool_count(medkit) == 0
            && value.get_tool_cfg(medkit) == medkit_id, "empty regular tool keeps slot");
        check(!value.start_use(medkit, 1), "empty tool cannot start");
        const auto needle = tool_slot(world, "needle");
        value.change_tool(needle, value.get_tool_cfg(needle), 1);
        check(value.start_use(needle, 1), "begin consumable use");
        value.set_use_ticks(0);
        check(value.finish_use() && value.get_tool_cfg(needle) == "0",
            "empty consumable clears slot");
        world.drop(medkit_id, 1, value.x, value.y);
        check(world.pickup("73", "1") == "unsupported_pickup" && world.has_item("1"),
            "regular equipment never enters reward bag");
        const auto needle_id = std::to_string(accepted.consumables(0));
        world.drop(needle_id, 1, value.x, value.y);
        check(world.pickup("73", "2").empty() && !world.has_item("2")
            && value.get_tool_cfg(needle) == needle_id, "consumable pickup uses equipment slot");
        world.drop(needle_id, 1, value.x, value.y);
        const auto before_pickup = world.document();
        check(world.pickup("73", "3") == "consumable_full"
            && world.document() == before_pickup, "consumable capacity rejection is atomic");
        const auto bomb = tool_slot(world, "bomb");
        const auto bomb_id = value.get_tool_cfg(bomb);
        const auto bomb_count = value.get_tool_count(bomb);
        check(value.start_use(bomb, 1) && world.projectile_free() == 15,
            "bomb reserves flight capacity before use");
        const auto reserved_state = world.save();
        world.load(reserved_state);
        check(world.save() == reserved_state, "reservation state roundtrip");
        player(world).cancel_use();
        check(world.projectile_free() == 16 && player(world).get_tool_count(bomb) == bomb_count,
            "cancel returns reservation without consumption");
        auto& active = player(world);
        check(active.start_use(bomb, 1), "restart bomb");
        active.set_use_ticks(0);
        const auto projectile = world.spawn_projectile(bomb_id, active.x, active.y, 100, 0, 1600);
        check(active.finish_use() && world.projectile_alive(projectile)
            && world.projectile_free() == 15, "completion consumes reservation once");

        for (i32 index = 1; index < 16; ++index)
        {
            world.spawn_projectile(bomb_id, active.x, active.y, 100, 0, 1600);
        }

        check(world.projectile_free() == 0, "projectile capacity bounded");
        active.change_tool(bomb, bomb_id, 1);
        check(!active.start_use(bomb, 1) && active.get_tool_count(bomb) == 1,
            "full projectile capacity rejects without consumption");
        world.remove_projectile(projectile);
        check(active.start_use(bomb, 1), "released capacity reusable");
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
        damaged["items"]["1"]["place"] = "Bag";
        damaged["items"]["1"]["owner_player_id"] = "73";
        damaged["items"]["1"]["x"] = 0;
        damaged["items"]["1"]["y"] = 0;
        rejects([&] { world.load(damaged.dump()); }, "equipment reward bag import rejected");
        damaged = world.document();
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

// 实现局内物品转移、精确随机数和撤离状态边界；玩法时序由 Lua 编排。
#include "game/World.h"
#include "common/Types.h"
#include <algorithm>
#include <limits>

namespace hunter
{
Str World::find_player(const Str& owner) const
{
    const auto player = read_id(owner);

    for (const auto index : order)
    {
        const auto* value = std::get_if<Player>(&actors[index]->value);
        if (value && value->player_id == player && !value->pending_remove)
        {
            return value->get_id();
        }
    }

    return "0";
}

bool World::drop_once(const Str& actor)
{
    access.write();
    const auto id = read_id(actor);
    const auto index = slot(id);
    require(index < actors.size(), "unknown_actor");
    const auto& value = actors[index]->unit();
    require(value.kind == "monster" && !value.alive, "invalid_drop_source");
    return raid.dropped.insert(id).second;
}

i64 World::roll(i64 maximum)
{
    access.write();
    range(maximum, 1, 2147483647);
    const auto bound = static_cast<u32>(maximum);
    const auto limit = std::numeric_limits<u32>::max()
        - std::numeric_limits<u32>::max() % bound;
    u32 value = 0;

    do
    {
        raid.random ^= raid.random << 13;
        raid.random ^= raid.random >> 17;
        raid.random ^= raid.random << 5;
        value = raid.random;
    }
    while (value > limit);

    return static_cast<i64>((value - 1) % bound) + 1;
}

void World::drop(const Str& cfg_id, i64 count, i64 x, i64 y)
{
    access.write();
    range(count, 1, 2147483647);
    range(x, 0, content.at("map").at("width"));
    range(y, 0, content.at("map").at("height"));
    const i32 stack = content.at("items").at(cfg_id).at("max_stack");
    require(stack > 0, "invalid_stack");
    const auto required = (count + stack - 1) / stack;
    const auto free = std::count_if(items.begin(), items.end(),
        [](const auto& value) { return !value; });
    require(required <= free, "item_capacity");

    while (count > 0)
    {
        const auto amount = std::min<i64>(count, stack);
        const auto id = create_item(cfg_id, amount);
        auto& value = items[item_slot(read_id(id))]->value;
        value.place = "Ground";
        value.x = static_cast<i32>(x);
        value.y = static_cast<i32>(y);
        count -= amount;
    }
}

Str World::pickup(const Str& owner, const Str& item_id)
{
    access.write();

    if (phase != "Playing" || paused || raid.player_state != "Alive")
    {
        return "invalid_state";
    }

    const auto actor = slot(read_id(find_player(owner)));
    const auto index = item_slot(read_id(item_id));

    if (actor == actors.size() || !actors[actor]->unit().alive)
    {
        return "invalid_player";
    }

    if (std::get<Player>(actors[actor]->value).ladder_id != 0)
    {
        return "action_locked";
    }

    if (index == items.size() || items[index]->value.place != "Ground")
    {
        return "already_picked";
    }

    const auto& unit = actors[actor]->unit();
    const auto& item = items[index]->value;
    const i64 dx = static_cast<i64>(unit.x) - item.x;
    const i64 dy = static_cast<i64>(unit.y) - item.y;
    const i64 radius = content.at("bag").at("pickup_radius");
    if (dx * dx + dy * dy > radius * radius)
    {
        return "out_of_range";
    }

    if (content.at("tools").contains(item.get_cfg_id()))
    {
        const auto& cfg = content.at("tools").at(item.get_cfg_id());
        const Str kind = cfg.at("kind");

        if (kind == "needle" || kind == "bomb")
        {
            auto& player = std::get<Player>(actors[actor]->value);
            i32 target = 0;

            for (i32 tool_slot = 5; tool_slot <= 8; ++tool_slot)
            {
                if (player.get_tool_cfg(tool_slot) == item.get_cfg_id())
                {
                    target = tool_slot;
                    break;
                }

                if (target == 0 && player.get_tool_cfg(tool_slot) == "0")
                {
                    target = tool_slot;
                }
            }

            if (target == 0 || player.get_tool_count(target) + item.count
                > cfg.at("uses").get<i32>())
            {
                return "consumable_full";
            }

            player.change_tool(target, item.get_cfg_id(),
                player.get_tool_count(target) + item.count);
            items[index].reset();
            return "";
        }
    }

    if (content.at("items").at(item.get_cfg_id()).value("kind", 5) != 5)
    {
        return "unsupported_pickup";
    }

    auto next = items;
    i32 remaining = item.count;
    usize used = 0;
    const i32 stack = content.at("items").at(item.get_cfg_id()).at("max_stack");

    for (auto& entry : next)
    {
        if (!entry || entry->value.place != "Bag" || entry->value.owner_player_id != read_id(owner))
        {
            continue;
        }

        ++used;
        auto& target = entry->value;

        if (target.cfg_id == item.cfg_id)
        {
            const auto amount = std::min(remaining, stack - target.count);
            target.count += amount;
            remaining -= amount;
        }
    }

    if (remaining > 0 && used >= content.at("bag").at("slots").get<usize>())
    {
        return "bag_full";
    }

    if (remaining == 0)
    {
        next[index].reset();
    }
    else
    {
        next[index]->value.place = "Bag";
        next[index]->value.owner_player_id = read_id(owner);
        next[index]->value.count = remaining;
        next[index]->value.x = 0;
        next[index]->value.y = 0;
    }

    items = std::move(next);
    return "";
}

void World::hurt(const Str& actor)
{
    access.write();

    if (read_id(actor) == read_id(find_player(get_player_id())))
    {
        raid.damage_tick = tick_id;
    }
}

bool World::hurt_now() const
{
    return tick_id > 0 && raid.damage_tick == tick_id;
}

i64 World::get_extract_ticks() const
{
    return raid.extract_ticks;
}

void World::set_extract(i64 id, i64 ticks, Str reason)
{
    access.write();
    range(id, 0, 2147483647);
    range(ticks, 0, 36000);
    require(reason == "locked" || reason == "outside" || reason == "hurt"
        || reason == "dead" || reason == "counting" || reason == "complete",
        "invalid_extract_reason");
    raid.extract_id = static_cast<u32>(id);
    raid.extract_ticks = static_cast<i32>(ticks);
    raid.extract_reason = std::move(reason);
}

void World::finish(Str outcome)
{
    access.write();
    require(phase == "Playing", "invalid_state");
    require(outcome == "Dead" || outcome == "Extracted" || outcome == "Abandoned",
        "invalid_outcome");
    raid.player_state = std::move(outcome);
    phase = "Settling";
    clear_input();
    clear_actions();

    for (const auto index : order)
    {
        auto& unit = actors[index]->unit();
        unit.vx = 0;
        unit.vy = 0;
    }
}
}

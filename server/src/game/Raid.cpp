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

void World::drop(const Str& cfg_id, const Str& text, i64 x, i64 y)
{
    access.write();
    require(text.size() <= 4096, "drop_too_large");
    const auto cfg = read_id(cfg_id);
    require(cfg > 0 && cfg <= 2147483647 && match_id != 0, "invalid_drop");
    range(x, 0, map_width);
    range(y, 0, map_height);
    const auto stacks = nlohmann::json::parse(text);
    require(stacks.is_array() && !stacks.empty() && stacks.size() <= items.size(),
        "invalid_drop_stacks");
    const auto free = static_cast<usize>(std::count_if(items.begin(), items.end(),
        [](const auto& value) { return !value; }));
    require(stacks.size() <= free, "item_capacity");
    require(stacks.size() <= std::numeric_limits<u64>::max() - last_item_id
        && stacks.size() <= std::numeric_limits<u32>::max() - serial,
        "item_id_exhausted");
    auto next = items;
    auto next_id = last_item_id;
    auto next_serial = serial;
    usize index = 0;

    for (const auto& amount : stacks)
    {
        while (next[index])
        {
            ++index;
        }

        ItemSlot entry;
        entry.generation = ++next_serial;
        entry.value.access = &access;
        entry.value.id = ++next_id;
        entry.value.cfg_id = static_cast<u32>(cfg);
        entry.value.count = read_integer(amount, 1, 2147483647);
        entry.value.place = "Ground";
        entry.value.x = static_cast<i32>(x);
        entry.value.y = static_cast<i32>(y);
        next[index++] = std::move(entry);
    }

    items = std::move(next);
    serial = next_serial;
    last_item_id = next_id;
}

Str World::inventory() const
{
    access.read();
    auto result = nlohmann::json::array();

    for (const auto& entry : items)
    {
        if (entry)
        {
            const auto& item = entry->value;
            result.push_back({{"id", item.get_id()}, {"cfg_id", item.get_cfg_id()},
                {"count", item.count}, {"place", item.place}, {"x", item.x}, {"y", item.y},
                {"owner_player_id", std::to_string(item.owner_player_id)}});
        }
    }

    return result.dump();
}

Str World::commit(const Str& text)
{
    access.write();

    if (phase != "Playing" || paused || raid.player_state != "Alive")
    {
        return "invalid_state";
    }

    require(text.size() <= 32768, "batch_too_large");
    const auto batch = nlohmann::json::parse(text);
    require(batch.is_object() && batch.contains("owner") && batch.contains("items")
        && batch.contains("tools") && batch.size() == (batch.contains("scene") ? 4 : 3),
        "invalid_batch");
    const auto owner = read_id(batch.at("owner").get<Str>());
    const auto actor = slot(read_id(find_player(std::to_string(owner))));

    if (actor == actors.size() || !actors[actor]->unit().alive)
    {
        return "invalid_player";
    }

    auto& player = std::get<Player>(actors[actor]->value);
    const auto& item_changes = batch.at("items");
    const auto& tool_changes = batch.at("tools");
    require(item_changes.is_array() && item_changes.size() <= items.size()
        && tool_changes.is_array() && tool_changes.size() <= player.tools.size(),
        "batch_capacity");
    auto next_items = items;
    auto next_tools = player.tools;
    auto next_instance = player.last_tool_instance;
    Set<u64> item_ids;
    Set<i32> tool_slots;
    bool cancel = false;

    for (const auto& change : item_changes)
    {
        read_fields(change, {"id", "expected_count", "count", "place"});
        const auto id = read_id(change.at("id").get<Str>());
        require(id > 0 && item_ids.insert(id).second, "duplicate_batch_item");
        const auto index = item_slot(id);
        const auto before = read_integer(change.at("expected_count"), 1, 2147483647);
        const auto count = read_integer(change.at("count"), 0, 2147483647);
        const auto place = change.at("place").get<Str>();
        require(place == "Ground" || place == "Bag", "invalid_item_place");

        if (index == items.size() || items[index]->value.count != before)
        {
            return "stale_item";
        }

        const auto& source = items[index]->value;

        if (source.place != "Ground" && (source.place != "Bag"
            || source.owner_player_id != owner))
        {
            return "invalid_item_owner";
        }

        if (count == 0)
        {
            next_items[index].reset();
            continue;
        }

        auto& item = next_items[index]->value;
        item.count = count;
        item.place = place;
        item.owner_player_id = place == "Bag" ? owner : 0;

        if (place == "Bag")
        {
            item.x = 0;
            item.y = 0;
        }
    }

    for (const auto& change : tool_changes)
    {
        read_fields(change, {"slot", "expected_instance", "cfg_id", "count"});
        const auto slot = read_integer(change.at("slot"), 1, 8);
        require(tool_slots.insert(slot).second, "duplicate_batch_slot");
        const auto expected = read_id(change.at("expected_instance").get<Str>());
        const auto cfg = read_id(change.at("cfg_id").get<Str>());
        require(cfg <= 2147483647, "invalid_tool_cfg");
        const auto count = read_integer(change.at("count"), 0, 100000);
        require(cfg != 0 || count == 0, "invalid_empty_tool");
        const auto index = static_cast<usize>(slot - 1);

        if (player.tools[index].instance != expected)
        {
            return "stale_tool";
        }

        auto& tool = next_tools[index];

        if (cfg != tool.cfg_id)
        {
            cancel = cancel || player.use_slot == slot;

            if (cfg != 0)
            {
                require(next_instance < std::numeric_limits<u64>::max(), "tool_id_exhausted");
                tool.instance = ++next_instance;
            }
        }

        tool.cfg_id = static_cast<u32>(cfg);
        tool.count = count;

        if (cfg == 0)
        {
            tool = {};
        }
    }

    i32 scene_index = 0;
    bool scene_used = false;

    if (batch.contains("scene"))
    {
        const auto& change = batch.at("scene");
        read_fields(change, {"index", "expected_used", "used"});
        scene_index = read_integer(change.at("index"), 1, scene_count());
        require(change.at("expected_used").is_boolean() && change.at("used").is_boolean(),
            "invalid_scene_usage");

        if (used_scenes[static_cast<usize>(scene_index - 1)]
            != change.at("expected_used").get<bool>())
        {
            return "stale_scene";
        }

        scene_used = change.at("used").get<bool>();
    }

    const auto count = std::count_if(next_items.begin(), next_items.end(),
        [](const auto& item) { return item && item->value.place == "Bag"; });

    if (count > bag_slots)
    {
        return "bag_full";
    }

    if (cancel)
    {
        player.cancel_use();
    }

    items = std::move(next_items);
    player.tools = next_tools;
    player.last_tool_instance = next_instance;

    if (scene_index > 0)
    {
        used_scenes[static_cast<usize>(scene_index - 1)] = scene_used;
    }

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

void World::set_extract_view(bool unlocked, i64 remaining)
{
    access.write();
    range(remaining, 0, 36000);
    raid.extract_unlocked = unlocked;
    raid.extract_remaining = static_cast<i32>(remaining);
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

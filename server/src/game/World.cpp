// 管理原生对象生命周期及高频输入，玩法算法仍由 Lua 调用属性推进。
#include "game/World.h"
#include "common/Types.h"
#include <algorithm>
#include <limits>

namespace hunter
{
Unit& Actor::unit()
{
    return std::visit([](auto& value) -> Unit&
    {
        return static_cast<Unit&>(value);
    }, value);
}

const Unit& Actor::unit() const
{
    return std::visit([](const auto& value) -> const Unit&
    {
        return static_cast<const Unit&>(value);
    }, value);
}

void World::reset()
{
    require(access.owner == std::this_thread::get_id(), "wrong_thread");
    *this = World{};
}

void World::configure(const nlohmann::json& bounds, const Str& key)
{
    access.read();
    require(!configured && !key.empty() && key.size() <= 128, "already_configured");
    read_fields(bounds, {"map_width", "map_height", "bag_slots", "scene_ids"});
    const auto width = read_integer(bounds.at("map_width"), 1000, 100000);
    const auto height = read_integer(bounds.at("map_height"), 1000, 100000);
    const auto capacity = read_integer(bounds.at("bag_slots"), 0, 64);
    const auto& scenes = bounds.at("scene_ids");
    require(scenes.is_array() && scenes.size() <= 32, "scene_capacity");
    Vec<u32> ids;
    Set<u64> seen;

    for (const auto& value : scenes)
    {
        const auto id = read_id(value.get<Str>());
        require(id > 0 && id <= 2147483647 && seen.insert(id).second, "invalid_scene_id");
        ids.push_back(static_cast<u32>(id));
    }

    content_key = key;
    scene_ids = std::move(ids);
    map_width = width;
    map_height = height;
    bag_slots = capacity;
    configured = true;
}

u32 World::next_generation()
{
    require(serial < std::numeric_limits<u32>::max(), "generation_exhausted");
    return ++serial;
}

u32 World::slot(u64 id) const
{
    for (const auto index : order)
    {
        if (actors[index]->unit().id == id)
        {
            return index;
        }
    }

    return 64;
}

u32 World::item_slot(u64 id) const
{
    for (u32 index = 0; index < items.size(); ++index)
    {
        if (items[index] && items[index]->value.id == id)
        {
            return index;
        }
    }

    return 64;
}

bool World::contains(const Str& id) const
{
    const auto index = slot(read_id(id));
    return index < 64 && !actors[index]->unit().pending_remove;
}

i64 World::count() const
{
    return static_cast<i64>(order.size());
}

Str World::entity_id(i64 index) const
{
    range(index, 1, count());
    return std::to_string(actors[order[static_cast<usize>(index - 1)]]->unit().id);
}

void World::login(const Str& owner)
{
    access.write();
    require(phase == "Unauthenticated", "invalid_state");
    const auto id = read_id(owner);
    require(id > 0 && id <= static_cast<u64>(std::numeric_limits<i64>::max()),
        "invalid_player_id");
    player_id = id;
    phase = "Lobby";
}

void World::begin(const Str& req, const Str& after, const Str& match, const Str& world)
{
    access.write();
    require(!req.empty() && req.size() <= 128 && read_id(after) == match_id,
        "invalid_start");
    const auto assigned = read_id(match);
    const auto next_world = read_id(world);
    require(assigned > match_id && assigned <= static_cast<u64>(std::numeric_limits<i64>::max())
        && next_world > world_id && revision < std::numeric_limits<u32>::max(),
        "identity_exhausted");
    require(configured && pending_loadout.player_cfg_id() > 0, "missing_loadout");
    active_loadout = pending_loadout;
    pending_loadout.Clear();
    used_scenes = {};
    projectiles = {};
    last_projectile_id = 0;
    action_seq = 0;
    actors = {};
    items = {};
    order.clear();
    ++revision;
    last_after = match_id;
    match_id = assigned;
    world_id = next_world;
    raid = {};
    raid.random = static_cast<u32>(match_id ^ (match_id >> 32)) | 1U;
    last_match = match_id;
    last_req = req;
    player_entity_id = 0;
    last_entity_id = 0;
    last_item_id = 0;
    phase = "Playing";
}

Str World::spawn(const Str& kind, const Str& text)
{
    access.write();

    if (phase != "Playing" || paused)
    {
        return ":invalid_state";
    }

    if (order.size() == actors.size())
    {
        return ":entity_capacity";
    }

    if (last_entity_id == std::numeric_limits<u64>::max())
    {
        return ":entity_id_exhausted";
    }

    require(text.size() <= 16384, "spawn_too_large");
    const auto spec = nlohmann::json::parse(text);
    Actor actor;

    if (kind == "player")
    {
        if (player_entity_id != 0)
        {
            return ":invalid_player";
        }

        read_fields(spec, {"cfg_id", "x", "y", "hp", "width", "height", "grounded",
            "stamina", "weapons", "health_segments", "tools"});
        auto& player = std::get<Player>(actor.value);
        player.access = &access;
        player.world = this;
        player.player_id = player_id;
        player.stamina = read_integer(spec.at("stamina"), 0, 1000000);
        const auto& weapons = spec.at("weapons");
        const auto& segments = spec.at("health_segments");
        const auto& tools = spec.at("tools");
        require(weapons.is_array() && !weapons.empty() && weapons.size() <= 2
            && segments.is_array() && !segments.empty() && segments.size() <= 6
            && tools.is_array() && tools.size() <= 8, "invalid_spawn_slots");
        player.weapon_count = static_cast<i32>(weapons.size());
        player.active_weapon = 1;

        for (usize index = 0; index < weapons.size(); ++index)
        {
            const auto& entry = weapons[index];
            read_fields(entry, {"cfg_id", "ammo_cfg_id", "ammo", "reserve"});
            auto& weapon = index == 0 ? player.weapon : player.other_weapon;
            const auto cfg = read_id(entry.at("cfg_id").get<Str>());
            const auto ammo = read_id(entry.at("ammo_cfg_id").get<Str>());
            require(cfg > 0 && cfg <= 2147483647 && ammo > 0 && ammo <= 2147483647,
                "invalid_weapon_cfg");
            weapon.access = &access;
            weapon.cfg_id = static_cast<u32>(cfg);
            weapon.ammo = read_integer(entry.at("ammo"), 0, 1000);
            player.ammo_cfg_ids[index] = static_cast<u32>(ammo);
            (index == 0 ? player.reserve : player.other_reserve)
                = read_integer(entry.at("reserve"), 0, 100000);
        }

        for (const auto& amount : segments)
        {
            player.health_segments.push_back(read_integer(amount, 1, 1000000));
        }

        Set<i32> used;

        for (const auto& entry : tools)
        {
            read_fields(entry, {"slot", "cfg_id", "count"});
            const auto slot = read_integer(entry.at("slot"), 1, 8);
            require(used.insert(slot).second, "duplicate_tool_slot");
            player.change_tool(slot, entry.at("cfg_id").get<Str>(),
                read_integer(entry.at("count"), 0, 100000));
        }
    }
    else if (kind == "monster")
    {
        read_fields(spec, {"cfg_id", "x", "y", "hp", "width", "height", "grounded",
            "spawn_id"});
        const auto spawn = read_id(spec.at("spawn_id").get<Str>());
        require(spawn > 0 && spawn <= 2147483647, "invalid_spawn_id");
        actor.value = Monster{};
        std::get<Monster>(actor.value).spawn_id = static_cast<u32>(spawn);
    }
    else
    {
        return ":invalid_kind";
    }

    auto& unit = actor.unit();
    unit.access = &access;
    const auto cfg = read_id(spec.at("cfg_id").get<Str>());
    require(cfg > 0 && cfg <= 2147483647, "invalid_spawn_cfg");
    unit.cfg_id = static_cast<u32>(cfg);
    unit.kind = kind;
    unit.id = last_entity_id + 1;
    unit.hp = read_integer(spec.at("hp"), 1, 1000000);
    unit.max_hp = unit.hp;
    unit.width = read_integer(spec.at("width"), 2, 10000);
    unit.height = read_integer(spec.at("height"), 2, 10000);
    require(unit.width % 2 == 0 && unit.height % 2 == 0, "invalid_body");
    unit.x = read_integer(spec.at("x"), unit.width / 2, map_width - unit.width / 2);
    unit.y = read_integer(spec.at("y"), 0, map_height - unit.height);
    require(spec.at("grounded").is_boolean(), "invalid_grounded");
    unit.grounded = spec.at("grounded").get<bool>();
    if (const auto* player = std::get_if<Player>(&actor.value))
    {
        require(player->cfg_id == active_loadout.player_cfg_id()
            && player->weapon_count == active_loadout.weapons_size()
            && player->health_segments.size()
                == static_cast<usize>(active_loadout.health_segments_size()), "loadout_mismatch");
        i64 total = 0;

        for (usize index = 0; index < player->health_segments.size(); ++index)
        {
            const auto amount = player->health_segments[index];
            require(amount == static_cast<i32>(
                active_loadout.health_segments(static_cast<i32>(index))),
                "loadout_mismatch");
            total += amount;
        }

        require(total == unit.max_hp, "invalid_health_segments");

        for (i32 index = 0; index < player->weapon_count; ++index)
        {
            require(player->weapon_at(index + 1).cfg_id == active_loadout.weapons(index).cfg_id()
                && player->ammo_cfg_ids[static_cast<usize>(index)]
                    == active_loadout.weapons(index).ammo_cfg_id(), "loadout_mismatch");
        }
    }

    u32 index = 0;

    while (actors[index])
    {
        ++index;
    }

    require(revision < std::numeric_limits<u32>::max(), "revision_exhausted");
    order.reserve(order.size() + 1);
    actor.generation = next_generation();
    actors[index] = std::move(actor);
    order.push_back(index);
    ++revision;
    ++last_entity_id;

    if (kind == "player")
    {
        player_entity_id = last_entity_id;
    }

    return std::to_string(last_entity_id);
}

bool World::remove(const Str& id)
{
    access.write();
    const auto index = slot(read_id(id));
    if (phase != "Playing" || paused || index == 64)
    {
        return false;
    }

    Entity& entity = actors[index]->unit();

    if (entity.kind != "monster" || entity.pending_remove)
    {
        return false;
    }

    entity.pending_remove = true;
    return true;
}

void World::flush()
{
    access.write();
    const auto old_size = order.size();
    require(revision < std::numeric_limits<u32>::max(), "revision_exhausted");
    std::erase_if(order, [this](u32 index)
    {
        if (!actors[index]->unit().pending_remove)
        {
            return false;
        }

        actors[index].reset();
        return true;
    });

    if (order.size() != old_size)
    {
        ++revision;
    }
}

void World::clear_input()
{
    access.write();
    const auto index = slot(player_entity_id);
    if (index == 64)
    {
        return;
    }

    auto& player = std::get<Player>(actors[index]->value);
    player.move_x = 0;
    player.move_y = 0;
    player.run = false;
    player.want_prone = player.prone;
    player.melee = false;
    player.aim_x = player.facing * 1000;
    player.aim_y = 0;
    player.jump = false;
    player.fire = false;
    player.fire_once = false;
    player.reload = false;
}

wire::Envelope World::input(const wire::FrameInput& input, u64 applied_tick)
{
    access.write();
    require(input.seq() != 0 && input.move_x() >= -1 && input.move_x() <= 1
        && input.aim_x() >= -1000 && input.aim_x() <= 1000
        && input.aim_y() >= -1000 && input.aim_y() <= 1000
        && input.move_y() >= -1 && input.move_y() <= 1, "invalid_input");
    Str error;

    if (input.seq() <= seq)
    {
        error = "stale_input";
    }
    else
    {
        seq = input.seq();

        if (phase == "Unauthenticated")
        {
            error = "not_logged_in";
        }
        else if (input.match_id() != match_id)
        {
            error = "stale_match";
        }
        else if (phase != "Playing" || paused)
        {
            error = "invalid_state";
        }
    }

    wire::Envelope out;

    if (!error.empty())
    {
        auto& msg = *out.mutable_error();
        msg.set_code(error);
        msg.set_seq(input.seq());
        msg.set_match_id(match_id);
        return out;
    }

    require(tick_id < static_cast<u64>(std::numeric_limits<i64>::max())
        && applied_tick == tick_id + 1, "invalid_applied_tick");
    auto& player = std::get<Player>(actors[slot(player_entity_id)]->value);
    player.move_x = input.move_x();
    player.move_y = input.move_y();
    player.run = input.run();
    player.want_prone = input.prone();
    player.aim_x = input.aim_x();
    player.aim_y = input.aim_y();

    if (player.aim_x == 0 && player.aim_y == 0)
    {
        player.aim_x = (input.move_x() != 0 ? input.move_x() : player.facing) * 1000;
    }

    player.jump = player.jump || input.jump();
    player.fire = input.fire();
    player.fire_once = player.fire_once || input.fire();
    player.reload = player.reload || input.reload();
    auto& ack = *out.mutable_ack();
    ack.set_seq(seq);
    ack.set_match_id(match_id);
    ack.set_applied_tick(applied_tick);
    return out;
}

Str World::create_item(const Str& cfg_id, i64 count)
{
    access.write();
    const auto cfg = read_id(cfg_id);
    require(cfg > 0 && cfg <= 2147483647, "invalid_item_cfg");
    range(count, 1, 2147483647);
    require(match_id != 0, "invalid_state");
    require(last_item_id < std::numeric_limits<u64>::max(), "item_id_exhausted");
    u32 index = 0;

    while (index < items.size() && items[index])
    {
        ++index;
    }

    require(index < items.size(), "item_capacity");
    ItemSlot item;
    item.generation = next_generation();
    item.value.access = &access;
    item.value.id = last_item_id + 1;
    item.value.cfg_id = static_cast<u32>(cfg);
    item.value.count = static_cast<i32>(count);
    items[index] = item;
    return std::to_string(++last_item_id);
}

bool World::has_item(const Str& id) const
{
    return item_slot(read_id(id)) < items.size();
}

bool World::remove_item(const Str& id)
{
    access.write();
    const auto index = item_slot(read_id(id));
    if (index == items.size())
    {
        return false;
    }

    items[index].reset();
    return true;
}

Str World::get_tick_id() const
{
    return std::to_string(tick_id);
}

Str World::get_seq() const
{
    return std::to_string(seq);
}

Str World::get_match_id() const
{
    return std::to_string(match_id);
}

Str World::get_player_id() const
{
    return std::to_string(player_id);
}

Str World::get_player_entity_id() const
{
    return std::to_string(player_entity_id);
}

Str World::get_last_entity_id() const
{
    return std::to_string(last_entity_id);
}

Str World::get_last_req() const
{
    return last_req;
}

Str World::get_last_after() const
{
    return std::to_string(last_after);
}

i64 World::get_revision() const
{
    return revision;
}

Str World::get_phase() const
{
    return phase;
}

bool World::get_paused() const
{
    return paused;
}

void World::set_phase(Str value)
{
    access.write();
    require(value == "Preparing" || value == "Playing" || value == "Settling" || value == "Finished"
        || value == "Aborted" || value == "Dead" || value == "Cleared", "invalid_phase");
    phase = std::move(value);
}

void World::set_paused(bool value)
{
    access.write();
    paused = value;
    clear_input();
}

void World::prepare_loadout(const wire::Loadout& input)
{
    access.write();
    require(input.player_cfg_id() > 0 && input.player_cfg_id() <= 2147483647
        && input.weapons_size() > 0 && input.weapons_size() <= 2
        && input.health_segments_size() > 0 && input.health_segments_size() <= 6
        && input.tools_size() <= 4 && input.consumables_size() <= 4, "invalid_loadout");

    for (const auto& gun : input.weapons())
    {
        require(gun.cfg_id() > 0 && gun.cfg_id() <= 2147483647
            && gun.ammo_cfg_id() > 0 && gun.ammo_cfg_id() <= 2147483647, "invalid_loadout");
    }

    for (const auto amount : input.health_segments())
    {
        range(amount, 1, 1000000);
    }

    for (const auto id : input.tools())
    {
        range(id, 1, 2147483647);
    }

    for (const auto id : input.consumables())
    {
        range(id, 1, 2147483647);
    }

    pending_loadout = input;
}

wire::Loadout World::get_loadout() const
{
    access.read();
    return active_loadout;
}

Str World::get_world_id() const
{
    return std::to_string(world_id);
}

i64 World::scene_count() const
{
    return static_cast<i64>(scene_ids.size());
}

bool World::scene_used(i64 index) const
{
    range(index, 1, scene_count());
    return used_scenes[static_cast<usize>(index - 1)];
}

void World::set_scene_used(i64 index, bool used)
{
    access.write();
    range(index, 1, scene_count());
    used_scenes[static_cast<usize>(index - 1)] = used;
}

i64 World::projectile_free() const
{
    return static_cast<i64>(std::count_if(projectiles.begin(), projectiles.end(),
        [](const auto& value) { return !value.active && !value.reserved; }));
}

i32 World::reserve_projectile()
{
    access.write();

    for (usize index = 0; index < projectiles.size(); ++index)
    {
        if (!projectiles[index].active && !projectiles[index].reserved)
        {
            projectiles[index].reserved = true;
            return static_cast<i32>(index + 1);
        }
    }

    return 0;
}

i64 World::spawn_projectile(const Str& cfg_id, i64 x, i64 y, i64 vx, i64 vy, i64 remaining)
{
    access.write();
    require(phase == "Playing" && !paused && raid.player_state == "Alive", "invalid_state");
    const auto cfg = read_id(cfg_id);
    require(cfg > 0 && cfg <= 2147483647, "invalid_projectile_cfg");
    range(x, 0, map_width);
    range(y, 0, map_height);
    range(vx, -1000, 1000);
    range(vy, -1000, 1000);
    range(remaining, 1, 100000);
    require(vx != 0 || vy != 0, "invalid_projectile_velocity");
    require(last_projectile_id < std::numeric_limits<u64>::max(), "projectile_id_exhausted");
    Player* player = nullptr;
    const auto player_slot = slot(player_entity_id);

    if (player_slot < actors.size())
    {
        player = std::get_if<Player>(&actors[player_slot]->value);
    }

    i32 reserved = player ? player->reserved_projectile : 0;

    if (reserved == 0)
    {
        reserved = reserve_projectile();
    }

    require(reserved > 0, "projectile_capacity");
    auto& value = projectiles[static_cast<usize>(reserved - 1)];
    require(value.reserved && !value.active, "invalid_projectile_reservation");
    value = {++last_projectile_id, static_cast<u32>(read_id(cfg_id)),
        static_cast<i32>(x), static_cast<i32>(y), static_cast<i32>(vx), static_cast<i32>(vy),
        static_cast<i32>(remaining), true, false};

    if (player)
    {
        player->reserved_projectile = 0;
    }

    return reserved;
}

bool World::projectile_alive(i64 index) const
{
    range(index, 1, 16);
    return projectiles[static_cast<usize>(index - 1)].active;
}

std::tuple<Str, i64, i64, i64, i64, i64> World::read_projectile(i64 index) const
{
    require(projectile_alive(index), "missing_projectile");
    const auto& value = projectiles[static_cast<usize>(index - 1)];
    return {std::to_string(value.cfg_id), value.x, value.y, value.vx, value.vy, value.remaining};
}

void World::write_projectile(i64 index, i64 x, i64 y, i64 remaining)
{
    access.write();
    require(projectile_alive(index), "missing_projectile");
    range(x, 0, map_width);
    range(y, 0, map_height);
    auto& value = projectiles[static_cast<usize>(index - 1)];
    range(remaining, 0, value.remaining);
    value.x = static_cast<i32>(x);
    value.y = static_cast<i32>(y);
    value.remaining = static_cast<i32>(remaining);
}

void World::remove_projectile(i64 index)
{
    access.write();
    range(index, 1, 16);
    projectiles[static_cast<usize>(index - 1)] = {};
}

void World::clear_actions()
{
    access.write();

    for (auto& actor : actors)
    {
        if (actor)
        {
            if (auto* player = std::get_if<Player>(&actor->value))
            {
                player->cancel_use();
                player->weapon.reload_ticks = 0;
                player->weapon.shot_ticks = 0;
                player->other_weapon.reload_ticks = 0;
                player->other_weapon.shot_ticks = 0;
                player->running = false;
                player->melee_ticks = 0;
            }
        }
    }

    projectiles = {};
}
}

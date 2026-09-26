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

void World::configure(const nlohmann::json& cfg)
{
    access.read();
    require(content.is_null(), "already_configured");
    require(cfg.is_object() && cfg.at("v") == 4 && cfg.at("tick_hz") == 60, "invalid_content");
    require(cfg.at("scenes").is_array() && cfg.at("scenes").size() <= 32,
        "scene_capacity");
    content = cfg;
    content_key = cfg.dump();
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
    active_loadout = check_loadout(pending_loadout);
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

Str World::spawn(const Str& kind, const Str& spawn_id)
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

    const nlohmann::json* spawn = nullptr;

    if (kind == "player")
    {
        if (player_entity_id != 0 || !spawn_id.empty())
        {
            return ":invalid_player";
        }

        spawn = &content.at("map").at("spawn");
    }
    else if (kind == "monster")
    {
        spawn = Monster::spawn_cfg(content, spawn_id);

        if (!spawn)
        {
            return ":invalid_spawn";
        }
    }
    else
    {
        return ":invalid_kind";
    }

    const auto cfg_id = kind == "player" ? std::to_string(active_loadout.player_cfg_id())
        : spawn->at("cfg_id").get<Str>();
    const auto& cfgs = content.at(kind == "player" ? "players" : "monsters");

    if (!cfgs.contains(cfg_id))
    {
        return ":invalid_cfg";
    }

    Actor actor;

    if (kind == "monster")
    {
        actor.value = Monster{};
        auto& monster = std::get<Monster>(actor.value);
        monster.spawn_id = static_cast<u32>(read_id(spawn_id));
        monster.max_attack_ticks = cfgs.at(cfg_id).at("attack_ticks");
    }
    else
    {
        auto& player = std::get<Player>(actor.value);
        player.access = &access;
        player.world = this;
        player.player_id = player_id;
        player.weapon_count = active_loadout.weapons_size();
        player.active_weapon = 1;
        player.stamina = cfgs.at(cfg_id).at("stamina");

        for (i32 index = 0; index < player.weapon_count; ++index)
        {
            const auto& choice = active_loadout.weapons(index);
            auto& weapon = index == 0 ? player.weapon : player.other_weapon;
            weapon.access = &access;
            weapon.cfg_id = choice.cfg_id();
            const auto& gun = content.at("weapons").at(weapon.get_cfg_id());
            weapon.ammo = gun.at("magazine");
            (index == 0 ? player.reserve : player.other_reserve) = gun.at("reserve");
            player.ammo_cfg_ids[static_cast<usize>(index)] = choice.ammo_cfg_id();
        }

        for (const auto amount : active_loadout.health_segments())
        {
            player.health_segments.push_back(static_cast<i32>(amount));
        }

        for (i32 index = 0; index < active_loadout.tools_size(); ++index)
        {
            const auto id = std::to_string(active_loadout.tools(index));
            player.change_tool(index + 1, id, content.at("tools").at(id).at("uses"));
        }

        for (i32 index = 0; index < active_loadout.consumables_size(); ++index)
        {
            const auto id = std::to_string(active_loadout.consumables(index));
            player.change_tool(index + 5, id, content.at("tools").at(id).at("uses"));
        }
    }

    auto& unit = actor.unit();
    unit.access = &access;
    unit.hp = cfgs.at(cfg_id).at("hp");
    unit.max_hp = unit.hp;
    unit.id = last_entity_id + 1;
    unit.kind = kind;
    unit.cfg_id = static_cast<u32>(read_id(cfg_id));
    unit.x = spawn->at("x");
    unit.y = spawn->at("y");
    unit.grounded = unit.y == 0;
    const i32 half = cfgs.at(cfg_id).at("width").get<i32>() / 2;

    for (const auto& solid : content.at("map").at("solids"))
    {
        const i32 x = solid.at("x");
        const i32 y = solid.at("y");
        const i32 w = solid.at("w");
        const i32 h = solid.at("h");
        unit.grounded = unit.grounded || (unit.y == y + h
            && unit.x - half < x + w && unit.x + half > x);
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

wire::Loadout World::check_loadout(const wire::Loadout& input) const
{
    access.read();
    wire::Loadout result = input;

    if (result.ByteSizeLong() == 0)
    {
        const auto& defaults = content.at("default_loadout");
        result.set_player_cfg_id(static_cast<u32>(read_id(defaults.at("player_cfg_id"))));

        for (const auto& value : defaults.at("hp_segments"))
        {
            result.add_health_segments(value.get<u32>());
        }

        for (usize index = 0; index < defaults.at("weapons").size(); ++index)
        {
            auto* weapon = result.add_weapons();
            weapon->set_cfg_id(static_cast<u32>(read_id(defaults.at("weapons")[index])));
            weapon->set_ammo_cfg_id(static_cast<u32>(read_id(defaults.at("ammo")[index])));
        }

        for (const auto& value : defaults.at("tools"))
        {
            result.add_tools(static_cast<u32>(read_id(value)));
        }

        for (const auto& value : defaults.at("consumables"))
        {
            result.add_consumables(static_cast<u32>(read_id(value)));
        }
    }

    const auto player_key = std::to_string(result.player_cfg_id());
    require(content.at("players").contains(player_key), "invalid_player_cfg");
    const auto& player = content.at("players").at(player_key);
    require(result.health_segments_size() > 0 && result.health_segments_size() <= 6,
        "invalid_health_segments");
    u32 total = 0;

    for (const auto value : result.health_segments())
    {
        require(value == 25 || value == 50, "invalid_health_segments");
        total += value;
    }

    require(total == player.at("hp").get<u32>(), "invalid_health_segments");
    require(result.weapons_size() > 0 && result.weapons_size() <= 2,
        "invalid_weapon_slots");
    i64 weight = 0;

    for (const auto& choice : result.weapons())
    {
        const auto key = std::to_string(choice.cfg_id());
        require(content.at("weapons").contains(key), "invalid_weapon_cfg");
        const auto& gun = content.at("weapons").at(key);
        require(gun.at("ammo_cfg_id") == std::to_string(choice.ammo_cfg_id())
            && content.at("ammo").contains(std::to_string(choice.ammo_cfg_id())),
            "invalid_ammo_cfg");
        weight += gun.at("weight").get<i32>();
    }

    require(weight <= player.at("weight").get<i32>(), "loadout_overweight");
    require(result.tools_size() <= 4 && result.consumables_size() <= 4,
        "invalid_tool_slots");
    Set<u32> selected;
    const auto validate_tool = [&](u32 id, bool consumable)
    {
        const auto key = std::to_string(id);
        require(content.at("tools").contains(key) && selected.insert(id).second,
            "invalid_tool_cfg");
        const Str kind = content.at("tools").at(key).at("kind");
        require(kind == "knife" || kind == "medkit" || kind == "needle" || kind == "bomb",
            "invalid_tool_kind");
        require((kind == "needle" || kind == "bomb") == consumable, "invalid_tool_slot");
    };

    for (const auto id : result.tools())
    {
        validate_tool(id, false);
    }

    for (const auto id : result.consumables())
    {
        validate_tool(id, true);
    }

    return result;
}

void World::prepare_loadout(const wire::Loadout& input)
{
    access.write();
    pending_loadout = check_loadout(input);
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
    return static_cast<i64>(content.at("scenes").size());
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
    require(!used || content.at("scenes")[static_cast<usize>(index - 1)].at("kind") == "supply",
        "invalid_scene_usage");
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
    require(content.at("tools").contains(cfg_id)
        && content.at("tools").at(cfg_id).at("kind") == "bomb", "invalid_projectile_cfg");
    range(x, 0, content.at("map").at("width"));
    range(y, 0, content.at("map").at("height"));
    range(vx, -1000, 1000);
    range(vy, -1000, 1000);
    range(remaining, 1, content.at("tools").at(cfg_id).at("throw_range"));
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
    range(x, 0, content.at("map").at("width"));
    range(y, 0, content.at("map").at("height"));
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

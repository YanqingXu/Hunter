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
    require(cfg.is_object() && cfg.at("v") == 2 && cfg.at("tick_hz") == 60, "invalid_content");
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

void World::login()
{
    access.write();
    require(phase == "Unauthenticated", "invalid_state");
    player_id = 1;
    phase = "Lobby";
}

void World::begin(const Str& req, const Str& after)
{
    access.write();
    require(!req.empty() && req.size() <= 128 && read_id(after) == match_id,
        "invalid_start");
    require(match_id < std::numeric_limits<u64>::max()
        && revision < std::numeric_limits<u32>::max(), "identity_exhausted");
    actors = {};
    items = {};
    order.clear();
    ++revision;
    last_after = match_id++;
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

    const auto cfg_id = spawn->at("cfg_id").get<Str>();
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
        const auto gun_id = spawn->at("weapon_cfg_id").get<Str>();
        if (!content.at("weapons").contains(gun_id))
        {
            return ":invalid_cfg";
        }

        auto& player = std::get<Player>(actor.value);
        player.weapon.access = &access;
        player.weapon.cfg_id = static_cast<u32>(read_id(gun_id));
        const auto& gun = content.at("weapons").at(gun_id);
        player.weapon.ammo = gun.at("magazine");
        player.reserve = gun.at("reserve");
        player.player_id = player_id;
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
    player.aim_x = 1000;
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
        && (input.aim_x() != 0 || input.aim_y() != 0), "invalid_input");
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
    player.aim_x = input.aim_x();
    player.aim_y = input.aim_y();
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
    require(value == "Playing" || value == "Dead" || value == "Cleared", "invalid_phase");
    phase = std::move(value);
}

void World::set_paused(bool value)
{
    access.write();
    paused = value;
    clear_input();
}
}

// 实现 Player 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Player.h"
#include "game/World.h"
#include <limits>
#include "common/Types.h"

namespace hunter
{
Str Player::get_player_id() const
{
    return std::to_string(player_id);
}

i64 Player::get_reserve() const
{
    return reserve;
}

void Player::set_reserve(i64 value)
{
    access->write();
    range(value, 0, 100000);
    reserve = static_cast<i32>(value);
}

i64 Player::get_move_x() const
{
    return move_x;
}

void Player::set_move_x(i64 value)
{
    access->write();
    range(value, -1, 1);
    move_x = static_cast<i32>(value);
}

i64 Player::get_aim_x() const
{
    return aim_x;
}

void Player::set_aim_x(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    aim_x = static_cast<i32>(value);
}

i64 Player::get_aim_y() const
{
    return aim_y;
}

void Player::set_aim_y(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    aim_y = static_cast<i32>(value);
}

bool Player::get_jump() const
{
    return jump;
}

void Player::set_jump(bool value)
{
    access->write();
    jump = value;
}

bool Player::get_fire() const
{
    return fire;
}

void Player::set_fire(bool value)
{
    access->write();
    fire = value;
}

bool Player::get_fire_once() const
{
    return fire_once;
}

void Player::set_fire_once(bool value)
{
    access->write();
    fire_once = value;
}

bool Player::get_reload() const
{
    return reload;
}

void Player::set_reload(bool value)
{
    access->write();
    reload = value;
}
i64 Player::get_move_y() const
{
    return move_y;
}

void Player::set_move_y(i64 value)
{
    access->write();
    range(value, -1, 1);
    move_y = static_cast<i32>(value);
}

i64 Player::get_stamina() const
{
    return stamina;
}

void Player::set_stamina(i64 value)
{
    access->write();
    range(value, 0, 1000000);
    stamina = static_cast<i32>(value);
}

i64 Player::get_stamina_delay() const
{
    return stamina_delay;
}

void Player::set_stamina_delay(i64 value)
{
    access->write();
    range(value, 0, 36000);
    stamina_delay = static_cast<i32>(value);
}

i64 Player::get_stamina_rem() const
{
    return stamina_rem;
}

void Player::set_stamina_rem(i64 value)
{
    access->write();
    range(value, 0, 59);
    stamina_rem = static_cast<i32>(value);
}

i64 Player::get_health_delay() const
{
    return health_delay;
}

void Player::set_health_delay(i64 value)
{
    access->write();
    range(value, 0, 36000);
    health_delay = static_cast<i32>(value);
}

i64 Player::get_health_rem() const
{
    return health_rem;
}

void Player::set_health_rem(i64 value)
{
    access->write();
    range(value, 0, 59);
    health_rem = static_cast<i32>(value);
}

i64 Player::get_melee_ticks() const
{
    return melee_ticks;
}

void Player::set_melee_ticks(i64 value)
{
    access->write();
    range(value, 0, 3600);
    melee_ticks = static_cast<i32>(value);
}

i64 Player::get_ladder_id() const
{
    return ladder_id;
}

void Player::set_ladder_id(i64 value)
{
    access->write();
    range(value, 0, 2147483647);
    ladder_id = static_cast<i32>(value);
}

i64 Player::get_selected_slot() const
{
    return selected_slot;
}

void Player::set_selected_slot(i64 value)
{
    access->write();
    range(value, 0, 8);
    selected_slot = static_cast<i32>(value);
}

i64 Player::get_use_slot() const
{
    return use_slot;
}

void Player::set_use_slot(i64 value)
{
    access->write();
    range(value, 0, 8);
    use_slot = static_cast<i32>(value);
}

i64 Player::get_use_ticks() const
{
    return use_ticks;
}

void Player::set_use_ticks(i64 value)
{
    access->write();
    range(value, 0, 36000);
    use_ticks = static_cast<i32>(value);
}

bool Player::get_run() const
{
    return run;
}

void Player::set_run(bool value)
{
    access->write();
    run = value;
}

bool Player::get_prone() const
{
    return prone;
}

void Player::set_prone(bool value)
{
    access->write();
    prone = value;
}

bool Player::get_want_prone() const
{
    return want_prone;
}

void Player::set_want_prone(bool value)
{
    access->write();
    want_prone = value;
}

bool Player::get_running() const
{
    return running;
}

void Player::set_running(bool value)
{
    access->write();
    running = value;
}

bool Player::get_melee() const
{
    return melee;
}

void Player::set_melee(bool value)
{
    access->write();
    melee = value;
}

i64 Player::get_weapon_ammo(i64 slot) const
{
    return weapon_at(slot).get_ammo();
}

void Player::set_weapon_ammo(i64 slot, i64 value)
{
    weapon_at(slot).set_ammo(value);
}

i64 Player::get_weapon_shot_ticks(i64 slot) const
{
    return weapon_at(slot).get_shot_ticks();
}

void Player::set_weapon_shot_ticks(i64 slot, i64 value)
{
    weapon_at(slot).set_shot_ticks(value);
}

i64 Player::get_weapon_reload_ticks(i64 slot) const
{
    return weapon_at(slot).get_reload_ticks();
}

void Player::set_weapon_reload_ticks(i64 slot, i64 value)
{
    weapon_at(slot).set_reload_ticks(value);
}


i64 Player::get_segment_count() const
{
    return static_cast<i64>(health_segments.size());
}

i64 Player::get_segment(i64 index) const
{
    range(index, 1, get_segment_count());
    return health_segments[static_cast<usize>(index - 1)];
}

i64 Player::get_weapon_count() const
{
    return weapon_count;
}

i64 Player::get_active_weapon() const
{
    return active_weapon;
}

Weapon& Player::weapon_at(i64 slot)
{
    range(slot, 1, weapon_count);
    return slot == active_weapon ? weapon : other_weapon;
}

const Weapon& Player::weapon_at(i64 slot) const
{
    range(slot, 1, weapon_count);
    return slot == active_weapon ? weapon : other_weapon;
}

Str Player::get_weapon_cfg(i64 slot) const
{
    return weapon_at(slot).get_cfg_id();
}

i64 Player::get_weapon_reserve(i64 slot) const
{
    range(slot, 1, weapon_count);
    return slot == active_weapon ? reserve : other_reserve;
}

void Player::set_weapon_reserve(i64 slot, i64 value)
{
    access->write();
    range(slot, 1, weapon_count);
    range(value, 0, 100000);
    (slot == active_weapon ? reserve : other_reserve) = static_cast<i32>(value);
}

bool Player::switch_weapon(i64 slot)
{
    access->write();

    if (slot < 1 || slot > weapon_count)
    {
        return false;
    }

    cancel_use();
    weapon.reload_ticks = 0;

    if (slot != active_weapon)
    {
        std::swap(weapon, other_weapon);
        std::swap(reserve, other_reserve);
        active_weapon = static_cast<i32>(slot);
    }

    selected_slot = 0;
    fire = false;
    fire_once = false;
    reload = false;
    return true;
}

Str Player::get_tool_cfg(i64 slot) const
{
    range(slot, 1, 8);
    return std::to_string(tools[static_cast<usize>(slot - 1)].cfg_id);
}

i64 Player::get_tool_count(i64 slot) const
{
    range(slot, 1, 8);
    return tools[static_cast<usize>(slot - 1)].count;
}

Str Player::get_tool_instance(i64 slot) const
{
    range(slot, 1, 8);
    return std::to_string(tools[static_cast<usize>(slot - 1)].instance);
}

void Player::change_tool(i64 slot, const Str& tool_cfg_id, i64 count)
{
    access->write();
    range(slot, 1, 8);
    const auto cfg = read_id(tool_cfg_id);
    range(count, 0, 100000);
    auto& tool = tools[static_cast<usize>(slot - 1)];

    if (cfg == 0)
    {
        require(count == 0, "invalid_tool_count");

        if (use_slot == slot)
        {
            cancel_use();
        }

        tool = {};
        return;
    }

    require(cfg <= 2147483647, "invalid_tool_cfg");

    if (tool.cfg_id != cfg)
    {
        require(last_tool_instance < std::numeric_limits<u64>::max(), "tool_id_exhausted");

        if (use_slot == slot)
        {
            cancel_use();
        }

        tool.instance = ++last_tool_instance;
    }

    tool.cfg_id = static_cast<u32>(cfg);
    tool.count = static_cast<i32>(count);
}

bool Player::start_use(i64 slot, i64 ticks, bool projectile)
{
    access->write();
    range(slot, 1, 8);
    range(ticks, 1, 36000);
    const auto& tool = tools[static_cast<usize>(slot - 1)];

    if (use_slot != 0 || tool.cfg_id == 0 || tool.count <= 0)
    {
        return false;
    }

    if (projectile)
    {
        require(world != nullptr, "missing_world");
        reserved_projectile = world->reserve_projectile();

        if (reserved_projectile == 0)
        {
            return false;
        }
    }

    use_slot = static_cast<i32>(slot);
    use_ticks = static_cast<i32>(ticks);
    use_instance = tool.instance;
    fire = false;
    fire_once = false;
    return true;
}

void Player::cancel_use()
{
    access->write();

    if (reserved_projectile != 0 && world)
    {
        world->remove_projectile(reserved_projectile);
    }

    reserved_projectile = 0;
    use_slot = 0;
    use_ticks = 0;
    use_instance = 0;
}

bool Player::finish_use(i64 count, bool clear)
{
    access->write();

    if (use_slot == 0 || use_ticks != 0)
    {
        return false;
    }

    auto& tool = tools[static_cast<usize>(use_slot - 1)];

    if (tool.instance != use_instance || tool.cfg_id == 0 || tool.count <= 0)
    {
        cancel_use();
        return false;
    }

    range(count, 0, tool.count);
    require(!clear || count == 0, "invalid_tool_clear");
    tool.count = static_cast<i32>(count);

    if (clear)
    {
        tool = {};
        selected_slot = 0;
    }

    cancel_use();
    return true;
}

Str Player::get_use_instance() const
{
    return std::to_string(use_instance);
}
}

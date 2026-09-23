// 实现 Unit 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Unit.h"
#include "common/Types.h"

namespace hunter
{
std::tuple<i64, i64, i64, i64, bool, i64, bool, bool> Unit::read_motion() const
{
    return {x, y, vx, vy, grounded, facing, alive, pending_remove};
}

void Unit::write_motion(i64 new_x, i64 new_y, i64 new_vx, i64 new_vy,
    bool on_ground, i64 new_facing)
{
    access->write();
    range(new_x, 0, 100000);
    range(new_y, 0, 100000);
    range(new_vx, -1000, 1000);
    range(new_vy, -1000, 1000);
    require(new_facing == -1 || new_facing == 1, "invalid_facing");
    require((alive && hp > 0) || (new_vx == 0 && new_vy == 0), "dead_unit_motion");
    x = static_cast<i32>(new_x);
    y = static_cast<i32>(new_y);
    facing = static_cast<i32>(new_facing);
    vx = static_cast<i32>(new_vx);
    vy = static_cast<i32>(new_vy);
    grounded = on_ground;
}

i64 Unit::get_vx() const
{
    return vx;
}

void Unit::set_vx(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    require((alive && hp > 0) || value == 0, "dead_unit_motion");
    vx = static_cast<i32>(value);
}

i64 Unit::get_vy() const
{
    return vy;
}

void Unit::set_vy(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    require((alive && hp > 0) || value == 0, "dead_unit_motion");
    vy = static_cast<i32>(value);
}

bool Unit::get_grounded() const
{
    return grounded;
}

void Unit::set_grounded(bool value)
{
    access->write();
    grounded = value;
}

i64 Unit::get_hp() const
{
    return hp;
}

void Unit::set_hp(i64 value)
{
    access->write();
    range(value, 0, max_hp);
    require(value == 0 || (alive && hp > 0), "dead_unit_health");
    hp = static_cast<i32>(value);
}

i64 Unit::get_max_hp() const
{
    return max_hp;
}

bool Unit::get_alive() const
{
    return alive;
}

void Unit::set_alive(bool value)
{
    access->write();
    require(value == (hp > 0) && (alive || !value), "invalid_unit_alive");
    alive = value;
}
}

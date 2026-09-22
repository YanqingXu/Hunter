// 实现 Unit 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Unit.h"
#include "common/Types.h"

namespace hunter
{
std::tuple<i64, i64, i64, i64, bool, i64, bool, bool> Unit::read_motion() const
{
    return {entity.x, entity.y, vx, vy, grounded, entity.facing, alive, entity.pending_remove};
}

void Unit::write_motion(i64 x, i64 y, i64 new_vx, i64 new_vy, bool on_ground, i64 facing)
{
    access->write();
    range(x, 0, 100000);
    range(y, 0, 100000);
    range(new_vx, -1000, 1000);
    range(new_vy, -1000, 1000);
    require(facing == -1 || facing == 1, "invalid_facing");
    entity.x = static_cast<i32>(x);
    entity.y = static_cast<i32>(y);
    entity.facing = static_cast<i32>(facing);
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
    alive = value;
}
}

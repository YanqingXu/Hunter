// 实现 Weapon 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Weapon.h"
#include "common/Types.h"

namespace hunter
{
Str Weapon::get_cfg_id() const
{
    return std::to_string(cfg_id);
}

i64 Weapon::get_ammo() const
{
    return ammo;
}

void Weapon::set_ammo(i64 value)
{
    access->write();
    range(value, 0, 1000);
    ammo = static_cast<i32>(value);
}

i64 Weapon::get_shot_ticks() const
{
    return shot_ticks;
}

void Weapon::set_shot_ticks(i64 value)
{
    access->write();
    range(value, 0, 3600);
    shot_ticks = static_cast<i32>(value);
}

i64 Weapon::get_reload_ticks() const
{
    return reload_ticks;
}

void Weapon::set_reload_ticks(i64 value)
{
    access->write();
    range(value, 0, 3600);
    reload_ticks = static_cast<i32>(value);
}
}

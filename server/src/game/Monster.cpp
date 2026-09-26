// 实现 Monster 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Monster.h"
#include "common/Types.h"

namespace hunter
{
Str Monster::get_spawn_id() const
{
    return std::to_string(spawn_id);
}

Str Monster::get_state() const
{
    return state;
}

void Monster::set_state(Str value)
{
    access->write();
    require(value == "spawn" || value == "patrol" || value == "chase" || value == "attack"
        || value == "dead" || value == "windup" || value == "recover", "invalid_ai");
    require((state != "dead" || value == "dead") && (value != "spawn" || state == "spawn"),
        "invalid_monster_transition");
    require(value == "dead" ? (!alive && hp == 0) : (alive && hp > 0),
        "invalid_monster_state");
    state = value;

    if (state == "dead")
    {
        attack_ticks = 0;
        vx = 0;
        vy = 0;
    }
}

i64 Monster::get_attack_ticks() const
{
    return attack_ticks;
}

void Monster::set_attack_ticks(i64 value)
{
    access->write();
    range(value, 0, 3600);
    require(value == 0 || (alive && hp > 0 && state != "spawn" && state != "dead"),
        "invalid_monster_cooldown");
    attack_ticks = static_cast<i32>(value);
}
}

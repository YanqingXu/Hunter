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
        aware = false;
        alert_ticks = 0;
        rage_ticks = 0;
        active_ability = 0;
        ability_phase = "idle";
        ability_ticks = 0;
        ability_cds = {};
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

bool Monster::get_aware() const
{
    return aware;
}

void Monster::set_aware(bool value)
{
    access->write();
    require(!value || alive, "invalid_dead_awareness");
    aware = value;
}

i64 Monster::get_alert_ticks() const
{
    return alert_ticks;
}

void Monster::set_alert_ticks(i64 value)
{
    access->write();
    range(value, 0, 36000);
    require(value == 0 || alive, "invalid_dead_timer");
    alert_ticks = static_cast<i32>(value);
}

i64 Monster::get_rage_ticks() const
{
    return rage_ticks;
}

void Monster::set_rage_ticks(i64 value)
{
    access->write();
    range(value, 0, 36000);
    require(value == 0 || alive, "invalid_dead_timer");
    rage_ticks = static_cast<i32>(value);
}

i64 Monster::get_rage_mask() const
{
    return rage_mask;
}

void Monster::set_rage_mask(i64 value)
{
    access->write();
    range(value, 0, 65535);
    require((static_cast<u32>(value) & rage_mask) == rage_mask, "rage_threshold_rearmed");
    rage_mask = static_cast<u32>(value);
}

i64 Monster::get_last_hp() const
{
    return last_hp;
}

void Monster::set_last_hp(i64 value)
{
    access->write();
    range(value, 0, max_hp);
    last_hp = static_cast<i32>(value);
}

Str Monster::get_active_ability() const
{
    return std::to_string(active_ability);
}

Str Monster::get_ability_phase() const
{
    return ability_phase;
}

i64 Monster::get_ability_ticks() const
{
    return ability_ticks;
}

void Monster::write_ability(const Str& ability_cfg, const Str& phase, i64 ticks)
{
    access->write();
    const auto cfg = read_id(ability_cfg);
    require(cfg <= 2147483647, "invalid_ability_cfg");
    range(ticks, 0, 36000);
    require(phase == "idle" || phase == "windup" || phase == "recover",
        "invalid_ability_phase");
    require(phase == "idle" ? (cfg == 0 && ticks == 0) : (cfg > 0 && ticks > 0 && alive),
        "invalid_ability_state");
    active_ability = static_cast<u32>(cfg);
    ability_phase = phase;
    ability_ticks = static_cast<i32>(ticks);
}

i64 Monster::get_ability_cd(i64 slot) const
{
    range(slot, 1, 8);
    return ability_cds[static_cast<usize>(slot - 1)];
}

void Monster::set_ability_cd(i64 slot, i64 ticks)
{
    access->write();
    range(slot, 1, 8);
    range(ticks, 0, 36000);
    require(ticks == 0 || alive, "invalid_dead_cooldown");
    ability_cds[static_cast<usize>(slot - 1)] = static_cast<i32>(ticks);
}

Str Monster::get_owner_id() const
{
    return std::to_string(owner_id);
}

i64 Monster::get_owner_ability() const
{
    return owner_ability;
}

i64 Monster::get_outside_ticks() const
{
    return outside_ticks;
}

void Monster::set_outside_ticks(i64 value)
{
    access->write();
    range(value, 0, 36000);
    outside_ticks = static_cast<i32>(value);
}

i64 Monster::get_wave() const
{
    return wave;
}
}

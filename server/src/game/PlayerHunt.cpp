// 保存技能消费、手动复活和特殊视角的权威状态，效果及配置规则由 Lua 决定。
#include "common/Types.h"
#include "game/Player.h"
#include "game/World.h"
#include <algorithm>
#include <limits>

namespace hunter
{
i64 Player::get_skill_count() const
{
    access->read();
    return static_cast<i64>(skills.size());
}

Str Player::get_skill_id(i64 slot) const
{
    access->read();
    range(slot, 1, static_cast<i64>(skills.size()));
    return std::to_string(skills[static_cast<usize>(slot - 1)].cfg_id);
}

bool Player::get_skill_spent(i64 slot) const
{
    access->read();
    range(slot, 1, static_cast<i64>(skills.size()));
    return skills[static_cast<usize>(slot - 1)].spent;
}

bool Player::add_skill(const Str& cfg)
{
    access->write();
    const auto skill_id = read_id(cfg);
    require(skill_id >= 1 && skill_id <= 2147483647, "invalid_skill_id");
    if (skills.size() >= 32 || std::ranges::any_of(skills,
        [skill_id](const SkillSlot& value) { return value.cfg_id == skill_id; }))
    {
        return false;
    }
    skills.push_back({static_cast<u32>(skill_id), false});
    return true;
}

bool Player::get_downed() const
{
    access->read();
    return downed;
}

void Player::enter_downed()
{
    access->write();
    require(!alive && hp == 0, "player_not_dead");
    if (!downed)
    {
        require(death_seq < std::numeric_limits<u64>::max(), "death_seq_exhausted");
        ++death_seq;
        downed = true;
    }
    vision = false;
    quiet_ticks = 0;
    running = false;
    melee_ticks = 0;
    other_weapon.shot_ticks = 0;
    other_weapon.reload_ticks = 0;
}

Str Player::get_death_seq() const
{
    access->read();
    return std::to_string(death_seq);
}

bool Player::revive(const Str& seq, i64 health, i64 energy, const Str& skill,
    const Str& removed)
{
    access->write();
    range(health, 1, max_hp);
    range(energy, 0, 1000000);
    const auto skill_id = read_id(skill);
    if (!downed || alive || hp != 0 || read_id(seq) != death_seq)
    {
        return false;
    }
    const auto used = std::ranges::find_if(skills, [skill_id](const SkillSlot& entry)
        { return entry.cfg_id == skill_id && !entry.spent; });
    if (used == skills.end())
    {
        return false;
    }
    const auto doc = nlohmann::json::parse(removed);
    require(doc.is_array() && doc.size() <= 32, "invalid_removed_skills");
    Set<u64> ids{skill_id};
    for (const auto& value : doc)
    {
        require(value.is_string(), "invalid_skill_id");
        const auto removed_id = read_id(value.get<Str>());
        require(std::ranges::any_of(skills, [removed_id](const SkillSlot& entry)
            { return entry.cfg_id == removed_id && !entry.spent; }), "invalid_removed_skill");
        ids.insert(removed_id);
    }
    for (auto& value : skills)
    {
        value.spent = value.spent || ids.contains(value.cfg_id);
    }
    hp = static_cast<i32>(health);
    stamina = static_cast<i32>(energy);
    alive = true;
    downed = false;
    ladder_id = 0;
    quiet_ticks = 180;
    health_rem = 0;
    stamina_rem = 0;
    return true;
}

i64 Player::get_quiet_ticks() const
{
    access->read();
    return quiet_ticks;
}

void Player::set_quiet_ticks(i64 ticks)
{
    access->write();
    range(ticks, 0, 180);
    quiet_ticks = static_cast<i32>(ticks);
}

bool Player::get_vision() const
{
    access->read();
    return vision;
}

void Player::set_vision(bool enabled)
{
    access->write();
    require(!enabled || (alive && !downed && ladder_id == 0), "invalid_vision_state");
    vision = enabled;
    move_x = 0;
    move_y = 0;
    jump = false;
    fire = false;
    fire_once = false;
    reload = false;
    melee = false;
    run = false;
    running = false;
    cancel_use();
    weapon.reload_ticks = 0;
}
}

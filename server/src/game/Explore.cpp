// 保存本局地图排除信息及技能拾取事务，随机与状态提交都由逻辑线程独占。
#include "common/Types.h"
#include "game/World.h"
#include <algorithm>

namespace hunter
{
void World::set_exploration(i64 region, const Str& spawn, i64 count)
{
    access.write();
    range(region, 1, 2147483647);
    range(count, 2, 16);
    const auto id = read_id(spawn);
    require(id > 0 && id <= 2147483647, "invalid_boss_spawn");
    require(phase == "Playing" && !paused && wave == 1 && boss_region == 0
        && boss_spawn == 0 && excluded_regions == 0 && region_count == 0,
        "exploration_already_started");
    boss_region = static_cast<i32>(region);
    region_count = static_cast<i32>(count);
    boss_spawn = static_cast<u32>(id);
}

i64 World::get_boss_region() const
{
    return boss_region;
}

Str World::get_boss_spawn() const
{
    return std::to_string(boss_spawn);
}

i64 World::get_excluded_regions() const
{
    return excluded_regions;
}

i64 World::reveal_region(i64 scene_index, const Str& candidates_text)
{
    access.write();
    range(scene_index, 1, scene_count());
    require(candidates_text.size() <= 4096, "region_candidates_too_large");
    const auto actor = slot(player_entity_id);

    if (phase != "Playing" || paused || boss_region == 0 || actor == actors.size()
        || !actors[actor]->unit().alive || used_scenes[static_cast<usize>(scene_index - 1)])
    {
        return 0;
    }

    const auto candidates = nlohmann::json::parse(candidates_text);
    require(candidates.is_array() && candidates.size() <= 16, "invalid_region_candidates");
    Vec<std::pair<i32, i32>> choices;
    Set<i32> indices;
    Set<i32> ids;

    for (const auto& entry : candidates)
    {
        read_fields(entry, {"index", "id"});
        const auto index = read_integer(entry.at("index"), 1, region_count);
        const auto id = read_integer(entry.at("id"), 1, 2147483647);
        require(indices.insert(index).second && ids.insert(id).second,
            "duplicate_region_candidate");
        require(id != boss_region && (excluded_regions & (1U << (index - 1))) == 0,
            "unavailable_region_candidate");
        choices.emplace_back(index, id);
    }

    if (choices.empty())
    {
        return 0;
    }

    const auto selected = static_cast<usize>(roll(static_cast<i64>(choices.size())) - 1);
    excluded_regions |= 1U << (choices[selected].first - 1);
    used_scenes[static_cast<usize>(scene_index - 1)] = true;
    return choices[selected].second;
}

Str World::commit_skill(const Str& batch, const Str& skill)
{
    access.write();
    const auto id = read_id(skill);
    require(id > 0 && id <= 2147483647, "invalid_skill_cfg");
    const auto actor = slot(player_entity_id);

    if (phase != "Playing" || paused || actor == actors.size())
    {
        return "invalid_state";
    }

    auto& player = std::get<Player>(actors[actor]->value);

    if (!player.alive || player.downed || player.vision)
    {
        return "action_locked";
    }

    if (std::ranges::any_of(player.skills, [id](const SkillSlot& entry)
        { return entry.cfg_id == id; }))
    {
        return "already_known_skill";
    }

    if (player.skills.size() >= 32)
    {
        return "skill_capacity";
    }

    player.skills.reserve(player.skills.size() + 1);
    const auto error = commit(batch);

    if (!error.empty())
    {
        return error;
    }

    player.skills.push_back({static_cast<u32>(id), false});
    return "";
}
}

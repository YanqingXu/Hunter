// 实现 Monster 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Monster.h"
#include "common/Types.h"
#include <algorithm>
#include <limits>

namespace hunter
{
namespace
{
using Json = nlohmann::json;

// 读取配置中的精确整数，避免浮点截断与无符号溢出绕过边界。
i32 integer(const Json& value, i64 low, i64 high)
{
    require(value.is_number_integer() && (!value.is_number_unsigned()
        || value.get<u64>() <= static_cast<u64>(std::numeric_limits<i64>::max())),
        "invalid_spawn_integer");
    const auto result = value.get<i64>();
    range(result, low, high);
    return static_cast<i32>(result);
}

// 检查出生体型和整个巡逻扫掠区域，平台必须连续支撑两端完整体宽。
void validate_spawn(const Json& spawn, const Json& cfg, const Json& map)
{
    const auto width = integer(cfg.at("width"), 2, 10000);
    const auto height = integer(cfg.at("height"), 2, 10000);
    require(width % 2 == 0 && height % 2 == 0, "invalid_monster_size");
    integer(cfg.at("hp"), 1, 1000000);
    integer(cfg.at("speed"), 1, 1000);
    const auto detect = integer(cfg.at("detect_range"), 1, 100000);
    integer(cfg.at("attack_range"), 1, detect);
    integer(cfg.at("damage"), 1, 1000000);
    integer(cfg.at("attack_ticks"), 1, 3600);
    const auto map_width = integer(map.at("width"), 1000, 100000);
    const auto map_height = integer(map.at("height"), 1000, 100000);
    const auto x = integer(spawn.at("x"), width / 2, map_width - width / 2);
    const auto y = integer(spawn.at("y"), 0, map_height - height);
    const auto low = integer(spawn.at("patrol_min"), width / 2, map_width - width / 2);
    const auto high = integer(spawn.at("patrol_max"), width / 2, map_width - width / 2);
    require(low < high && low <= x && x <= high, "invalid_patrol_bounds");
    const auto left = low - width / 2;
    const auto right = high + width / 2;
    Vec<std::pair<i32, i32>> supports;
    require(map.at("solids").is_array(), "invalid_spawn_solids");

    for (const auto& solid : map.at("solids"))
    {
        const auto sx = integer(solid.at("x"), 0, map_width);
        const auto sy = integer(solid.at("y"), 0, map_height);
        const auto sw = integer(solid.at("w"), 1, map_width - sx);
        const auto sh = integer(solid.at("h"), 1, map_height - sy);
        require(!(left < sx + sw && right > sx && y < sy + sh && y + height > sy),
            "invalid_patrol_collision");

        if (y == sy + sh)
        {
            supports.emplace_back(sx, sx + sw);
        }
    }

    if (y == 0)
    {
        return;
    }

    std::sort(supports.begin(), supports.end());
    auto covered = left;

    for (const auto& [start, end] : supports)
    {
        if (start <= covered && end > covered)
        {
            covered = end;
        }
    }

    require(covered >= right, "invalid_patrol_support");
}
}

const nlohmann::json* Monster::spawn_cfg(const nlohmann::json& content, const Str& id)
{
    const auto value = read_id(id);
    require(value > 0 && value <= 2147483647, "invalid_spawn_id");
    const auto& map = content.at("map");
    require(map.at("enemies").is_array(), "invalid_spawn_list");
    const Json* found = nullptr;
    Set<u64> seen;

    for (const auto& spawn : map.at("enemies"))
    {
        const auto spawn_id = spawn.at("spawn_id").get<Str>();
        const auto spawn_value = read_id(spawn_id);
        require(spawn_value > 0 && spawn_value <= 2147483647, "invalid_spawn_id");
        require(seen.insert(spawn_value).second, "duplicate_spawn_id");

        if (spawn_id == id)
        {
            found = &spawn;
        }
    }

    if (!found)
    {
        return nullptr;
    }

    const auto cfg_id = found->at("cfg_id").get<Str>();
    const auto cfg_value = read_id(cfg_id);
    require(cfg_value > 0 && cfg_value <= 2147483647, "invalid_monster_cfg");
    const auto& cfgs = content.at("monsters");
    require(cfgs.is_object() && cfgs.contains(cfg_id), "invalid_monster_cfg");
    validate_spawn(*found, cfgs.at(cfg_id), map);
    return found;
}

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
        || value == "dead", "invalid_ai");
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
    range(value, 0, max_attack_ticks);
    require(value == 0 || (alive && hp > 0 && state != "spawn" && state != "dead"),
        "invalid_monster_cooldown");
    attack_ticks = static_cast<i32>(value);
}
}

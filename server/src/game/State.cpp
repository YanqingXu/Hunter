// 对完整世界进行冷路径序列化和候选验证，状态发布前不修改活动对象。
#include "game/World.h"
#include "common/Types.h"
#include <limits>
#include <algorithm>
#include <cstdlib>

namespace hunter
{
namespace
{
using Json = nlohmann::json;

// 检查完整字段集合，拒绝未知字段和缺项。
void fields(const Json& value, std::initializer_list<const char*> names)
{
    require(value.is_object() && value.size() == names.size(), "invalid_state_fields");

    for (const auto name : names)
    {
        require(value.contains(name), "missing_state_field");
    }
}

// 读取有界精确整数，不接受浮点或布尔隐式转换。
i32 integer(const Json& value, i64 low, i64 high)
{
    require(value.is_number_integer() && (!value.is_number_unsigned()
        || value.get<u64>() <= static_cast<u64>(std::numeric_limits<i64>::max())),
        "invalid_state_integer");
    const auto result = value.get<i64>();
    range(result, low, high);
    return static_cast<i32>(result);
}

// 读取严格布尔字段。
bool boolean(const Json& value)
{
    require(value.is_boolean(), "invalid_state_boolean");
    return value.get<bool>();
}

// 读取规范且可为零的十进制身份。
u64 identity(const Json& value)
{
    require(value.is_string(), "invalid_state_id");
    return read_id(value.get<Str>());
}

// 读取非零配置身份。
u32 cfg_id(const Json& value)
{
    const auto result = identity(value);
    require(result > 0 && result <= 2147483647, "invalid_state_cfg");
    return static_cast<u32>(result);
}

// 将冻结配装转换为仅用于冷路径导入导出的对象。
Json loadout_doc(const wire::Loadout& loadout)
{
    Json result = {{"player_cfg_id", std::to_string(loadout.player_cfg_id())},
        {"health_segments", Json::array()}, {"weapons", Json::array()},
        {"tools", Json::array()}, {"consumables", Json::array()}};

    for (const auto value : loadout.health_segments())
    {
        result["health_segments"].push_back(value);
    }

    for (const auto& weapon : loadout.weapons())
    {
        result["weapons"].push_back({{"cfg_id", std::to_string(weapon.cfg_id())},
            {"ammo_cfg_id", std::to_string(weapon.ammo_cfg_id())}});
    }

    for (const auto value : loadout.tools())
    {
        result["tools"].push_back(std::to_string(value));
    }

    for (const auto value : loadout.consumables())
    {
        result["consumables"].push_back(std::to_string(value));
    }

    return result;
}

// 严格读取冷路径配装，活动对局另行执行完整内容校验。
wire::Loadout read_loadout(const Json& value)
{
    fields(value, {"player_cfg_id", "health_segments", "weapons", "tools", "consumables"});
    wire::Loadout loadout;
    const auto player = identity(value.at("player_cfg_id"));
    require(player <= 2147483647, "invalid_loadout_cfg");
    loadout.set_player_cfg_id(static_cast<u32>(player));
    require(value.at("health_segments").is_array() && value.at("health_segments").size() <= 6
        && value.at("weapons").is_array() && value.at("weapons").size() <= 2
        && value.at("tools").is_array() && value.at("tools").size() <= 4
        && value.at("consumables").is_array() && value.at("consumables").size() <= 4,
        "invalid_loadout_slots");

    for (const auto& amount : value.at("health_segments"))
    {
        loadout.add_health_segments(static_cast<u32>(integer(amount, 25, 50)));
    }

    for (const auto& entry : value.at("weapons"))
    {
        fields(entry, {"cfg_id", "ammo_cfg_id"});
        auto& weapon = *loadout.add_weapons();
        weapon.set_cfg_id(cfg_id(entry.at("cfg_id")));
        weapon.set_ammo_cfg_id(cfg_id(entry.at("ammo_cfg_id")));
    }

    for (const auto& id : value.at("tools"))
    {
        loadout.add_tools(cfg_id(id));
    }

    for (const auto& id : value.at("consumables"))
    {
        loadout.add_consumables(cfg_id(id));
    }

    return loadout;
}

// 将单个枪械组件转换为冷路径字段，不包含重复备用弹药。
Json weapon_doc(const Weapon& weapon)
{
    return {{"cfg_id", weapon.get_cfg_id()}, {"ammo", weapon.ammo},
        {"shot_ticks", weapon.shot_ticks}, {"reload_ticks", weapon.reload_ticks}};
}

// 校验空组件或完整枪械及其范围。
void read_weapon(Weapon& weapon, const Json& value, const Json& content, bool empty)
{
    fields(value, {"cfg_id", "ammo", "shot_ticks", "reload_ticks"});

    if (empty)
    {
        require(value == weapon_doc(Weapon{}), "invalid_empty_weapon");
        return;
    }

    weapon.cfg_id = cfg_id(value.at("cfg_id"));
    const auto& cfg = content.at("weapons").at(weapon.get_cfg_id());
    weapon.ammo = integer(value.at("ammo"), 0, cfg.at("magazine"));
    weapon.shot_ticks = integer(value.at("shot_ticks"), 0, cfg.at("fire_ticks"));
    weapon.reload_ticks = integer(value.at("reload_ticks"), 0, cfg.at("reload_ticks"));
}

// 将玩家增量状态导出为严格、无别名的冷路径字段。
Json demo_doc(const Player& player)
{
    Json result = {{"move_y", player.move_y}, {"run", player.run},
        {"want_prone", player.want_prone}, {"prone", player.prone},
        {"running", player.running}, {"stamina", player.stamina},
        {"stamina_delay", player.stamina_delay}, {"stamina_rem", player.stamina_rem},
        {"health_delay", player.health_delay}, {"health_rem", player.health_rem},
        {"melee_ticks", player.melee_ticks}, {"melee", player.melee},
        {"ladder_id", player.ladder_id}, {"selected_slot", player.selected_slot},
        {"use_slot", player.use_slot}, {"use_ticks", player.use_ticks},
        {"use_instance", std::to_string(player.use_instance)},
        {"last_tool_instance", std::to_string(player.last_tool_instance)},
        {"reserved_projectile", player.reserved_projectile},
        {"weapon_count", player.weapon_count}, {"active_weapon", player.active_weapon},
        {"other_weapon", weapon_doc(player.other_weapon)},
        {"other_reserve", player.other_reserve},
        {"ammo_cfg_ids", player.ammo_cfg_ids}, {"health_segments", player.health_segments},
        {"tools", Json::array()}};

    for (const auto& tool : player.tools)
    {
        result["tools"].push_back({{"cfg_id", std::to_string(tool.cfg_id)},
            {"count", tool.count}, {"instance", std::to_string(tool.instance)}});
    }

    return result;
}

// 读取增量状态并校验槽位、工具实例、计时器和配置关系。
void read_demo(Player& player, const Json& value, World& world)
{
    fields(value, {"move_y", "run", "want_prone", "prone", "running", "stamina",
        "stamina_delay", "stamina_rem", "health_delay", "health_rem", "melee_ticks",
        "melee", "ladder_id", "selected_slot", "use_slot", "use_ticks", "use_instance",
        "last_tool_instance", "reserved_projectile", "weapon_count", "active_weapon",
        "other_weapon", "other_reserve", "ammo_cfg_ids", "health_segments", "tools"});
    const auto& content = world.content;
    const auto& cfg = content.at("players").at(player.get_cfg_id());
    player.move_y = integer(value.at("move_y"), -1, 1);
    player.run = boolean(value.at("run"));
    player.want_prone = boolean(value.at("want_prone"));
    player.prone = boolean(value.at("prone"));
    player.running = boolean(value.at("running"));
    player.melee = boolean(value.at("melee"));
    require(!player.prone || !player.running, "invalid_prone_run");
    player.stamina = integer(value.at("stamina"), 0, cfg.at("stamina"));
    player.stamina_delay = integer(value.at("stamina_delay"), 0, cfg.at("stamina_delay"));
    player.stamina_rem = integer(value.at("stamina_rem"), 0, 59);
    player.health_delay = integer(value.at("health_delay"), 0, cfg.at("health_delay"));
    player.health_rem = integer(value.at("health_rem"), 0, 59);
    player.melee_ticks = integer(value.at("melee_ticks"), 0, content.at("rules").at("melee_ticks"));
    player.ladder_id = integer(value.at("ladder_id"), 0, 2147483647);
    player.selected_slot = integer(value.at("selected_slot"), 0, 8);
    player.use_slot = integer(value.at("use_slot"), 0, 8);
    player.use_ticks = integer(value.at("use_ticks"), 0, 36000);
    player.use_instance = identity(value.at("use_instance"));
    player.last_tool_instance = identity(value.at("last_tool_instance"));
    player.reserved_projectile = integer(value.at("reserved_projectile"), 0, 16);
    player.weapon_count = integer(value.at("weapon_count"), 1, 2);
    player.active_weapon = integer(value.at("active_weapon"), 1, player.weapon_count);
    read_weapon(player.other_weapon, value.at("other_weapon"), content, player.weapon_count == 1);
    player.other_reserve = integer(value.at("other_reserve"), 0,
        player.weapon_count == 1 ? 0 : content.at("weapons")
            .at(player.other_weapon.get_cfg_id()).at("reserve").get<i32>());
    require(player.other_weapon.reload_ticks == 0, "inactive_reload");
    const auto& ammo = value.at("ammo_cfg_ids");
    require(ammo.is_array() && ammo.size() == 2, "invalid_ammo_slots");

    for (usize index = 0; index < 2; ++index)
    {
        player.ammo_cfg_ids[index] = static_cast<u32>(integer(ammo[index], 0, 2147483647));

        if (index < static_cast<usize>(player.weapon_count))
        {
            require(content.at("weapons").at(player.get_weapon_cfg(index + 1))
                .at("ammo_cfg_id") == std::to_string(player.ammo_cfg_ids[index]),
                "invalid_ammo_reference");
        }
        else
        {
            require(player.ammo_cfg_ids[index] == 0, "invalid_empty_ammo");
        }
    }

    const auto& segments = value.at("health_segments");
    require(segments.is_array() && segments.size() > 0 && segments.size() <= 6,
        "invalid_health_segments");
    i32 total = 0;

    for (const auto& amount : segments)
    {
        const auto number = integer(amount, 25, 50);
        require(number == 25 || number == 50, "invalid_health_segments");
        total += number;
        player.health_segments.push_back(number);
    }

    require(total == player.max_hp, "invalid_health_segments");
    const auto& tools = value.at("tools");
    require(tools.is_array() && tools.size() == 8, "invalid_tool_slots");
    Set<u32> cfgs;
    Set<u64> instances;

    for (usize index = 0; index < 8; ++index)
    {
        const auto& entry = tools[index];
        fields(entry, {"cfg_id", "count", "instance"});
        auto& tool = player.tools[index];
        const auto id = identity(entry.at("cfg_id"));
        require(id <= 2147483647, "invalid_tool_cfg");
        tool.cfg_id = static_cast<u32>(id);
        tool.instance = identity(entry.at("instance"));

        if (id == 0)
        {
            require(tool.instance == 0 && integer(entry.at("count"), 0, 0) == 0,
                "invalid_empty_tool");
            continue;
        }

        require(cfgs.insert(tool.cfg_id).second && instances.insert(tool.instance).second
            && tool.instance > 0 && tool.instance <= player.last_tool_instance,
            "invalid_tool_identity");
        const auto& source = content.at("tools").at(std::to_string(id));
        const Str kind = source.at("kind");
        tool.maximum = source.at("uses");
        tool.consumable = kind == "needle" || kind == "bomb";
        tool.unlimited = kind == "knife";
        tool.projectile = kind == "bomb";
        tool.count = integer(entry.at("count"), tool.consumable ? 1 : 0, tool.maximum);
        require(tool.consumable == (index >= 4), "invalid_tool_slot");
    }

    if (player.use_slot == 0)
    {
        require(player.use_ticks == 0 && player.use_instance == 0
            && player.reserved_projectile == 0, "invalid_inactive_use");
    }
    else
    {
        const auto& tool = player.tools[static_cast<usize>(player.use_slot - 1)];
        require(tool.cfg_id > 0 && tool.instance == player.use_instance && tool.count > 0
            && player.alive && player.ladder_id == 0 && player.use_ticks > 0
            && player.use_ticks <= content.at("tools").at(std::to_string(tool.cfg_id))
                .at("use_ticks").get<i32>(), "invalid_active_use");
        require((player.reserved_projectile > 0) == tool.projectile,
            "invalid_projectile_reservation");
    }

    require(player.alive || (player.melee_ticks == 0 && !player.running
        && player.other_weapon.shot_ticks == 0 && player.other_weapon.reload_ticks == 0),
        "invalid_dead_demo");

    if (player.ladder_id > 0)
    {
        bool found = false;

        for (const auto& scene : content.at("scenes"))
        {
            if (scene.at("id") == std::to_string(player.ladder_id))
            {
                found = scene.at("kind") == "ladder" && !player.prone && !player.running
                    && player.vx == 0 && std::abs(player.vy) <= cfg.at("speed").get<i32>()
                    && player.x == scene.at("x").get<i32>() + scene.at("w").get<i32>() / 2
                    && player.y >= scene.at("y").get<i32>()
                    && player.y <= scene.at("y").get<i32>() + scene.at("h").get<i32>();
            }
        }

        require(found, "invalid_ladder_state");
    }
}

// 从快照读取单位并检查碰撞、运动和生命值之间的约束。
void read_unit(Unit& unit, const Json& obj, const Json& cfg, const Json& map)
{
    fields(obj.at("pose"), {"x", "y", "facing"});
    fields(obj.at("motion"), {"vx", "vy", "grounded"});
    fields(obj.at("health"), {"hp", "max_hp", "alive"});
    Entity& entity = unit;
    const i32 width = cfg.at("width");
    const i32 height = cfg.at("height");
    entity.x = integer(obj["pose"]["x"], width / 2, map.at("width").get<i32>() - width / 2);
    entity.y = integer(obj["pose"]["y"], 0, map.at("height").get<i32>() - height);
    entity.facing = integer(obj["pose"]["facing"], -1, 1);
    require(entity.facing != 0, "invalid_state_facing");
    const i32 speed = std::max(cfg.at("speed").get<i32>(), cfg.value("run_speed", 0));
    unit.vx = integer(obj["motion"]["vx"], -speed, speed);
    unit.vy = integer(obj["motion"]["vy"], -1000, cfg.value("jump_speed", 0));
    unit.grounded = boolean(obj["motion"]["grounded"]);
    unit.max_hp = integer(obj["health"]["max_hp"], cfg.at("hp"), cfg.at("hp"));
    unit.hp = integer(obj["health"]["hp"], 0, unit.max_hp);
    unit.alive = boolean(obj["health"]["alive"]);
    require(unit.alive == (unit.hp > 0) && (!unit.grounded || unit.vy == 0)
        && (unit.alive || (unit.vx == 0 && unit.vy == 0)), "invalid_unit_state");
    bool grounded = entity.y == 0;

    for (const auto& solid : map.at("solids"))
    {
        const i32 x = solid.at("x");
        const i32 y = solid.at("y");
        const i32 w = solid.at("w");
        const i32 h = solid.at("h");
        const bool overlap = entity.x - width / 2 < x + w && entity.x + width / 2 > x;
        require((obj.contains("demo") && obj.at("demo").at("ladder_id") != 0)
            || !(overlap && entity.y < y + h && entity.y + height > y), "state_collision");
        grounded = grounded || (overlap && entity.y == y + h);
    }

    require((obj.contains("demo") && obj.at("demo").at("ladder_id") != 0)
        || unit.grounded == grounded, "invalid_grounded");
}

// 读取并校验玩家专属输入、枪械和配置关系。
void read_player(Player& player, const Json& obj, World& world)
{
    fields(obj, {"id", "kind", "cfg_id", "pose", "pending_remove", "motion", "health",
        "player_id", "controls", "weapon", "reserve", "demo"});
    const auto& content = world.content;
    player.player_id = identity(obj["player_id"]);
    require(player.player_id > 0 && !player.pending_remove
        && content.at("players").contains(obj["cfg_id"].get<Str>()), "invalid_player");
    const auto& input = obj["controls"];
    fields(input, {"move_x", "aim_x", "aim_y", "jump", "fire", "fire_once", "reload"});
    player.move_x = integer(input["move_x"], -1, 1);
    player.aim_x = integer(input["aim_x"], -1000, 1000);
    player.aim_y = integer(input["aim_y"], -1000, 1000);
    require(player.aim_x != 0 || player.aim_y != 0, "invalid_aim");
    player.jump = boolean(input["jump"]);
    player.fire = boolean(input["fire"]);
    player.fire_once = boolean(input["fire_once"]);
    player.reload = boolean(input["reload"]);
    const auto& gun = obj["weapon"];
    fields(gun, {"cfg_id", "ammo", "shot_ticks", "reload_ticks"});
    require(content.at("weapons").contains(gun["cfg_id"].get<Str>()), "invalid_weapon_cfg");
    player.weapon.cfg_id = cfg_id(gun["cfg_id"]);
    const auto& cfg = content.at("weapons").at(gun["cfg_id"].get<Str>());
    player.weapon.ammo = integer(gun["ammo"], 0, cfg.at("magazine"));
    player.weapon.shot_ticks = integer(gun["shot_ticks"], 0, cfg.at("fire_ticks"));
    player.weapon.reload_ticks = integer(gun["reload_ticks"], 0, cfg.at("reload_ticks"));
    player.reserve = integer(obj["reserve"], 0, cfg.at("reserve"));
    require(player.alive || (player.weapon.shot_ticks == 0
        && player.weapon.reload_ticks == 0), "dead_weapon_cooldown");
    read_demo(player, obj.at("demo"), world);
}

// 校验怪物配置、出生引用和 AI 生死关系。
void read_monster(Monster& monster, const Json& obj, const Json& content)
{
    fields(obj, {"id", "kind", "cfg_id", "pose", "pending_remove", "motion", "health",
        "spawn_id", "ai"});
    monster.spawn_id = cfg_id(obj["spawn_id"]);
    const auto* spawn = Monster::spawn_cfg(content, monster.get_spawn_id());
    require(spawn && spawn->at("cfg_id") == obj["cfg_id"], "invalid_spawn_reference");
    fields(obj["ai"], {"state", "attack_ticks"});
    monster.state = obj["ai"]["state"].get<Str>();
    const auto& cfg = content.at("monsters").at(obj["cfg_id"].get<Str>());
    monster.max_attack_ticks = cfg.at("attack_ticks");
    monster.attack_ticks = integer(obj["ai"]["attack_ticks"], 0, cfg.at("attack_ticks"));
    require(monster.alive ? (monster.state == "spawn"
        || monster.state == "patrol" || monster.state == "chase"
        || monster.state == "attack" || monster.state == "windup" || monster.state == "recover")
        : (monster.state == "dead" && monster.attack_ticks == 0),
        "invalid_monster_state");
    require(monster.state != "spawn" || (monster.attack_ticks == 0 && monster.vx == 0
        && monster.vy == 0 && monster.grounded && monster.hp == monster.max_hp
        && monster.x == spawn->at("x") && monster.y == spawn->at("y")),
        "invalid_spawn_state");
}

// 将完整文档解码到独立世界，发布前验证全部引用和状态不变量。
void read_world(World& world, const Json& doc)
{
    fields(doc, {"v", "tick_id", "seq", "match_id", "player_id", "event_id", "phase",
        "paused", "content_key", "entities", "entity_ids", "player_entity_id",
        "last_entity_id", "last_start", "items", "last_item_id", "world_id", "raid",
        "loadout", "scene_used", "projectiles", "last_projectile_id", "action_seq"});
    integer(doc["v"], 6, 6);
    require(doc["content_key"] == world.content_key, "content_identity_mismatch");
    world.tick_id = identity(doc["tick_id"]);
    require(world.tick_id <= static_cast<u64>(std::numeric_limits<i64>::max()), "invalid_tick");
    world.seq = identity(doc["seq"]);
    world.match_id = identity(doc["match_id"]);
    world.world_id = identity(doc["world_id"]);
    world.action_seq = identity(doc.at("action_seq"));
    world.active_loadout = read_loadout(doc.at("loadout"));

    if (world.match_id != 0)
    {
        world.active_loadout = world.check_loadout(world.active_loadout);
    }
    else
    {
        require(world.active_loadout.ByteSizeLong() == 0, "inactive_loadout");
    }

    const auto& scene_used = doc.at("scene_used");
    require(scene_used.is_array() && scene_used.size() == world.content.at("scenes").size()
        && scene_used.size() <= 32, "invalid_scene_states");

    for (usize index = 0; index < scene_used.size(); ++index)
    {
        world.used_scenes[index] = boolean(scene_used[index]);
        require(!world.used_scenes[index] || world.content.at("scenes")[index].at("kind")
            == "supply", "invalid_scene_usage");
    }

    world.last_projectile_id = identity(doc.at("last_projectile_id"));
    const auto& projectiles = doc.at("projectiles");
    require(projectiles.is_array() && projectiles.size() == 16, "invalid_projectile_slots");
    Set<u64> projectile_ids;

    for (usize index = 0; index < projectiles.size(); ++index)
    {
        const auto& entry = projectiles[index];
        fields(entry, {"id", "cfg_id", "x", "y", "vx", "vy", "remaining", "active", "reserved"});
        auto& projectile = world.projectiles[index];
        projectile.active = boolean(entry.at("active"));
        projectile.reserved = boolean(entry.at("reserved"));
        require(!projectile.active || !projectile.reserved, "invalid_projectile_slot");
        projectile.id = identity(entry.at("id"));
        const auto projectile_cfg_id = identity(entry.at("cfg_id"));
        require(projectile_cfg_id <= 2147483647, "invalid_projectile_cfg");
        projectile.cfg_id = static_cast<u32>(projectile_cfg_id);
        projectile.x = integer(entry.at("x"), 0, world.content.at("map").at("width"));
        projectile.y = integer(entry.at("y"), 0, world.content.at("map").at("height"));
        projectile.vx = integer(entry.at("vx"), -1000, 1000);
        projectile.vy = integer(entry.at("vy"), -1000, 1000);
        projectile.remaining = integer(entry.at("remaining"), 0, 100000);

        if (projectile.active)
        {
            const auto& cfg = world.content.at("tools").at(std::to_string(projectile.cfg_id));
            require(projectile.id > 0 && projectile.id <= world.last_projectile_id
                && projectile_ids.insert(projectile.id).second && cfg.at("kind") == "bomb"
                && projectile.remaining > 0 && projectile.remaining <= cfg.at("throw_range")
                && (projectile.vx != 0 || projectile.vy != 0), "invalid_projectile");
        }
        else
        {
            require(projectile.id == 0 && projectile.cfg_id == 0 && projectile.x == 0
                && projectile.y == 0 && projectile.vx == 0 && projectile.vy == 0
                && projectile.remaining == 0, "invalid_inactive_projectile");
        }
    }

    const auto& raid = doc.at("raid");
    fields(raid, {"player_state", "random", "dropped", "extract_id", "extract_ticks",
        "extract_reason", "damage_tick"});
    world.raid.player_state = raid.at("player_state").get<Str>();
    require(world.raid.player_state == "Alive" || world.raid.player_state == "Dead"
        || world.raid.player_state == "Extracted" || world.raid.player_state == "Abandoned",
        "invalid_player_state");
    const auto random = identity(raid.at("random"));
    require(random > 0 && random <= 4294967295ULL, "invalid_random");
    world.raid.random = static_cast<u32>(random);
    world.raid.extract_id = static_cast<u32>(integer(raid.at("extract_id"), 0, 2147483647));
    world.raid.extract_ticks = integer(raid.at("extract_ticks"), 0, 36000);
    world.raid.extract_reason = raid.at("extract_reason").get<Str>();
    require(world.raid.extract_reason == "locked" || world.raid.extract_reason == "outside"
        || world.raid.extract_reason == "hurt" || world.raid.extract_reason == "dead"
        || world.raid.extract_reason == "counting" || world.raid.extract_reason == "complete",
        "invalid_extract_reason");
    world.raid.damage_tick = identity(raid.at("damage_tick"));
    require(world.raid.damage_tick <= world.tick_id, "invalid_damage_tick");
    require(raid.at("dropped").is_array() && raid.at("dropped").size() <= 64,
        "invalid_drop_index");

    for (const auto& value : raid.at("dropped"))
    {
        const auto id = identity(value);
        require(id > 0 && world.raid.dropped.insert(id).second, "invalid_drop_identity");
    }

    world.player_id = identity(doc["player_id"]);
    world.event_id = identity(doc["event_id"]);
    world.phase = doc["phase"].get<Str>();
    world.paused = boolean(doc["paused"]);
    world.player_entity_id = identity(doc["player_entity_id"]);
    world.last_entity_id = identity(doc["last_entity_id"]);
    world.last_item_id = identity(doc["last_item_id"]);
    const auto& last = doc["last_start"];
    fields(last, {"req_id", "after_match_id", "match_id"});
    world.last_req = last["req_id"].get<Str>();
    require(world.last_req.size() <= 128, "invalid_request_id");
    world.last_after = identity(last["after_match_id"]);
    world.last_match = identity(last["match_id"]);
    const auto& ids = doc["entity_ids"];
    const auto& entities = doc["entities"];
    require(ids.is_array() && ids.size() <= 64 && entities.is_object()
        && ids.size() == entities.size(), "invalid_entity_index");
    Set<u64> seen;
    const Player* player = nullptr;
    usize alive = 0;

    for (const auto& id : ids)
    {
        const auto value = identity(id);
        require(value != 0 && value <= world.last_entity_id && seen.insert(value).second,
            "invalid_entity_identity");
        const auto& obj = entities.at(id.get<Str>());
        require(obj.at("id") == id, "entity_key_mismatch");
        const auto kind = obj.at("kind").get<Str>();
        require(kind == "player" || kind == "monster", "invalid_entity_kind");
        Actor actor;

        if (kind == "monster")
        {
            actor.value = Monster{};
        }

        auto& unit = actor.unit();
        unit.id = value;
        unit.kind = kind;
        unit.cfg_id = cfg_id(obj.at("cfg_id"));
        unit.pending_remove = boolean(obj.at("pending_remove"));
        const auto& cfg = world.content.at(kind == "player" ? "players" : "monsters")
            .at(obj["cfg_id"].get<Str>());
        auto body = cfg;

        if (kind == "player")
        {
            const bool prone = boolean(obj.at("demo").at("prone"));
            const bool running = boolean(obj.at("demo").at("running"));
            body["speed"] = cfg.at(prone ? "prone_speed" : (running ? "run_speed" : "speed"));
            body["run_speed"] = body.at("speed");

            if (prone)
            {
                body["width"] = cfg.at("prone_width");
                body["height"] = cfg.at("prone_height");
            }
        }

        auto map = world.content.at("map");

        for (const auto& scene : world.content.at("scenes"))
        {
            if (scene.at("kind") == "cover")
            {
                map["solids"].push_back({{"x", scene.at("x")}, {"y", scene.at("y")},
                    {"w", scene.at("w")}, {"h", scene.at("h")}});
            }
        }

        read_unit(unit, obj, body, map);
        require(world.phase == "Playing" || (unit.vx == 0 && unit.vy == 0), "terminal_motion");
        const auto index = static_cast<u32>(world.order.size());

        if (kind == "player")
        {
            require(!player && value == world.player_entity_id, "invalid_player_reference");
            read_player(std::get<Player>(actor.value), obj, world);
        }
        else
        {
            read_monster(std::get<Monster>(actor.value), obj, world.content);
            alive += unit.alive && !unit.pending_remove ? 1 : 0;
        }

        world.actors[index] = std::move(actor);
        world.order.push_back(index);

        if (kind == "player")
        {
            player = &std::get<Player>(world.actors[index]->value);
        }
    }

    const auto& items = doc["items"];
    require(items.is_object() && items.size() <= 64, "invalid_items");
    u32 item_index = 0;

    for (auto entry = items.begin(); entry != items.end(); ++entry)
    {
        const auto& obj = entry.value();
        fields(obj, {"id", "cfg_id", "count", "place", "x", "y", "owner_player_id"});
        ItemSlot item;
        item.value.id = identity(obj["id"]);
        require(item.value.id != 0 && obj["id"] == entry.key()
            && item.value.id <= world.last_item_id, "invalid_item_identity");
        item.value.cfg_id = cfg_id(obj["cfg_id"]);
        item.value.count = integer(obj["count"], 1, 2147483647);
        item.value.place = obj.at("place").get<Str>();
        item.value.owner_player_id = identity(obj.at("owner_player_id"));
        require(item.value.owner_player_id == (item.value.place == "Bag" ? world.player_id : 0),
            "invalid_item_owner");
        require(item.value.place == "None" || item.value.place == "Ground"
            || item.value.place == "Bag", "invalid_item_place");
        item.value.x = integer(obj.at("x"), 0, world.content.at("map").at("width"));
        item.value.y = integer(obj.at("y"), 0, world.content.at("map").at("height"));

        if (item.value.place != "None")
        {
            const auto& cfg = world.content.at("items").at(item.value.get_cfg_id());
            require(item.value.count <= cfg.at("max_stack"), "invalid_stack");
            require(item.value.place != "Bag" || cfg.value("kind", 5) == 5,
                "equipment_in_reward_bag");
        }

        world.items[item_index++] = item;
    }

    usize bag_count = 0;

    for (const auto& item : world.items)
    {
        if (item && item->value.place == "Bag")
        {
            ++bag_count;
        }
    }

    require(bag_count == 0 || (world.content.contains("bag")
        && bag_count <= world.content.at("bag").at("slots").get<usize>()), "invalid_bag_capacity");

    for (const auto id : world.raid.dropped)
    {
        require(id <= world.last_entity_id, "invalid_drop_identity");
        const auto slot = world.slot(id);
        require(slot == world.actors.size() || (world.actors[slot]->unit().kind == "monster"
            && !world.actors[slot]->unit().alive), "invalid_drop_source");
    }

    if (world.phase == "Unauthenticated" || world.phase == "Lobby"
        || (world.phase == "Preparing" && world.match_id == 0))
    {
        require((world.phase != "Unauthenticated" ? world.player_id > 0 : world.player_id == 0)
            && world.match_id == 0 && world.order.empty() && world.player_entity_id == 0
            && world.last_entity_id == 0 && world.last_req.empty() && world.last_after == 0
            && world.last_match == 0 && items.empty() && world.last_item_id == 0
            && world.last_projectile_id == 0 && world.action_seq == 0
            && std::none_of(world.used_scenes.begin(), world.used_scenes.end(),
                [](bool value) { return value; })
            && std::none_of(world.projectiles.begin(), world.projectiles.end(),
                [](const auto& value) { return value.active || value.reserved; }),
            "invalid_inactive_state");
        return;
    }

    require(player && world.player_id == player->player_id && world.world_id > 0
        && world.match_id != 0 && !world.last_req.empty()
        && world.last_match == world.match_id && world.last_after < world.match_id
        , "invalid_match_state");
    require(!(world.paused || world.phase != "Playing") || (player->move_x == 0
        && player->move_y == 0 && !player->run && !player->melee
        && !player->jump && !player->fire && !player->fire_once && !player->reload),
        "inactive_controls");
    require(player->cfg_id == world.active_loadout.player_cfg_id()
        && player->weapon_count == world.active_loadout.weapons_size(), "loadout_mismatch");

    for (i32 index = 0; index < player->weapon_count; ++index)
    {
        require(player->weapon_at(index + 1).cfg_id == world.active_loadout.weapons(index).cfg_id()
            && player->ammo_cfg_ids[static_cast<usize>(index)]
                == world.active_loadout.weapons(index).ammo_cfg_id(), "loadout_mismatch");
    }

    require(static_cast<usize>(world.active_loadout.health_segments_size())
        == player->health_segments.size(), "loadout_mismatch");

    for (usize index = 0; index < player->health_segments.size(); ++index)
    {
        require(static_cast<u32>(player->health_segments[index])
            == world.active_loadout.health_segments(static_cast<i32>(index)), "loadout_mismatch");
    }

    for (usize index = 0; index < world.projectiles.size(); ++index)
    {
        require(world.projectiles[index].reserved
            == (player->reserved_projectile == static_cast<i32>(index + 1)),
            "orphan_projectile_reservation");
        require(world.phase == "Playing" || (!world.projectiles[index].active
            && !world.projectiles[index].reserved), "terminal_projectile");
    }

    require((world.phase == "Dead" && !player->alive)
        || (world.phase == "Cleared" && player->alive && alive == 0)
        || (world.phase == "Playing" && player->alive
            && (alive > 0 || world.content.contains("extracts")))
        || ((world.phase == "Settling" || world.phase == "Finished" || world.phase == "Preparing")
            && world.raid.player_state != "Alive"
            && (player->alive == (world.raid.player_state != "Dead"))), "invalid_phase_state");
}
}

nlohmann::json World::document() const
{
    access.read();
    Json doc = {{"v", 6}, {"tick_id", get_tick_id()}, {"seq", get_seq()},
        {"match_id", get_match_id()}, {"player_id", get_player_id()},
        {"world_id", std::to_string(world_id)},
        {"event_id", std::to_string(event_id)}, {"phase", phase}, {"paused", paused},
        {"content_key", content_key}, {"player_entity_id", get_player_entity_id()},
        {"last_entity_id", get_last_entity_id()}, {"last_item_id", std::to_string(last_item_id)},
        {"last_start", {{"req_id", last_req}, {"after_match_id", get_last_after()},
            {"match_id", std::to_string(last_match)}}}, {"entities", Json::object()},
        {"entity_ids", Json::array()}, {"items", Json::object()}};
    doc["loadout"] = loadout_doc(active_loadout);
    doc["scene_used"] = Json::array();

    for (usize index = 0; index < content.at("scenes").size(); ++index)
    {
        doc["scene_used"].push_back(used_scenes[index]);
    }

    doc["action_seq"] = std::to_string(action_seq);
    doc["last_projectile_id"] = std::to_string(last_projectile_id);
    doc["projectiles"] = Json::array();

    for (const auto& value : projectiles)
    {
        doc["projectiles"].push_back({{"id", std::to_string(value.id)},
            {"cfg_id", std::to_string(value.cfg_id)}, {"x", value.x}, {"y", value.y},
            {"vx", value.vx}, {"vy", value.vy}, {"remaining", value.remaining},
            {"active", value.active}, {"reserved", value.reserved}});
    }

    doc["raid"] = {{"player_state", raid.player_state},
        {"random", std::to_string(raid.random)}, {"dropped", Json::array()},
        {"extract_id", raid.extract_id}, {"extract_ticks", raid.extract_ticks},
        {"extract_reason", raid.extract_reason}, {"damage_tick", std::to_string(raid.damage_tick)}};

    for (const auto id : raid.dropped)
    {
        doc["raid"]["dropped"].push_back(std::to_string(id));
    }

    for (const auto index : order)
    {
        const auto& actor = *actors[index];
        const auto& unit = actor.unit();
        const Entity& e = unit;
        Json obj = {{"id", e.get_id()}, {"kind", e.kind}, {"cfg_id", e.get_cfg_id()},
            {"pending_remove", e.pending_remove},
            {"pose", {{"x", e.x}, {"y", e.y}, {"facing", e.facing}}},
            {"motion", {{"vx", unit.vx}, {"vy", unit.vy}, {"grounded", unit.grounded}}},
            {"health", {{"hp", unit.hp}, {"max_hp", unit.max_hp}, {"alive", unit.alive}}}};

        if (const auto* p = std::get_if<Player>(&actor.value))
        {
            obj["player_id"] = p->get_player_id();
            obj["demo"] = demo_doc(*p);
            obj["reserve"] = p->reserve;
            obj["controls"] = {{"move_x", p->move_x}, {"aim_x", p->aim_x}, {"aim_y", p->aim_y},
                {"jump", p->jump}, {"fire", p->fire}, {"fire_once", p->fire_once},
                {"reload", p->reload}};
            obj["weapon"] = {{"cfg_id", p->weapon.get_cfg_id()}, {"ammo", p->weapon.ammo},
                {"shot_ticks", p->weapon.shot_ticks}, {"reload_ticks", p->weapon.reload_ticks}};
        }
        else
        {
            const auto& m = std::get<Monster>(actor.value);
            obj["spawn_id"] = m.get_spawn_id();
            obj["ai"] = {{"state", m.state}, {"attack_ticks", m.attack_ticks}};
        }

        doc["entity_ids"].push_back(e.get_id());
        doc["entities"][e.get_id()] = std::move(obj);
    }

    for (const auto& item : items)
    {
        if (item)
        {
            const auto& value = item->value;
            doc["items"][value.get_id()] = {{"id", value.get_id()},
                {"cfg_id", value.get_cfg_id()}, {"count", value.count},
                {"place", value.place}, {"x", value.x}, {"y", value.y},
                {"owner_player_id", std::to_string(value.owner_player_id)}};
        }
    }

    return doc;
}

bool World::valid() const
{
    access.read();
    try
    {
        World candidate;
        candidate.configure(content);
        read_world(candidate, document());
        return true;
    }
    catch (const std::exception&)
    {
        return false;
    }
}

Str World::save() const
{
    require(valid(), "invalid_world_state");
    return document().dump();
}

void World::load(const Str& text)
{
    access.write();
    World candidate;
    candidate.configure(content);
    read_world(candidate, Json::parse(text));
    require(revision < std::numeric_limits<u32>::max(), "revision_exhausted");
    candidate.serial = serial;
    candidate.revision = revision + 1;
    candidate.access = access;

    for (auto& actor : candidate.actors)
    {
        if (actor)
        {
            actor->generation = candidate.next_generation();
            actor->unit().access = &access;

            if (auto* player = std::get_if<Player>(&actor->value))
            {
                player->weapon.access = &access;
                player->other_weapon.access = &access;
                player->world = this;
            }
        }
    }

    for (auto& item : candidate.items)
    {
        if (item)
        {
            item->generation = candidate.next_generation();
            item->value.access = &access;
        }
    }

    *this = std::move(candidate);
}
}

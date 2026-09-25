// 对完整世界进行冷路径序列化和候选验证，状态发布前不修改活动对象。
#include "game/World.h"
#include "common/Types.h"
#include <limits>

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
    const i32 speed = cfg.at("speed");
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
        require(!(overlap && entity.y < y + h && entity.y + height > y), "state_collision");
        grounded = grounded || (overlap && entity.y == y + h);
    }

    require(unit.grounded == grounded, "invalid_grounded");
}

// 读取并校验玩家专属输入、枪械和配置关系。
void read_player(Player& player, const Json& obj, const Json& content)
{
    fields(obj, {"id", "kind", "cfg_id", "pose", "pending_remove", "motion", "health",
        "player_id", "controls", "weapon", "reserve"});
    player.player_id = identity(obj["player_id"]);
    require(player.player_id > 0 && !player.pending_remove
        && obj["cfg_id"] == content["map"]["spawn"]["cfg_id"], "invalid_player");
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
    require(gun["cfg_id"] == content["map"]["spawn"]["weapon_cfg_id"], "invalid_weapon_cfg");
    player.weapon.cfg_id = cfg_id(gun["cfg_id"]);
    const auto& cfg = content.at("weapons").at(gun["cfg_id"].get<Str>());
    player.weapon.ammo = integer(gun["ammo"], 0, cfg.at("magazine"));
    player.weapon.shot_ticks = integer(gun["shot_ticks"], 0, cfg.at("fire_ticks"));
    player.weapon.reload_ticks = integer(gun["reload_ticks"], 0, cfg.at("reload_ticks"));
    player.reserve = integer(obj["reserve"], 0, cfg.at("reserve"));
    require(player.alive || (player.weapon.shot_ticks == 0
        && player.weapon.reload_ticks == 0), "dead_weapon_cooldown");
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
        "last_entity_id", "last_start", "items", "last_item_id", "world_id", "raid"});
    integer(doc["v"], 5, 5);
    require(doc["content_key"] == world.content_key, "content_identity_mismatch");
    world.tick_id = identity(doc["tick_id"]);
    require(world.tick_id <= static_cast<u64>(std::numeric_limits<i64>::max()), "invalid_tick");
    world.seq = identity(doc["seq"]);
    world.match_id = identity(doc["match_id"]);
    world.world_id = identity(doc["world_id"]);
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
        read_unit(unit, obj, cfg, world.content.at("map"));
        require(world.phase == "Playing" || (unit.vx == 0 && unit.vy == 0), "terminal_motion");
        const auto index = static_cast<u32>(world.order.size());

        if (kind == "player")
        {
            require(!player && value == world.player_entity_id, "invalid_player_reference");
            read_player(std::get<Player>(actor.value), obj, world.content);
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
    u32 index = 0;

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
        }

        world.items[index++] = item;
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
            && world.last_match == 0 && items.empty() && world.last_item_id == 0,
            "invalid_inactive_state");
        return;
    }

    require(player && world.player_id == player->player_id && world.world_id > 0
        && world.match_id != 0 && !world.last_req.empty()
        && world.last_match == world.match_id && world.last_after < world.match_id
        , "invalid_match_state");
    require(!(world.paused || world.phase != "Playing") || (player->move_x == 0
        && !player->jump && !player->fire && !player->fire_once && !player->reload),
        "inactive_controls");
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
    Json doc = {{"v", 5}, {"tick_id", get_tick_id()}, {"seq", get_seq()},
        {"match_id", get_match_id()}, {"player_id", get_player_id()},
        {"world_id", std::to_string(world_id)},
        {"event_id", std::to_string(event_id)}, {"phase", phase}, {"paused", paused},
        {"content_key", content_key}, {"player_entity_id", get_player_entity_id()},
        {"last_entity_id", get_last_entity_id()}, {"last_item_id", std::to_string(last_item_id)},
        {"last_start", {{"req_id", last_req}, {"after_match_id", get_last_after()},
            {"match_id", std::to_string(last_match)}}}, {"entities", Json::object()},
        {"entity_ids", Json::array()}, {"items", Json::object()}};
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

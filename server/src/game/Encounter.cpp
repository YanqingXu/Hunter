// 原子发布波次和召唤实体，仅校验结构、归属、容量及身份，不解释玩法配置。
#include "common/Types.h"
#include "game/World.h"
#include <limits>

namespace hunter
{
namespace
{
// 在独立候选中校验怪物出生值，失败不会修改当前世界。
Actor candidate(World& world, const nlohmann::json& spec, i32 wave)
{
    read_fields(spec, {"cfg_id", "spawn_id", "x", "y", "hp", "width", "height",
        "grounded"});
    Actor result;
    result.value = Monster{};
    auto& monster = std::get<Monster>(result.value);
    const auto cfg = read_id(spec.at("cfg_id").get<Str>());
    const auto spawn = read_id(spec.at("spawn_id").get<Str>());
    require(cfg > 0 && cfg <= 2147483647 && spawn > 0 && spawn <= 2147483647,
        "invalid_monster_identity");
    monster.access = &world.access;
    monster.kind = "monster";
    monster.cfg_id = static_cast<u32>(cfg);
    monster.spawn_id = static_cast<u32>(spawn);
    monster.hp = read_integer(spec.at("hp"), 1, 1000000);
    monster.max_hp = monster.hp;
    monster.last_hp = monster.hp;
    monster.width = read_integer(spec.at("width"), 2, 10000);
    monster.height = read_integer(spec.at("height"), 2, 10000);
    require(monster.width % 2 == 0 && monster.height % 2 == 0, "invalid_body");
    monster.x = read_integer(spec.at("x"), monster.width / 2,
        world.map_width - monster.width / 2);
    monster.y = read_integer(spec.at("y"), 0, world.map_height - monster.height);
    require(spec.at("grounded").is_boolean(), "invalid_grounded");
    monster.grounded = spec.at("grounded").get<bool>();
    monster.wave = wave;
    return result;
}

// 在所有可能失败的检查完成之前，不发布任何出生或推进分配水位。
bool reserve(World& world, usize count)
{
    if (count > world.actors.size() - world.order.size())
    {
        return false;
    }

    require(count <= std::numeric_limits<u64>::max() - world.last_entity_id,
        "entity_id_exhausted");
    require(count <= std::numeric_limits<u32>::max() - world.serial,
        "generation_exhausted");
    require(count <= std::numeric_limits<u32>::max() - world.revision,
        "revision_exhausted");
    world.order.reserve(world.order.size() + count);
    return true;
}

// 发布已经准备好的候选，容器容量已预留且本段不再进行玩法校验。
void publish(World& world, Actor&& actor)
{
    u32 index = 0;

    while (world.actors[index])
    {
        ++index;
    }

    actor.generation = ++world.serial;
    actor.unit().id = ++world.last_entity_id;
    world.actors[index] = std::move(actor);
    world.order.push_back(index);
    ++world.revision;
}
}

i64 World::get_wave() const
{
    return wave;
}

Str World::get_bounty_id() const
{
    return std::to_string(bounty_id);
}

Str World::get_hunter_id() const
{
    return std::to_string(hunter_id);
}

Str World::commit_wave(const Str& batch_text, const Str& specs_text, const Str& bounty)
{
    access.write();

    if (phase != "Playing" || paused || raid.player_state != "Alive")
    {
        return "invalid_state";
    }

    if (wave != 1 || bounty_id != 0)
    {
        return "wave_already_triggered";
    }

    require(batch_text.size() <= 32768 && specs_text.size() <= 32768, "batch_too_large");
    const auto id = read_id(bounty);
    const auto item = item_slot(id);

    if (id == 0 || item == items.size() || items[item]->value.place != "Ground")
    {
        return "already_picked";
    }

    const auto batch = nlohmann::json::parse(batch_text);
    require(batch.contains("items") && batch.at("items").is_array(), "invalid_batch");
    bool transfers = false;

    for (const auto& change : batch.at("items"))
    {
        transfers = transfers || (change.at("id").get<Str>() == bounty
            && change.at("place").get<Str>() == "Bag");
    }

    require(transfers, "missing_bounty_transfer");
    const auto specs = nlohmann::json::parse(specs_text);
    require(specs.is_array() && specs.size() <= 63, "invalid_wave_specs");
    Vec<Actor> next;
    next.reserve(specs.size());

    for (const auto& spec : specs)
    {
        next.push_back(candidate(*this, spec, 2));
    }

    if (!reserve(*this, next.size()))
    {
        return "entity_capacity";
    }

    const auto error = commit(batch_text);

    if (!error.empty())
    {
        return error;
    }

    for (auto& actor : next)
    {
        publish(*this, std::move(actor));
    }

    wave = 2;
    bounty_id = id;
    return "";
}

Str World::spawn_summon(const Str& owner, i64 ability_slot, const Str& spec)
{
    access.write();
    range(ability_slot, 1, 8);
    require(spec.size() <= 16384, "spawn_too_large");
    const auto id = read_id(owner);
    const auto index = slot(id);

    if (phase != "Playing" || paused || index == actors.size())
    {
        return ":invalid_state";
    }

    auto* source = std::get_if<Monster>(&actors[index]->value);

    if (!source || !source->alive || source->pending_remove || source->owner_id != 0)
    {
        return ":invalid_summoner";
    }

    for (const auto actor_index : order)
    {
        const auto* monster = std::get_if<Monster>(&actors[actor_index]->value);

        if (monster && monster->owner_id == id && !monster->pending_remove)
        {
            return ":summon_exists";
        }
    }

    auto actor = candidate(*this, nlohmann::json::parse(spec), source->wave);
    auto& summon = std::get<Monster>(actor.value);
    summon.owner_id = id;
    summon.owner_ability = static_cast<i32>(ability_slot);

    if (!reserve(*this, 1))
    {
        return ":entity_capacity";
    }

    const auto result = std::to_string(last_entity_id + 1);
    publish(*this, std::move(actor));
    return result;
}
}

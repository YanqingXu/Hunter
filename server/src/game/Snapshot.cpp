// 从原生对象直接生成拥有数据的网络消息，不经过 JSON 或脚本实体遍历。
#include "game/World.h"
#include "common/Types.h"
#include <limits>

namespace hunter
{
wire::Envelope World::snapshot(u64 observer) const
{
    access.read();
    require(observer == player_id, "invalid_observer");
    wire::Envelope out;
    auto& msg = *out.mutable_snapshot();
    msg.set_tick_id(tick_id);
    msg.set_seq(seq);
    msg.set_match_id(match_id);
    msg.set_phase(phase);
    msg.set_world_id(world_id);
    msg.set_player_entity_id(read_id(find_player(get_player_id())));
    msg.set_player_state(raid.player_state);
    msg.set_bag_slots(content.contains("bag") ? content.at("bag").at("slots").get<u32>() : 0);
    msg.set_extract_id(raid.extract_id);
    msg.set_extract_ticks(static_cast<u32>(raid.extract_ticks));
    msg.set_extract_reason(raid.extract_reason);

    if (content.contains("extracts"))
    {
        for (const auto& point : content.at("extracts"))
        {
            const auto spawn = read_id(point.at("boss_spawn_id").get<Str>());

            for (const auto index : order)
            {
                const auto* monster = std::get_if<Monster>(&actors[index]->value);
                if (monster && monster->spawn_id == spawn && !monster->alive)
                {
                    msg.set_extract_unlocked(true);
                }
            }

            const i32 duration = point.at("hold_ticks");
            msg.set_extract_remaining_ticks(static_cast<u32>(duration - raid.extract_ticks));
        }
    }

    for (const auto& entry : items)
    {
        if (entry && entry->value.place != "None")
        {
            const auto& item = entry->value;
            auto& value = *msg.add_items();
            value.set_item_id(item.id);
            value.set_cfg_id(item.cfg_id);
            value.set_count(static_cast<u32>(item.count));
            value.set_place(item.place);
            value.set_owner_player_id(item.owner_player_id);
            value.set_x(item.x);
            value.set_y(item.y);
        }
    }

    for (const auto index : order)
    {
        const auto& actor = *actors[index];
        const auto& unit = actor.unit();
        const Entity& entity = unit;

        if (entity.pending_remove)
        {
            continue;
        }

        auto& value = *msg.add_entities();
        value.set_id(entity.id);
        value.set_cfg_id(entity.cfg_id);
        value.set_kind(entity.kind == "player" ? "player" : "enemy");
        value.set_x(entity.x);
        value.set_y(entity.y);
        value.set_facing(entity.facing);
        value.set_vx(unit.vx);
        value.set_vy(unit.vy);
        value.set_grounded(unit.grounded);
        value.set_hp(unit.hp);
        value.set_max_hp(unit.max_hp);
        value.set_alive(unit.alive);

        if (const auto* player = std::get_if<Player>(&actor.value))
        {
            value.set_ammo(player->weapon.ammo);
            value.set_reserve(player->reserve);
            value.set_reload_ticks(player->weapon.reload_ticks);
            value.set_ai(unit.alive ? "idle" : "dead");
        }
        else
        {
            value.set_ai(std::get<Monster>(actor.value).state);
            value.set_attack_ticks(static_cast<u32>(std::get<Monster>(actor.value).attack_ticks));
        }
    }

    return out;
}

wire::Envelope World::event(const Str& kind, const Str& actor, const Str& target,
    i64 x, i64 y, i64 amount)
{
    access.write();
    require(event_id < std::numeric_limits<u64>::max(), "event_id_exhausted");
    range(x, -100000, 100000);
    range(y, -100000, 100000);
    range(amount, 0, 1000000);
    const auto actor_id = read_id(actor);
    const auto target_id = read_id(target);
    wire::Envelope out;
    auto& msg = *out.mutable_event();
    msg.set_match_id(match_id);
    msg.set_event_id(++event_id);
    msg.set_tick_id(tick_id);
    msg.set_kind(kind);
    msg.set_actor_id(actor_id);
    msg.set_target_id(target_id);
    msg.set_x(static_cast<i32>(x));
    msg.set_y(static_cast<i32>(y));
    msg.set_amount(static_cast<i32>(amount));
    return out;
}
}

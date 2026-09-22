// 从原生对象直接生成拥有数据的网络消息，不经过 JSON 或脚本实体遍历。
#include "game/World.h"
#include "common/Types.h"
#include <limits>

namespace hunter
{
wire::Envelope World::snapshot() const
{
    access.read();
    wire::Envelope out;
    auto& msg = *out.mutable_snapshot();
    msg.set_tick_id(tick_id);
    msg.set_seq(seq);
    msg.set_match_id(match_id);
    msg.set_phase(phase);

    for (const auto index : order)
    {
        const auto& actor = *actors[index];
        const auto& unit = actor.unit();
        const auto& entity = unit.entity;
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

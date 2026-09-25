// 绑定只含身份的 Luax 句柄，解析时检查真实槽位和代次，不转移原生所有权。
#include "script/Objects.h"
#include "common/Types.h"
#include <luax/bind/Bind.hpp>

template <> struct luax::bind::ObjectHandleTraits<hunter::Entity>
{
    static constexpr luax::TypeTag type{0x48550000U + 1, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::Unit>
{
    static constexpr luax::TypeTag type{0x48550000U + 2, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::Weapon>
{
    static constexpr luax::TypeTag type{0x48550000U + 3, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::Player>
{
    static constexpr luax::TypeTag type{0x48550000U + 4, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::Monster>
{
    static constexpr luax::TypeTag type{0x48550000U + 5, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::Item>
{
    static constexpr luax::TypeTag type{0x48550000U + 6, 1};
};

template <> struct luax::bind::ObjectHandleTraits<hunter::World>
{
    static constexpr luax::TypeTag type{0x48550000U + 7, 1};
};

namespace hunter
{
namespace
{
template <typename T>
using Handle = luax::bind::ActorObjectHandle<T>;

// 借用基类视图、具体对象或组件；句柄类型不同的请求不能相互替代。
template <typename T>
T* object(World& world, u32 slot)
{
    if constexpr (std::is_same_v<T, World>)
    {
        return slot == 0 ? &world : nullptr;
    }
    else if constexpr (std::is_same_v<T, Item>)
    {
        return slot < world.items.size() && world.items[slot]
            ? &world.items[slot]->value : nullptr;
    }
    else
    {
        if (slot >= world.actors.size() || !world.actors[slot])
        {
            return nullptr;
        }

        auto& actor = *world.actors[slot];

        if constexpr (std::is_same_v<T, Entity>)
        {
            return static_cast<Entity*>(&actor.unit());
        }
        else if constexpr (std::is_same_v<T, Unit>)
        {
            return &actor.unit();
        }
        else if constexpr (std::is_same_v<T, Weapon>)
        {
            auto* player = std::get_if<Player>(&actor.value);
            return player ? &player->weapon : nullptr;
        }
        else
        {
            return std::get_if<T>(&actor.value);
        }
    }
}

// 读取槽位当前代次，已释放对象永远不匹配旧句柄。
template <typename T>
u32 generation(World& world, u32 slot)
{
    if constexpr (std::is_same_v<T, World>)
    {
        return 1;
    }
    else if constexpr (std::is_same_v<T, Item>)
    {
        return world.items[slot]->generation;
    }
    else
    {
        return world.actors[slot]->generation;
    }
}

// 将规范业务身份转换为不含指针的类型化句柄。
template <typename T>
Handle<T> find(World& world, const Str& id)
{
    world.access.read();
    u32 slot = 0;
    if constexpr (std::is_same_v<T, Item>)
    {
        slot = world.item_slot(read_id(id));
    }
    else if constexpr (!std::is_same_v<T, World>)
    {
        slot = world.slot(read_id(id));
    }

    require(object<T>(world, slot) != nullptr, "missing_object");
    return Handle<T>({luax::bind::ObjectHandleTraits<T>::type, slot,
        generation<T>(world, slot), 1, 1, 1});
}

// 构造统一解析器，确保只在当前所属线程解析有效实例。
template <typename T>
auto builder(World& world, const Str& name)
{
    return luax::bind::class_<Handle<T>>(name, {1, 1, 1},
        [&world](const Handle<T>& handle) -> luax::Result<std::reference_wrapper<T>>
        {
            world.access.read();
            const auto& value = handle.handle();
            auto* target = object<T>(world, value.slot);
            if (!target || generation<T>(world, value.slot) != value.generation)
            {
                return std::unexpected(luax::Error(luax::ErrorCode::stale_handle,
                    luax::ErrorPhase::execution, "stale object generation"));
            }

            return std::ref(*target);
        });
}
}

luax::Status register_objects(World& world, const luax::Isolate& isolate,
    luax::ModuleHandle module)
{
    {
        auto type = builder<Entity>(world, "Entity");
        type.factory("find", [&world](Str id) { return find<Entity>(world, id); });
        type.method("get_id", &Entity::get_id);
        type.method("get_kind", &Entity::get_kind);
        type.method("get_cfg_id", &Entity::get_cfg_id);
        type.method("get_x", &Entity::get_x);
        type.method("set_x", &Entity::set_x);
        type.method("get_y", &Entity::get_y);
        type.method("set_y", &Entity::set_y);
        type.method("get_facing", &Entity::get_facing);
        type.method("set_facing", &Entity::set_facing);
        type.method("get_pending_remove", &Entity::get_pending_remove);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    {
        auto type = builder<Unit>(world, "Unit");
        type.method("read_motion", &Unit::read_motion);
        type.method("write_motion", &Unit::write_motion);
        type.factory("find", [&world](Str id) { return find<Unit>(world, id); });
        type.method("get_vx", &Unit::get_vx);
        type.method("set_vx", &Unit::set_vx);
        type.method("get_vy", &Unit::get_vy);
        type.method("set_vy", &Unit::set_vy);
        type.method("get_grounded", &Unit::get_grounded);
        type.method("set_grounded", &Unit::set_grounded);
        type.method("get_hp", &Unit::get_hp);
        type.method("set_hp", &Unit::set_hp);
        type.method("get_max_hp", &Unit::get_max_hp);
        type.method("get_alive", &Unit::get_alive);
        type.method("set_alive", &Unit::set_alive);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    {
        auto type = builder<Weapon>(world, "Weapon");
        type.factory("find", [&world](Str id) { return find<Weapon>(world, id); });
        type.method("get_cfg_id", &Weapon::get_cfg_id);
        type.method("get_ammo", &Weapon::get_ammo);
        type.method("set_ammo", &Weapon::set_ammo);
        type.method("get_shot_ticks", &Weapon::get_shot_ticks);
        type.method("set_shot_ticks", &Weapon::set_shot_ticks);
        type.method("get_reload_ticks", &Weapon::get_reload_ticks);
        type.method("set_reload_ticks", &Weapon::set_reload_ticks);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    {
        auto type = builder<Player>(world, "Player");
        type.factory("find", [&world](Str id) { return find<Player>(world, id); });
        type.method("get_player_id", &Player::get_player_id);
        type.method("get_reserve", &Player::get_reserve);
        type.method("set_reserve", &Player::set_reserve);
        type.method("get_move_x", &Player::get_move_x);
        type.method("set_move_x", &Player::set_move_x);
        type.method("get_aim_x", &Player::get_aim_x);
        type.method("set_aim_x", &Player::set_aim_x);
        type.method("get_aim_y", &Player::get_aim_y);
        type.method("set_aim_y", &Player::set_aim_y);
        type.method("get_jump", &Player::get_jump);
        type.method("set_jump", &Player::set_jump);
        type.method("get_fire", &Player::get_fire);
        type.method("set_fire", &Player::set_fire);
        type.method("get_fire_once", &Player::get_fire_once);
        type.method("set_fire_once", &Player::set_fire_once);
        type.method("get_reload", &Player::get_reload);
        type.method("set_reload", &Player::set_reload);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    {
        auto type = builder<Monster>(world, "Monster");
        type.factory("find", [&world](Str id) { return find<Monster>(world, id); });
        type.method("get_spawn_id", &Monster::get_spawn_id);
        type.method("get_state", &Monster::get_state);
        type.method("set_state", &Monster::set_state);
        type.method("get_attack_ticks", &Monster::get_attack_ticks);
        type.method("set_attack_ticks", &Monster::set_attack_ticks);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    {
        auto type = builder<Item>(world, "Item");
        type.factory("find", [&world](Str id) { return find<Item>(world, id); });
        type.factory("new", [&world](Str cfg_id, i64 count)
        {
            return find<Item>(world, world.create_item(cfg_id, count));
        });
        type.method("get_id", &Item::get_id);
        type.method("get_cfg_id", &Item::get_cfg_id);
        type.method("get_count", &Item::get_count);
        type.method("set_count", &Item::set_count);
        auto result = isolate.registerHostClass(module, type.build());
        if (!result)
        {
            return result;
        }
    }

    auto type = builder<World>(world, "World");
    type.factory("current", [&world]() { return find<World>(world, "0"); });
    type.method("get_tick_id", &World::get_tick_id);
    type.method("get_seq", &World::get_seq);
    type.method("get_match_id", &World::get_match_id);
    type.method("get_player_id", &World::get_player_id);
    type.method("get_player_entity_id", &World::get_player_entity_id);
    type.method("find_player", &World::find_player);
    type.method("drop_once", &World::drop_once);
    type.method("roll", &World::roll);
    type.method("drop", &World::drop);
    type.method("pickup", &World::pickup);
    type.method("hurt", &World::hurt);
    type.method("hurt_now", &World::hurt_now);
    type.method("get_extract_ticks", &World::get_extract_ticks);
    type.method("set_extract", &World::set_extract);
    type.method("finish", &World::finish);
    type.method("get_last_entity_id", &World::get_last_entity_id);
    type.method("get_last_req", &World::get_last_req);
    type.method("get_last_after", &World::get_last_after);
    type.method("get_revision", &World::get_revision);
    type.method("get_phase", &World::get_phase);
    type.method("set_phase", &World::set_phase);
    type.method("get_paused", &World::get_paused);
    type.method("set_paused", &World::set_paused);
    type.method("contains", &World::contains);
    type.method("count", &World::count);
    type.method("entity_id", &World::entity_id);
    type.method("login", &World::login);
    type.method("begin", &World::begin);
    type.method("spawn", &World::spawn);
    type.method("remove", &World::remove);
    type.method("flush", &World::flush);
    type.method("clear_input", &World::clear_input);
    type.method("create_item", &World::create_item);
    type.method("has_item", &World::has_item);
    type.method("remove_item", &World::remove_item);
    type.method("save", &World::save);
    type.method("load", &World::load);
    type.method("valid", &World::valid);
    return isolate.registerHostClass(module, type.build());
}
}

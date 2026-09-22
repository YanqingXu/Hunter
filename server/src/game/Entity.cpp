// 实现 Entity 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Entity.h"
#include "common/Types.h"

namespace hunter
{
Str Entity::get_id() const
{
    return std::to_string(id);
}

Str Entity::get_kind() const
{
    return kind;
}

Str Entity::get_cfg_id() const
{
    return std::to_string(cfg_id);
}

i64 Entity::get_x() const
{
    return x;
}

void Entity::set_x(i64 value)
{
    access->write();
    range(value, 0, 100000);
    x = static_cast<i32>(value);
}

i64 Entity::get_y() const
{
    return y;
}

void Entity::set_y(i64 value)
{
    access->write();
    range(value, 0, 100000);
    y = static_cast<i32>(value);
}

i64 Entity::get_facing() const
{
    return facing;
}

void Entity::set_facing(i64 value)
{
    access->write();
    range(value, -1, 1);
    require(value != 0, "invalid_facing");
    facing = static_cast<i32>(value);
}

bool Entity::get_pending_remove() const
{
    return pending_remove;
}
}

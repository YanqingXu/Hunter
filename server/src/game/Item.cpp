// 实现 Item 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Item.h"
#include "common/Types.h"

namespace hunter
{
Str Item::get_id() const
{
    return std::to_string(id);
}

Str Item::get_cfg_id() const
{
    return std::to_string(cfg_id);
}

i64 Item::get_count() const
{
    return count;
}

void Item::set_count(i64 value)
{
    access->write();
    range(value, 1, 2147483647);
    count = static_cast<i32>(value);
}
}

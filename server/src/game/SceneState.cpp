// 提供场景权威状态的标量边界；伤害、交互完成和引信规则由 Lua 决定。
#include "game/World.h"
#include "common/Types.h"

namespace hunter
{
bool World::scene_begin(i64 index, i64 ticks)
{
    access.write();
    range(index, 1, scene_count());
    range(ticks, 1, 36000);
    if (scene_state.index != 0)
    {
        return false;
    }

    scene_state.index = static_cast<i32>(index);
    scene_state.ticks = static_cast<i32>(ticks);
    return true;
}

void World::scene_cancel()
{
    access.write();
    scene_state.index = 0;
    scene_state.ticks = 0;
}

i64 World::scene_index() const
{
    access.read();
    return scene_state.index;
}

i64 World::scene_ticks() const
{
    access.read();
    return scene_state.ticks;
}

void World::scene_set_ticks(i64 ticks)
{
    access.write();
    range(ticks, 0, 36000);
    require(scene_state.index != 0, "missing_scene_interaction");
    scene_state.ticks = static_cast<i32>(ticks);
}

i64 World::barrel_hp(i64 index) const
{
    access.read();
    range(index, 1, scene_count());
    return scene_state.barrels[static_cast<usize>(index - 1)].hp;
}

i64 World::barrel_fuse(i64 index) const
{
    access.read();
    range(index, 1, scene_count());
    return scene_state.barrels[static_cast<usize>(index - 1)].fuse;
}

bool World::barrel_exploded(i64 index) const
{
    access.read();
    range(index, 1, scene_count());
    return scene_state.barrels[static_cast<usize>(index - 1)].exploded;
}

bool World::barrel_write(i64 index, i64 hp, i64 fuse, bool exploded)
{
    access.write();
    range(index, 1, scene_count());
    range(hp, 0, 1000000);
    range(fuse, 0, 36000);
    auto& value = scene_state.barrels[static_cast<usize>(index - 1)];
    if ((value.exploded && (!exploded || hp != 0 || fuse != 0))
        || (value.fuse > 0 && hp > 0) || (exploded && (hp != 0 || fuse != 0))
        || (hp > 0 && fuse > 0))
    {
        return false;
    }

    value = {static_cast<i32>(hp), static_cast<i32>(fuse), exploded};
    return true;
}
}

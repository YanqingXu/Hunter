// 保存场景交互与炸药桶的有界权威状态，不持有玩法配置。
#pragma once

#include "common/Types.h"

namespace hunter
{
struct BarrelState
{
    i32 hp = 0;
    i32 fuse = 0;
    bool exploded = false;
};

struct SceneState
{
    Arr<BarrelState, 32> barrels{};
    i32 index = 0;
    i32 ticks = 0;
};
}

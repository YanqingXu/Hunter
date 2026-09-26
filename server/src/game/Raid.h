// 保存撤离对局的原生规则状态；世界独占，脚本通过明确方法读取与修改。
#pragma once

#include "common/Types.h"

namespace hunter
{
struct Raid
{
    Str player_state = "Alive";
    u32 random = 1;
    Set<u64> dropped;
    bool extract_unlocked = false;
    i32 extract_remaining = 0;
    u32 extract_id = 0;
    i32 extract_ticks = 0;
    Str extract_reason = "locked";
    u64 damage_tick = 0;
};
}

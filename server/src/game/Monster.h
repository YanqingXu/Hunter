// 在单位基类上增加出生引用和怪物状态边界；行为选择仍由 Lua 负责。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Unit.h"

namespace hunter
{

class Monster : public Unit
{
public:
    u32 spawn_id = 0;
    Str state = "spawn";
    i32 attack_ticks = 0;

    // 读取spawn_id，不转移对象所有权。
    Str get_spawn_id() const;

    // 读取state，不转移对象所有权。
    Str get_state() const;

    // 校验状态和生死关系，禁止退回出生或离开死亡；死亡同时清除速度和冷却。
    void set_state(Str value);

    // 读取attack_ticks，不转移对象所有权。
    i64 get_attack_ticks() const;

    // 检查通用攻击冷却范围，出生和死亡只接受零值。
    void set_attack_ticks(i64 value);
};

}

// 持有 Monster 的原生状态，通过受控属性供脚本访问。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Unit.h"

namespace hunter
{

class Monster
{
public:
    Access* access = nullptr;
    Unit unit;
    u32 spawn_id = 0;
    Str state = "patrol";
    i32 attack_ticks = 0;

    // 读取spawn_id，不转移对象所有权。
    Str get_spawn_id() const;

    // 读取state，不转移对象所有权。
    Str get_state() const;

    // 校验并修改state，只允许当前可写执行片段。
    void set_state(Str value);

    // 读取attack_ticks，不转移对象所有权。
    i64 get_attack_ticks() const;

    // 校验并修改attack_ticks，只允许当前可写执行片段。
    void set_attack_ticks(i64 value);
};

}

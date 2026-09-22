// 持有 Weapon 的原生状态，通过受控属性供脚本访问。
#pragma once

#include "common/Types.h"
#include "game/Value.h"

namespace hunter
{

class Weapon
{
public:
    Access* access = nullptr;
    u32 cfg_id = 0;
    i32 ammo = 0;
    i32 shot_ticks = 0;
    i32 reload_ticks = 0;

    // 读取cfg_id，不转移对象所有权。
    Str get_cfg_id() const;

    // 读取ammo，不转移对象所有权。
    i64 get_ammo() const;

    // 校验并修改ammo，只允许当前可写执行片段。
    void set_ammo(i64 value);

    // 读取shot_ticks，不转移对象所有权。
    i64 get_shot_ticks() const;

    // 校验并修改shot_ticks，只允许当前可写执行片段。
    void set_shot_ticks(i64 value);

    // 读取reload_ticks，不转移对象所有权。
    i64 get_reload_ticks() const;

    // 校验并修改reload_ticks，只允许当前可写执行片段。
    void set_reload_ticks(i64 value);
};

}

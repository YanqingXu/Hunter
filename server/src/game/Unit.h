// 在实体基类上增加运动与生命状态；访问门禁和身份均继承自 Entity。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Entity.h"
#include <tuple>

namespace hunter
{

class Unit : public Entity
{
public:
    i32 vx = 0;
    i32 vy = 0;
    bool grounded = false;
    i32 hp = 0;
    i32 max_hp = 0;
    bool alive = true;

    // 一次读取运动所需标量，返回值只用于当前计算片段。
    std::tuple<i64, i64, i64, i64, bool, i64, bool, bool> read_motion() const;

    // 完整校验后一次写入运动结果，死单位仅接受零速度，不执行运动规则。
    void write_motion(i64 x, i64 y, i64 vx, i64 vy, bool grounded, i64 facing);

    // 读取vx，不转移对象所有权。
    i64 get_vx() const;

    // 校验并修改横向速度，死单位仅接受零值。
    void set_vx(i64 value);

    // 读取vy，不转移对象所有权。
    i64 get_vy() const;

    // 校验并修改竖向速度，死单位仅接受零值。
    void set_vy(i64 value);

    // 读取grounded，不转移对象所有权。
    bool get_grounded() const;

    // 校验并修改grounded，只允许当前可写执行片段。
    void set_grounded(bool value);

    // 读取hp，不转移对象所有权。
    i64 get_hp() const;

    // 校验并修改生命值，归零后不允许恢复正值。
    void set_hp(i64 value);

    // 读取max_hp，不转移对象所有权。
    i64 get_max_hp() const;

    // 读取alive，不转移对象所有权。
    bool get_alive() const;

    // 生死标志须与生命值一致，死亡后不能恢复存活。
    void set_alive(bool value);
};

}

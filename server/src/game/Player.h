// 在单位基类上增加玩家身份、输入和备用弹药；枪械仍由玩家组合持有。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Unit.h"
#include "game/Weapon.h"

namespace hunter
{

class Player : public Unit
{
public:
    Weapon weapon;
    u64 player_id = 1;
    i32 reserve = 0;
    i32 move_x = 0;
    i32 aim_x = 1000;
    i32 aim_y = 0;
    bool jump = false;
    bool fire = false;
    bool fire_once = false;
    bool reload = false;

    // 读取player_id，不转移对象所有权。
    Str get_player_id() const;

    // 读取reserve，不转移对象所有权。
    i64 get_reserve() const;

    // 校验并修改reserve，只允许当前可写执行片段。
    void set_reserve(i64 value);

    // 读取move_x，不转移对象所有权。
    i64 get_move_x() const;

    // 校验并修改move_x，只允许当前可写执行片段。
    void set_move_x(i64 value);

    // 读取aim_x，不转移对象所有权。
    i64 get_aim_x() const;

    // 校验并修改aim_x，只允许当前可写执行片段。
    void set_aim_x(i64 value);

    // 读取aim_y，不转移对象所有权。
    i64 get_aim_y() const;

    // 校验并修改aim_y，只允许当前可写执行片段。
    void set_aim_y(i64 value);

    // 读取jump，不转移对象所有权。
    bool get_jump() const;

    // 校验并修改jump，只允许当前可写执行片段。
    void set_jump(bool value);

    // 读取fire，不转移对象所有权。
    bool get_fire() const;

    // 校验并修改fire，只允许当前可写执行片段。
    void set_fire(bool value);

    // 读取fire_once，不转移对象所有权。
    bool get_fire_once() const;

    // 校验并修改fire_once，只允许当前可写执行片段。
    void set_fire_once(bool value);

    // 读取reload，不转移对象所有权。
    bool get_reload() const;

    // 校验并修改reload，只允许当前可写执行片段。
    void set_reload(bool value);
};

}

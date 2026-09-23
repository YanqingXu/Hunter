// 保存实体共有状态和借用的访问门禁；脚本仅通过受控句柄与显式方法访问。
#pragma once

#include "common/Types.h"
#include "game/Value.h"

namespace hunter
{

class Entity
{
public:
    Access* access = nullptr;
    u64 id = 0;
    Str kind = {};
    u32 cfg_id = 0;
    i32 x = 0;
    i32 y = 0;
    i32 facing = 1;
    bool pending_remove = false;

    // 读取id，不转移对象所有权。
    Str get_id() const;

    // 读取kind，不转移对象所有权。
    Str get_kind() const;

    // 读取cfg_id，不转移对象所有权。
    Str get_cfg_id() const;

    // 读取x，不转移对象所有权。
    i64 get_x() const;

    // 校验并修改x，只允许当前可写执行片段。
    void set_x(i64 value);

    // 读取y，不转移对象所有权。
    i64 get_y() const;

    // 校验并修改y，只允许当前可写执行片段。
    void set_y(i64 value);

    // 读取facing，不转移对象所有权。
    i64 get_facing() const;

    // 校验并修改facing，只允许当前可写执行片段。
    void set_facing(i64 value);

    // 读取pending_remove，不转移对象所有权。
    bool get_pending_remove() const;
};

}

// 持有 Item 的原生状态，通过受控属性供脚本访问。
#pragma once

#include "common/Types.h"
#include "game/Value.h"

namespace hunter
{

class Item
{
public:
    Access* access = nullptr;
    u64 id = 0;
    u32 cfg_id = 0;
    i32 count = 1;

    // 读取id，不转移对象所有权。
    Str get_id() const;

    // 读取cfg_id，不转移对象所有权。
    Str get_cfg_id() const;

    // 读取count，不转移对象所有权。
    i64 get_count() const;

    // 校验并修改count，只允许当前可写执行片段。
    void set_count(i64 value);
};

}

// 将唯一脚本契约解码为原生值，供脚本提交和协议转换共同校验。
#pragma once

#include "common/Types.h"
#include "script/Script.h"

#include <expected>

namespace hunter
{

struct ScriptMsg
{
    bool snapshot = false;
    u64 seq = 0;
    u64 tick_id = 0;
    i64 count = 0;
};

// 按构建时生成的权威 schema 检查完整字段与范围，返回精确整数值。
std::expected<ScriptMsg, Str> decode_output(const ScriptOut& out, usize max_json_bytes);

}

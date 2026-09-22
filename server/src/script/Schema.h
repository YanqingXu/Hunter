// 将唯一脚本契约解码为拥有数据的瞬时输出，供提交与协议转换共同校验。
#pragma once

#include "common/Types.h"
#include "script/Script.h"

#include <expected>
#include <nlohmann/json.hpp>

namespace hunter
{

// 直接校验类型化协议输出及消息大小，不执行 JSON 转换。
std::expected<void, Str> validate_output(const ScriptOut& out, usize max_bytes);

// 为诊断和契约测试投影协议消息，热路径禁止使用此接口。
nlohmann::json output_json(const ScriptOut& out);

}

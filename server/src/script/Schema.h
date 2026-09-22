// 将唯一脚本契约解码为拥有数据的瞬时输出，供提交与协议转换共同校验。
#pragma once

#include "common/Types.h"
#include "script/Script.h"

#include <expected>
#include <nlohmann/json.hpp>

namespace hunter
{

// 严格校验完整字段与值域；十进制 ID 转为无损 u64，其余字段保留原有类型。
std::expected<nlohmann::json, Str> decode_output(const ScriptOut& out, usize max_json_bytes);

}

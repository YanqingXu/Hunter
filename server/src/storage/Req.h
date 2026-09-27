// 共享持久请求的纯校验和确定性 JSON 编码，不依赖数据库连接或事务。
#pragma once

#include "common/Types.h"
#include "storage/Model.h"

#include <nlohmann/json_fwd.hpp>

#include <limits>

namespace hunter::storage
{

// 验证完整 typed 请求并生成确定的 v1 JSON，奖励顺序保持原样。
Str encode_req(const CommitMatch& req);

// 校验命令边界并规范化 JSON，异步队列只保留拥有型值。
Str encode_hunter(const HunterReq& req);

// 校验并解析规范命令载荷，编码器和业务后端共用相同字段及整数约束。
nlohmann::json hunter_payload(const HunterReq& req);

namespace req
{

constexpr i64 max_value = std::numeric_limits<i64>::max();
constexpr usize max_payload = 262144;

// 检查持久身份的正数及有符号整数范围。
void valid_id(u64 value);

// 对纯校验和业务拒绝使用相同的拥有型存储错误。
void ensure(bool ok, const char* message, Code code = Code::Invalid);

// 读取精确有界整数，不接受浮点或布尔转换。
i64 number(const nlohmann::json& value, i64 low = 0, i64 high = max_value);

// 校验猎人领域身份并转换为有符号持久整数。
i64 identity(u64 value);

// 读取无空字节的有限文本。
Str text_value(const nlohmann::json& value, usize limit);

}
}

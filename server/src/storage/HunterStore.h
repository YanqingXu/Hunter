// 在存档拥有线程处理猎人资产、永久操作去重、出战基线和原子终局。
#pragma once

#include "common/Types.h"
#include "storage/Db.h"

namespace hunter::storage
{

// 返回 V2 增量表的冻结 DDL，迁移和完整性检查使用相同定义。
const Map<Str, Str>& hunter_tables();

// 校验命令边界并规范化 JSON，异步队列只保留拥有型值。
Str encode_hunter(const HunterReq& req);

// 在单一读事务中返回账号、猎人、技能、装备和可用仓库。
HunterResult read_hunters(Db& db, u64 player_id, usize limit);

// 按客户端原始身份意图查询持久操作，不重新执行已变化档案上的玩法校验。
HunterResult find_hunter_op(Db& db, u64 player_id, const Str& op_id,
    const Str& intent_json, usize limit);

// 在一次事务中执行并记录幂等操作，失败不发布任何局部资产变化。
HunterResult write_hunter(Db& db, const HunterReq& req, const Str& json, usize limit);

// 启动时只回退未结算出战；已提交终局保持原样，回退保留持久局号高水位。
void recover_hunters(Db& db);

}

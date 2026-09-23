// 提供工作线程内的持久业务事务；仅由 Storage 调用，不暴露 SQL 给玩法。
#pragma once

#include "common/Types.h"
#include "storage/Db.h"

namespace hunter::storage
{

// 验证完整 typed 请求并生成确定的 v1 JSON，奖励顺序保持原样。
Str encode_req(const CommitMatch& req);

// 读取永久玩家；按结果容量逐行计费，不返回部分存档。
PlayerSave read_player(Db& db, u64 player_id, usize limit);

// 原子分配持久对局 ID，提交成功前不向逻辑线程交付。
MatchId next_match(Db& db);

// 查询已提交结果并限制字符串分配大小。
MatchResult read_match(Db& db, u64 match_id, usize limit);

// 在单事务中处理去重、奖励、revision 和结果；保存前完成序列化与容量检查。
MatchResult write_match(Db& db, const CommitMatch& req, const Str& json, usize limit);

}

// 定义存档拥有型值、操作关联和错误；不引用运行时世界或脚本对象。
#pragma once

#include "common/Types.h"

#include <expected>
#include <variant>

namespace hunter::storage
{

enum class Code
{
    Invalid, WrongThread, NotReady, Closed, Capacity, TooLarge, NotFound,
    Conflict, Revision, Overflow, Open, Version, Corrupt, Busy, Write, Internal
};

struct Error
{
    Code code = Code::Internal;
    i32 sqlite_code = 0;
    bool commit_unknown = false;
    Str message;
};

struct Key
{
    u64 instance = 0;
    u64 op = 0;
};

struct Accepted
{
    Key key;
};

struct ItemDelta
{
    u32 cfg_id = 0;
    i32 count = 0;
};

struct ItemSave
{
    u64 item_uid = 0;
    u32 cfg_id = 0;
    i32 count = 0;
    u64 acquired_match_id = 0;
};

struct PlayerSave
{
    u64 player_id = 0;
    u64 revision = 0;
    u64 last_match_id = 0;
    Vec<ItemSave> items;
};

struct CommitMatch
{
    u64 match_id = 0;
    u64 player_id = 0;
    u64 expected_revision = 0;
    Str outcome;
    Str content_key;
    Vec<ItemDelta> items;
};

struct MatchResult
{
    // 只在已确认 COMMIT 或查询到已提交记录时成功返回；Accepted 不生成此值。
    u64 match_id = 0;
    u64 revision = 0;
    bool replayed = false;
    // 包含 v、身份、结果、内容键、前后 revision、时间及带 UID 的奖励。
    Str result_json;
};

struct Opened {};
struct Closed {};
struct MatchId
{
    u64 value = 0;
};

using Value = std::variant<Opened, Closed, PlayerSave, MatchId, MatchResult>;

struct Rsp
{
    Key key;
    std::expected<Value, Error> result;
};

struct StorageCfg
{
    usize max_ops = 64;
    usize max_req_bytes = 1024 * 1024;
    usize max_done_bytes = 4 * 1024 * 1024;
    usize max_result_bytes = 1024 * 1024;
};

}

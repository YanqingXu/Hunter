// 将比赛用独占 SQLite 文件、结构迁移和业务事务封装到后端内部。
#pragma once

#include "common/Types.h"
#include "storage/Backend.h"

namespace hunter::storage
{

class Db;

class SqliteBackend final : public Backend
{
public:
    // 在存档线程创建尚未打开的 SQLite 后端。
    SqliteBackend();

    // 在同一线程释放连接，异常路径由 Db 负责关闭。
    ~SqliteBackend() override;

    // 初始化或迁移 V2，并回退独占文件中未提交的出战。
    void open(const OpenCfg& cfg) override;

    // 显式关闭独占连接，保留关闭失败的诊断。
    void close() override;

    // 读取玩家及未装备的永久物品。
    PlayerSave load_player(u64 player_id, usize limit) override;

    // 在 SQLite 事务中更新持久局号高水位。
    MatchId alloc_match() override;

    // 保持 SQLite 现有原子奖励和精确幂等语义。
    MatchResult commit_match(const CommitMatch& req, const Str& json, usize limit) override;

    // 返回 SQLite 中已提交的原始结算。
    MatchResult find_match(u64 match_id, usize limit) override;

    // 读取一致的猎人资产快照。
    HunterResult load_hunters(u64 player_id, usize limit) override;

    // 在一个 SQLite 事务中执行猎人命令。
    HunterResult apply_hunter(const HunterReq& req, const Str& json, usize limit) override;

    // 按账号及操作身份查询精确意图对应的结果。
    HunterResult find_hunter_op(u64 player_id, const Str& op_id,
        const Str& intent_json, usize limit) override;

private:
    // 仅在成功打开后返回连接，避免直接使用后端时解引用空指针。
    Db& db();

    UPtr<Db> db_;
};

}

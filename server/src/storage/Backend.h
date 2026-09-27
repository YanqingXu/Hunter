// 定义存档线程内的同步业务后端；不暴露连接、SQL 或驱动资源。
#pragma once

#include "common/Types.h"
#include "storage/Model.h"

namespace hunter::storage
{

class Backend
{
public:
    // 后端在存档线程销毁；析构不得抛出异常。
    virtual ~Backend() = default;

    // 打开并验证后端；失败抛出 Error，恢复策略由具体后端负责。
    virtual void open(const OpenCfg& cfg) = 0;

    // 在最后一个完成交付后关闭，失败仍须允许同线程销毁资源。
    virtual void close() = 0;

    // 返回永久玩家及可用仓库物品，超限时拒绝而不截断。
    virtual PlayerSave load_player(u64 player_id, usize limit) = 0;

    // 持久分配跨启动不复用的局号，允许空洞。
    virtual MatchId alloc_match() = 0;

    // 原子提交结果和奖励，同身份同请求返回保存的原始结果。
    virtual MatchResult commit_match(const CommitMatch& req, const Str& json, usize limit) = 0;

    // 查询已提交的结算，缺失抛出 NotFound。
    virtual MatchResult find_match(u64 match_id, usize limit) = 0;

    // 在一致性快照中返回完整猎人和仓库档案。
    virtual HunterResult load_hunters(u64 player_id, usize limit) = 0;

    // 原子执行永久猎人命令及去重记录，提交成功才返回结果。
    virtual HunterResult apply_hunter(const HunterReq& req, const Str& json, usize limit) = 0;

    // 按原始身份意图查询已提交操作，不重新裁决玩法规则。
    virtual HunterResult find_hunter_op(u64 player_id, const Str& op_id,
        const Str& intent_json, usize limit) = 0;
};

// 工厂只在存档线程调用；必须返回新后端或抛出 Error，不返回共享连接。
using BackendFactory = Func<UPtr<Backend>(const OpenCfg&)>;

// 选择已编译后端，未知或未实现的后端明确失败而不回退。
UPtr<Backend> make_backend(const OpenCfg& cfg);

}

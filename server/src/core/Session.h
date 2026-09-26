// 保存服务端认证身份与世界归属，单人容量限制集中在会话入口。
#pragma once

#include "common/Types.h"
#include <expected>

namespace hunter
{
struct CmdCtx
{
    u64 session_id = 0;
    u64 player_id = 0;
    u64 world_id = 0;
    u64 match_id = 0;
};

class Session
{
public:
    // 绑定已认证连接与存档身份；同一绑定幂等，其他连接或身份明确拒绝。
    bool bind(u64 session_id, u64 player_id)
    {
        if (session_id == 0 || player_id == 0)
        {
            return false;
        }

        if (ctx_.session_id != 0)
        {
            return ctx_.session_id == session_id && ctx_.player_id == player_id;
        }

        ctx_ = {session_id, player_id, 0, 0};
        return true;
    }

    // 关联新建世界与持久对局；调用方负责在终态之后切换世界。
    bool enter(u64 world_id, u64 match_id)
    {
        if (ctx_.session_id == 0 || world_id == 0 || match_id == 0)
        {
            return false;
        }

        ctx_.world_id = world_id;
        ctx_.match_id = match_id;
        return true;
    }

    // 从连接和请求作用域构造上下文；玩家身份始终由绑定产生。
    Expect<CmdCtx, Str> resolve(u64 session_id, u64 world_id, u64 match_id) const
    {
        if (session_id == 0 || session_id != ctx_.session_id)
        {
            return Unexpect("stale_session");
        }

        if (world_id != ctx_.world_id || match_id != ctx_.match_id)
        {
            return Unexpect("stale_match");
        }

        return ctx_;
    }

    // 返回当前绑定供登录投影，不允许调用者改变身份。
    const CmdCtx& viewer() const
    {
        return ctx_;
    }

    // 验证定向输出接收者，不把其他会话的消息送给唯一连接。
    bool accepts(u64 session_id) const
    {
        return session_id != 0 && session_id == ctx_.session_id;
    }

    // 断连撤销所有归属，使保留的调用上下文失效。
    void clear()
    {
        ctx_ = {};
    }

private:
    CmdCtx ctx_;
};
}

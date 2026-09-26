// 为连接内低频动作保留单调水位和有界结果，防止消费与补给请求重放。
#pragma once

#include "common/Types.h"
#include "hunter.pb.h"
#include <deque>
#include <optional>

namespace hunter
{
class ActionLog
{
public:
    enum class Kind { Accepted, Pending, Replay, Rejected };

    struct Result
    {
        Kind kind;
        Str error;
        std::optional<wire::Envelope> response;
    };

    // 接受新序号或返回旧请求状态；任何缓存淘汰均不降低连接处理水位。
    Result begin(const wire::ActionReq& req)
    {
        if (req.action_seq() == 0)
        {
            return {Kind::Rejected, "invalid_action_seq", {}};
        }

        for (const auto& entry : entries_)
        {
            if (entry.req.action_seq() != req.action_seq())
            {
                continue;
            }

            if (!same(entry.req, req))
            {
                return {Kind::Rejected, "request_conflict", {}};
            }

            return entry.response ? Result{Kind::Replay, "", entry.response}
                : Result{Kind::Pending, "", {}};
        }

        if (req.action_seq() <= high_)
        {
            return {Kind::Rejected, "stale_action", {}};
        }

        high_ = req.action_seq();

        if (entries_.size() == 128)
        {
            entries_.pop_front();
        }

        wire::ActionReq copy;
        copy.set_req_id(req.req_id());
        copy.set_action_seq(req.action_seq());
        copy.set_world_id(req.world_id());
        copy.set_match_id(req.match_id());
        copy.set_kind(req.kind());
        copy.set_item_id(req.item_id());
        copy.set_slot(req.slot());
        copy.set_target_id(req.target_id());
        entries_.push_back({std::move(copy), {}});
        return {Kind::Accepted, "", {}};
    }

    // 保存拥有数据的最终接收响应；已经淘汰的序号仍由高水位拒绝重放。
    void complete(u64 seq, const wire::Envelope& response)
    {
        for (auto& entry : entries_)
        {
            if (entry.req.action_seq() == seq)
            {
                entry.response = response;
                return;
            }
        }
    }

    // 返回连接已经受理的最大动作序号，用于暂停作废通知。
    u64 high() const
    {
        return high_;
    }

private:
    struct Entry
    {
        wire::ActionReq req;
        std::optional<wire::Envelope> response;
    };

    // 只比较正式业务字段，未知协议字段不进入持久缓存。
    static bool same(const wire::ActionReq& left, const wire::ActionReq& right)
    {
        return left.req_id() == right.req_id() && left.world_id() == right.world_id()
            && left.match_id() == right.match_id() && left.kind() == right.kind()
            && left.item_id() == right.item_id() && left.slot() == right.slot()
            && left.target_id() == right.target_id();
    }

    u64 high_ = 0;
    std::deque<Entry> entries_;
};
}

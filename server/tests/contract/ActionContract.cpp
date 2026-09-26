// 验证动作重发、参数冲突、缓存淘汰和暂停作废不会再次执行消费操作。
#include "common/Types.h"
#include "core/ActionLog.h"
#include <iostream>
#include <stdexcept>

namespace
{
// 将断言失败转成明确的契约诊断。
void check(bool value, const char* reason)
{
    if (!value)
    {
        throw std::runtime_error(reason);
    }
}

// 创建仅包含客户端意图的有效动作。
hunter::wire::ActionReq request(u64 seq)
{
    hunter::wire::ActionReq req;
    req.set_req_id("use-" + std::to_string(seq));
    req.set_action_seq(seq);
    req.set_world_id(7);
    req.set_match_id(9);
    req.set_kind(hunter::wire::ActionReq::USE);
    req.set_slot(5);
    return req;
}
}

// 在不依赖网络时序的条件下检查有界防重放契约。
int main()
{
    try
    {
        hunter::ActionLog log;
        using Kind = hunter::ActionLog::Kind;
        auto req = request(1);
        check(log.begin(req).kind == Kind::Accepted, "first accepted");
        check(log.begin(req).kind == Kind::Pending, "pending retry cannot execute");
        auto conflict = req;
        conflict.set_slot(6);
        check(log.begin(conflict).error == "request_conflict", "changed intent rejected");
        hunter::wire::Envelope response;
        response.mutable_error()->set_code("paused");
        response.mutable_error()->set_req_id(req.req_id());
        response.mutable_error()->set_seq(1);
        log.complete(1, response);
        const auto replay = log.begin(req);
        check(replay.kind == Kind::Replay && replay.response->error().code() == "paused",
            "pause rejection remains replayable without executing");

        for (u64 seq = 2; seq <= 130; ++seq)
        {
            check(log.begin(request(seq)).kind == Kind::Accepted, "monotonic action");
            log.complete(seq, response);
        }

        check(log.begin(req).error == "stale_action", "eviction cannot reopen old sequence");
        check(log.high() == 130, "watermark survives eviction");
        check(log.begin(request(0)).error == "invalid_action_seq", "zero sequence rejected");
        std::cout << "action replay contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

// 固定步长的单调时钟调度；暂停和积压丢弃不修改脚本世界状态。
#pragma once

#include "common/Types.h"

#include <chrono>

namespace hunter
{
class TickClock
{
public:
    using Clock = std::chrono::steady_clock;
    using Time = Clock::time_point;

    struct Due
    {
        u32 count = 0;
        bool dropped = false;
    };

    // 创建固定频率时钟；频率与补算限制必须为正数。
    TickClock(u32 hz, u32 max_catchup);

    // 从当前时间重新开始等待一个完整步长。
    void reset(Time now);

    // 计算到期步数并舍弃超出补算限制的墙钟积压。
    Due poll(Time now);

    // 返回下一次截止点供 Asio 定时器使用。
    Time next() const;

private:
    Clock::duration step_;
    Time next_{};
    u32 max_catchup_;
};
}

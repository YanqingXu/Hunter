// 实现有补算上限的固定步长计算，允许测试注入单调时间。
#include "core/TickClock.h"
#include "common/Types.h"

#include <stdexcept>

namespace hunter
{
TickClock::TickClock(u32 hz, u32 max_catchup) : max_catchup_(max_catchup)
{
    if (hz == 0 || hz > 1000 || max_catchup == 0)
    {
        throw std::invalid_argument("invalid tick clock limits");
    }

    step_ = std::chrono::duration_cast<Clock::duration>(std::chrono::duration<f64>(1.0 / hz));
}

void TickClock::reset(Time now)
{
    next_ = now + step_;
}

TickClock::Due TickClock::poll(Time now)
{
    Due due;

    while (now >= next_ && due.count < max_catchup_)
    {
        ++due.count;
        next_ += step_;
    }

    if (now >= next_)
    {
        due.dropped = true;
        next_ = now + step_;
    }

    return due;
}

TickClock::Time TickClock::next() const
{
    return next_;
}
}

// 验证固定步长、暂停恢复重置，以及队列双重容量限制。
#include "common/Types.h"
#include "core/BoundedQueue.h"
#include "core/Runtime.h"
#include "core/TickClock.h"

#include <iostream>
#include <stdexcept>

namespace
{
// 断言契约条件；失败向测试进程传播明确错误。
void check(bool ok, const char* detail)
{
    if (!ok)
    {
        throw std::runtime_error(detail);
    }
}
}

// 执行确定性的调度与有界队列契约测试。
int main()
{
    using namespace hunter;
    using namespace std::chrono;

    try
    {
        TickClock clock(60, 4);
        const auto start = TickClock::Time{};
        clock.reset(start);
        check(clock.poll(start).count == 0, "tick before deadline");
        check(clock.poll(start + milliseconds(17)).count == 1, "first tick");
        const auto due = clock.poll(start + seconds(1));
        check(due.count == 4 && due.dropped, "bounded catchup");
        check(clock.poll(start + seconds(1)).count == 0, "discarded backlog");
        clock.reset(start + seconds(20));
        check(clock.poll(start + seconds(20)).count == 0, "resume catches up pause");
        check(clock.poll(start + seconds(20) + milliseconds(17)).count == 1, "resume next tick");

        BoundedQueue<Str> queue(2, 5);
        check(queue.push("abc", 3), "first admission");
        check(!queue.push("def", 3), "byte cap");
        check(queue.push("xy", 2), "exact byte cap");
        check(!queue.push("", 0), "count cap");
        check(queue.pop() == std::optional<Str>("abc"), "owning FIFO");
        check(queue.bytes() == 2, "capacity release");
        queue.clear();
        check(queue.size() == 0 && queue.bytes() == 0, "queue clear");

        Cfg cfg;
        cfg.max_json_bytes = 32;
        Runtime runtime(cfg);
        check(!runtime.submit(Str(33, 'x')), "control size rejection before enqueue");
        runtime.stop();
        runtime.stop();
        runtime.join();
        check(runtime.finished(), "repeated stop finishes before Start");
        check(!runtime.faulted(), "repeated stop remains successful");
        check(!runtime.submit("{}"), "commands rejected after stop");

        cfg.tick_hz = 0;
        Runtime invalid(cfg);
        invalid.join();
        const auto fault = invalid.next_evt(0);
        check(fault && fault->find("runtime_failed") != Str::npos,
            "construction failure publishes terminal error");
        check(invalid.faulted(), "failure is visible without draining all events");

        cfg.tick_hz = 60;
        Runtime host_error(cfg);
        host_error.stop("control_line_size");
        host_error.join();
        check(host_error.faulted(), "host failure persists without an output reader");

        cfg.max_inputs_per_tick = 0;
        Runtime zero_inputs(cfg);
        zero_inputs.join();
        check(zero_inputs.faulted(), "zero input dispatch limit rejected");
        cfg.max_inputs_per_tick = 64;
        cfg.tick_work_ms = 0;
        Runtime zero_work(cfg);
        zero_work.join();
        check(zero_work.faulted(), "zero dispatch time budget rejected");
        std::cout << "core contracts passed\n";
        return 0;
    }
    catch (const std::exception& err)
    {
        std::cerr << err.what() << '\n';
        return 1;
    }
}

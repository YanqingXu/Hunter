// 使用真实 Luax continuation 验证 Asio 定时器和后台完成包的唯一终态。
#include "common/Types.h"
#include "script/Async.h"

#include <luax/bind/Bind.hpp>

#include <iostream>
#include <stdexcept>
#include <thread>

namespace
{

// 断言公开边界并保留失败说明。
void check(bool ok, const Str& msg)
{
    if (!ok)
    {
        throw std::runtime_error(msg);
    }
}

// 提取真实 Luax 结果，不隐藏 Runtime 返回的失败原因。
template <typename T>
T take(luax::Result<T> result)
{
    if (!result)
    {
        throw std::runtime_error(Str(result.error().message()));
    }

    return std::move(*result);
}

struct Probe
{
    UPtr<luax::Runtime> runtime;
    luax::Isolate isolate;
    luax::ModuleHandle module;
    luax::FunctionHandle function;
    bool reentry_rejected = false;

    // 创建真实待挂起函数，Host 回调只声明请求，不持有 VM 指针。
    explicit Probe(u64 delay_ns)
    {
        runtime = take(luax::Runtime::create());
        isolate = take(runtime->createIsolate());
        module = take(isolate.compile(
            "-- 提供受控挂起和重入行为的真实 Luax 探针。\n"
            "-- 通过宿主请求返回可恢复的挂起操作。\n"
            "function run()\n    return wait()\nend\n\n"
            "-- 验证宿主回调不能同步重入同一 Isolate。\n"
            "function nested()\n    return reenter()\nend\n\nreturn true\n"));
        take(isolate.registerHostFunction(module, "wait", luax::bind::function([delay_ns]
        {
            return luax::bind::AsyncReturn<luax::Result<bool>>(luax::TimerAwait{delay_ns});
        })));
        take(isolate.registerHostFunction(module, "reenter", luax::bind::function([this]
        {
            auto inner = isolate.call(function);
            reentry_rejected = !inner && inner.error().code() == luax::ErrorCode::invalid_state;
            return reentry_rejected;
        })));
        take(isolate.call(module));
        function = take(isolate.findFunction(module, "run"));
    }

    // 保证所有测试的 VM 均在创建线程释放。
    ~Probe()
    {
        static_cast<void>(isolate.close());
        static_cast<void>(runtime->shutdown());
    }

    // 挂起操作也必须具有有限执行预算。
    luax::ExecutionSuspended suspend()
    {
        luax::ExecutionOptions options;
        options.limits.instructionBudget = 10000;
        options.limits.nativeWorkBudget = 10000;
        auto step = take(isolate.dispatch(function, {}, std::move(options)));
        auto* pending = std::get_if<luax::ExecutionSuspended>(&step);
        check(pending != nullptr, "real Luax function must suspend");
        return std::move(*pending);
    }
};

// 完成回调检查恢复后的标量值，不读取测试私有实现。
void completed(std::expected<luax::CallResult, Str> result, u32& count)
{
    check(result.has_value(), result ? "" : result.error());
    check(result->values.size() == 1 && result->values[0].booleanIf() &&
        *result->values[0].booleanIf(), "resume typed bool result");
    ++count;
}

// 验证异步完成、容量拒绝、取消及超时之间的边界。
void async_contract()
{
    Probe probe(1000000);
    Probe second(1000000);
    asio::io_context io;
    hunter::Cfg cfg;
    cfg.max_async_ops = 1;
    hunter::Async async(io, cfg, 11, 2);
    u32 count = 0;
    auto timer = async.timer(probe.isolate, probe.suspend(), [&](auto result)
    {
        completed(std::move(result), count);
    }, 500);
    check(timer.has_value(), "timer reserved");
    check(count == 0 && async.pending() == 1, "timer never synchronously reenters");
    auto overflow = async.reserve(second.isolate, second.suspend(), [](auto)
    {
    }, 500);
    check(!overflow, "completion slot reserved before starting second operation");
    io.run();
    check(count == 1 && async.pending() == 0, "timer has exactly one terminal result");

    io.restart();
    auto ticket = async.reserve(probe.isolate, probe.suspend(), [&](auto result)
    {
        completed(std::move(result), count);
    }, 500);
    check(ticket.has_value(), "foreign completion reservation");
    bool accepted = false;
    bool duplicate = true;
    std::thread producer([owned = *ticket, &accepted, &duplicate]
    {
        accepted = owned.complete(true);
        duplicate = owned.complete(true);
    });
    producer.join();
    check(accepted && !duplicate, "detached producer accepts one terminal packet");
    check(count == 1, "foreign producer cannot run VM");
    io.run();
    check(count == 2, "owner resumes detached completion");

    io.restart();
    u32 cancelled = 0;
    ticket = async.reserve(probe.isolate, probe.suspend(), [&](auto result)
    {
        check(!result && result.error() == "async_cancelled", "cancel terminal");
        ++cancelled;
    }, 500);
    check(ticket.has_value(), "cancel reservation");
    const auto key = ticket->key();
    check(!async.cancel({key.instance, key.op, key.generation + 1}), "reject old generation");
    check(ticket->complete(true), "completion queued before cancellation race");
    check(async.cancel(key) && !async.cancel(key), "cancel has a unique terminal");
    check(!ticket->complete(true), "late packet rejected after cancellation");
    io.run();
    check(cancelled == 1, "queued completion cannot resurrect cancellation");

    io.restart();
    u32 expired = 0;
    ticket = async.reserve(probe.isolate, probe.suspend(), [&](auto result)
    {
        check(!result && result.error() == "async_timeout", "deadline terminal");
        ++expired;
    }, 5);
    check(ticket.has_value(), "timeout reservation");
    io.run();
    check(expired == 1 && !ticket->complete(true), "timeout invalidates late result");

    io.restart();
    u32 stopped = 0;
    ticket = async.reserve(probe.isolate, probe.suspend(), [&](auto result)
    {
        check(!result && result.error() == "async_stopped", "stop terminal");
        ++stopped;
    }, 500);
    check(ticket.has_value(), "stop reservation");
    async.stop();
    async.stop();
    check(stopped == 1 && !ticket->complete(true), "shutdown closes foreign mailbox");
    io.run();
}

// 直接验证固定 Luax 的线程、重入、同步挂起和过期句柄边界。
void vm_contract()
{
    Probe probe(1000000);
    auto nested = take(probe.isolate.findFunction(probe.module, "nested"));
    take(probe.isolate.call(nested));
    check(probe.reentry_rejected, "Luax must reject Host synchronous reentry");
    bool wrong_thread = false;
    std::thread foreign([&]
    {
        const auto result = probe.isolate.call(probe.function);
        wrong_thread = !result && result.error().code() == luax::ErrorCode::wrong_thread;
    });
    foreign.join();
    check(wrong_thread, "Luax must reject wrong thread");
    check(!probe.isolate.call(probe.function), "synchronous call cannot suspend");
    check(probe.isolate.unload(probe.module).has_value(), "unload probe module");
    const auto stale = probe.isolate.call(probe.function);
    check(!stale && stale.error().code() == luax::ErrorCode::stale_handle,
        "unloaded function handle is stale");
}

}

// 运行异步与 VM 契约并返回可用于 CTest 的退出状态。
int main()
{
    try
    {
        async_contract();
        vm_contract();
        std::cout << "async contract passed\n";
        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }
}

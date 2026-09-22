// 为结构化 Luax 挂起操作提供定时器、有限完成槽和拥有线程恢复入口。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"

#include <asio/io_context.hpp>
#include <luax/Runtime.hpp>
#include <luax/bind/AsyncCompletion.hpp>

#include <expected>

namespace hunter
{

struct AsyncKey
{
    u64 instance = 0;
    u64 op = 0;
    u64 generation = 0;
};

struct AsyncSlot;

class AsyncTicket
{
public:
    // 创建不关联 VM 或活动操作的空票据。
    AsyncTicket() = default;

    // 在任意线程编码拥有数据的结果；重复、取消和旧代次返回 false。
    bool complete(luax::Result<bool> result) const;

    // 返回可跨线程保存的操作标识，不泄露 VM 句柄。
    AsyncKey key() const;

private:
    friend class Async;

    // 完成槽已在逻辑线程预留，票据只持有数据队列能力。
    explicit AsyncTicket(Ptr<AsyncSlot> slot);

    Ptr<AsyncSlot> slot_;
};

class Async
{
public:
    using Done = Func<void(std::expected<luax::CallResult, Str>)>;

    // 在逻辑线程创建操作表；io 必须比适配器及其票据的关闭阶段活得更久。
    Async(asio::io_context& io, const Cfg& cfg, u64 instance, u64 generation);

    // 在拥有线程取消所有操作并关闭跨线程收件箱。
    ~Async();

    // 操作表和 continuation 不能复制。
    Async(const Async&) = delete;

    // 操作表和 continuation 不能复制赋值。
    Async& operator=(const Async&) = delete;

    // 在启动外部工作前预留完成槽；失败时取消传入 continuation。
    std::expected<AsyncTicket, Str> reserve(luax::Isolate isolate,
        luax::ExecutionSuspended suspended, Done done, u32 timeout_ms);

    // 将 TimerAwait 适配到 Asio；回调一定延后，不同步重入 VM。
    std::expected<AsyncKey, Str> timer(luax::Isolate isolate,
        luax::ExecutionSuspended suspended, Done done, u32 timeout_ms);

    // 仅拥有线程可取消当前实例和代次的操作；成功表示唯一终态已交付。
    bool cancel(AsyncKey key);

    // 幂等关闭；取消后的后台结果被票据明确拒绝。
    void stop();

    // 在拥有线程查询已预留的活动操作数。
    usize pending() const;

private:
    struct Impl;
    Ptr<Impl> impl_;
};

}

// 用预留槽和合并唤醒连接后台 detached 完成数据与逻辑线程 VM。
#include "common/Types.h"
#include "script/Async.h"

#include <asio/post.hpp>
#include <asio/steady_timer.hpp>

#include <chrono>
#include <algorithm>
#include <limits>
#include <mutex>
#include <optional>
#include <thread>

namespace hunter
{
namespace
{

using AsyncResult = luax::bind::AsyncReturn<luax::Result<bool>>;

struct AsyncInbox
{
    std::mutex mutex;
    Vec<WPtr<AsyncSlot>> slots;
    Func<void()> wake;
    bool notified = false;
    bool closed = false;
};

// 取消由拥有线程执行，返回稳定终态而不把 Error 对象跨线程借用。
luax::Error terminal_error(const Str& reason)
{
    return {luax::ErrorCode::invalid_state, luax::ErrorPhase::execution, reason};
}

}

struct AsyncSlot
{
    AsyncKey key;
    Ptr<AsyncInbox> inbox;
    luax::bind::AsyncCompletionLimits limits;
    std::optional<luax::bind::AsyncCompletionPacket> packet;
    bool active = true;
    bool completed = false;
};

struct Async::Impl : std::enable_shared_from_this<Impl>
{
    struct Op
    {
        luax::Isolate isolate;
        luax::ContinuationHandle continuation;
        Ptr<AsyncSlot> slot;
        UPtr<asio::steady_timer> deadline;
        UPtr<asio::steady_timer> timer;
        Done done;
    };

    asio::io_context& io;
    const std::thread::id owner = std::this_thread::get_id();
    const usize capacity;
    const u64 instance;
    const u64 generation;
    Ptr<AsyncInbox> inbox = std::make_shared<AsyncInbox>();
    Map<u64, Op> ops;
    u64 next_op = 1;
    bool stopped = false;

    // 固定当前实例和代次以及最大完成槽数。
    Impl(asio::io_context& ctx, const Cfg& cfg, u64 inst, u64 gen)
        : io(ctx), capacity(std::min({cfg.max_async_ops, cfg.max_control_count,
              cfg.max_control_bytes / 4096})),
          instance(inst), generation(gen)
    {
        inbox->slots.reserve(capacity);
    }

    // 终态先移出操作表再恢复或通知，避免回调取消已终结的操作。
    void finish(u64 op_id, std::optional<luax::bind::AsyncCompletionPacket> packet,
        const Str& reason)
    {
        auto found = ops.find(op_id);
        if (found == ops.end())
        {
            return;
        }

        Op op = std::move(found->second);
        ops.erase(found);
        {
            std::scoped_lock lock(inbox->mutex);
            op.slot->active = false;
            op.slot->packet.reset();
        }

        op.deadline->cancel();

        if (op.timer)
        {
            op.timer->cancel();
        }

        if (!packet)
        {
            static_cast<void>(op.isolate.cancel(op.continuation, terminal_error(reason)));
            op.done(std::unexpected(reason));
            return;
        }

        if (const auto* failure = packet->failureIf())
        {
            static_cast<void>(op.isolate.cancel(op.continuation, failure->toError()));
            op.done(std::unexpected(Str(failure->message())));
            return;
        }

        auto resumed = op.isolate.resume(op.continuation, packet->values());
        if (!resumed)
        {
            op.done(std::unexpected(Str(resumed.error().message())));
            return;
        }

        if (auto* done = std::get_if<luax::ExecutionCompleted>(&*resumed))
        {
            op.done(std::move(done->result));
            return;
        }

        const auto& again = std::get<luax::ExecutionSuspended>(*resumed);
        static_cast<void>(op.isolate.cancel(again.continuation,
            terminal_error("repeat_suspend_unsupported")));
        op.done(std::unexpected("repeat_suspend_unsupported"));
    }

    // 一次唤醒提取有限槽里的全部完成包，VM 操作只发生在锁外。
    void drain()
    {
        Vec<std::pair<u64, luax::bind::AsyncCompletionPacket>> ready;
        {
            std::scoped_lock lock(inbox->mutex);
            inbox->notified = false;
            auto& slots = inbox->slots;

            for (auto iter = slots.begin(); iter != slots.end();)
            {
                auto slot = iter->lock();
                if (!slot || !slot->active)
                {
                    iter = slots.erase(iter);
                    continue;
                }

                if (slot->packet)
                {
                    ready.emplace_back(slot->key.op, std::move(*slot->packet));
                    slot->packet.reset();
                }

                ++iter;
            }
        }

        for (auto& [op_id, packet] : ready)
        {
            finish(op_id, std::move(packet), "");
        }
    }
};

AsyncTicket::AsyncTicket(Ptr<AsyncSlot> slot) : slot_(std::move(slot))
{
}

bool AsyncTicket::complete(luax::Result<bool> result) const
{
    if (!slot_)
    {
        return false;
    }

    luax::bind::AsyncCompletionEncoder<AsyncResult> encoder(slot_->limits);
    auto packet = encoder.encode(std::move(result));
    auto& inbox = *slot_->inbox;
    std::scoped_lock lock(inbox.mutex);

    if (inbox.closed || !slot_->active || slot_->completed)
    {
        return false;
    }

    slot_->completed = true;
    slot_->packet.emplace(std::move(packet));

    if (!inbox.notified)
    {
        inbox.notified = true;

        try
        {
            inbox.wake();
        }
        catch (...)
        {
            inbox.notified = false;
            slot_->completed = false;
            slot_->packet.reset();
            return false;
        }
    }

    return true;
}

AsyncKey AsyncTicket::key() const
{
    return slot_ ? slot_->key : AsyncKey{};
}

Async::Async(asio::io_context& io, const Cfg& cfg, u64 instance, u64 generation)
    : impl_(std::make_shared<Impl>(io, cfg, instance, generation))
{
    const WPtr<Impl> weak = impl_;
    impl_->inbox->wake = [&io, weak]
    {
        asio::post(io, [weak]
        {
            if (auto self = weak.lock())
            {
                self->drain();
            }
        });
    };
}

Async::~Async()
{
    stop();
}

std::expected<AsyncTicket, Str> Async::reserve(luax::Isolate isolate,
    luax::ExecutionSuspended suspended, Done done, u32 timeout_ms)
{
    auto& self = *impl_;
    if (self.owner != std::this_thread::get_id())
    {
        return std::unexpected("wrong_thread");
    }

    if (self.stopped || self.ops.size() >= self.capacity || !done || timeout_ms == 0 ||
        self.next_op == 0)
    {
        static_cast<void>(isolate.cancel(suspended.continuation,
            terminal_error("async_capacity_or_closed")));
        return std::unexpected("async_capacity_or_closed");
    }

    auto slot = std::make_shared<AsyncSlot>();
    slot->key = {self.instance, self.next_op++, self.generation};
    slot->inbox = self.inbox;
    slot->limits.maxResults = 1;
    slot->limits.maxErrorBytes = 4096;
    {
        std::scoped_lock lock(self.inbox->mutex);
        auto& slots = self.inbox->slots;
        std::erase_if(slots, [](const WPtr<AsyncSlot>& weak)
        {
            auto old = weak.lock();
            return !old || !old->active;
        });
        slots.push_back(slot);
    }

    Impl::Op op;
    op.isolate = isolate;
    op.continuation = suspended.continuation;
    op.slot = slot;
    op.done = std::move(done);
    op.deadline = std::make_unique<asio::steady_timer>(self.io);
    op.deadline->expires_after(std::chrono::milliseconds(timeout_ms));
    const WPtr<Impl> weak = impl_;
    const u64 op_id = slot->key.op;
    op.deadline->async_wait([weak, op_id](const std::error_code& error)
    {
        if (!error)
        {
            if (auto owner = weak.lock())
            {
                owner->finish(op_id, {}, "async_timeout");
            }
        }
    });
    self.ops.emplace(op_id, std::move(op));
    return AsyncTicket(std::move(slot));
}

std::expected<AsyncKey, Str> Async::timer(luax::Isolate isolate,
    luax::ExecutionSuspended suspended, Done done, u32 timeout_ms)
{
    if (impl_->owner != std::this_thread::get_id())
    {
        return std::unexpected("wrong_thread");
    }

    const auto* req = std::get_if<luax::TimerAwait>(&suspended.request);
    if (!req || req->delayNanoseconds > static_cast<u64>(std::numeric_limits<i64>::max()))
    {
        static_cast<void>(isolate.cancel(suspended.continuation,
            terminal_error("unsupported_await")));
        return std::unexpected("unsupported_await");
    }

    const auto delay = std::chrono::nanoseconds(req->delayNanoseconds);
    auto ticket = reserve(isolate, std::move(suspended), std::move(done), timeout_ms);
    if (!ticket)
    {
        return std::unexpected(ticket.error());
    }

    auto& op = impl_->ops.at(ticket->key().op);
    op.timer = std::make_unique<asio::steady_timer>(impl_->io);
    op.timer->expires_after(delay);
    op.timer->async_wait([ticket = *ticket](const std::error_code& error)
    {
        if (!error)
        {
            static_cast<void>(ticket.complete(true));
        }
    });
    return ticket->key();
}

bool Async::cancel(AsyncKey key)
{
    auto& self = *impl_;
    if (self.owner != std::this_thread::get_id() || key.instance != self.instance ||
        key.generation != self.generation || !self.ops.contains(key.op))
    {
        return false;
    }

    self.finish(key.op, {}, "async_cancelled");
    return true;
}

void Async::stop()
{
    auto& self = *impl_;
    if (self.owner != std::this_thread::get_id() || self.stopped)
    {
        return;
    }

    self.stopped = true;
    {
        std::scoped_lock lock(self.inbox->mutex);
        self.inbox->closed = true;
        self.inbox->wake = {};
    }

    while (!self.ops.empty())
    {
        self.finish(self.ops.begin()->first, {}, "async_stopped");
    }
}

usize Async::pending() const
{
    return impl_->owner == std::this_thread::get_id() ? impl_->ops.size() : 0;
}

}

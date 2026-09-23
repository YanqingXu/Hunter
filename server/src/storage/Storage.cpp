// 用预留完成容量和单一工作线程连接 SQLite 与所属逻辑线程，不同步执行回调。
#include "common/Types.h"
#include "storage/Storage.h"
#include "storage/Db.h"
#include "storage/Schema.h"
#include "storage/Store.h"

#include <asio/executor_work_guard.hpp>
#include <asio/post.hpp>

#include <condition_variable>
#include <deque>
#include <limits>
#include <mutex>
#include <thread>

namespace hunter::storage
{
namespace
{

enum class Kind { Open, Load, Alloc, Commit, Find, Stop };
enum class State { New, Opening, Ready, Faulted, Stopping, Stopped };
constexpr usize small_result = 1024;

struct Op
{
    Kind kind = Kind::Alloc;
    Key key;
    Storage::Done done;
    Str path;
    u64 id = 0;
    CommitMatch req;
    Str json;
    usize req_bytes = 0;
    usize done_bytes = 0;
};

// 返回有限错误值，拒绝发生时尚未接受或执行任务。
std::unexpected<Error> rejected(Code code, const char* message)
{
    return std::unexpected(Error{code, 0, false, message});
}

// 逐项计费并避免任意长度输入使计数加法溢出。
void charge(usize& bytes, usize value, usize limit)
{
    if (bytes > limit || value > limit - bytes)
    {
        fail(Code::TooLarge, "storage_request_limit");
    }

    bytes += value;
}

// 校验正数领域 ID，提交前拒绝无符号到有符号回绕。
bool id_ok(u64 id)
{
    return id > 0 && id <= static_cast<u64>(std::numeric_limits<i64>::max());
}

}

struct Storage::Impl : std::enable_shared_from_this<Impl>
{
    asio::io_context& io;
    asio::executor_work_guard<asio::io_context::executor_type> guard;
    const StorageCfg cfg;
    const u64 instance;
    const std::thread::id owner = std::this_thread::get_id();
    State state = State::New;
    u64 next_op = 1;
    usize pending = 0;
    usize req_bytes = 0;
    usize done_bytes = 0;
    std::mutex mutex;
    std::condition_variable wake;
    std::deque<Ptr<Op>> queue;
    usize undelivered = 0;
    bool abandoned = false;
    std::jthread worker;

    // 固定配置并保活事件循环，直到关闭完成已被交付。
    Impl(asio::io_context& ctx, StorageCfg limits, u64 inst)
        : io(ctx), guard(asio::make_work_guard(ctx)), cfg(limits), instance(inst)
    {
        if (instance == 0 || cfg.max_ops == 0 || cfg.max_req_bytes < sizeof(Op) ||
            cfg.max_result_bytes < small_result || cfg.max_done_bytes < cfg.max_result_bytes ||
            cfg.max_result_bytes > static_cast<usize>(std::numeric_limits<i32>::max()))
        {
            fail(Code::Invalid, "invalid_storage_cfg");
        }
    }

    // 检查逻辑线程及生命周期；工作线程从不访问这些状态字段。
    std::expected<void, Error> allowed(Kind kind) const
    {
        if (std::this_thread::get_id() != owner)
        {
            return rejected(Code::WrongThread, "storage_owner_thread");
        }

        if (state == State::Stopping || state == State::Stopped)
        {
            return rejected(Code::Closed, "storage_stopping_or_closed");
        }

        if (kind == Kind::Open && state != State::New)
        {
            return rejected(Code::Invalid, "storage_already_opened");
        }

        if (kind != Kind::Open && kind != Kind::Stop && state != State::Ready)
        {
            return rejected(Code::NotReady, "storage_not_ready");
        }

        return {};
    }

    // 原子预留请求和完成预算后入队；独立停止槽不消耗普通操作额度。
    std::expected<Accepted, Error> submit(Ptr<Op> op)
    {
        auto valid = allowed(op->kind);
        if (!valid)
        {
            return std::unexpected(valid.error());
        }

        const bool stop = op->kind == Kind::Stop;
        if (!op->done || (!stop && next_op == std::numeric_limits<u64>::max()))
        {
            return rejected(Code::Invalid, "missing_callback_or_operation_exhausted");
        }

        if (!stop && (pending >= cfg.max_ops || op->req_bytes > cfg.max_req_bytes - req_bytes ||
            op->done_bytes > cfg.max_done_bytes - done_bytes))
        {
            return rejected(Code::Capacity, "storage_capacity");
        }

        op->key = {instance, next_op};
        {
            std::scoped_lock lock(mutex);
            queue.push_back(op);

            if (!stop)
            {
                ++undelivered;
            }
        }

        if (stop)
        {
            state = State::Stopping;
        }
        else
        {
            ++next_op;
            ++pending;
            req_bytes += op->req_bytes;
            done_bytes += op->done_bytes;

            if (op->kind == Kind::Open)
            {
                state = State::Opening;
            }
        }

        wake.notify_one();
        return Accepted{op->key};
    }

    // 交付前归还预算，允许回调提交后续任务；关闭等待回调处理完毕。
    void deliver(const Ptr<Op>& op, std::expected<Value, Error> result)
    {
        if (std::this_thread::get_id() != owner)
        {
            fail(Code::WrongThread, "storage_io_owner_thread");
        }

        if (op->kind == Kind::Stop)
        {
            state = State::Stopped;
            guard.reset();
            op->done({op->key, std::move(result)});
            return;
        }

        --pending;
        req_bytes -= op->req_bytes;
        done_bytes -= op->done_bytes;

        if (op->kind == Kind::Open && state == State::Opening)
        {
            state = result ? State::Ready : State::Faulted;
        }

        try
        {
            op->done({op->key, std::move(result)});
        }
        catch (...)
        {
            delivered();
            throw;
        }

        delivered();
    }

    // 向工作线程确认完成已交付，停止事务不抢先关闭数据库。
    void delivered()
    {
        {
            std::scoped_lock lock(mutex);
            --undelivered;
        }

        wake.notify_one();
    }

    // 工作线程只操作拥有型数据，捕获强引用让旧完成独立于门面寿命。
    void complete(Ptr<Op> op, std::expected<Value, Error> result)
    {
        asio::post(io, [self = shared_from_this(), op = std::move(op),
            result = std::move(result)]() mutable
        {
            self->deliver(op, std::move(result));
        });
    }

    // 在唯一存档线程执行任务，异常不会越过线程入口或伪装为保存成功。
    std::expected<Value, Error> execute(const Op& op, UPtr<Db>& db)
    {
        try
        {
            if (op.kind == Kind::Open)
            {
                auto candidate = std::make_unique<Db>(op.path);
                open_schema(*candidate);
                db = std::move(candidate);
                return Opened{};
            }

            if (op.kind == Kind::Stop)
            {
                if (db)
                {
                    db->close();
                    db.reset();
                }

                return Closed{};
            }

            if (!db)
            {
                return rejected(Code::NotReady, "storage_connection_unavailable");
            }

            switch (op.kind)
            {
            case Kind::Load:
                return read_player(*db, op.id, cfg.max_result_bytes);
            case Kind::Alloc:
                return next_match(*db);
            case Kind::Commit:
                return write_match(*db, op.req, op.json, cfg.max_result_bytes);
            case Kind::Find:
                return read_match(*db, op.id, cfg.max_result_bytes);
            default:
                return rejected(Code::Internal, "unknown_storage_operation");
            }
        }
        catch (const Error& error)
        {
            return std::unexpected(error);
        }
        catch (const std::exception&)
        {
            return rejected(Code::Internal, "storage_execution_exception");
        }
    }

    // 连接在本函数创建并释放，关闭屏障等待正常回调或析构保底通知。
    void run()
    {
        UPtr<Db> db;

        for (;;)
        {
            Ptr<Op> op;
            {
                std::unique_lock lock(mutex);
                wake.wait(lock, [&] { return abandoned || !queue.empty(); });

                if (queue.empty())
                {
                    break;
                }

                op = std::move(queue.front());
                queue.pop_front();

                if (op->kind == Kind::Stop)
                {
                    wake.wait(lock, [&] { return abandoned || undelivered == 0; });
                }
            }

            auto result = execute(*op, db);
            Str{}.swap(op->req.outcome);
            Str{}.swap(op->req.content_key);
            Vec<ItemDelta>{}.swap(op->req.items);
            Str{}.swap(op->json);
            Str{}.swap(op->path);
            const bool stop = op->kind == Kind::Stop;
            complete(std::move(op), std::move(result));

            if (stop)
            {
                break;
            }
        }
    }

    // 析构不能等待所属线程执行回调；排空已接受磁盘工作后再回收线程。
    void shutdown()
    {
        {
            std::scoped_lock lock(mutex);
            abandoned = true;
        }

        wake.notify_one();

        if (worker.joinable())
        {
            worker.join();
        }

        guard.reset();
    }

    // 准备无可变请求体的任务，完成槽按最坏结果大小预留。
    std::expected<Accepted, Error> simple(Kind kind, u64 id, Done done)
    {
        auto valid = allowed(kind);
        if (!valid)
        {
            return std::unexpected(valid.error());
        }

        if ((kind == Kind::Find || kind == Kind::Load) && !id_ok(id))
        {
            return rejected(Code::Invalid, "persistent_id_range");
        }

        auto op = std::make_shared<Op>();
        op->kind = kind;
        op->id = id;
        op->done = std::move(done);
        op->req_bytes = sizeof(Op);
        op->done_bytes = kind == Kind::Load || kind == Kind::Find ?
            cfg.max_result_bytes : small_result;
        return submit(std::move(op));
    }
};

Storage::Storage(asio::io_context& io, StorageCfg cfg, u64 instance)
    : impl_(std::make_shared<Impl>(io, cfg, instance))
{
    impl_->worker = std::jthread([self = impl_.get()] { self->run(); });
}

Storage::~Storage()
{
    impl_->shutdown();
}

std::expected<Accepted, Error> Storage::open(Str path, Done done)
{
    auto& self = *impl_;
    auto valid = self.allowed(Kind::Open);
    if (!valid)
    {
        return std::unexpected(valid.error());
    }

    if (path.empty() || path.find('\0') != Str::npos)
    {
        return rejected(Code::Invalid, "invalid_database_path");
    }

    try
    {
        usize bytes = sizeof(Op);
        charge(bytes, path.capacity(), self.cfg.max_req_bytes);
        auto op = std::make_shared<Op>();
        op->kind = Kind::Open;
        op->path = std::move(path);
        op->done = std::move(done);
        op->req_bytes = bytes;
        op->done_bytes = small_result;
        return self.submit(std::move(op));
    }
    catch (const Error& error)
    {
        return std::unexpected(error);
    }
}

std::expected<Accepted, Error> Storage::load_player(u64 player_id, Done done)
{
    return impl_->simple(Kind::Load, player_id, std::move(done));
}

std::expected<Accepted, Error> Storage::alloc_match(Done done)
{
    return impl_->simple(Kind::Alloc, 0, std::move(done));
}

std::expected<Accepted, Error> Storage::commit_match(CommitMatch req, Done done)
{
    auto& self = *impl_;
    auto valid = self.allowed(Kind::Commit);
    if (!valid)
    {
        return std::unexpected(valid.error());
    }

    try
    {
        usize bytes = sizeof(Op);
        charge(bytes, req.outcome.capacity(), self.cfg.max_req_bytes);
        charge(bytes, req.content_key.capacity(), self.cfg.max_req_bytes);

        if (req.items.capacity() > self.cfg.max_req_bytes / sizeof(ItemDelta))
        {
            return rejected(Code::TooLarge, "item_request_limit");
        }

        charge(bytes, req.items.capacity() * sizeof(ItemDelta), self.cfg.max_req_bytes);
        auto json = encode_req(req);
        charge(bytes, json.capacity(), self.cfg.max_req_bytes);
        auto op = std::make_shared<Op>();
        op->kind = Kind::Commit;
        op->req = std::move(req);
        op->json = std::move(json);
        op->done = std::move(done);
        op->req_bytes = bytes;
        op->done_bytes = self.cfg.max_result_bytes;
        return self.submit(std::move(op));
    }
    catch (const Error& error)
    {
        return std::unexpected(error);
    }
}

std::expected<Accepted, Error> Storage::find_match(u64 match_id, Done done)
{
    return impl_->simple(Kind::Find, match_id, std::move(done));
}

std::expected<Accepted, Error> Storage::stop(Done done)
{
    return impl_->simple(Kind::Stop, 0, std::move(done));
}

}

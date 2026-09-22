// 组装单线程服务、脚本入口和有界宿主通道，不维护脚本世界的副本。
#include "core/Runtime.h"
#include "common/Types.h"
#include "core/BoundedQueue.h"
#include "core/TickClock.h"
#include "net/Transport.h"
#include "script/Script.h"

#include <asio.hpp>
#include <nlohmann/json.hpp>

#include <atomic>
#include <condition_variable>
#include <exception>
#include <fstream>
#include <mutex>
#include <thread>

#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#endif

namespace hunter
{
namespace
{
// 从系统随机源生成十六进制临时秘密，失败时禁止发布就绪信息。
Str random_hex(usize size)
{
    Vec<u8> bytes(size);
#ifdef _WIN32
    const auto result = BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(size),
        BCRYPT_USE_SYSTEM_PREFERRED_RNG);

    if (result != 0)
    {
        throw std::runtime_error("system random source failed");
    }
#else
    std::ifstream source("/dev/urandom", std::ios::binary);
    source.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(size));

    if (!source)
    {
        throw std::runtime_error("system random source failed");
    }
#endif
    constexpr char digits[] = "0123456789abcdef";
    Str text;
    text.reserve(size * 2);

    for (const auto byte : bytes)
    {
        text.push_back(digits[byte >> 4]);
        text.push_back(digits[byte & 15]);
    }

    return text;
}
}

struct Runtime::State
{
    // 创建跨线程通道；逻辑对象稍后在逻辑线程内构造。
    explicit State(Cfg value)
        : cfg(std::move(value)), commands(cfg.max_queue_count, cfg.max_queue_bytes),
          events(cfg.max_control_count > 0 ? cfg.max_control_count - 1 : 0,
              cfg.max_control_bytes >= 512 ? cfg.max_control_bytes - 512 : 0)
    {
        if (cfg.max_control_count == 0 || cfg.max_control_bytes < 512)
        {
            throw std::invalid_argument("control capacity must reserve one 512-byte terminal slot");
        }
    }

    Cfg cfg;
    std::mutex mutex;
    std::condition_variable changed;
    BoundedQueue<Str> commands;
    BoundedQueue<Str> events;
    std::optional<Str> terminal;
    std::thread thread;
    asio::io_context* io = nullptr;
    Func<void()> drain;
    bool wake_pending = false;
    bool stopping = false;
    std::atomic<bool> done = false;
    std::atomic<bool> faulted = false;
};

struct Runtime::Loop
{
    // 在逻辑线程创建全部异步与 VM 所有者。
    explicit Loop(State& shared)
        : shared(shared), timer(io), guard(asio::make_work_guard(io)),
          clock(shared.cfg.tick_hz, shared.cfg.max_catchup),
          inputs(shared.cfg.max_queue_count, shared.cfg.max_queue_bytes)
    {
        if (shared.cfg.max_inputs_per_tick == 0 || shared.cfg.tick_work_ms == 0)
        {
            throw std::invalid_argument("tick input and work limits must be positive");
        }
    }

    // 撤销跨线程可见指针后才析构事件循环，避免退出边界投递到已释放对象。
    ~Loop()
    {
        std::lock_guard lock(shared.mutex);
        shared.io = nullptr;
        shared.drain = {};
        shared.stopping = true;
    }

    // 发布回调入口并运行事件循环，退出前排空取消回调。
    void run()
    {
        {
            std::lock_guard lock(shared.mutex);
            shared.io = &io;
            shared.drain = [this]
            {
                drain();
            };
            shared.wake_pending = true;
            asio::post(io, shared.drain);
        }

        try
        {
            io.run();
        }
        catch (...)
        {
            const auto failure = std::current_exception();
            shared.faulted = true;
            // 分别尝试各段收尾，次生异常不得跳过另一段或替换最初的运行异常。
            const auto cleanup = [this](auto action)
            {
                try
                {
                    action();
                }
                catch (...)
                {
                    try
                    {
                        emit({{"type", "Diagnostic"}, {"code", "runtime_cleanup_failed"}});
                    }
                    catch (...)
                    {
                        // 连诊断分配也失败时保留独立故障标志，仍传播最初异常。
                        shared.faulted = true;
                    }
                }
            };
            cleanup([this]
            {
                close_script("runtime_failed", "");
            });
            cleanup([this]
            {
                stop("");
            });
            std::rethrow_exception(failure);
        }

        script.reset();
        net.reset();
    }

    // 移出一批宿主命令，合并唤醒并允许 Stop 绕过排队积压。
    void drain()
    {
        Vec<Str> commands;
        bool stop_requested = false;

        {
            std::lock_guard lock(shared.mutex);
            shared.wake_pending = false;
            stop_requested = shared.stopping;

            while (auto item = shared.commands.pop())
            {
                commands.push_back(std::move(*item));
            }
        }

        if (stop_requested)
        {
            stop("");
            return;
        }

        for (const auto& line : commands)
        {
            command(line);
        }
    }

    // 发布有界控制事件；容量不足使用预留终态槽并停止服务。
    void emit(nlohmann::json evt)
    {
        if (evt.value("state", "") == "Faulted")
        {
            shared.faulted = true;
        }

        auto line = evt.dump(-1, ' ', false, nlohmann::json::error_handler_t::replace);
        bool accepted = false;

        {
            std::lock_guard lock(shared.mutex);
            const auto bytes = line.size();
            accepted = shared.events.push(std::move(line), bytes);

            if (!accepted)
            {
                shared.terminal = "{\"type\":\"Error\",\"state\":\"Faulted\","
                    "\"code\":\"control_backpressure\"}";
                shared.stopping = true;
                shared.faulted = true;
            }
        }

        shared.changed.notify_all();

        if (!accepted && !stopped)
        {
            stop("");
        }
    }

    // 返回关联到控制请求的状态，保留宿主与会话两套状态。
    nlohmann::json rsp(const Str& type, const Str& req_id) const
    {
        return {{"type", type}, {"req_id", req_id}, {"state", state}, {"session", session}};
    }

    // 解码控制请求并在当前状态下执行幂等生命周期操作。
    void command(const Str& line)
    {
        Str req_id;

        try
        {
            const auto msg = nlohmann::json::parse(line);
            req_id = msg.at("req_id").get<Str>();
            const auto cmd = msg.at("cmd").get<Str>();

            if (req_id.empty() || req_id.size() > 128 || msg.size() != 2)
            {
                throw std::runtime_error("invalid control schema");
            }

            if (cmd == "Stop")
            {
                stop(req_id);
            }
            else if (cmd == "Start" && state == "Starting")
            {
                start(req_id);
            }
            else if (cmd == "Start" && state == "Ready" && session != "Aborted")
            {
                ready(req_id);
            }
            else if (cmd == "Pause" && state == "Ready" && session != "Aborted")
            {
                paused = true;
                session = "Paused";
                timer.cancel();
                emit(rsp("Rsp", req_id));
            }
            else if (cmd == "Resume" && state == "Ready" && session != "Aborted")
            {
                const bool was_paused = paused;
                paused = false;
                session = authenticated ? "Running" : "Idle";

                if (was_paused)
                {
                    clock.reset(TickClock::Clock::now());
                    schedule();
                }

                emit(rsp("Rsp", req_id));
            }
            else
            {
                auto evt = rsp("Error", req_id);
                evt["code"] = "invalid_command_or_state";
                emit(std::move(evt));
            }
        }
        catch (const std::exception& err)
        {
            auto evt = rsp("Error", req_id.substr(0, 128));
            evt["code"] = "invalid_control";
            evt["detail"] = Str(err.what()).substr(0, 256);
            emit(std::move(evt));
        }
    }

    // 装载脚本并绑定端口，全部成功后发布本次实例的就绪信息。
    void start(const Str& req_id)
    {
        try
        {
            const auto& cfg = shared.cfg;
            if (cfg.snapshot_hz == 0 || cfg.snapshot_hz > cfg.tick_hz
                || cfg.tick_hz % cfg.snapshot_hz != 0)
            {
                throw std::runtime_error("invalid snapshot frequency");
            }

            instance = random_hex(16);
            token = random_hex(32);
            script = std::make_unique<Script>();
            const nlohmann::json ctx = {{"v", 1},
                {"snapshot_every", cfg.tick_hz / cfg.snapshot_hz}};
            auto out = script->open(cfg, ctx.dump());

            if (!flush_logs())
            {
                return;
            }

            if (!out || !out->empty())
            {
                throw std::runtime_error(out ? "init must not emit network output" : out.error());
            }

            net = std::make_unique<Transport>(io, cfg,
                [this](wire::Envelope msg)
                {
                    receive(std::move(msg));
                },
                [this](Str reason)
                {
                    abort(std::move(reason));
                });
            port = net->open();
            state = "Ready";
            ready(req_id);
        }
        catch (const std::exception& err)
        {
            state = "Faulted";
            auto evt = rsp("Error", req_id);
            evt["code"] = "start_failed";
            evt["detail"] = Str(err.what()).substr(0, 512);
            emit(std::move(evt));
            stop("");
        }
    }

    // 发布当前端口及临时握手凭据，日志不包含令牌。
    void ready(const Str& req_id)
    {
        auto evt = rsp("Ready", req_id);
        evt["port"] = port;
        evt["instance"] = instance;
        evt["token"] = token;
        evt["protocol_version"] = 1;
        evt["content_version"] = shared.cfg.content_version;
        emit(std::move(evt));
    }

    // 按认证与序号边界接收消息，输入仅入队而不直接执行脚本。
    void receive(wire::Envelope msg)
    {
        if (!authenticated)
        {
            const auto& hello = msg.hello();
            if (!msg.has_hello() || hello.protocol_version() != 1
                || hello.content_version() != shared.cfg.content_version
                || hello.instance() != instance || hello.token() != token)
            {
                abort("handshake_rejected");
                return;
            }

            authenticated = true;
            net->authenticate();
            wire::Envelope reply;
            auto* ack = reply.mutable_hello_ack();
            ack->set_protocol_version(1);
            ack->set_content_version(shared.cfg.content_version);
            ack->set_instance(instance);

            if (!send_msg(reply))
            {
                return;
            }

            session = paused ? "Paused" : "Running";
            clock.reset(TickClock::Clock::now());
            schedule();
            return;
        }

        if (!msg.has_input() || msg.input().seq() == 0
            || msg.input().value() < -1000 || msg.input().value() > 1000)
        {
            abort("invalid_input");
            return;
        }

        const auto& input = msg.input();
        if (input.seq() <= received_seq)
        {
            if (last_ack && input.seq() == last_ack->ack().seq())
            {
                send_msg(*last_ack);
            }
            else if (!pending_seqs.contains(input.seq()))
            {
                wire::Envelope reply;
                reply.mutable_error()->set_code("stale_input");
                send_msg(reply);
            }

            return;
        }

        if (!inputs.push(input, input.ByteSizeLong()))
        {
            abort("input_backpressure");
            return;
        }

        received_seq = input.seq();
        pending_seqs.insert(input.seq());
    }

    // 发送一条控制协议消息，无法保留关键输出时中止会话。
    bool send_msg(const wire::Envelope& msg)
    {
        auto frame = encode_frame(msg, shared.cfg.max_frame_bytes);
        if (!frame || !net->send({std::move(*frame)}))
        {
            abort("send_backpressure");
            return false;
        }

        return true;
    }

    // 校验并一次提交成功入口的全部输出，失败后不再使用脚本状态。
    bool commit(std::expected<Vec<ScriptOut>, Str> out)
    {
        if (!flush_logs())
        {
            return false;
        }

        if (!out)
        {
            abort("script_error:" + out.error().substr(0, 256));
            return false;
        }

        auto frames = script_frames(*out, shared.cfg);
        if (!frames)
        {
            abort(frames.error());
            return false;
        }

        std::optional<wire::Envelope> ack;

        for (const auto& frame : *frames)
        {
            auto msg = decode_frame(frame.bytes.substr(4), shared.cfg.max_frame_bytes);
            if (msg && msg->has_ack())
            {
                ack = std::move(*msg);
            }
        }

        if (!net->send(std::move(*frames)))
        {
            abort("send_backpressure");
            return false;
        }

        if (ack)
        {
            last_ack = std::move(ack);
        }

        return true;
    }

    // 将入口返回后的脚本日志移入有界宿主通道，逻辑线程不执行管道写入。
    bool flush_logs()
    {
        auto logs = script->take_logs();

        for (auto& text : logs)
        {
            emit({{"type", "Diagnostic"}, {"code", "script_log"}, {"detail", std::move(text)}});

            if (stopped)
            {
                return false;
            }
        }

        return true;
    }

    // 为下一次固定步长设置单调定时器，暂停或终止时不再调度。
    void schedule()
    {
        if (stopped || paused || !authenticated || session == "Aborted")
        {
            return;
        }

        timer.expires_at(clock.next());
        timer.async_wait([this](asio::error_code err)
        {
            if (err || stopped || paused || session == "Aborted")
            {
                return;
            }

            const auto due = clock.poll(TickClock::Clock::now());
            if (due.dropped)
            {
                emit({{"type", "Diagnostic"}, {"code", "tick_backlog_dropped"}});
            }

            const auto deadline = TickClock::Clock::now()
                + std::chrono::milliseconds(shared.cfg.tick_work_ms);

            for (u32 i = 0; i < due.count && !stopped && session != "Aborted"; ++i)
            {
                if (i > 0 && TickClock::Clock::now() >= deadline)
                {
                    emit({{"type", "Diagnostic"}, {"code", "tick_work_yield"}});
                    break;
                }

                step(deadline);
            }

            schedule();
        });
    }

    // 在本次输入条数和共享墙钟预算内顺序执行，剩余输入保留到后续 Tick。
    void step(TickClock::Time deadline)
    {
        for (usize count = 0; count < shared.cfg.max_inputs_per_tick; ++count)
        {
            if (TickClock::Clock::now() >= deadline)
            {
                break;
            }

            auto input = inputs.pop();
            if (!input)
            {
                break;
            }

            pending_seqs.erase(input->seq());
            const nlohmann::json payload = {{"v", 1}, {"seq", std::to_string(input->seq())},
                {"value", input->value()}};

            if (!commit(script->event(1, payload.dump())))
            {
                return;
            }
        }

        ++tick_id;
        commit(script->tick(tick_id, 1.0 / shared.cfg.tick_hz));
    }

    // 终止当前会话并清空输入，不在新连接上恢复旧局。
    void abort(Str reason)
    {
        if (session == "Aborted" || stopped)
        {
            return;
        }

        session = "Aborted";
        timer.cancel();
        inputs.clear();
        pending_seqs.clear();

        if (net)
        {
            net->stop();
        }

        const bool closed = close_script(reason, "");

        auto evt = rsp("Error", "");
        evt["code"] = std::move(reason);
        emit(std::move(evt));

        if (!closed)
        {
            stop("");
        }
    }

    // 收集关闭结果和退出日志后再释放脚本；失败仍完成资源回收并标记宿主故障。
    bool close_script(const Str& reason, const Str& req_id)
    {
        if (!script)
        {
            return true;
        }

        auto closing = std::move(script);
        auto result = closing->shutdown(reason);
        auto logs = closing->take_logs();
        closing.reset();

        if (!result)
        {
            state = "Faulted";
            shared.faulted = true;
            auto evt = rsp("Error", req_id);
            evt["code"] = "shutdown_failed";
            evt["detail"] = result.error().substr(0, 256);
            emit(std::move(evt));
            emit({{"type", "Diagnostic"}, {"code", "shutdown_failed"},
                {"detail", result.error().substr(0, 256)}});
        }

        for (auto& text : logs)
        {
            emit({{"type", "Diagnostic"}, {"code", "script_log"}, {"detail", std::move(text)}});
        }

        return result.has_value();
    }

    // 幂等取消资源并撤销工作守卫，让所有取消回调排空后结束线程。
    void stop(const Str& req_id)
    {
        if (stopped)
        {
            if (!req_id.empty())
            {
                emit(rsp("Rsp", req_id));
            }

            return;
        }

        stopped = true;
        const bool had_fault = shared.faulted || state == "Faulted";
        state = "Stopping";

        {
            std::lock_guard lock(shared.mutex);
            shared.stopping = true;
        }

        timer.cancel();
        inputs.clear();
        pending_seqs.clear();

        if (net)
        {
            net->stop();
        }

        const bool closed = close_script("host_stop", req_id);

        state = had_fault || !closed || shared.faulted ? "Faulted" : "Stopped";
        emit(rsp("Stopped", req_id));
        guard.reset();
    }

    State& shared;
    asio::io_context io{1};
    asio::steady_timer timer;
    asio::executor_work_guard<asio::io_context::executor_type> guard;
    TickClock clock;
    BoundedQueue<wire::FrameInput> inputs;
    Set<u64> pending_seqs;
    UPtr<Script> script;
    UPtr<Transport> net;
    std::optional<wire::Envelope> last_ack;
    Str state = "Starting";
    Str session = "Idle";
    Str instance;
    Str token;
    u16 port = 0;
    u64 tick_id = 0;
    u64 received_seq = 0;
    bool authenticated = false;
    bool paused = false;
    bool stopped = false;
};

Runtime::Runtime(Cfg cfg) : state_(std::make_unique<State>(std::move(cfg)))
{
    state_->thread = std::thread([state = state_.get()]
    {
        try
        {
            Loop loop(*state);
            loop.run();
        }
        catch (const std::exception& err)
        {
            std::lock_guard lock(state->mutex);
            state->faulted = true;
            state->terminal = nlohmann::json({{"type", "Error"}, {"state", "Faulted"},
                {"code", "runtime_failed"}, {"detail", Str(err.what()).substr(0, 64)}})
                .dump(-1, ' ', false, nlohmann::json::error_handler_t::replace);
        }
        catch (...)
        {
            std::lock_guard lock(state->mutex);
            state->faulted = true;
            state->terminal = "{\"type\":\"Error\",\"state\":\"Faulted\","
                "\"code\":\"runtime_failed\",\"detail\":\"unknown_exception\"}";
        }

        {
            std::lock_guard lock(state->mutex);
            state->io = nullptr;
            state->drain = {};
            state->stopping = true;
            state->done = true;
        }

        state->changed.notify_all();
    });
}

Runtime::~Runtime()
{
    stop();
    join();
}

bool Runtime::submit(Str line)
{
    std::lock_guard lock(state_->mutex);

    if (state_->stopping || line.size() > state_->cfg.max_json_bytes)
    {
        return false;
    }

    const auto bytes = line.size();
    if (!state_->commands.push(std::move(line), bytes))
    {
        return false;
    }

    if (state_->io && !state_->wake_pending)
    {
        state_->wake_pending = true;
        asio::post(*state_->io, state_->drain);
    }

    return true;
}

std::optional<Str> Runtime::next_evt(u32 timeout_ms)
{
    std::unique_lock lock(state_->mutex);
    state_->changed.wait_for(lock, std::chrono::milliseconds(timeout_ms), [this]
    {
        return state_->events.size() > 0 || state_->terminal || state_->done.load();
    });
    auto evt = state_->events.pop();
    if (evt)
    {
        return evt;
    }

    return std::exchange(state_->terminal, std::nullopt);
}

void Runtime::stop(Str reason)
{
    std::lock_guard lock(state_->mutex);
    const bool was_stopping = state_->stopping;
    state_->stopping = true;

    if (!reason.empty() && !was_stopping)
    {
        state_->faulted = true;
        state_->terminal = nlohmann::json({{"type", "Error"}, {"state", "Faulted"},
            {"code", reason.substr(0, 64)}})
            .dump(-1, ' ', false, nlohmann::json::error_handler_t::replace);
        state_->changed.notify_all();
    }

    if (state_->io && !state_->wake_pending)
    {
        state_->wake_pending = true;
        asio::post(*state_->io, state_->drain);
    }
}

void Runtime::join()
{
    if (state_->thread.joinable())
    {
        state_->thread.join();
    }
}

bool Runtime::finished() const
{
    return state_->done.load();
}

bool Runtime::faulted() const
{
    return state_->faulted.load();
}
}

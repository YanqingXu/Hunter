// 组装单线程服务、脚本入口和有界宿主通道，不维护脚本世界的副本。
#include "core/Runtime.h"
#include "common/Types.h"
#include "core/BoundedQueue.h"
#include "core/TickClock.h"
#include "core/Session.h"
#include "core/ActionLog.h"
#include "game/World.h"
#include "storage/Storage.h"
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
    Opt<Str> terminal;
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
                discard_through_seq = received_seq;
                discard_action_seq = actions.high();
                pending_start_discarded = pending_start.has_value();

                while (auto queued = inputs.pop())
                {
                    if (queued->has_login_req())
                    {
                        reject("paused", queued->login_req().req_id());
                    }
                    else if (queued->has_start_req())
                    {
                        reject("paused", queued->start_req().req_id());
                    }
                    else if (queued->has_action_req())
                    {
                        reject_action("paused", queued->action_req());
                    }
                    else if (queued->has_hunter_req())
                    {
                        reject("paused", queued->hunter_req().req_id());
                    }
                }

                pending_seqs.clear();

                if (session == "Aborted" || stopped)
                {
                    return;
                }

                if (!commit(script->event(4, R"({"v":8,"paused":true})")))
                {
                    return;
                }

                notify_pause();

                if (session == "Aborted" || stopped)
                {
                    return;
                }

                emit(rsp("Rsp", req_id));
            }
            else if (cmd == "Resume" && state == "Ready" && session != "Aborted")
            {
                const bool was_paused = paused;
                paused = false;
                session = authenticated ? "Running" : "Idle";

                if (was_paused)
                {
                    if (!commit(script->event(4, R"({"v":8,"paused":false})")))
                    {
                        return;
                    }

                    clock.reset(TickClock::Clock::now());
                    schedule();
                }

                notify_pause();

                if (session == "Aborted" || stopped)
                {
                    return;
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
        if (storage)
        {
            auto evt = rsp("Error", req_id);
            evt["code"] = "starting";
            emit(std::move(evt));
            return;
        }

        try
        {
            const auto& cfg = shared.cfg;

            if (cfg.tick_hz != 60 || cfg.snapshot_hz != 20)
            {
                throw std::runtime_error("invalid snapshot frequency");
            }

            instance = random_hex(16);
            token = random_hex(32);
            instance_id = std::stoull(instance.substr(0, 16), nullptr, 16) | 1ULL;
            script = std::make_unique<Script>();
            const nlohmann::json ctx = {{"v", 8},
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

            storage::StorageCfg limits;
            require(cfg.max_frame_bytes >= 2048 && cfg.max_json_bytes >= 2048,
                "hunter_capacity_too_small");
            limits.max_hunter_bytes = std::min(cfg.max_frame_bytes, cfg.max_json_bytes) - 1024;
            storage = std::make_unique<storage::Storage>(io, limits, instance_id);
            const auto accepted = storage->open(cfg.storage, [this, req_id](storage::Rsp result)
            {
                if (stopped)
                {
                    return;
                }

                if (!result.result)
                {
                    start_failed(req_id, result.result.error().message);
                    return;
                }

                const auto load = storage->load_player(local_profile(),
                    [this, req_id](storage::Rsp loaded)
                    {
                        if (stopped)
                        {
                            return;
                        }

                        if (!loaded.result)
                        {
                            start_failed(req_id, loaded.result.error().message);
                            return;
                        }

                        profile = std::get<storage::PlayerSave>(std::move(*loaded.result));
                        open_transport(req_id);
                    });

                if (!load)
                {
                    start_failed(req_id, load.error().message);
                }
            });

            if (!accepted)
            {
                throw std::runtime_error(accepted.error().message);
            }
        }
        catch (const std::exception& err)
        {
            start_failed(req_id, err.what());
        }
    }

    // 本地存档 V1 的身份入口；业务层只使用实际加载的 PlayerSave 身份。
    u64 local_profile() const
    {
        return 1;
    }

    // 统一报告启动阶段失败并排空已接受的存档操作。
    void start_failed(const Str& req_id, const Str& detail)
    {
        state = "Faulted";
        auto evt = rsp("Error", req_id);
        evt["code"] = "start_failed";
        evt["detail"] = detail.substr(0, 512);
        emit(std::move(evt));
        stop("");
    }

    // 存档加载成功后才开放连接并发布 Ready，回调边界吸收异常。
    void open_transport(const Str& req_id)
    {
        try
        {
            net = std::make_unique<Transport>(io, shared.cfg,
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
            start_failed(req_id, err.what());
        }
    }

    // 发布当前端口及临时握手凭据，日志不包含令牌。
    void ready(const Str& req_id)
    {
        auto evt = rsp("Ready", req_id);
        evt["port"] = port;
        evt["instance"] = instance;
        evt["token"] = token;
        evt["protocol_version"] = 6;
        evt["content_version"] = script->world().content_key;
        emit(std::move(evt));
    }

    // 按认证与序号边界接收消息，输入仅入队而不直接执行脚本。
    void receive(wire::Envelope msg)
    {
        if (!authenticated)
        {
            const auto& hello = msg.hello();

            if (!msg.has_hello() || hello.protocol_version() != 6
                || hello.content_version() != script->world().content_key
                || hello.instance() != instance || hello.token() != token)
            {
                abort("handshake_rejected");
                return;
            }

            authenticated = true;
            binding.bind(instance_id, profile.player_id);
            net->authenticate();
            wire::Envelope reply;
            auto* ack = reply.mutable_hello_ack();
            ack->set_protocol_version(6);
            ack->set_content_version(Str(script->world().content_key));
            ack->set_instance(instance);

            if (!send_msg(reply))
            {
                return;
            }

            session = paused ? "Paused" : "Running";
            notify_pause();
            clock.reset(TickClock::Clock::now());
            schedule();
            return;
        }

        if (msg.has_save_req())
        {
            save_query(msg.save_req());
            return;
        }

        if (msg.has_hunter_req())
        {
            const auto& req = msg.hunter_req();
            if (req.req_id().empty() || req.req_id().size() > 128)
            {
                reject("invalid_request");
            }
            else if (req.kind() == wire::HunterReq::PROFILE)
            {
                hunter_query(req);
            }
            else if (paused)
            {
                reject("paused", req.req_id());
            }
            else
            {
                enqueue(std::move(msg));
            }
            return;
        }

        if (msg.has_login_req() || msg.has_start_req() || msg.has_action_req())
        {
            const auto& req = msg.has_login_req() ? msg.login_req().req_id()
                : msg.has_start_req() ? msg.start_req().req_id() : msg.action_req().req_id();

            if (req.empty() || req.size() > 128)
            {
                reject("invalid_request");
            }
            else if (msg.has_action_req())
            {
                const auto result = actions.begin(msg.action_req());
                if (result.kind == ActionLog::Kind::Rejected)
                {
                    reject(result.error, req, msg.action_req().action_seq(),
                        msg.action_req().match_id());
                }
                else if (result.kind == ActionLog::Kind::Replay)
                {
                    send_msg(*result.response);
                }
                else if (result.kind == ActionLog::Kind::Accepted)
                {
                    if (paused)
                    {
                        discard_action_seq = actions.high();
                        reject_action("paused", msg.action_req());
                    }
                    else
                    {
                        enqueue(std::move(msg));
                    }
                }
            }
            else if (paused)
            {
                reject("paused", req);
            }
            else
            {
                enqueue(std::move(msg));
            }

            return;
        }

        if (!msg.has_input() || msg.input().seq() == 0 || msg.input().match_id() == 0
            || msg.input().move_x() < -1 || msg.input().move_x() > 1
            || msg.input().move_y() < -1 || msg.input().move_y() > 1
            || msg.input().aim_x() < -1000 || msg.input().aim_x() > 1000
            || msg.input().aim_y() < -1000 || msg.input().aim_y() > 1000)
        {
            abort("invalid_input");
            return;
        }

        const auto& input = msg.input();

        if (!binding.resolve(binding.viewer().session_id, input.world_id(), input.match_id()))
        {
            reject("stale_match", "", input.seq(), input.match_id());
            return;
        }

        if (input.seq() <= received_seq)
        {
            if (last_ack && input.seq() == last_ack->ack().seq()
                && input.match_id() == last_ack->ack().match_id())
            {
                send_msg(*last_ack);
            }
            else if (!pending_seqs.contains(input.seq()))
            {
                reject(input.seq() <= discard_through_seq ? "input_discarded" : "stale_input",
                    "", input.seq(), input.match_id());
            }

            return;
        }

        received_seq = input.seq();

        if (paused)
        {
            discard_through_seq = received_seq;
            reject("paused", "", input.seq(), input.match_id());
            return;
        }

        pending_seqs.insert(input.seq());
        enqueue(std::move(msg));
    }

    // 缓存拥有数据的协议命令，在逻辑 Tick 边界处理输入或低频脚本控制。
    void enqueue(wire::Envelope msg)
    {
        const auto bytes = msg.ByteSizeLong();

        if (!inputs.push(std::move(msg), bytes))
        {
            abort("input_backpressure");
        }
    }

    // 关联正常业务拒绝，不关闭仍可处理后续合法命令的会话。
    void reject(const Str& code, const Str& req = "", u64 seq = 0, u64 match = 0)
    {
        wire::Envelope msg;
        auto* err = msg.mutable_error();
        err->set_code(code);
        err->set_req_id(req);
        err->set_seq(seq);
        err->set_match_id(match);
        send_msg(msg);
    }

    // 缓存动作的业务拒绝，暂停或失败后的重复请求不能再次执行消费逻辑。
    void reject_action(const Str& code, const wire::ActionReq& req)
    {
        wire::Envelope msg;
        auto* err = msg.mutable_error();
        err->set_code(code);
        err->set_req_id(req.req_id());
        err->set_seq(req.action_seq());
        err->set_match_id(req.match_id());
        actions.complete(req.action_seq(), msg);
        send_msg(msg);
    }

    // 认证后的连接观察宿主暂停状态以及不允许重放的输入高水位。
    void notify_pause()
    {
        if (authenticated && session != "Aborted")
        {
            wire::Envelope msg;
            msg.mutable_pause()->set_paused(paused);
            msg.mutable_pause()->set_discard_through_seq(discard_through_seq);
            msg.mutable_pause()->set_discard_action_seq(discard_action_seq);
            send_msg(msg);
        }
    }

    // 发送一条控制协议消息，无法保留关键输出时中止会话。
    bool send_msg(const wire::Envelope& msg)
    {
        return send_to(binding.viewer().session_id, msg);
    }

    // 只向指定的当前认证会话发送，失效回调不得借唯一连接误投递。
    bool send_to(u64 recipient, const wire::Envelope& msg)
    {
        if (!binding.accepts(recipient) || stopped || session == "Aborted" || !net)
        {
            return false;
        }

        auto frame = encode_frame(msg, shared.cfg.max_frame_bytes);
        if (!frame || !net->send({std::move(*frame)}))
        {
            abort("send_backpressure");
            return false;
        }

        return true;
    }

    // 校验并一次提交成功入口的全部输出，失败后不再使用脚本状态。
    bool commit(Expect<Vec<ScriptOut>, Str> out)
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

        for (auto& value : *out)
        {
            const auto& world = script->world();

            if (value.message.has_login_rsp())
            {
                auto* reply = value.message.mutable_login_rsp();
                reply->set_session_id(binding.viewer().session_id);
                reply->set_world_id(world.world_id);
                reply->set_player_entity_id(world.player_entity_id);
            }
            else if (value.message.has_start_rsp())
            {
                auto* reply = value.message.mutable_start_rsp();
                reply->set_world_id(world.world_id);
                reply->set_player_entity_id(world.player_entity_id);
                *reply->mutable_loadout() = world.get_loadout();
                reply->set_hunter_id(world.hunter_id);
                reply->set_test_mode(world.hunter_id == 0);
            }
        }

        auto frames = script_frames(*out, shared.cfg);
        if (!frames)
        {
            abort(frames.error());
            return false;
        }

        if (frames->empty())
        {
            return true;
        }

        Opt<wire::Envelope> ack;

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
            if (!script || stopped || session == "Aborted")
            {
                return;
            }

            if (TickClock::Clock::now() >= deadline)
            {
                break;
            }

            auto input = inputs.pop();
            if (!input)
            {
                break;
            }

            if (input->has_input())
            {
                const auto& cmd = input->input();
                pending_seqs.erase(cmd.seq());

                if (!binding.resolve(binding.viewer().session_id, cmd.world_id(), cmd.match_id()))
                {
                    reject("stale_match", "", cmd.seq(), cmd.match_id());
                    continue;
                }

                if (!commit(script->input(cmd, tick_id + 1)))
                {
                    return;
                }

                continue;
            }

            if (input->has_start_req())
            {
                start_match(input->start_req());
                continue;
            }

            if (input->has_hunter_req())
            {
                hunter_command(input->hunter_req());
                continue;
            }

            if (input->has_action_req())
            {
                action(input->action_req());
                continue;
            }

            nlohmann::json payload = {{"v", 8}};
            i64 event_id = 2;
            if (input->has_login_req())
            {
                event_id = 2;
                payload["req_id"] = input->login_req().req_id();
                payload["player_id"] = std::to_string(profile.player_id);
            }
            else
            {
                event_id = 3;
                payload["req_id"] = input->start_req().req_id();
                payload["after_match_id"] = std::to_string(input->start_req().after_match_id());
            }

            if (!commit(script->event(event_id, payload.dump())))
            {
                return;
            }
        }

        if (!script || stopped || session == "Aborted")
        {
            return;
        }

        ++tick_id;

        if (commit(script->tick(tick_id, 1.0 / shared.cfg.tick_hz)))
        {
            freeze_result();
        }
    }

    // 从永久档案刷新查询投影；历史操作重放不会倒退当前版本。
    void update_hunters(const nlohmann::json& value)
    {
        const auto revision = value.at("revision").get<u64>();
        if (revision < profile.revision)
        {
            return;
        }
        hunter_profile = value;
        profile.revision = revision;
        profile.items.clear();
        for (const auto& item : value.at("stash"))
        {
            profile.items.push_back({item.at("item_uid").get<u64>(),
                item.at("cfg_id").get<u32>(), item.at("count").get<i32>(),
                item.at("acquired_match_id").get<u64>()});
        }
    }

    // 返回永久操作原始响应；当前档案另由 PROFILE 显式查询。
    void hunter_reply(const wire::HunterReq& req, const storage::HunterResult& result)
    {
        if (result.result_json.size() + 512 > shared.cfg.max_frame_bytes)
        {
            reject("hunter_profile_too_large", req.req_id());
            return;
        }
        wire::Envelope reply;
        auto& value = *reply.mutable_hunter_rsp();
        value.set_req_id(req.req_id());
        value.set_op_id(req.op_id());
        value.set_revision(result.revision);
        value.set_hunter_id(result.hunter_id);
        value.set_match_id(result.match_id);
        value.set_replayed(result.replayed);
        value.set_result_json(result.result_json);
        send_msg(reply);
    }

    // 暂停期间也允许读取档案，价格和资产来源始终来自存档和脚本。
    void hunter_query(const wire::HunterReq& req)
    {
        if (!script || script->world().player_id == 0)
        {
            reject("not_logged_in", req.req_id());
            return;
        }
        const auto accepted = storage->load_hunters(profile.player_id,
            [this, req](storage::Rsp result)
            {
                if (stopped || !script)
                {
                    return;
                }
                if (!result.result)
                {
                    reject(result.result.error().message, req.req_id());
                    return;
                }
                const auto& value = std::get<storage::HunterResult>(*result.result);
                update_hunters(nlohmann::json::parse(value.result_json));
                hunter_reply(req, value);
            });
        if (!accepted)
        {
            reject(accepted.error().message, req.req_id());
        }
    }

    // 计算已授权原始意图的规范指纹，不包含可变档案或由 Lua 计算的价额。
    Str hunter_intent(const wire::HunterReq& req) const
    {
        return nlohmann::json({{"kind", static_cast<i32>(req.kind())},
            {"revision", req.revision()}, {"hunter_id", req.hunter_id()},
            {"cfg_id", req.cfg_id()}, {"skill_id", req.skill_id()},
            {"slot", req.slot()}, {"item_uid", req.item_uid()}}).dump();
    }

    // 调用只读经济规则，仅将成功且形状正确的服务端载荷交给存储。
    Expect<nlohmann::json, Str> hunter_rules(nlohmann::json value)
    {
        value["v"] = 8;
        const auto result = script->check_hunter(value.dump());
        if (!result)
        {
            return Unexpect(result.error());
        }
        const auto parsed = nlohmann::json::parse(*result, nullptr, false);
        if (!parsed.is_object() || !parsed.contains("ok") || !parsed["ok"].is_boolean())
        {
            return Unexpect(Str("invalid_hunter_rule_result"));
        }
        if (!parsed["ok"].get<bool>())
        {
            return Unexpect(parsed.value("error", Str("hunter_rule_rejected")));
        }
        if (!parsed.contains("payload") || !parsed["payload"].is_object())
        {
            return Unexpect(Str("invalid_hunter_rule_payload"));
        }
        return parsed["payload"];
    }

    // 先查持久操作，再对当前档案校验，保证退役和消费后的重发仍可得到原结果。
    void hunter_command(const wire::HunterReq& req)
    {
        if (!script || script->world().player_id == 0)
        {
            reject("not_logged_in", req.req_id());
            return;
        }
        if (req.kind() < wire::HunterReq::RECRUIT || req.kind() > wire::HunterReq::RETIRE
            || req.op_id().empty() || req.op_id().size() > 96
            || req.op_id().find('\0') != Str::npos || req.revision() == 0)
        {
            reject("invalid_hunter_request", req.req_id());
            return;
        }
        if (hunter_busy || pending_start || save_busy || script->world().phase == "Playing"
            || script->world().phase == "Settling")
        {
            reject("hunter_busy", req.req_id());
            return;
        }
        hunter_busy = true;
        const auto intent = hunter_intent(req);
        const auto found = storage->find_hunter_op(profile.player_id, "ui:" + req.op_id(), intent,
            [this, req, intent](storage::Rsp result)
            {
                if (stopped || !script)
                {
                    hunter_busy = false;
                    return;
                }
                if (result.result)
                {
                    hunter_busy = false;
                    hunter_reply(req, std::get<storage::HunterResult>(*result.result));
                    return;
                }
                if (result.result.error().code != storage::Code::NotFound)
                {
                    hunter_busy = false;
                    reject(result.result.error().message, req.req_id());
                    return;
                }
                load_hunter_command(req, intent);
            });
        if (!found)
        {
            hunter_busy = false;
            reject(found.error().message, req.req_id());
        }
    }

    // 异步读取真实档案后计算经济参数，直到提交完成保持单操作屏障。
    void load_hunter_command(const wire::HunterReq& req, const Str& intent)
    {
        const auto accepted = storage->load_hunters(profile.player_id,
            [this, req, intent](storage::Rsp result)
            {
                if (stopped || !script)
                {
                    hunter_busy = false;
                    return;
                }
                if (!result.result)
                {
                    hunter_busy = false;
                    reject(result.result.error().message, req.req_id());
                    return;
                }
                update_hunters(nlohmann::json::parse(
                    std::get<storage::HunterResult>(*result.result).result_json));
                if (paused || req.revision() != profile.revision)
                {
                    hunter_busy = false;
                    reject(paused ? "paused" : "player_revision_conflict", req.req_id());
                    return;
                }
                static constexpr const char* kinds[] = {"profile", "recruit", "equip",
                    "buy_skill", "remove_skill", "retire"};
                const Str kind = kinds[static_cast<i32>(req.kind())];
                const auto payload = hunter_rules({{"kind", kind}, {"profile", hunter_profile},
                    {"hunter_id", req.hunter_id()}, {"cfg_id", req.cfg_id()},
                    {"skill_id", req.skill_id()}, {"slot", req.slot()},
                    {"item_uid", req.item_uid()}});
                if (!payload)
                {
                    hunter_busy = false;
                    reject(payload.error(), req.req_id());
                    return;
                }
                storage::HunterReq command{profile.player_id, profile.revision, req.hunter_id(),
                    "ui:" + req.op_id(), kind, payload->dump(), intent};
                const auto submitted = storage->apply_hunter(std::move(command),
                    [this, req](storage::Rsp saved)
                    {
                        hunter_busy = false;
                        if (stopped || !script)
                        {
                            return;
                        }
                        if (!saved.result)
                        {
                            reject(saved.result.error().message, req.req_id());
                            return;
                        }
                        const auto& value = std::get<storage::HunterResult>(*saved.result);
                        update_hunters(nlohmann::json::parse(value.result_json).at("profile"));
                        hunter_reply(req, value);
                    });
                if (!submitted)
                {
                    hunter_busy = false;
                    reject(submitted.error().message, req.req_id());
                }
            });
        if (!accepted)
        {
            hunter_busy = false;
            reject(accepted.error().message, req.req_id());
        }
    }

    // 将已由只读 Lua 校验的永久配装投影为原生协议值，不填充免费默认物资。
    wire::Loadout owned_loadout(const nlohmann::json& value) const
    {
        wire::Loadout result;
        const auto cfg_id = [](const nlohmann::json& entry) -> u32
        {
            const auto id = entry.is_string() ? read_id(entry.get<Str>()) : entry.get<u64>();
            require(id > 0 && id <= 2147483647, "invalid_hunter_loadout_cfg");
            return static_cast<u32>(id);
        };
        result.set_player_cfg_id(cfg_id(value.at("player_cfg_id")));
        for (const auto& entry : value.at("health_segments"))
        {
            result.add_health_segments(entry.get<u32>());
        }
        for (const auto& entry : value.at("weapons"))
        {
            auto& weapon = *result.add_weapons();
            weapon.set_cfg_id(cfg_id(entry.at("cfg_id")));
            weapon.set_ammo_cfg_id(cfg_id(entry.at("ammo_cfg_id")));
        }
        for (const auto& entry : value.at("tools"))
        {
            result.add_tools(cfg_id(entry));
        }
        for (const auto& entry : value.at("consumables"))
        {
            result.add_consumables(cfg_id(entry));
        }
        for (const auto& entry : value.at("skills"))
        {
            result.add_skills(cfg_id(entry));
        }
        require(result.weapons_size() > 0 && result.weapons_size() <= 2
            && result.tools_size() <= 4 && result.consumables_size() <= 4
            && result.skills_size() <= 32, "invalid_hunter_loadout_capacity");
        return result;
    }

    // 恢复尚未建立世界的准备阶段，不将等待或暂停当成永久死亡。
    void reject_hunter_start(const wire::StartReq& req, const Str& phase, const Str& error)
    {
        hunter_busy = false;
        pending_start.reset();
        pending_start_discarded = false;
        if (!stopped && script)
        {
            if (!script->change([&](World& world) { world.phase = phase; }))
            {
                abort("prepare_failed");
                return;
            }
            reject(error, req.req_id());
        }
    }

    // 永久开局先读取档案并验证所有权，再原子登记基线和局号，完成后才创建世界。
    void start_hunter(const wire::StartReq& incoming)
    {
        const auto& world = script->world();
        const Str intent = nlohmann::json({{"hunter_id", incoming.hunter_id()},
            {"revision", incoming.revision()}, {"after_match_id", incoming.after_match_id()}}).dump();
        if (incoming.test_mode() || incoming.has_loadout() || incoming.revision() == 0
            || incoming.req_id().size() > 96 || world.player_id == 0)
        {
            reject("invalid_hunter_start", incoming.req_id());
            return;
        }
        if (incoming.req_id() == world.last_req)
        {
            if (intent != active_start_intent || world.hunter_id != incoming.hunter_id())
            {
                reject("request_conflict", incoming.req_id());
                return;
            }
            auto req = incoming;
            *req.mutable_loadout() = world.get_loadout();
            begin_match(req, world.match_id, world.world_id);
            return;
        }
        if (hunter_busy || pending_start || save_busy || world.phase == "Playing"
            || world.phase == "Settling" || incoming.after_match_id() != world.match_id)
        {
            reject("invalid_state", incoming.req_id());
            return;
        }
        const auto phase = world.phase;
        hunter_busy = true;
        pending_start = incoming;
        pending_start_discarded = false;
        if (!script->change([](World& value) { value.set_phase("Preparing"); }))
        {
            reject_hunter_start(incoming, phase, "prepare_failed");
            return;
        }
        const auto found = storage->find_hunter_op(profile.player_id,
            "start:" + incoming.req_id(), intent,
            [this, incoming, phase, intent](storage::Rsp prior)
            {
                if (stopped || !script)
                {
                    return;
                }
                if (prior.result || prior.result.error().code != storage::Code::NotFound)
                {
                    reject_hunter_start(incoming, phase, prior.result
                        ? "hunter_start_already_closed" : prior.result.error().message);
                    return;
                }
                load_hunter_start(incoming, phase, intent);
            });
        if (!found)
        {
            reject_hunter_start(incoming, phase, found.error().message);
        }
    }

    // 从当前永久状态构造配装，缺配置或资产不足时不分配局号。
    void load_hunter_start(const wire::StartReq& incoming, const Str& phase, const Str& intent)
    {
        const auto loaded = storage->load_hunters(profile.player_id,
            [this, incoming, phase, intent](storage::Rsp result)
            {
                if (stopped || !script)
                {
                    return;
                }
                if (!result.result)
                {
                    reject_hunter_start(incoming, phase, result.result.error().message);
                    return;
                }
                update_hunters(nlohmann::json::parse(
                    std::get<storage::HunterResult>(*result.result).result_json));
                if (paused || pending_start_discarded || incoming.revision() != profile.revision)
                {
                    reject_hunter_start(incoming, phase, paused || pending_start_discarded
                        ? "paused" : "player_revision_conflict");
                    return;
                }
                const auto payload = hunter_rules({{"kind", "start"},
                    {"profile", hunter_profile}, {"hunter_id", incoming.hunter_id()},
                    {"cfg_id", 0}, {"item_uid", 0}, {"slot", 0}, {"skill_id", 0}});
                if (!payload)
                {
                    reject_hunter_start(incoming, phase, payload.error());
                    return;
                }
                auto req = incoming;
                Map<u32, i32> counts;
                try
                {
                    *req.mutable_loadout() = owned_loadout(payload->at("loadout"));
                    for (const auto& item : payload->at("tool_counts"))
                    {
                        const auto cfg = read_id(item.at("cfg_id").get<Str>());
                        const auto count = item.at("count").get<i64>();
                        require(cfg > 0 && cfg <= 2147483647 && count >= 0 && count <= 100000,
                            "invalid_hunter_tool_count");
                        require(counts.emplace(static_cast<u32>(cfg), static_cast<i32>(count)).second,
                            "duplicate_hunter_tool");
                    }
                }
                catch (const std::exception& error)
                {
                    reject_hunter_start(incoming, phase, error.what());
                    return;
                }
                storage::HunterReq command{profile.player_id, profile.revision, req.hunter_id(),
                    "start:" + req.req_id(), "begin_raid", nlohmann::json({{"content_key",
                        script->world().content_key}}).dump(), intent};
                const auto accepted = storage->apply_hunter(std::move(command),
                    [this, req, phase, intent, counts](storage::Rsp saved)
                    {
                        if (stopped || !script)
                        {
                            return;
                        }
                        if (!saved.result)
                        {
                            reject_hunter_start(req, phase, saved.result.error().message);
                            return;
                        }
                        const auto& value = std::get<storage::HunterResult>(*saved.result);
                        update_hunters(nlohmann::json::parse(value.result_json).at("profile"));
                        if (paused || pending_start_discarded)
                        {
                            cancel_hunter_start(req, phase, value.match_id);
                            return;
                        }
                        hunter_busy = false;
                        pending_start.reset();
                        pending_start_discarded = false;
                        if (!script->change([&](World& world) { world.phase = phase; }))
                        {
                            abort("prepare_failed");
                            return;
                        }
                        active_hunter = req.hunter_id();
                        active_start_intent = intent;
                        hunter_tool_counts = counts;
                        begin_match(req, value.match_id, ++next_world);
                    });
                if (!accepted)
                {
                    reject_hunter_start(incoming, phase, accepted.error().message);
                }
            });
        if (!loaded)
        {
            reject_hunter_start(incoming, phase, loaded.error().message);
        }
    }

    // 暂停作废已登记开局时先解除持久占用；异常退出仍由下次启动回退。
    void cancel_hunter_start(const wire::StartReq& req, const Str& phase, u64 match)
    {
        storage::HunterReq command{profile.player_id, profile.revision, req.hunter_id(),
            "cancel:" + std::to_string(match), "recover_raid",
            nlohmann::json({{"match_id", match}}).dump(), {}};
        const auto accepted = storage->apply_hunter(std::move(command),
            [this, req, phase](storage::Rsp result)
            {
                if (result.result)
                {
                    const auto& value = std::get<storage::HunterResult>(*result.result);
                    update_hunters(nlohmann::json::parse(value.result_json).at("profile"));
                }
                reject_hunter_start(req, phase,
                    result.result ? "paused" : result.result.error().message);
            });
        if (!accepted)
        {
            reject_hunter_start(req, phase, accepted.error().message);
        }
    }

    // 在 Tick 边界申请持久局号，同请求只保留一次分配，完成后才创建世界。
    void start_match(const wire::StartReq& incoming)
    {
        if (incoming.hunter_id() != 0)
        {
            start_hunter(incoming);
            return;
        }
        if (!incoming.test_mode() || incoming.revision() != 0 || hunter_busy)
        {
            reject("invalid_test_start", incoming.req_id());
            return;
        }
        const auto& world = script->world();
        auto req = incoming;

        const auto loadout = script->check_loadout(incoming.loadout());
        if (!loadout)
        {
            reject(loadout.error(), req.req_id());
            return;
        }

        *req.mutable_loadout() = *loadout;

        if (world.player_id == 0)
        {
            reject("not_logged_in", req.req_id());
            return;
        }

        if (pending_start)
        {
            if (pending_start->req_id() != req.req_id()
                || pending_start->after_match_id() != req.after_match_id()
                || pending_start->loadout().SerializeAsString()
                    != req.loadout().SerializeAsString())
            {
                reject(pending_start->req_id() == req.req_id()
                    ? "request_conflict" : "start_pending", req.req_id());
            }

            return;
        }

        if (req.req_id() == world.last_req)
        {
            if (req.after_match_id() != world.last_after
                || req.loadout().SerializeAsString() != world.get_loadout().SerializeAsString())
            {
                reject("request_conflict", req.req_id());
                return;
            }

            begin_match(req, world.match_id, world.world_id);
            return;
        }

        if (req.after_match_id() != world.match_id)
        {
            reject("stale_match", req.req_id());
            return;
        }

        if (world.phase == "Playing" || world.phase == "Settling" || save_busy)
        {
            reject("invalid_state", req.req_id());
            return;
        }

        pending_start = req;
        pending_start_discarded = false;
        const auto previous_phase = world.phase;

        if (!script->change([](World& value) { value.set_phase("Preparing"); }))
        {
            abort("prepare_failed");
            return;
        }

        const auto accepted = storage->alloc_match([this, req, previous_phase](storage::Rsp result)
        {
            const bool discarded = pending_start_discarded;
            pending_start.reset();
            pending_start_discarded = false;

            if (stopped || !script || session == "Aborted")
            {
                return;
            }

            if (!script->change([&](World& value) { value.phase = previous_phase; }))
            {
                abort("prepare_failed");
                return;
            }

            if (!result.result)
            {
                reject("alloc_failed", req.req_id());
                return;
            }

            if (paused || discarded)
            {
                reject("paused", req.req_id());
                return;
            }

            const auto match = std::get<storage::MatchId>(*result.result).value;
            begin_match(req, match, ++next_world);
        });

        if (!accepted)
        {
            pending_start.reset();

            if (!script->change([&](World& value) { value.phase = previous_phase; }))
            {
                abort("prepare_failed");
                return;
            }

            reject("alloc_failed", req.req_id());
        }
    }

    // 通过受版本约束的脚本入口创建对局，再关联接收者与世界身份。
    void begin_match(const wire::StartReq& req, u64 match, u64 world_id)
    {
        const auto previous = script->world().match_id;
        if (previous != match && !script->change([&](World& world)
            { world.hunter_id = req.hunter_id(); world.prepare_loadout(req.loadout()); }))
        {
            abort("loadout_prepare_failed");
            return;
        }

        const nlohmann::json payload = {{"v", 8}, {"req_id", req.req_id()},
            {"after_match_id", std::to_string(req.after_match_id())},
            {"match_id", std::to_string(match)}, {"world_id", std::to_string(world_id)}};

        auto output = script->event(3, payload.dump());
        if (output && previous != match && req.hunter_id() != 0)
        {
            const auto changed = script->change([&](World& world)
            {
                auto& player = std::get<Player>(
                    world.actors[world.slot(world.player_entity_id)]->value);
                for (auto& tool : player.tools)
                {
                    if (tool.cfg_id != 0)
                    {
                        const auto found = hunter_tool_counts.find(tool.cfg_id);
                        require(found != hunter_tool_counts.end() && found->second <= tool.count,
                            "invalid_hunter_tool_count");
                        tool.count = found->second;
                    }
                }
            });
            if (!changed)
            {
                abort(changed.error());
                return;
            }
            for (auto& value : *output)
            {
                if (value.message.has_snapshot())
                {
                    value.message = script->world().snapshot(profile.player_id);
                }
            }
        }
        if (!commit(std::move(output)))
        {
            return;
        }

        const auto& world = script->world();

        if (world.match_id != previous)
        {
            binding.enter(world.world_id, world.match_id);
            frozen.reset();
            frozen_hunter.reset();
            saved.reset();
            save_state = "Idle";
            save_error.clear();
        }
    }

    // 原生低频操作仅在 Tick 边界执行，客户端不能提交结果或奖励列表。
    void action(const wire::ActionReq& req)
    {
        const auto ctx = binding.resolve(binding.viewer().session_id,
            req.world_id(), req.match_id());

        if (!ctx || ctx->match_id == 0)
        {
            reject_action("stale_match", req);
            return;
        }

        if (req.slot() > 8)
        {
            reject_action("invalid_slot", req);
            return;
        }

        if (!script->change([&](World& world) { world.action_seq = req.action_seq(); }))
        {
            abort("action_watermark_failed");
            return;
        }

        const i32 kind = static_cast<i32>(req.kind());
        if (kind < wire::ActionReq::BAG || kind > wire::ActionReq::VISION_OFF)
        {
            reject_action("invalid_request", req);
            return;
        }

        static constexpr const char* names[] = {"bag", "pickup", "abandon", "switch_weapon",
            "select_tool", "melee", "use", "interact", "revive", "vision_on", "vision_off"};
        const auto target = req.kind() == wire::ActionReq::PICKUP
            ? req.item_id() : req.target_id();
        const nlohmann::json payload = {{"v", 8}, {"req_id", req.req_id()},
            {"kind", names[kind]}, {"slot", req.slot()},
            {"target_id", std::to_string(target)},
            {"action_seq", std::to_string(req.action_seq())},
            {"death_seq", std::to_string(req.death_seq())}};
        auto output = script->event(5, payload.dump());
        Opt<wire::Envelope> response;

        if (output)
        {
            for (const auto& value : *output)
            {
                if ((value.message.has_action_rsp()
                        && value.message.action_rsp().req_id() == req.req_id())
                    || (value.message.has_error()
                        && value.message.error().req_id() == req.req_id()))
                {
                    if (response)
                    {
                        abort("duplicate_action_response");
                        return;
                    }

                    response = value.message;
                }
            }
        }

        if (!commit(std::move(output)))
        {
            return;
        }

        if (!response)
        {
            abort("missing_action_response");
            return;
        }

        actions.complete(req.action_seq(), *response);

        if (script && req.kind() <= wire::ActionReq::ABANDON)
        {
            send_to(ctx->session_id, script->world().snapshot(ctx->player_id));
            freeze_result();
        }
    }

    // 成功玩法入口之后一次冻结奖励；持久化线程不读取世界或脚本对象。
    void freeze_result()
    {
        if (!script || frozen || script->world().phase != "Settling")
        {
            return;
        }

        const auto& world = script->world();
        storage::CommitMatch req;
        req.match_id = world.match_id;
        req.player_id = world.player_id;
        req.expected_revision = profile.revision;
        req.outcome = world.raid.player_state;
        req.content_key = Str(script->world().content_key);

        if (world.hunter_id != 0)
        {
            frozen = req;
            const auto snapshot = script->export_state();
            if (!snapshot)
            {
                save_failed({storage::Code::Invalid, 0, false, snapshot.error()});
                return;
            }
            const auto payload = hunter_rules({{"kind", "finish_raid"},
                {"profile", hunter_profile}, {"hunter_id", world.hunter_id},
                {"cfg_id", 0}, {"item_uid", 0}, {"slot", 0}, {"skill_id", 0},
                {"state", nlohmann::json::parse(*snapshot)}});
            if (!payload)
            {
                save_failed({storage::Code::Invalid, 0, false, payload.error()});
                return;
            }
            frozen_hunter = storage::HunterReq{profile.player_id, profile.revision,
                world.hunter_id, "finish:" + std::to_string(world.match_id), "finish_raid",
                payload->dump(), {}};
            submit_result();
            return;
        }

        if (req.outcome == "Extracted")
        {
            for (const auto& entry : world.items)
            {
                if (entry && entry->value.place == "Bag"
                    && entry->value.owner_player_id == req.player_id)
                {
                    req.items.push_back({entry->value.cfg_id, entry->value.count});
                }
            }
        }

        frozen = std::move(req);
        submit_result();
    }

    // 区分受理、失败和提交未知状态，保留同一冻结请求供查询与重试。
    void save_failed(const storage::Error& error)
    {
        save_busy = false;
        save_state = error.commit_unknown ? "Unknown" : "Failed";
        save_error = error.message.substr(0, 128);
        notify_save("");
    }

    // 使用已提交记录更新永久视图；重放结果不能重复附加物品。
    void committed(storage::MatchResult result, bool refreshed = false)
    {
        try
        {
            const auto doc = nlohmann::json::parse(result.result_json);
            if (doc.at("player_id").get<u64>() != profile.player_id)
            {
                throw std::runtime_error("result_player_mismatch");
            }

            if (doc.at("v") == 2 && !refreshed)
            {
                const auto loaded = storage->load_hunters(profile.player_id,
                    [this, result](storage::Rsp value) mutable
                    {
                        if (!value.result)
                        {
                            save_failed(value.result.error());
                            return;
                        }
                        update_hunters(nlohmann::json::parse(
                            std::get<storage::HunterResult>(*value.result).result_json));
                        committed(std::move(result), true);
                    });
                if (!loaded)
                {
                    save_failed(loaded.error());
                }
                return;
            }

            if (profile.revision < result.revision)
            {
                for (const auto& item : doc.at("items"))
                {
                    profile.items.push_back({item.at("item_uid").get<u64>(),
                        item.at("cfg_id").get<u32>(), item.at("count").get<i32>(),
                        result.match_id});
                }

                profile.revision = result.revision;
                profile.last_match_id = result.match_id;
            }

            saved = std::move(result);
            profile.last_match_id = saved->match_id;
            active_hunter = 0;
            save_busy = false;
            save_state = "Committed";
            save_error.clear();

            if (script && !stopped && script->world().match_id == saved->match_id)
            {
                const auto changed = script->change([](World& world)
                {
                    world.set_phase("Finished");
                });

                if (!changed)
                {
                    abort("finish_failed");
                    return;
                }
            }

            notify_save("");
        }
        catch (const std::exception& error)
        {
            save_failed({storage::Code::Internal, 0, true, error.what()});
        }
    }

    // 提交完全相同的拥有型请求，Accepted 仅表示进入保存中。
    void submit_result()
    {
        if (!frozen || save_busy || stopped)
        {
            return;
        }

        save_busy = true;
        save_state = "Saving";
        save_error.clear();
        if (active_hunter != 0 || frozen_hunter)
        {
            if (!frozen_hunter)
            {
                save_failed({storage::Code::Invalid, 0, false, "hunter_result_not_validated"});
                return;
            }
            const auto accepted = storage->apply_hunter(*frozen_hunter,
                [this](storage::Rsp result)
                {
                    if (!result.result)
                    {
                        save_failed(result.result.error());
                        return;
                    }
                    const auto& value = std::get<storage::HunterResult>(*result.result);
                    const auto doc = nlohmann::json::parse(value.result_json);
                    update_hunters(doc.at("profile"));
                    committed({value.match_id, value.revision, value.replayed,
                        doc.at("result").dump()}, true);
                });
            if (!accepted)
            {
                save_failed(accepted.error());
                return;
            }
            notify_save("");
            return;
        }
        const auto accepted = storage->commit_match(*frozen, [this](storage::Rsp result)
        {
            if (result.result)
            {
                committed(std::get<storage::MatchResult>(std::move(*result.result)));
            }
            else
            {
                save_failed(result.result.error());
            }
        });

        if (!accepted)
        {
            save_failed(accepted.error());
            return;
        }

        notify_save("");
    }

    // 输出明确保存状态，不以请求受理冒充奖励到账。
    void notify_save(const Str& req)
    {
        wire::Envelope reply;
        auto& value = *reply.mutable_save_rsp();
        value.set_req_id(req);
        value.set_match_id(frozen ? frozen->match_id : profile.last_match_id);
        value.set_state(save_state);
        value.set_revision(profile.revision);
        value.set_last_match_id(profile.last_match_id);
        value.set_error_code(save_error);

        if (saved)
        {
            value.set_result_json(saved->result_json);
        }

        send_msg(reply);
    }

    // 保存查询不依赖模拟 Tick，暂停期间仍能查询、分页和确认未知提交。
    void save_query(const wire::SaveReq& req)
    {
        if (req.req_id().empty() || req.req_id().size() > 128 || !script
            || script->world().player_id == 0)
        {
            reject("invalid_request", req.req_id());
            return;
        }

        if (req.kind() == wire::SaveReq::STATUS)
        {
            notify_save(req.req_id());
            return;
        }

        if (req.kind() == wire::SaveReq::STASH)
        {
            stash(req);
            return;
        }

        if (req.kind() == wire::SaveReq::RETRY)
        {
            if (!frozen || req.match_id() != frozen->match_id || save_busy)
            {
                reject("invalid_state", req.req_id());
                return;
            }

            if (save_state == "Committed")
            {
                notify_save(req.req_id());
                return;
            }
        }
        else if (req.kind() != wire::SaveReq::RESULT || req.match_id() == 0)
        {
            reject("invalid_request", req.req_id());
            return;
        }

        const bool retry = req.kind() == wire::SaveReq::RETRY;
        if (retry)
        {
            save_busy = true;
        }

        const auto recipient = binding.viewer().session_id;
        const auto accepted = storage->find_match(req.match_id(),
            [this, req, retry, recipient](storage::Rsp result)
            {
                if (retry)
                {
                    save_busy = false;
                }

                if (!result.result)
                {
                    if (retry && result.result.error().code == storage::Code::NotFound)
                    {
                        submit_result();
                        notify_save(req.req_id());
                    }
                    else if (retry)
                    {
                        save_failed(result.result.error());
                        notify_save(req.req_id());
                    }
                    else if (binding.accepts(recipient))
                    {
                        reject(result.result.error().code == storage::Code::NotFound
                            ? "result_not_found" : "save_query_failed", req.req_id());
                    }

                    return;
                }

                auto value = std::get<storage::MatchResult>(std::move(*result.result));
                const auto doc = nlohmann::json::parse(value.result_json, nullptr, false);
                if (!doc.is_object() || !doc.contains("player_id")
                    || !doc.at("player_id").is_number_integer()
                    || doc.at("player_id").get<u64>() != profile.player_id)
                {
                    if (binding.accepts(recipient))
                    {
                        reject("result_not_found", req.req_id());
                    }

                    return;
                }

                if (retry)
                {
                    committed(std::move(value));
                    notify_save(req.req_id());
                    return;
                }

                wire::Envelope reply;
                auto& out = *reply.mutable_save_rsp();
                out.set_req_id(req.req_id());
                out.set_match_id(value.match_id);
                out.set_state("Committed");
                out.set_result_json(value.result_json);
                out.set_revision(value.revision);
                out.set_last_match_id(profile.last_match_id);
                send_to(recipient, reply);
            });

        if (!accepted)
        {
            if (retry)
            {
                save_failed(accepted.error());
            }

            reject("save_busy", req.req_id());
        }
    }

    // 按稳定 UID 顺序分页永久仓库，revision 变化时拒绝继续旧页面。
    void stash(const wire::SaveReq& req)
    {
        const usize limit = req.limit() == 0 ? 128 : req.limit();
        if (limit > 128 || req.cursor() > profile.items.size()
            || (req.cursor() > 0 && req.revision() == 0)
            || (req.revision() != 0 && req.revision() != profile.revision))
        {
            reject("stale_revision_or_page", req.req_id());
            return;
        }

        wire::Envelope reply;
        auto& out = *reply.mutable_save_rsp();
        out.set_req_id(req.req_id());
        out.set_state("Committed");
        out.set_revision(profile.revision);
        out.set_last_match_id(profile.last_match_id);
        const auto end = std::min<usize>(profile.items.size(),
            static_cast<usize>(req.cursor()) + limit);

        for (usize index = static_cast<usize>(req.cursor()); index < end; ++index)
        {
            const auto& item = profile.items[index];
            auto& value = *out.add_items();
            value.set_item_uid(item.item_uid);
            value.set_cfg_id(item.cfg_id);
            value.set_count(static_cast<u32>(item.count));
            value.set_acquired_match_id(item.acquired_match_id);
        }

        out.set_next_cursor(end < profile.items.size() ? end : 0);
        send_msg(reply);
    }

    // 终止当前会话并清空输入，不在新连接上恢复旧局。
    void abort(Str reason)
    {
        if (session == "Aborted" || stopped)
        {
            return;
        }

        session = "Aborted";
        binding.clear();
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

        const auto done = [this, req_id, had_fault, closed](storage::Rsp)
        {
            state = had_fault || !closed || shared.faulted ? "Faulted" : "Stopped";
            emit(rsp("Stopped", req_id));
            guard.reset();
        };

        if (storage)
        {
            const auto accepted = storage->stop(done);
            if (!accepted)
            {
                shared.faulted = true;
                emit({{"type", "Diagnostic"}, {"code", "storage_stop_rejected"}});
                done({});
            }
        }
        else
        {
            done({});
        }
    }

    State& shared;
    asio::io_context io{1};
    asio::steady_timer timer;
    asio::executor_work_guard<asio::io_context::executor_type> guard;
    TickClock clock;
    BoundedQueue<wire::Envelope> inputs;
    Set<u64> pending_seqs;
    UPtr<Script> script;
    UPtr<Transport> net;
    UPtr<storage::Storage> storage;
    storage::PlayerSave profile;
    nlohmann::json hunter_profile;
    bool hunter_busy = false;
    u64 active_hunter = 0;
    Str active_start_intent;
    Map<u32, i32> hunter_tool_counts;
    Session binding;
    ActionLog actions;
    u64 instance_id = 0;
    u64 next_world = 0;
    Opt<wire::StartReq> pending_start;
    bool pending_start_discarded = false;
    Opt<storage::CommitMatch> frozen;
    Opt<storage::HunterReq> frozen_hunter;
    Opt<storage::MatchResult> saved;
    Str save_state = "Idle";
    Str save_error;
    bool save_busy = false;
    Opt<wire::Envelope> last_ack;
    Str state = "Starting";
    Str session = "Idle";
    Str instance;
    Str token;
    u16 port = 0;
    u64 tick_id = 0;
    u64 received_seq = 0;
    u64 discard_through_seq = 0;
    u64 discard_action_seq = 0;
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

Opt<Str> Runtime::next_evt(u32 timeout_ms)
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

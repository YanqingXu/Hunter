// 把真实 Luax 的线程、预算、签名授权与输出事务收敛到同步脚本会话。
#include "common/Types.h"
#include "script/Script.h"
#include "script/Schema.h"
#include "script/Objects.h"
#include "SchemaSpec.h"

#include <luax/Runtime.hpp>
#include <luax/bind/Bind.hpp>
#include <nlohmann/json.hpp>

#include <array>
#include <charconv>
#include <chrono>
#include <cmath>
#include <fstream>
#include <limits>
#include <optional>
#include <thread>
#include <utility>

namespace hunter
{
namespace
{

// 在文件分配前检查上限，拒绝缺失或读取不完整的制品。
std::expected<Str, Str> read_file(const Str& path, usize limit)
{
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    const auto size = stream.tellg();
    if (!stream || size < 0 || static_cast<u64>(size) > limit)
    {
        return std::unexpected("artifact_read_failed: " + path);
    }

    Str bytes(static_cast<usize>(size), '\0');
    stream.seekg(0);
    stream.read(bytes.data(), static_cast<std::streamsize>(bytes.size()));

    if (!stream)
    {
        return std::unexpected("artifact_read_incomplete: " + path);
    }

    return bytes;
}

// 将应用错误转为 Bind 可传播的受保护执行错误。
luax::Error host_error(const Str& msg)
{
    return {luax::ErrorCode::invalid_argument, luax::ErrorPhase::execution, msg};
}

// 读取有限长度 JSON，明确拒绝解析失败或非对象结果。
bool json_object(const Str& text, usize limit)
{
    if (text.size() > limit)
    {
        return false;
    }

    return nlohmann::json::parse(text, nullptr, false).is_object();
}

// 严格读取固定长度的十六进制公钥和身份摘要。
template <usize N>
bool read_hex(const nlohmann::json& value, std::array<std::byte, N>& bytes)
{
    if (!value.is_string())
    {
        return false;
    }

    const auto& text = value.get_ref<const Str&>();

    if (text.size() != N * 2)
    {
        return false;
    }

    for (usize index = 0; index < N; ++index)
    {
        u32 byte = 0;
        const char* pos = text.data() + index * 2;
        const auto parsed = std::from_chars(pos, pos + 2, byte, 16);
        if (parsed.ec != std::errc{} || parsed.ptr != pos + 2)
        {
            return false;
        }

        bytes[index] = static_cast<std::byte>(byte);
    }

    return true;
}

// 独立授权文件冻结公钥、epoch 和全部兼容性身份，不从 Bundle 自授信任。
std::expected<luax::ProductionBundlePolicy, Str> read_policy(const Str& path)
{
    auto text = read_file(path, 1024 * 1024);
    if (!text)
    {
        return std::unexpected(text.error());
    }

    const auto obj = nlohmann::json::parse(*text, nullptr, false);
    if (!obj.is_object() || !obj.contains("v") || obj["v"] != 1 ||
        !obj.contains("identities") ||
        !obj["identities"].is_object() || !obj.contains("public_key") ||
        !obj.contains("build_epoch") || !obj["build_epoch"].is_number_unsigned())
    {
        return std::unexpected("invalid_bundle_policy");
    }

    luax::ProductionBundlePolicy policy;

    if (!read_hex(obj["public_key"], policy.publicKey))
    {
        return std::unexpected("invalid_bundle_public_key");
    }

    policy.epoch = obj["build_epoch"].get<u64>();
    auto& compat = policy.compatibility;
    const std::array versions{
        luax::currentBundleCompatibilityVersion, luax::currentBytecodeCompatibilityVersion,
        luax::currentCompilerCompatibilityVersion, luax::currentRuntimeCompatibilityVersion,
        luax::languageProfileId(luax::LanguageProfile::yan_game_strict_v1),
        luax::currentHostApiCompatibilityVersion, luax::currentCapabilityManifestVersion,
        luax::currentStateSchemaVersion, luax::currentEffectSchemaVersion};
    const std::array names{"bundle", "bytecode", "compiler", "runtime", "language",
        "host_api", "capability", "state", "effect"};
    const std::array fields{&compat.bundle, &compat.bytecode, &compat.compiler, &compat.runtime,
        &compat.language, &compat.hostApi, &compat.capabilities, &compat.stateSchema,
        &compat.effectSchema};
    const auto& ids = obj["identities"];
    const std::array expected{schema::host_api_hash, schema::capability_hash,
        schema::state_hash, schema::effect_hash};

    for (usize index = 0; index < expected.size(); ++index)
    {
        if (!ids.contains(names[index + 5]) || ids[names[index + 5]] != expected[index])
        {
            return std::unexpected("host_contract_mismatch: " + Str(names[index + 5]));
        }
    }

    for (usize index = 0; index < names.size(); ++index)
    {
        fields[index]->version = versions[index];

        if (!ids.contains(names[index]) || !read_hex(ids[names[index]], fields[index]->hash))
        {
            return std::unexpected("invalid_bundle_identity: " + Str(names[index]));
        }
    }

    return policy;
}

}

struct Script::Impl
{
    const std::thread::id owner = std::this_thread::get_id();
    Cfg cfg;
    World world;
    u64 snapshot_every = 3;
    UPtr<luax::Runtime> runtime;
    luax::Isolate isolate;
    luax::ModuleHandle module;
    Map<Str, luax::FunctionHandle> funcs;
    Vec<ScriptOut> pending;
    Vec<Str> logs;
    Str ctx_json;
    Str host_fault;
    Str close_error;
    usize output_bytes = 0;
    usize log_bytes = 0;
    bool busy = false;
    bool alive = false;
    bool allow_output = false;

    // 只读线程标识先于任何 VM 或可变会话字段访问。
    std::expected<void, Str> enter() const
    {
        if (owner != std::this_thread::get_id())
        {
            return std::unexpected("wrong_thread");
        }

        if (busy)
        {
            return std::unexpected("reentrant_call");
        }

        if (!alive)
        {
            return std::unexpected("stale_session");
        }

        return {};
    }

    // 每个入口获得独立的执行预算和墙钟截止时间。
    luax::ExecutionOptions options() const
    {
        luax::ExecutionOptions result;
        result.limits.instructionBudget = cfg.instruction_budget;
        result.limits.nativeWorkBudget = cfg.native_work_budget;
        result.limits.finalizerBudgetPerDrain = cfg.native_work_budget;
        result.limits.deadline = std::chrono::steady_clock::now() +
            std::chrono::milliseconds(cfg.script_deadline_ms);
        return result;
    }

    // 入口失败后丢弃输出，防止继续操作已经部分更新的状态。
    Str fail(Str error)
    {
        if (error.find("memory") != Str::npos && isolate.valid())
        {
            const auto metrics = isolate.metricsSnapshot();
            if (metrics)
            {
                const auto& memory = metrics->memory.generation;
                error += " live=" + std::to_string(memory.liveBytes)
                    + " peak=" + std::to_string(memory.peakBytes)
                    + " rejected=" + std::to_string(memory.lastRejectedRequestBytes)
                    + " gc=" + std::to_string(metrics->gc.completedCycles);

                for (const auto& category : memory.categories)
                {
                    error += " " + Str(luax::memoryCategoryName(category.category)) + "="
                        + std::to_string(category.liveBytes);
                }
            }
        }

        pending.clear();
        output_bytes = 0;
        alive = false;
        busy = false;
        return error.substr(0, 4096);
    }

    // 对原生输出计费并暂存，外部副作用只能在入口成功后提交。
    luax::Result<luax::HostStep> stage(luax::HostCallContext& ctx, ScriptOut out)
    {
        const auto bytes = out.message.ByteSizeLong();
        if (!allow_output || !validate_output(out, cfg.max_frame_bytes)
            || pending.size() >= cfg.max_outputs || bytes > cfg.max_output_bytes - output_bytes)
        {
            host_fault = "output_rejected";
            return std::unexpected(host_error(host_fault));
        }

        auto work = ctx.consumeNativeWork(bytes);
        auto charge = ctx.consumeEffectBytes(bytes);
        if (!work || !charge)
        {
            host_fault = "output_budget_exhausted";
            return std::unexpected(host_error(host_fault));
        }

        output_bytes += bytes;
        pending.push_back(std::move(out));
        return luax::HostStep::completed({luax::OwnedValue::boolean(true)});
    }

    // 注册有界网络请求及原生对象，命名空间不允许脚本改写。
    luax::Status register_hosts()
    {
        auto result = register_objects(world, isolate, module);
        if (!result)
        {
            return result;
        }

        luax::HostNamespace net("net");
        net.add(luax::HostNamespaceEntry::function("emit", luax::HostFunction(
            [this](luax::HostCallContext& ctx,
                std::span<const luax::ValueView> args) -> luax::Result<luax::HostStep>
            {
                if (args.size() != 2 || !args[0].stringIf() || !args[1].stringIf()
                    || args[1].stringIf()->size() > cfg.max_json_bytes)
                {
                    host_fault = "invalid_emit_arguments";
                    return std::unexpected(host_error(host_fault));
                }

                auto work = ctx.consumeNativeWork(args[0].stringIf()->size()
                    + args[1].stringIf()->size());

                if (!work)
                {
                    host_fault = "native_work_exhausted";
                    return std::unexpected(work.error());
                }

                return stage(ctx, ScriptOut(Str(*args[0].stringIf()), Str(*args[1].stringIf())));
            })));
        net.add(luax::HostNamespaceEntry::function("event", luax::HostFunction(
            [this](luax::HostCallContext& ctx,
                std::span<const luax::ValueView> args) -> luax::Result<luax::HostStep>
            {
                if (args.size() != 6 || !args[0].stringIf() || !args[1].stringIf()
                    || !args[2].stringIf() || !args[3].integerIf() || !args[4].integerIf()
                    || !args[5].integerIf() || !allow_output)
                {
                    host_fault = "invalid_event_arguments";
                    return std::unexpected(host_error(host_fault));
                }

                try
                {
                    return stage(ctx, ScriptOut(world.event(Str(*args[0].stringIf()),
                        Str(*args[1].stringIf()), Str(*args[2].stringIf()), *args[3].integerIf(),
                        *args[4].integerIf(), *args[5].integerIf())));
                }
                catch (const std::exception&)
                {
                    host_fault = "invalid_event_value";
                    return std::unexpected(host_error(host_fault));
                }
            })));
        net.add(luax::HostNamespaceEntry::function("snapshot", luax::HostFunction(
            [this](luax::HostCallContext& ctx,
                std::span<const luax::ValueView> args) -> luax::Result<luax::HostStep>
            {
                if (!args.empty())
                {
                    host_fault = "invalid_snapshot_arguments";
                    return std::unexpected(host_error(host_fault));
                }

                return stage(ctx, ScriptOut(world.snapshot(world.player_id)));
            })));
        result = isolate.registerHostNamespace(module, std::move(net));

        if (!result)
        {
            return result;
        }

        luax::HostNamespace cfg_space("cfg");
        cfg_space.add(luax::HostNamespaceEntry::function("get", luax::bind::function(
            [this]() -> Str
            {
                return ctx_json;
            })));
        result = isolate.registerHostNamespace(module, std::move(cfg_space));

        if (!result)
        {
            return result;
        }

        luax::HostNamespace diagnostics("diagnostics");
        diagnostics.add(luax::HostNamespaceEntry::function("log", luax::bind::function(
            [this](Str msg) -> luax::Result<bool>
            {
                if (logs.size() >= cfg.max_outputs || msg.size() > cfg.max_json_bytes - log_bytes)
                {
                    return std::unexpected(host_error("diagnostic_capacity"));
                }

                log_bytes += msg.size();
                logs.push_back(std::move(msg));
                return true;
            })));
        return isolate.registerHostNamespace(module, std::move(diagnostics));
    }

    // 对所有具名同步入口统一执行类型转换、预算和失败终止。
    template <typename Result, typename... Args>
    std::expected<Result, Str> invoke(const Str& name, bool output, Args&&... args)
    {
        auto guard = enter();
        if (!guard)
        {
            return std::unexpected(guard.error());
        }

        busy = true;
        allow_output = output;
        world.access.writable = output || name == "import_state" || name == "shutdown";
        pending.clear();
        output_bytes = 0;
        host_fault.clear();
        const auto result = luax::bind::callAs<Result>(isolate, funcs.at(name), options(),
            std::forward<Args>(args)...);
        busy = false;
        allow_output = false;
        world.access.writable = false;

        if (!result)
        {
            return std::unexpected(fail(name + ": " + Str(result.error().message())));
        }

        if (!host_fault.empty())
        {
            return std::unexpected(fail(host_fault));
        }

        return *result;
    }

    // 成功返回 true 后再验证并移交整批输出。
    std::expected<Vec<ScriptOut>, Str> commit(std::expected<bool, Str> result)
    {
        if (!result)
        {
            return std::unexpected(result.error());
        }

        if (!*result)
        {
            return std::unexpected(fail("script_entry_rejected"));
        }

        for (const auto& out : pending)
        {
            const auto decoded = validate_output(out, cfg.max_frame_bytes);
            if (!decoded)
            {
                return std::unexpected(fail(decoded.error()));
            }
        }

        return std::move(pending);
    }

    // 逐阶段记录有界退出错误，不因先前错误跳过后续资源关闭。
    void record_close_error(const Str& code, const Str& detail = "")
    {
        if (close_error.size() >= 4096)
        {
            return;
        }

        const Str msg = (close_error.empty() ? "" : "; ") + code +
            (detail.empty() ? "" : ": " + detail);
        close_error.append(msg, 0, 4096 - close_error.size());
    }
};

Script::Script() : impl_(std::make_unique<Impl>())
{
}

Script::~Script()
{
    static_cast<void>(shutdown("destroy"));
}

std::expected<Vec<ScriptOut>, Str> Script::open(const Cfg& cfg, const Str& ctx_json)
{
    auto& self = *impl_;

    if (self.owner != std::this_thread::get_id())
    {
        return std::unexpected("wrong_thread");
    }

    if (self.runtime || self.busy || !json_object(ctx_json, cfg.max_json_bytes))
    {
        return std::unexpected("invalid_open_state_or_context");
    }

    self.cfg = cfg;
    self.ctx_json = ctx_json;
    self.close_error.clear();
    self.host_fault.clear();
    self.logs.clear();
    self.log_bytes = 0;
    try
    {
        self.world.reset();
        const auto ctx = nlohmann::json::parse(ctx_json);
        if (ctx.contains("content"))
        {
            require(ctx.at("v") == 6 && ctx.size() == 3, "invalid_context_version");
            self.snapshot_every = ctx.at("snapshot_every").get<u64>();
            require(self.snapshot_every >= 1 && self.snapshot_every <= 3600,
                "invalid_snapshot_frequency");
            self.world.configure(ctx.at("content"));
        }
    }
    catch (const std::exception& error)
    {
        return std::unexpected(self.fail(error.what()));
    }

    luax::RuntimeConfig rt_cfg;
    rt_cfg.maxIsolates = 1;
    rt_cfg.maxModulesPerIsolate = 1;
    rt_cfg.maxArgumentStringBytes = cfg.max_json_bytes;
    rt_cfg.maxResultStringBytes = cfg.max_json_bytes;
    rt_cfg.hostBoundary.effectBytes = cfg.max_output_bytes;
    rt_cfg.memory.generation.hardLimitBytes = static_cast<usize>(cfg.script_memory_bytes);
    rt_cfg.memory.module.hardLimitBytes = static_cast<usize>(cfg.script_memory_bytes);
    rt_cfg.memory.invocation.hardLimitBytes = static_cast<usize>(cfg.script_memory_bytes);
#if HUNTER_PRODUCTION
    auto policy = read_policy(cfg.policy_path);
    if (!policy)
    {
        return std::unexpected(policy.error());
    }

    rt_cfg.bundlePolicy = *policy;
#else

    if (!cfg.bundle_path.empty())
    {
        auto policy = read_policy(cfg.policy_path);
        if (!policy)
        {
            return std::unexpected(policy.error());
        }

        rt_cfg.bundlePolicy = *policy;
    }
#endif
    auto rt = luax::Runtime::create(rt_cfg);
    if (!rt)
    {
        return std::unexpected(self.fail(Str(rt.error().message())));
    }

    self.runtime = std::move(*rt);
    auto isolate = self.runtime->createIsolate();
    if (!isolate)
    {
        return std::unexpected(self.fail(Str(isolate.error().message())));
    }

    self.isolate = *isolate;
    luax::Result<luax::ModuleHandle> module = std::unexpected(host_error("missing_artifact"));
#if !HUNTER_PRODUCTION

    if (cfg.bundle_path.empty())
    {
        auto source = read_file(cfg.source_path, rt_cfg.maxSourceBytes);
        if (!source)
        {
            return std::unexpected(self.fail(source.error()));
        }

        module = self.isolate.compile(*source, cfg.source_path);
    }
    else
#endif
    {
        auto bundle = read_file(cfg.bundle_path, rt_cfg.maxBytecodeBytes);
        if (!bundle)
        {
            return std::unexpected(self.fail(bundle.error()));
        }

        module = self.isolate.loadBundle(std::as_bytes(std::span(*bundle)), cfg.bundle_path);
    }

    if (!module)
    {
        return std::unexpected(self.fail(Str(module.error().message())));
    }

    self.module = *module;
    auto hosts = self.register_hosts();
    if (!hosts)
    {
        return std::unexpected(self.fail(Str(hosts.error().message())));
    }

    self.busy = true;
    auto top = luax::bind::callAs<bool>(self.isolate, self.module, self.options());
    self.busy = false;

    if (!top || !*top || !self.host_fault.empty())
    {
        const Str error = top ? "module_rejected: " + self.host_fault : Str(top.error().message());
        return std::unexpected(self.fail(error));
    }

    for (const Str name : {"init", "on_event", "tick", "export_state", "import_state",
        "validate_state", "shutdown"})
    {
        auto func = self.isolate.findFunction(self.module, name);
        if (!func)
        {
            return std::unexpected(self.fail(Str(func.error().message())));
        }

        self.funcs.emplace(name, *func);
    }

    self.alive = true;
    return self.commit(self.invoke<bool>("init", true, ctx_json));
}

std::expected<Vec<ScriptOut>, Str> Script::event(i64 event_id, const Str& payload_json)
{
    auto guard = impl_->enter();
    if (!guard)
    {
        return std::unexpected(guard.error());
    }

    if (!json_object(payload_json, impl_->cfg.max_json_bytes))
    {
        return std::unexpected(impl_->fail("invalid_event_json"));
    }

    return impl_->commit(impl_->invoke<bool>("on_event", true, event_id, payload_json));
}

std::expected<Vec<ScriptOut>, Str> Script::input(const wire::FrameInput& input, u64 applied_tick)
{
    auto& self = *impl_;
    const auto guard = self.enter();
    if (!guard)
    {
        return std::unexpected(guard.error());
    }

    self.pending.clear();
    self.world.access.writable = true;
    try
    {
        self.pending.emplace_back(self.world.input(input, applied_tick));
        self.world.access.writable = false;

        if (self.cfg.max_outputs == 0
            || self.pending.front().message.ByteSizeLong() > self.cfg.max_output_bytes)
        {
            return std::unexpected(self.fail("output_capacity"));
        }

        return self.commit(true);
    }
    catch (const std::exception& error)
    {
        self.world.access.writable = false;
        return std::unexpected(self.fail(error.what()));
    }
}

std::expected<Vec<ScriptOut>, Str> Script::tick(u64 tick_id, f64 dt_seconds)
{
    auto guard = impl_->enter();
    if (!guard)
    {
        return std::unexpected(guard.error());
    }

    if (tick_id > static_cast<u64>(std::numeric_limits<i64>::max()) ||
        !std::isfinite(dt_seconds) || dt_seconds <= 0)
    {
        return std::unexpected(impl_->fail("invalid_tick_arguments"));
    }

    auto& self = *impl_;
    const bool game = !self.world.content.is_null();
    const auto previous_phase = self.world.phase;

    if (game)
    {
        if (tick_id <= self.world.tick_id || std::abs(dt_seconds - 1.0 / 60.0) > 0.000001)
        {
            return std::unexpected(self.fail("invalid_game_tick"));
        }

        self.world.tick_id = tick_id;
    }

    auto invoked = self.invoke<bool>("tick", true, static_cast<i64>(tick_id), dt_seconds);
    if (invoked && *invoked && game
        && (previous_phase != self.world.phase || tick_id % self.snapshot_every == 0))
    {
        ScriptOut snapshot(self.world.snapshot(self.world.player_id));
        const auto bytes = snapshot.message.ByteSizeLong();
        if (self.pending.size() >= self.cfg.max_outputs
            || bytes > self.cfg.max_output_bytes - self.output_bytes)
        {
            return std::unexpected(self.fail("output_capacity"));
        }

        self.pending.push_back(std::move(snapshot));
    }

    return self.commit(std::move(invoked));
}

std::expected<Str, Str> Script::export_state()
{
    auto result = impl_->invoke<Str>("export_state", false);
    if (result && !json_object(*result, impl_->cfg.max_json_bytes))
    {
        return std::unexpected(impl_->fail("invalid_state_json"));
    }

    return result;
}

std::expected<void, Str> Script::import_state(const Str& snapshot_json)
{
    auto guard = impl_->enter();
    if (!guard)
    {
        return guard;
    }

    if (!json_object(snapshot_json, impl_->cfg.max_json_bytes))
    {
        return std::unexpected(impl_->fail("invalid_state_json"));
    }

    auto result = impl_->commit(impl_->invoke<bool>("import_state", false, snapshot_json));
    if (!result)
    {
        return std::unexpected(result.error());
    }

    return {};
}

std::expected<void, Str> Script::validate_state()
{
    auto result = impl_->commit(impl_->invoke<bool>("validate_state", false));
    if (!result)
    {
        return std::unexpected(result.error());
    }

    return {};
}

const World& Script::world() const
{
    const auto guard = impl_->enter();
    require(guard.has_value(), guard ? "" : guard.error());
    return impl_->world;
}

std::expected<void, Str> Script::change(Func<void(World&)> action)
{
    auto& self = *impl_;
    const auto guard = self.enter();
    if (!guard)
    {
        return std::unexpected(guard.error());
    }

    self.world.access.writable = true;
    try
    {
        action(self.world);
        self.world.access.writable = false;
        return {};
    }
    catch (const std::exception& error)
    {
        self.world.access.writable = false;
        return std::unexpected(self.fail(error.what()));
    }
}

Vec<Str> Script::take_logs() noexcept
{
    auto& self = *impl_;

    if (self.owner != std::this_thread::get_id() || self.busy)
    {
        return {};
    }

    self.log_bytes = 0;
    return std::exchange(self.logs, {});
}

std::expected<ScriptStats, Str> Script::stats() const
{
    const auto guard = impl_->enter();
    if (!guard)
    {
        return std::unexpected(guard.error());
    }

    const auto value = impl_->isolate.metricsSnapshot();
    if (!value)
    {
        return std::unexpected(Str(value.error().message()));
    }

    return ScriptStats{value->memory.generation.liveBytes, value->memory.generation.peakBytes,
        value->gc.completedCycles};
}

std::expected<void, Str> Script::shutdown(const Str& reason)
{
    auto& self = *impl_;

    if (self.owner != std::this_thread::get_id())
    {
        return std::unexpected("wrong_thread");
    }

    if (self.busy)
    {
        return std::unexpected("reentrant_call");
    }

    if (self.runtime)
    {
        if (self.alive)
        {
            const auto result = self.invoke<bool>("shutdown", false, reason);
            if (!result)
            {
                self.record_close_error("shutdown_failed", result.error());
            }
            else if (!*result)
            {
                self.record_close_error("shutdown_rejected");
            }
        }

        self.alive = false;
        self.world.access.alive = false;
        self.world.actors = {};
        self.world.items = {};
        self.world.order.clear();
        self.pending.clear();
        self.funcs.clear();

        if (self.isolate.valid())
        {
            const auto closed = self.isolate.close();
            if (!closed)
            {
                self.record_close_error("isolate_close_failed", Str(closed.error().message()));
            }
        }

        const auto closed = self.runtime->shutdown();
        if (!closed)
        {
            self.record_close_error("runtime_shutdown_failed", Str(closed.error().message()));
        }

        self.isolate = {};
        self.module = {};
        self.runtime.reset();
    }

    if (!self.close_error.empty())
    {
        return std::unexpected(self.close_error);
    }

    return {};
}

}

// 把真实 Luax 的线程、预算、签名授权与输出事务收敛到同步脚本会话。
#include "common/Types.h"
#include "script/Script.h"
#include "script/Schema.h"

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
        pending.clear();
        output_bytes = 0;
        alive = false;
        busy = false;
        return error.substr(0, 4096);
    }

    // 注册拥有数据的同步函数，命名空间由 Luax 保证不可改写。
    luax::Status register_hosts()
    {
        luax::HostNamespace net("net");
        net.add(luax::HostNamespaceEntry::function("emit", luax::HostFunction(
            [this](luax::HostCallContext& ctx,
                std::span<const luax::ValueView> args) -> luax::Result<luax::HostStep>
            {
                if (args.size() != 2 || !args[0].stringIf() || !args[1].stringIf())
                {
                    host_fault = "invalid_emit_arguments";
                    return std::unexpected(host_error(host_fault));
                }

                const auto& kind = *args[0].stringIf();
                const auto& payload = *args[1].stringIf();
                const usize bytes = kind.size() + payload.size();
                if (!allow_output || kind.empty() || kind.size() > 32 ||
                    payload.size() > cfg.max_json_bytes || pending.size() >= cfg.max_outputs ||
                    bytes > cfg.max_output_bytes - output_bytes)
                {
                    host_fault = "output_rejected";
                    return std::unexpected(host_error("output_rejected"));
                }

                auto work = ctx.consumeNativeWork(bytes);
                if (!work)
                {
                    host_fault = "native_work_exhausted";
                    return std::unexpected(work.error());
                }

                auto charge = ctx.consumeEffectBytes(bytes);
                if (!charge)
                {
                    host_fault = "output_budget_exhausted";
                    return std::unexpected(charge.error());
                }

                pending.push_back({Str(kind), Str(payload)});
                output_bytes += bytes;
                return luax::HostStep::completed({luax::OwnedValue::boolean(true)});
            })));
        auto result = isolate.registerHostNamespace(module, std::move(net));
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
        pending.clear();
        output_bytes = 0;
        host_fault.clear();
        const auto result = luax::bind::callAs<Result>(isolate, funcs.at(name), options(),
            std::forward<Args>(args)...);
        busy = false;
        allow_output = false;

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
            const auto decoded = decode_output(out, cfg.max_json_bytes);
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

    return impl_->commit(impl_->invoke<bool>("tick", true, static_cast<i64>(tick_id), dt_seconds));
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

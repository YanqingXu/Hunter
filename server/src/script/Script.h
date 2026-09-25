// 封装逻辑线程独占的 Luax 会话和原子提交的脚本输出。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"
#include "hunter.pb.h"

#include <expected>

namespace hunter
{
class World;

struct ScriptStats
{
    usize live_bytes = 0;
    usize peak_bytes = 0;
    u64 gc_cycles = 0;
};

struct ScriptOut
{
    Str kind;
    wire::Envelope message;
    Str error;

    // 接收原生协议输出，消息独占自身数据。
    explicit ScriptOut(wire::Envelope value);

    // 在低频 JSON 边界立即校验和转换，不保留 JSON 载荷。
    ScriptOut(Str name, const Str& json);
};

class Script
{
public:
    // 在调用线程建立尚未加载的会话。
    Script();

    // 在拥有线程释放 VM；宿主必须先关闭会话再销毁逻辑线程。
    ~Script();

    // 会话拥有唯一 VM，不允许复制或转移线程。
    Script(const Script&) = delete;

    // 会话拥有唯一 VM，不允许复制赋值。
    Script& operator=(const Script&) = delete;

    // 注册只读能力并加载源码或签名 Bundle，成功后调用 init。
    std::expected<Vec<ScriptOut>, Str> open(const Cfg& cfg, const Str& ctx_json);

    // 在 Tick 边界执行低频 JSON 控制，失败时中止当前会话。
    std::expected<Vec<ScriptOut>, Str> event(i64 event_id, const Str& payload_json);

    // 在 Tick 边界原生处理输入及确认，不执行 Lua 或 JSON 编解码。
    std::expected<Vec<ScriptOut>, Str> input(const wire::FrameInput& input, u64 applied_tick);

    // 使用精确整数 Tick 和固定秒数步长推进一次脚本。
    std::expected<Vec<ScriptOut>, Str> tick(u64 tick_id, f64 dt_seconds);

    // 在禁止输出的能力下导出带版本的状态 JSON。
    std::expected<Str, Str> export_state();

    // 在禁止输出的能力下导入状态，失败后禁止继续执行本局。
    std::expected<void, Str> import_state(const Str& snapshot_json);

    // 在禁止输出的能力下检查当前状态。
    std::expected<void, Str> validate_state();

    // 在拥有线程的入口边界读取原生内存和回收指标，供验证与诊断使用。
    std::expected<ScriptStats, Str> stats() const;

    // 拥有线程在入口返回后提取日志；其他线程或同步重入返回空集合。
    Vec<Str> take_logs() noexcept;

    // 借用入口外的只读权威世界，调用者不得保留到脚本关闭或重开之后。
    const World& world() const;

    // 所属线程在入口外修改原生业务状态，异常返回失败并中止后续入口。
    std::expected<void, Str> change(Func<void(World&)> action);

    // 尽力释放全部 VM 资源并返回有界退出错误；重复调用保留相同终态与未提取日志。
    std::expected<void, Str> shutdown(const Str& reason);

private:
    struct Impl;
    UPtr<Impl> impl_;
};

}

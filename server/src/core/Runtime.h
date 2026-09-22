// 宿主侧线程安全的服务入口；逻辑线程独占网络、定时器和脚本生命周期。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"

#include <optional>

namespace hunter
{
class Runtime
{
public:
    // 启动独占逻辑线程，等待 Start 命令后装载脚本。
    explicit Runtime(Cfg cfg);

    // 请求停止并等待逻辑线程释放全部资源。
    ~Runtime();

    // 运行时线程和队列不可复制。
    Runtime(const Runtime&) = delete;

    // 运行时线程和队列不可复制赋值。
    Runtime& operator=(const Runtime&) = delete;

    // 提交拥有数据的控制行；停止或容量不足时明确拒绝。
    bool submit(Str line);

    // 等待并移出下一个控制事件，超时或已排空时返回空值。
    std::optional<Str> next_evt(u32 timeout_ms);

    // 从任意宿主线程幂等请求退出；非空错误码使终态失败，不等待管道或逻辑线程。
    void stop(Str reason = {});

    // 等待线程完成；宿主在停止请求后调用。
    void join();

    // 查询逻辑线程是否已经释放资源并结束。
    bool finished() const;

    // 查询已记录的失败状态，不依赖控制事件是否被宿主读取。
    bool faulted() const;

private:
    struct State;
    struct Loop;
    UPtr<State> state_;
};
}

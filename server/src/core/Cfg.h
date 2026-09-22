// 集中定义框架容量、执行预算和资源路径；配置在启动后保持不变。
#pragma once

#include "common/Types.h"

namespace hunter
{
struct Cfg
{
    Str source_path;
    Str bundle_path;
    Str policy_path;
    u32 tick_hz = 60;
    u32 snapshot_hz = 20;
    u32 max_catchup = 4;
    usize max_inputs_per_tick = 64;
    u32 tick_work_ms = 4;
    usize max_frame_bytes = 64 * 1024;
    usize max_json_bytes = 64 * 1024;
    usize max_queue_count = 256;
    usize max_queue_bytes = 1024 * 1024;
    usize max_send_count = 256;
    usize max_send_bytes = 1024 * 1024;
    usize max_control_count = 256;
    usize max_control_bytes = 1024 * 1024;
    usize max_outputs = 256;
    usize max_output_bytes = 1024 * 1024;
    usize max_async_ops = 64;
    u32 handshake_timeout_ms = 5000;
    u32 stop_timeout_ms = 5000;
    u64 instruction_budget = 1000000;
    u64 native_work_budget = 1000000;
    u64 script_memory_bytes = 16 * 1024 * 1024;
    u32 script_deadline_ms = 50;
};
}

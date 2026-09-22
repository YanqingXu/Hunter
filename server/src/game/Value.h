// 定义原生对象的访问门禁、精确身份及数值边界。
#pragma once
#include "common/Types.h"
#include <thread>

namespace hunter
{
struct Access
{
    std::thread::id owner = std::this_thread::get_id();
    bool writable = false;
    bool alive = true;

    // 验证所属线程和会话生命期。
    void read() const;

    // 验证当前片段允许修改权威状态。
    void write() const;
};

// 拒绝破坏原生数据边界的参数。
void require(bool valid, const Str& error);

// 验证有符号整数的闭区间。
void range(i64 value, i64 low, i64 high);

// 读取完整规范十进制身份，拒绝符号、前导零和溢出。
u64 read_id(const Str& text);
}

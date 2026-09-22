// 转换唯一 Protobuf 协议与有界桥接输出，并管理不可变发送缓存。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"
#include "script/Script.h"
#include "hunter.pb.h"

#include <deque>
#include <expected>

namespace hunter
{
struct Frame
{
    Str bytes;
    bool snapshot = false;
};

// 编码为四字节大端长度头加 Protobuf；过大或空消息返回错误。
std::expected<Frame, Str> encode_frame(const wire::Envelope& msg, usize max_bytes);

// 解码已经去掉长度头的完整消息；拒绝空信封和非法 Protobuf。
std::expected<wire::Envelope, Str> decode_frame(const Str& bytes, usize max_bytes);

// 整批校验类型化输出并编码协议；失败不返回任何可提交输出。
std::expected<Vec<Frame>, Str> script_frames(const Vec<ScriptOut>& out, const Cfg& cfg);

class SendQueue
{
public:
    // 创建同时受条数和字节限制的发送队列。
    SendQueue(usize max_count, usize max_bytes);

    // 整批预留并提交；合并未发送快照，失败时保留原队列。
    bool push_batch(Vec<Frame> frames);

    // 标记首帧正在发送并返回拥有不可变缓存的共享指针。
    Ptr<const Frame> start();

    // 归还已完成首帧容量；仅发送回调调用。
    void finish();

    // 清除等待缓存；在途回调仍持有自己的不可变缓存。
    void clear();

    // 返回尚未完成的帧数量。
    usize size() const;

    // 返回尚未完成的帧总字节数。
    usize bytes() const;

private:
    std::deque<Ptr<const Frame>> frames_;
    usize max_count_;
    usize max_bytes_;
    usize bytes_ = 0;
    bool writing_ = false;
};
}

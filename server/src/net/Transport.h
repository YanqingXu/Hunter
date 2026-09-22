// 在单个 Asio 线程上管理回环监听、握手超时、分帧和发送背压。
#pragma once

#include "common/Types.h"
#include "core/Cfg.h"
#include "net/Protocol.h"

#include <asio.hpp>

#include <array>

namespace hunter
{
class Transport
{
public:
    // 创建逻辑线程专属传输；回调始终在同一 io_context 上执行。
    Transport(asio::io_context& io, const Cfg& cfg,
        Func<void(wire::Envelope)> on_msg, Func<void(Str)> on_close);

    // 关闭全部资源；析构必须在逻辑线程且排空回调后执行。
    ~Transport();

    // 绑定临时回环端口并开始接受连接。
    u16 open();

    // 将当前连接标记为已验证并取消握手定时器。
    void authenticate();

    // 整批提交发送缓存；容量不足返回失败，不部分提交。
    bool send(Vec<Frame> frames);

    // 关闭监听和连接并取消未完成 I/O；不发起关闭通知。
    void stop();

    // 返回当前是否存在已连接且未关闭的客户端。
    bool connected() const;

private:
    // 持续接受连接，已有客户端时拒绝额外连接。
    void accept();

    // 读取下一帧四字节长度并在分配前检查边界。
    void read_head();

    // 读取完整消息体并交付拥有数据的信封。
    void read_body(usize size);

    // 开始发送下一份不可变缓存，保持最多一个写操作。
    void write();

    // 一次性关闭客户端并通知会话所有者。
    void fail(Str reason);

    asio::io_context& io_;
    const Cfg& cfg_;
    asio::ip::tcp::acceptor acceptor_;
    asio::ip::tcp::socket socket_;
    asio::steady_timer handshake_;
    SendQueue send_;
    Func<void(wire::Envelope)> on_msg_;
    Func<void(Str)> on_close_;
    std::array<u8, 4> head_{};
    Str body_;
    bool stopped_ = false;
    bool used_ = false;
};
}

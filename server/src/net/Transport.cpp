// 实现 TCP 操作；所有对象、回调和容量计费均由逻辑线程独占。
#include "net/Transport.h"
#include "common/Types.h"

namespace hunter
{
Transport::Transport(asio::io_context& io, const Cfg& cfg,
    Func<void(wire::Envelope)> on_msg, Func<void(Str)> on_close)
    : io_(io), cfg_(cfg), acceptor_(io), socket_(io), handshake_(io),
      send_(cfg.max_send_count, cfg.max_send_bytes), on_msg_(std::move(on_msg)),
      on_close_(std::move(on_close))
{
}

Transport::~Transport()
{
    stop();
}

u16 Transport::open()
{
    const asio::ip::tcp::endpoint endpoint(asio::ip::make_address_v4("127.0.0.1"), 0);
    acceptor_.open(endpoint.protocol());
    acceptor_.bind(endpoint);
    acceptor_.listen(1);
    const auto port = acceptor_.local_endpoint().port();
    accept();
    return port;
}

void Transport::authenticate()
{
    handshake_.cancel();
}

bool Transport::send(Vec<Frame> frames)
{
    if (!socket_.is_open() || stopped_ || !send_.push_batch(std::move(frames)))
    {
        return false;
    }

    write();
    return true;
}

void Transport::stop()
{
    stopped_ = true;
    asio::error_code ignored;
    acceptor_.close(ignored);
    socket_.close(ignored);
    handshake_.cancel();
    send_.clear();
}

bool Transport::connected() const
{
    return socket_.is_open();
}

void Transport::accept()
{
    auto pending = std::make_shared<asio::ip::tcp::socket>(io_);
    acceptor_.async_accept(*pending, [this, pending](asio::error_code err)
    {
        if (err || stopped_)
        {
            return;
        }

        if (used_)
        {
            asio::error_code ignored;
            pending->close(ignored);
        }
        else
        {
            used_ = true;
            socket_ = std::move(*pending);
            asio::error_code ignored;
            socket_.set_option(asio::ip::tcp::no_delay(true), ignored);
            socket_.set_option(asio::socket_base::send_buffer_size(16384), ignored);
            handshake_.expires_after(std::chrono::milliseconds(cfg_.handshake_timeout_ms));
            handshake_.async_wait([this](asio::error_code timer_err)
            {
                if (!timer_err && !stopped_)
                {
                    fail("handshake_timeout");
                }
            });
            read_head();
        }

        accept();
    });
}

void Transport::read_head()
{
    asio::async_read(socket_, asio::buffer(head_), [this](asio::error_code err, usize)
    {
        if (stopped_)
        {
            return;
        }

        if (err)
        {
            fail("connection_closed");
            return;
        }

        u32 size = 0;

        for (const auto byte : head_)
        {
            size = (size << 8) | byte;
        }

        if (size == 0 || size > cfg_.max_frame_bytes)
        {
            fail("frame_size");
            return;
        }

        read_body(size);
    });
}

void Transport::read_body(usize size)
{
    body_.resize(size);
    asio::async_read(socket_, asio::buffer(body_), [this](asio::error_code err, usize)
    {
        if (stopped_)
        {
            return;
        }

        if (err)
        {
            fail("connection_closed");
            return;
        }

        auto msg = decode_frame(body_, cfg_.max_frame_bytes);
        if (!msg)
        {
            fail(msg.error());
            return;
        }

        on_msg_(std::move(*msg));

        if (!stopped_ && socket_.is_open())
        {
            read_head();
        }
    });
}

void Transport::write()
{
    const auto frame = send_.start();
    if (!frame)
    {
        return;
    }

    asio::async_write(socket_, asio::buffer(frame->bytes),
        [this, frame](asio::error_code err, usize)
    {
        if (stopped_)
        {
            return;
        }

        if (err)
        {
            fail("send_failed");
            return;
        }

        send_.finish();
        write();
    });
}

void Transport::fail(Str reason)
{
    if (stopped_)
    {
        return;
    }

    stop();
    on_close_(std::move(reason));
}
}

// 实现框架消息的严格 schema 校验和原子发送容量预留。
#include "net/Protocol.h"
#include "common/Types.h"
#include "script/Schema.h"
#include <limits>

namespace hunter
{
std::expected<Frame, Str> encode_frame(const wire::Envelope& msg, usize max_bytes)
{
    const auto size = msg.ByteSizeLong();
    if (size == 0 || size > max_bytes
        || size > static_cast<usize>(std::numeric_limits<i32>::max()))
    {
        return std::unexpected("frame_size");
    }

    Frame frame;
    frame.snapshot = msg.has_snapshot();
    frame.bytes.resize(4 + size);
    const auto count = static_cast<u32>(size);

    for (usize i = 0; i < 4; ++i)
    {
        frame.bytes[i] = static_cast<char>(count >> (24 - i * 8));
    }

    if (!msg.SerializeToArray(frame.bytes.data() + 4, static_cast<i32>(size)))
    {
        return std::unexpected("frame_encode");
    }

    return frame;
}

std::expected<wire::Envelope, Str> decode_frame(const Str& bytes, usize max_bytes)
{
    wire::Envelope msg;
    if (bytes.empty() || bytes.size() > max_bytes
        || bytes.size() > static_cast<usize>(std::numeric_limits<i32>::max())
        || !msg.ParseFromArray(bytes.data(), static_cast<i32>(bytes.size()))
        || msg.body_case() == wire::Envelope::BODY_NOT_SET)
    {
        return std::unexpected("frame_decode");
    }

    return msg;
}

std::expected<Vec<Frame>, Str> script_frames(const Vec<ScriptOut>& out, const Cfg& cfg)
{
    Vec<Frame> frames;
    usize bytes = 0;

    try
    {
        for (const auto& item : out)
        {
            const auto decoded = decode_output(item, cfg.max_json_bytes);
            if (!decoded)
            {
                return std::unexpected(decoded.error());
            }

            wire::Envelope msg;

            if (decoded->snapshot)
            {
                msg.mutable_snapshot()->set_tick_id(decoded->tick_id);
                msg.mutable_snapshot()->set_seq(decoded->seq);
                msg.mutable_snapshot()->set_count(decoded->count);
            }
            else
            {
                msg.mutable_ack()->set_seq(decoded->seq);
                msg.mutable_ack()->set_count(decoded->count);
            }

            auto frame = encode_frame(msg, cfg.max_frame_bytes);
            if (!frame)
            {
                return std::unexpected(frame.error());
            }

            if (frames.size() >= cfg.max_outputs
                || frame->bytes.size() > cfg.max_output_bytes - bytes)
            {
                return std::unexpected("output_batch_limit");
            }

            bytes += frame->bytes.size();
            frames.push_back(std::move(*frame));
        }
    }
    catch (const std::exception&)
    {
        return std::unexpected("output_schema");
    }

    return frames;
}

SendQueue::SendQueue(usize max_count, usize max_bytes)
    : max_count_(max_count), max_bytes_(max_bytes)
{
}

bool SendQueue::push_batch(Vec<Frame> frames)
{
    auto pending = frames_;
    auto bytes = bytes_;

    for (auto& frame : frames)
    {
        if (frame.snapshot)
        {
            auto iter = pending.begin();
            if (writing_ && iter != pending.end())
            {
                ++iter;
            }

            while (iter != pending.end())
            {
                if ((*iter)->snapshot)
                {
                    bytes -= (*iter)->bytes.size();
                    iter = pending.erase(iter);
                }
                else
                {
                    ++iter;
                }
            }
        }

        if (pending.size() >= max_count_ || frame.bytes.size() > max_bytes_ - bytes)
        {
            return false;
        }

        bytes += frame.bytes.size();
        pending.push_back(std::make_shared<Frame>(std::move(frame)));
    }

    frames_ = std::move(pending);
    bytes_ = bytes;
    return true;
}

Ptr<const Frame> SendQueue::start()
{
    if (writing_ || frames_.empty())
    {
        return {};
    }

    writing_ = true;
    return frames_.front();
}

void SendQueue::finish()
{
    if (writing_ && !frames_.empty())
    {
        bytes_ -= frames_.front()->bytes.size();
        frames_.pop_front();
    }

    writing_ = false;
}

void SendQueue::clear()
{
    frames_.clear();
    bytes_ = 0;
    writing_ = false;
}

usize SendQueue::size() const
{
    return frames_.size();
}

usize SendQueue::bytes() const
{
    return bytes_;
}
}

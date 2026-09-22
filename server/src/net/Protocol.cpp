// 转换严格校验的玩法输出并原子预留发送容量，保持快照可合并与事件不可丢失。
#include "net/Protocol.h"
#include "common/Types.h"
#include "script/Schema.h"
#include <limits>

namespace hunter
{
namespace
{

// 将已校验的实体投影写入 Protobuf，不保留可独立修改的实体状态。
void encode_entity(const nlohmann::json& obj, wire::Entity& entity)
{
    entity.set_id(obj.at("id").get<u64>());
    entity.set_kind(obj.at("kind").get<Str>());
    entity.set_x(obj.at("x").get<i32>());
    entity.set_y(obj.at("y").get<i32>());
    entity.set_vx(obj.at("vx").get<i32>());
    entity.set_vy(obj.at("vy").get<i32>());
    entity.set_hp(obj.at("hp").get<i32>());
    entity.set_max_hp(obj.at("max_hp").get<i32>());
    entity.set_ammo(obj.at("ammo").get<i32>());
    entity.set_reserve(obj.at("reserve").get<i32>());
    entity.set_reload_ticks(obj.at("reload_ticks").get<i32>());
    entity.set_grounded(obj.at("grounded").get<bool>());
    entity.set_alive(obj.at("alive").get<bool>());
    entity.set_facing(obj.at("facing").get<i32>());
    entity.set_ai(obj.at("ai").get<Str>());
}

// 按冻结输出种类构造信封；输入对象已经完成字段、精度和范围检查。
wire::Envelope encode_output(const Str& kind, const nlohmann::json& obj)
{
    wire::Envelope msg;

    if (kind == "ack")
    {
        auto& ack = *msg.mutable_ack();
        ack.set_seq(obj.at("seq").get<u64>());
        ack.set_match_id(obj.at("match_id").get<u64>());
        ack.set_applied_tick(obj.at("applied_tick").get<u64>());
    }
    else if (kind == "snapshot")
    {
        auto& snapshot = *msg.mutable_snapshot();
        snapshot.set_tick_id(obj.at("tick_id").get<u64>());
        snapshot.set_seq(obj.at("seq").get<u64>());
        snapshot.set_match_id(obj.at("match_id").get<u64>());
        snapshot.set_phase(obj.at("phase").get<Str>());

        for (const auto& entity : obj.at("entities"))
        {
            encode_entity(entity, *snapshot.add_entities());
        }
    }
    else if (kind == "login")
    {
        auto& login = *msg.mutable_login_rsp();
        login.set_req_id(obj.at("req_id").get<Str>());
        login.set_player_id(obj.at("player_id").get<u64>());
        login.set_match_id(obj.at("match_id").get<u64>());
        login.set_phase(obj.at("phase").get<Str>());
    }
    else if (kind == "start")
    {
        auto& start = *msg.mutable_start_rsp();
        start.set_req_id(obj.at("req_id").get<Str>());
        start.set_match_id(obj.at("match_id").get<u64>());
        start.set_phase(obj.at("phase").get<Str>());
    }
    else if (kind == "event")
    {
        auto& event = *msg.mutable_event();
        event.set_match_id(obj.at("match_id").get<u64>());
        event.set_event_id(obj.at("event_id").get<u64>());
        event.set_tick_id(obj.at("tick_id").get<u64>());
        event.set_kind(obj.at("kind").get<Str>());
        event.set_actor_id(obj.at("actor_id").get<u64>());
        event.set_target_id(obj.at("target_id").get<u64>());
        event.set_x(obj.at("x").get<i32>());
        event.set_y(obj.at("y").get<i32>());
        event.set_amount(obj.at("amount").get<i32>());
    }
    else if (kind == "error")
    {
        auto& error = *msg.mutable_error();
        error.set_code(obj.at("code").get<Str>());
        error.set_detail(obj.at("detail").get<Str>());
        error.set_req_id(obj.at("req_id").get<Str>());
        error.set_seq(obj.at("seq").get<u64>());
        error.set_match_id(obj.at("match_id").get<u64>());
    }

    return msg;
}

}

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

            const auto msg = encode_output(item.kind, *decoded);
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

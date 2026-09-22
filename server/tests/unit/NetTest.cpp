// 验证玩法协议、无损 ID、嵌套输出 schema 与有界原子发送批次。
#include "common/Types.h"
#include "net/Protocol.h"

#include <iostream>
#include <stdexcept>
#include <nlohmann/json.hpp>

namespace
{
// 断言外部可观察契约，失败立即终止本测试。
void check(bool ok, const char* detail)
{
    if (!ok)
    {
        throw std::runtime_error(detail);
    }
}

// 返回覆盖有符号坐标与全部字段的合法实体投影。
nlohmann::json entity()
{
    return {{"id", "1"}, {"cfg_id", "2147483647"}, {"kind", "player"}, {"x", -100}, {"y", 20},
        {"vx", -30}, {"vy", 0}, {"hp", 100}, {"max_hp", 100}, {"ammo", 6},
        {"reserve", 30}, {"reload_ticks", 0}, {"grounded", true}, {"alive", true},
        {"facing", -1}, {"ai", "idle"}};
}

// 在一个合法输出后放入非法输出，确认任何错误都会拒绝整批。
void rejects(const Str& kind, const nlohmann::json& value, const hunter::Cfg& cfg)
{
    const Vec<hunter::ScriptOut> out{
        {"ack", R"({"v":3,"seq":"1","match_id":"1","applied_tick":"1"})"},
        {kind, value.dump()}};
    check(!hunter::script_frames(out, cfg), "invalid output rejects entire batch");
}

// 验证全部输出投影及最重要的精度、字段与容器边界。
void outputs(const hunter::Cfg& cfg)
{
    const nlohmann::json ack = {{"v", 3}, {"seq", "18446744073709551615"},
        {"match_id", "18446744073709551615"}, {"applied_tick", "9223372036854775807"}};
    const nlohmann::json snapshot = {{"v", 3}, {"tick_id", "9223372036854775807"},
        {"seq", "18446744073709551615"}, {"match_id", "1"}, {"phase", "Playing"},
        {"entities", nlohmann::json::array({entity()})}};
    const Vec<hunter::ScriptOut> good{
        {"ack", ack.dump()}, {"snapshot", snapshot.dump()},
        {"login", R"({"v":3,"req_id":"l","player_id":"1","match_id":"0","phase":"Lobby"})"},
        {"start", R"({"v":3,"req_id":"s","match_id":"1","phase":"Playing"})"},
        {"event", R"({"v":3,"match_id":"1","event_id":"2","tick_id":"3","kind":"hit",)"
            R"("actor_id":"1","target_id":"2","x":-100,"y":0,"amount":20})"},
        {"error", R"({"v":3,"code":"stale_input","detail":"","req_id":"","seq":"9",)"
            R"("match_id":"1"})"}};
    const auto frames = hunter::script_frames(good, cfg);
    check(frames && frames->size() == good.size(), "all output kinds encode");
    const auto a = hunter::decode_frame(frames->at(0).bytes.substr(4), cfg.max_frame_bytes);
    check(a && a->ack().seq() == 18446744073709551615ULL
        && a->ack().applied_tick() == 9223372036854775807ULL, "exact ID and Tick maxima");
    const auto s = hunter::decode_frame(frames->at(1).bytes.substr(4), cfg.max_frame_bytes);
    check(s && s->snapshot().entities_size() == 1 && s->snapshot().entities(0).x() == -100
        && s->snapshot().entities(0).facing() == -1, "signed entity fields");
    check(s->snapshot().entities(0).cfg_id() == 2147483647U, "configuration ID projection");
    check(frames->at(1).snapshot && !frames->at(4).snapshot, "only snapshots coalesce");
    check(hunter::decode_frame(frames->at(2).bytes.substr(4), 65536)->login_rsp().req_id() == "l",
        "login projection");
    check(hunter::decode_frame(frames->at(3).bytes.substr(4), 65536)->start_rsp().match_id() == 1,
        "start projection");
    check(hunter::decode_frame(frames->at(4).bytes.substr(4), 65536)->event().amount() == 20,
        "event projection");
    check(hunter::decode_frame(frames->at(5).bytes.substr(4), 65536)->error().seq() == 9,
        "error correlation");

    for (const Str seq : {"0", "01", "+1", "-1", "18446744073709551616", ""})
    {
        auto bad = ack;
        bad["seq"] = seq;
        rejects("ack", bad, cfg);
    }

    for (const nlohmann::json seq : {nlohmann::json(1), nlohmann::json(1.0),
        nlohmann::json(true)})
    {
        auto bad = ack;
        bad["seq"] = seq;
        rejects("ack", bad, cfg);
    }

    auto bad = ack;
    bad["applied_tick"] = "9223372036854775808";
    rejects("ack", bad, cfg);
    bad = ack;
    bad["v"] = 3.0;
    rejects("ack", bad, cfg);
    bad = ack;
    bad["v"] = true;
    rejects("ack", bad, cfg);
    bad = ack;
    bad["extra"] = 0;
    rejects("ack", bad, cfg);
    bad = ack;
    bad.erase("match_id");
    rejects("ack", bad, cfg);

    for (const Str field : {"x", "hp", "facing"})
    {
        bad = snapshot;
        bad["entities"][0][field] = field == "facing" ? 0 : 1000000001;
        rejects("snapshot", bad, cfg);
    }

    for (const nlohmann::json cfg_id : {nlohmann::json("0"), nlohmann::json("2147483648"),
        nlohmann::json("01"), nlohmann::json(1), nlohmann::json(true)})
    {
        bad = snapshot;
        bad["entities"][0]["cfg_id"] = cfg_id;
        rejects("snapshot", bad, cfg);
    }

    bad = snapshot;
    bad["entities"][0].erase("cfg_id");
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["v"] = 2;
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["entities"][0]["alive"] = 1;
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["entities"][0]["kind"] = "boss";
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["entities"][0]["extra"] = 0;
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["phase"] = "Unknown";
    rejects("snapshot", bad, cfg);
    bad = snapshot;
    bad["entities"] = nlohmann::json::array();

    for (usize i = 0; i < 65; ++i)
    {
        bad["entities"].push_back(entity());
    }

    rejects("snapshot", bad, cfg);
    bad["entities"].erase(64);
    check(hunter::script_frames({{"snapshot", bad.dump()}}, cfg).has_value(),
        "bounded entity array maximum");
    bad = nlohmann::json::parse(good[2].payload);
    bad["req_id"] = Str(129, 'x');
    rejects("login", bad, cfg);
    rejects("unknown", ack, cfg);

    auto small = cfg;
    small.max_outputs = 1;
    check(!hunter::script_frames(good, small), "output count bound");
    small = cfg;
    small.max_output_bytes = 1;
    check(!hunter::script_frames(good, small), "output byte bound");
}
}

// 执行真实 Protobuf 帧、输出校验与发送队列的边界场景。
int main()
{
    using namespace hunter;

    try
    {
        wire::Envelope msg;
        msg.mutable_input()->set_seq(1);
        msg.mutable_input()->set_match_id(1);
        msg.mutable_input()->set_move_x(-1);
        msg.mutable_input()->set_aim_x(1000);
        const auto frame = encode_frame(msg, 65536);
        check(frame.has_value(), "encode input");
        check(frame->bytes == Str("\0\0\0\x0b\x3a\x09\x08\1\x18\1\x20\1\x28\xd0\x0f", 15),
            "unchanged protobuf input golden frame");
        const auto decoded = decode_frame(frame->bytes.substr(4), 65536);
        check(decoded && decoded->input().move_x() == -1, "decode signed input");
        check(!decode_frame("", 65536), "empty protobuf");
        check(!decode_frame(Str(1, '\xff'), 65536), "invalid protobuf");
        check(!decode_frame(Str("\x1a\2\x08\1", 4), 65536), "retired envelope tag rejected");
        check(!decode_frame(frame->bytes.substr(4), 1), "frame limit");
        outputs(Cfg{});

        SendQueue queue(3, 10);
        check(queue.push_batch({{"aaa", true}}), "first snapshot");
        const auto active = queue.start();
        check(active && active->bytes == "aaa", "active cache lifetime");
        check(queue.push_batch({{"bb", true}, {"cc", true}}), "snapshot replacement");
        check(queue.size() == 2 && queue.bytes() == 5, "pending snapshot coalesced");
        check(!queue.push_batch({{"1234", false}, {"5678", false}}), "batch cap");
        check(queue.size() == 2 && queue.bytes() == 5, "failed batch is atomic");
        queue.finish();
        check(queue.start()->bytes == "cc", "latest snapshot retained");
        queue.clear();
        check(active->bytes == "aaa", "inflight buffer remains owned");
        std::cout << "net contracts passed\n";
        return 0;
    }
    catch (const std::exception& err)
    {
        std::cerr << err.what() << '\n';
        return 1;
    }
}

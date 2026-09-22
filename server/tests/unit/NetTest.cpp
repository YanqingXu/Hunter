// 验证协议真实编解码、严格桥接 schema 与慢读时的有界缓存。
#include "common/Types.h"
#include "net/Protocol.h"

#include <iostream>
#include <stdexcept>

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
}

// 执行完整帧、桥接批次和发送队列的边界场景。
int main()
{
    using namespace hunter;

    try
    {
        wire::Envelope msg;
        msg.mutable_input()->set_seq(1);
        msg.mutable_input()->set_value(-1);
        const auto frame = encode_frame(msg, 65536);
        check(frame.has_value(), "encode input");
        check(frame->bytes == Str("\0\0\0\6\x1a\4\x08\1\x10\1", 10), "protobuf golden frame");
        const auto decoded = decode_frame(frame->bytes.substr(4), 65536);
        check(decoded && decoded->input().value() == -1, "decode signed input");
        check(!decode_frame("", 65536), "empty protobuf");
        check(!decode_frame(Str(1, '\xff'), 65536), "invalid protobuf");
        check(!decode_frame(frame->bytes.substr(4), 1), "frame limit");

        Cfg cfg;
        Vec<ScriptOut> good{{"ack", "{\"v\":1,\"seq\":\"18446744073709551615\",\"count\":2}"}};
        check(script_frames(good, cfg).has_value(), "lossless uint64");
        auto bad = good;
        bad.push_back({"snapshot", "{\"v\":1,\"tick_id\":2}"});
        check(!script_frames(bad, cfg), "batch schema rejection");
        check(!script_frames({{"ack", "{\"v\":1,\"seq\":\"01\",\"count\":2}"}}, cfg),
            "noncanonical id");

        const Vec<ScriptOut> rejected{
            {"ack", R"({"v":1,"seq":"0","count":1})"},
            {"ack", R"({"v":1,"seq":"1","count":1000000001})"},
            {"ack", R"({"v":1,"seq":"1","count":-1000000001})"},
            {"ack", R"({"v":1,"seq":"1","count":18446744073709551615})"},
            {"ack", R"({"v":18446744073709551615,"seq":"1","count":0})"},
            {"ack", R"({"v":1,"seq":"1","count":0,"extra":0})"},
            {"ack", R"({"v":1.0,"seq":"1","count":0})"},
            {"snapshot", R"({"v":1,"seq":"0","tick_id":"9223372036854775808","count":0})"},
            {"snapshot", R"({"v":1,"seq":"0","tick_id":"0","count":0,"extra":0})"}};

        for (const auto& item : rejected)
        {
            auto batch = good;
            batch.push_back(item);
            check(!script_frames(batch, cfg), "schema violation rejects entire frame batch");
        }

        const Vec<ScriptOut> edges{
            {"ack", R"({"v":1,"seq":"18446744073709551615","count":1000000000})"},
            {"ack", R"({"v":1,"seq":"1","count":-1000000000})"},
            {"snapshot", R"({"v":1,"seq":"0","tick_id":"0","count":0})"},
            {"snapshot", R"({"v":1,"seq":"1","tick_id":"9223372036854775807","count":0})"}};
        const auto edge_frames = script_frames(edges, cfg);
        check(edge_frames && edge_frames->size() == edges.size(), "accept schema boundary values");
        const auto max_tick = decode_frame(edge_frames->back().bytes.substr(4),
            cfg.max_frame_bytes);
        check(max_tick && max_tick->snapshot().tick_id() == 9223372036854775807ULL,
            "snapshot conversion preserves exact signed Tick maximum");

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

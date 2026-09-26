// 接收 Ready 和有界 JSON 命令，以正式 Protobuf 连接本机服务并持续输出完整协议结果。
#include "common/Types.h"
#include "net/Protocol.h"
#include "ContentId.h"

#include <asio.hpp>
#include <nlohmann/json.hpp>
#include <windows.h>

#include <array>
#include <atomic>
#include <charconv>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <deque>
#include <mutex>
#include <limits>
#include <algorithm>
#include <optional>
#include <stdexcept>
#include <thread>

namespace
{
using Json = nlohmann::json;
using Tcp = asio::ip::tcp;
constexpr usize max_bytes = 65536;
constexpr usize max_count = 256;
constexpr usize queue_bytes = 1024 * 1024;

// 对命令对象精确匹配字段，避免拼写错误被静默忽略。
void fields(const Json& value, std::initializer_list<const char*> names)
{
    if (!value.is_object() || value.size() != names.size())
    {
        throw std::runtime_error("command_fields");
    }

    for (const auto* name : names)
    {
        if (!value.contains(name))
        {
            throw std::runtime_error("command_fields");
        }
    }
}

// 校验必需字段并允许有限的可选意图，仍拒绝未知键和拼写错误。
void optional_fields(const Json& value, std::initializer_list<const char*> required,
    std::initializer_list<const char*> optional)
{
    auto copy = value;

    for (const auto* name : optional)
    {
        copy.erase(name);
    }

    fields(copy, required);
}

// 读取有界 JSON 整数，布尔和浮点不属于协议整数。
i32 number(const Json& value, i32 minimum, i32 maximum)
{
    if (!value.is_number_integer())
    {
        throw std::runtime_error("integer_required");
    }

    if (value.is_number_unsigned() && value.get<u64>() > static_cast<u64>(maximum))
    {
        throw std::runtime_error("integer_range");
    }

    const auto parsed = value.get<i64>();
    if (parsed < minimum || parsed > maximum)
    {
        throw std::runtime_error("integer_range");
    }

    return static_cast<i32>(parsed);
}

// 解析规范十进制字符串，所有跨端 64 位标识都避免经过浮点转换。
u64 id(const Json& value, bool positive)
{
    const auto text = value.get<Str>();
    u64 parsed = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), parsed);
    if (text.empty() || (text.size() > 1 && text.front() == '0')
        || result.ec != std::errc{} || result.ptr != text.data() + text.size()
        || (positive && parsed == 0))
    {
        throw std::runtime_error("canonical_id_required");
    }

    return parsed;
}

// 读取服务器关联请求所需的非空标识，不自动产生隐藏请求。
Str req_id(const Json& value)
{
    const auto text = value.get<Str>();
    if (text.empty() || text.size() > 128)
    {
        throw std::runtime_error("req_id_size");
    }

    return text;
}

// 将完整免费配装转换为生成协议，属性和伤害由服务端配置决定。
hunter::wire::Loadout loadout(const Json& doc)
{
    fields(doc, {"player_cfg_id", "health_segments", "weapons", "tools", "consumables"});
    hunter::wire::Loadout value;
    value.set_player_cfg_id(static_cast<u32>(number(doc.at("player_cfg_id"), 1, 2147483647)));

    for (const auto& part : doc.at("health_segments"))
    {
        value.add_health_segments(static_cast<u32>(number(part, 1, 150)));
    }

    for (const auto& gun : doc.at("weapons"))
    {
        fields(gun, {"cfg_id", "ammo_cfg_id"});
        auto& out = *value.add_weapons();
        out.set_cfg_id(static_cast<u32>(number(gun.at("cfg_id"), 1, 2147483647)));
        out.set_ammo_cfg_id(static_cast<u32>(number(gun.at("ammo_cfg_id"), 1, 2147483647)));
    }

    for (const auto& item : doc.at("tools"))
    {
        value.add_tools(static_cast<u32>(number(item, 1, 2147483647)));
    }

    for (const auto& item : doc.at("consumables"))
    {
        value.add_consumables(static_cast<u32>(number(item, 1, 2147483647)));
    }

    return value;
}

// 输出服务端接受的配装，便于客户端核对默认值和槽位顺序。
Json loadout(const hunter::wire::Loadout& value)
{
    auto weapons = Json::array();

    for (const auto& gun : value.weapons())
    {
        weapons.push_back({{"cfg_id", gun.cfg_id()}, {"ammo_cfg_id", gun.ammo_cfg_id()}});
    }

    return {{"player_cfg_id", value.player_cfg_id()},
        {"health_segments", Vec<u32>(value.health_segments().begin(),
            value.health_segments().end())},
        {"weapons", std::move(weapons)},
        {"tools", Vec<u32>(value.tools().begin(), value.tools().end())},
        {"consumables", Vec<u32>(value.consumables().begin(), value.consumables().end())}};
}

// 将用户命令转换为生成协议对象，坐标、生命和伤害不接受客户端指定。
hunter::wire::Envelope command(const Json& doc)
{
    hunter::wire::Envelope msg;
    const auto cmd = doc.at("cmd").get<Str>();
    if (cmd == "login")
    {
        fields(doc, {"cmd", "req_id"});
        msg.mutable_login_req()->set_req_id(req_id(doc.at("req_id")));
    }
    else if (cmd == "start")
    {
        optional_fields(doc, {"cmd", "req_id", "after_match_id"}, {"loadout"});
        auto* start = msg.mutable_start_req();
        start->set_req_id(req_id(doc.at("req_id")));
        start->set_after_match_id(id(doc.at("after_match_id"), false));

        if (doc.contains("loadout"))
        {
            *start->mutable_loadout() = loadout(doc.at("loadout"));
        }
    }
    else if (cmd == "input")
    {
        optional_fields(doc, {"cmd", "seq", "match_id", "world_id", "move_x", "aim_x", "aim_y",
            "jump", "fire", "reload"}, {"move_y", "run", "prone"});
        auto* input = msg.mutable_input();
        input->set_seq(id(doc.at("seq"), true));
        input->set_match_id(id(doc.at("match_id"), true));
        input->set_world_id(id(doc.at("world_id"), true));
        input->set_move_x(number(doc.at("move_x"), -1, 1));
        input->set_aim_x(number(doc.at("aim_x"), -1000, 1000));
        input->set_aim_y(number(doc.at("aim_y"), -1000, 1000));
        input->set_jump(doc.at("jump").get<bool>());
        input->set_fire(doc.at("fire").get<bool>());
        input->set_reload(doc.at("reload").get<bool>());
        input->set_move_y(number(doc.value("move_y", Json(0)), -1, 1));
        input->set_run(doc.value("run", false));
        input->set_prone(doc.value("prone", false));

    }
    else if (cmd == "bag" || cmd == "pickup" || cmd == "abandon" || cmd == "switch_weapon"
        || cmd == "select_tool" || cmd == "melee" || cmd == "use" || cmd == "interact")
    {
        if (cmd == "pickup")
        {
            fields(doc, {"cmd", "req_id", "world_id", "match_id", "item_id", "action_seq"});
        }
        else
        {
            optional_fields(doc, {"cmd", "req_id", "world_id", "match_id", "action_seq"},
                {"slot", "target_id"});
        }

        auto& req = *msg.mutable_action_req();
        req.set_req_id(req_id(doc.at("req_id")));
        req.set_world_id(id(doc.at("world_id"), true));
        req.set_match_id(id(doc.at("match_id"), true));
        req.set_action_seq(id(doc.at("action_seq"), true));
        const Map<Str, hunter::wire::ActionReq::Kind> kinds = {
            {"bag", hunter::wire::ActionReq::BAG}, {"pickup", hunter::wire::ActionReq::PICKUP},
            {"abandon", hunter::wire::ActionReq::ABANDON},
            {"switch_weapon", hunter::wire::ActionReq::SWITCH_WEAPON},
            {"select_tool", hunter::wire::ActionReq::SELECT_TOOL},
            {"melee", hunter::wire::ActionReq::MELEE}, {"use", hunter::wire::ActionReq::USE},
            {"interact", hunter::wire::ActionReq::INTERACT}};
        req.set_kind(kinds.at(cmd));
        req.set_slot(static_cast<u32>(number(doc.value("slot", Json(0)), 0, 8)));
        req.set_target_id(static_cast<u32>(number(doc.value("target_id", Json(0)), 0, 2147483647)));

        if (cmd == "pickup")
        {
            req.set_item_id(id(doc.at("item_id"), true));
        }
    }
    else if (cmd == "status" || cmd == "retry" || cmd == "result" || cmd == "stash")
    {
        auto& req = *msg.mutable_save_req();
        req.set_req_id(req_id(doc.at("req_id")));

        if (cmd == "stash")
        {
            fields(doc, {"cmd", "req_id", "revision", "cursor", "limit"});
            req.set_kind(hunter::wire::SaveReq::STASH);
            req.set_revision(id(doc.at("revision"), false));
            req.set_cursor(id(doc.at("cursor"), false));
            req.set_limit(static_cast<u32>(number(doc.at("limit"), 0, 128)));
        }
        else if (cmd == "status")
        {
            fields(doc, {"cmd", "req_id"});
            req.set_kind(hunter::wire::SaveReq::STATUS);
        }
        else
        {
            fields(doc, {"cmd", "req_id", "match_id"});
            req.set_kind(cmd == "retry" ? hunter::wire::SaveReq::RETRY
                : hunter::wire::SaveReq::RESULT);
            req.set_match_id(id(doc.at("match_id"), true));
        }
    }
    else
    {
        throw std::runtime_error("unknown_command");
    }

    return msg;
}

// 输出完整实体字段，默认零值和布尔也保留，便于肉眼与自动化工具检查。
Json entity(const hunter::wire::Entity& value)
{
    auto weapons = Json::array();
    auto tools = Json::array();

    for (const auto& gun : value.weapons())
    {
        weapons.push_back({{"slot", gun.slot()}, {"cfg_id", gun.cfg_id()},
            {"ammo_cfg_id", gun.ammo_cfg_id()}, {"ammo", gun.ammo()},
            {"reserve", gun.reserve()}, {"shot_ticks", gun.shot_ticks()},
            {"reload_ticks", gun.reload_ticks()}});
    }

    for (const auto& tool : value.tools())
    {
        tools.push_back({{"slot", tool.slot()}, {"cfg_id", tool.cfg_id()},
            {"count", tool.count()}, {"instance", std::to_string(tool.instance())}});
    }

    return {{"id", std::to_string(value.id())}, {"kind", value.kind()}, {"x", value.x()},
        {"y", value.y()}, {"vx", value.vx()}, {"vy", value.vy()}, {"hp", value.hp()},
        {"max_hp", value.max_hp()}, {"ammo", value.ammo()}, {"reserve", value.reserve()},
        {"reload_ticks", value.reload_ticks()}, {"grounded", value.grounded()},
        {"alive", value.alive()}, {"facing", value.facing()}, {"ai", value.ai()},
        {"cfg_id", std::to_string(value.cfg_id())}, {"attack_ticks", value.attack_ticks()},
        {"prone", value.prone()}, {"running", value.running()}, {"stamina", value.stamina()},
        {"width", value.width()}, {"height", value.height()},
        {"active_weapon", value.active_weapon()}, {"weapons", std::move(weapons)},
        {"tools", std::move(tools)}, {"use_slot", value.use_slot()},
        {"use_ticks", value.use_ticks()}, {"ladder_id", value.ladder_id()},
        {"selected_slot", value.selected_slot()},
        {"health_segments", Vec<u32>(value.health_segments().begin(),
            value.health_segments().end())}};
}

// 把服务端消息转换成一行 JSON；64 位标识统一保持规范十进制字符串。
Json response(const hunter::wire::Envelope& msg)
{
    if (msg.has_hello_ack())
    {
        const auto& value = msg.hello_ack();
        return {{"type", "hello_ack"}, {"protocol_version", value.protocol_version()},
            {"content_version", value.content_version()}, {"instance", value.instance()}};
    }

    if (msg.has_login_rsp())
    {
        const auto& value = msg.login_rsp();
        return {{"type", "login_rsp"}, {"req_id", value.req_id()},
            {"player_id", std::to_string(value.player_id())},
            {"session_id", std::to_string(value.session_id())},
            {"world_id", std::to_string(value.world_id())},
            {"player_entity_id", std::to_string(value.player_entity_id())},
            {"match_id", std::to_string(value.match_id())}, {"phase", value.phase()}};
    }

    if (msg.has_start_rsp())
    {
        const auto& value = msg.start_rsp();
        return {{"type", "start_rsp"}, {"req_id", value.req_id()},
            {"world_id", std::to_string(value.world_id())},
            {"player_entity_id", std::to_string(value.player_entity_id())},
            {"match_id", std::to_string(value.match_id())}, {"phase", value.phase()},
            {"loadout", loadout(value.loadout())}};
    }

    if (msg.has_ack())
    {
        const auto& value = msg.ack();
        return {{"type", "ack"}, {"seq", std::to_string(value.seq())},
            {"match_id", std::to_string(value.match_id())},
            {"applied_tick", std::to_string(value.applied_tick())}};
    }

    if (msg.has_snapshot())
    {
        const auto& value = msg.snapshot();
        auto entities = Json::array();
        auto items = Json::array();
        auto scenes = Json::array();
        auto projectiles = Json::array();

        for (const auto& item : value.scenes())
        {
            scenes.push_back({{"id", item.id()}, {"used", item.used()}});
        }

        for (const auto& item : value.projectiles())
        {
            projectiles.push_back({{"id", std::to_string(item.id())}, {"cfg_id", item.cfg_id()},
                {"x", item.x()}, {"y", item.y()}, {"vx", item.vx()}, {"vy", item.vy()},
                {"remaining", item.remaining()}});
        }

        for (const auto& item : value.items())
        {
            items.push_back({{"item_id", std::to_string(item.item_id())},
                {"cfg_id", item.cfg_id()}, {"count", item.count()}, {"place", item.place()},
                {"owner_player_id", std::to_string(item.owner_player_id())},
                {"x", item.x()}, {"y", item.y()}});
        }

        for (const auto& item : value.entities())
        {
            entities.push_back(entity(item));
        }

        return {{"type", "snapshot"}, {"tick_id", std::to_string(value.tick_id())},
            {"seq", std::to_string(value.seq())}, {"match_id", std::to_string(value.match_id())},
            {"world_id", std::to_string(value.world_id())},
            {"player_entity_id", std::to_string(value.player_entity_id())},
            {"player_state", value.player_state()}, {"items", std::move(items)},
            {"bag_slots", value.bag_slots()}, {"extract_id", value.extract_id()},
            {"extract_ticks", value.extract_ticks()}, {"extract_reason", value.extract_reason()},
            {"extract_remaining_ticks", value.extract_remaining_ticks()},
            {"extract_unlocked", value.extract_unlocked()},
            {"action_seq", std::to_string(value.action_seq())},
            {"scenes", std::move(scenes)}, {"projectiles", std::move(projectiles)},
            {"phase", value.phase()}, {"entities", std::move(entities)}};
    }

    if (msg.has_event())
    {
        const auto& value = msg.event();
        return {{"type", "event"}, {"match_id", std::to_string(value.match_id())},
            {"event_id", std::to_string(value.event_id())},
            {"tick_id", std::to_string(value.tick_id())}, {"kind", value.kind()},
            {"actor_id", std::to_string(value.actor_id())},
            {"target_id", std::to_string(value.target_id())}, {"x", value.x()},
            {"y", value.y()}, {"amount", value.amount()}};
    }

    if (msg.has_pause())
    {
        const auto& value = msg.pause();
        return {{"type", "pause"}, {"paused", value.paused()},
            {"discard_through_seq", std::to_string(value.discard_through_seq())},
            {"discard_action_seq", std::to_string(value.discard_action_seq())}};
    }

    if (msg.has_error())
    {
        const auto& value = msg.error();
        return {{"type", "error"}, {"code", value.code()}, {"detail", value.detail()},
            {"req_id", value.req_id()}, {"seq", std::to_string(value.seq())},
            {"match_id", std::to_string(value.match_id())}};
    }

    if (msg.has_action_rsp())
    {
        const auto& value = msg.action_rsp();
        return {{"type", "action_rsp"}, {"req_id", value.req_id()},
            {"world_id", std::to_string(value.world_id())},
            {"match_id", std::to_string(value.match_id())},
            {"action_seq", std::to_string(value.action_seq())}};
    }

    if (msg.has_save_rsp())
    {
        const auto& value = msg.save_rsp();
        auto items = Json::array();

        for (const auto& item : value.items())
        {
            items.push_back({{"item_uid", std::to_string(item.item_uid())},
                {"cfg_id", item.cfg_id()}, {"count", item.count()},
                {"acquired_match_id", std::to_string(item.acquired_match_id())}});
        }

        return {{"type", "save_rsp"}, {"req_id", value.req_id()}, {"state", value.state()},
            {"match_id", std::to_string(value.match_id())}, {"result_json", value.result_json()},
            {"revision", std::to_string(value.revision())}, {"error_code", value.error_code()},
            {"last_match_id", std::to_string(value.last_match_id())}, {"items", std::move(items)},
            {"next_cursor", std::to_string(value.next_cursor())}};
    }

    throw std::runtime_error("unexpected_server_message");
}

// 网络仅归属运行线程；输入与输出线程只通过有界文本队列交换拥有的数据。
class Client
{
public:
    // 同时启动可取消管道和网络循环；连接断开或 EOF 后有限等待并回收全部线程。
    i32 run();

private:
    // 从标准输入读取完整有界行，EOF 允许一秒钟接收已经提交命令的响应。
    void read_input();

    // 从有界结果队列写 stdout；失败时请求网络线程停止。
    void write_output();

    // 提交完整输入行；容量不足时拒绝并停止，避免在 Asio 队列积压闭包。
    bool submit(Str line);

    // 网络线程处理 Ready 和已认证会话命令；握手中保留有界命令队列。
    void commands();

    // 校验编译身份和 Ready 字段，再发起回环连接及带令牌的握手。
    void connect(const Json& ready);

    // 在有界发送队列提交生成的 Protobuf 帧，不自定义线格式。
    void send(const hunter::wire::Envelope& msg);

    // 串行提交异步发送，缓存由回调独占直到操作完成。
    void write_frame();

    // 接收固定四字节长度头，在分配正文前检查容量。
    void read_header();

    // 接收完整正文并校验握手响应，随后输出所有服务端消息。
    void read_body(usize size);

    // 加入有界输出队列，stdout 慢读时停止连接而不阻塞网络线程。
    bool emit(const Json& value);

    // 请求网络线程执行统一收尾，可从任意管道线程调用。
    void request_stop(Str reason);

    // 关闭 Socket 和计时器；有原因时输出客户端错误并设置失败返回值。
    void stop(const Str& reason);

    asio::io_context io_;
    asio::executor_work_guard<asio::io_context::executor_type> work_{io_.get_executor()};
    Tcp::socket socket_{io_};
    asio::steady_timer handshake_{io_};
    asio::steady_timer eof_{io_};
    std::array<u8, 4> header_{};
    Str body_;
    Str instance_;
    bool ready_ = false;
    bool authed_ = false;
    u64 action_seq_ = 0;
    bool stopped_ = false;
    i32 result_ = 0;
    std::deque<Ptr<const hunter::Frame>> sends_;
    usize send_bytes_ = 0;
    std::mutex input_mutex_;
    std::deque<Str> inputs_;
    usize input_bytes_ = 0;
    std::mutex output_mutex_;
    std::condition_variable output_cv_;
    std::deque<Str> outputs_;
    usize output_bytes_ = 0;
    std::atomic<bool> read_canceled_ = false;
    std::atomic<bool> write_canceled_ = false;
    std::atomic<bool> read_done_ = false;
    std::atomic<bool> write_done_ = false;
    std::atomic<bool> io_done_ = false;
};

// 有界等待平台同步 I/O 取消，重复取消覆盖进入系统调用前后的竞争窗口。
void join(std::thread& thread, std::atomic<bool>& canceled, const std::atomic<bool>& done)
{
    canceled = true;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);

    while (!done)
    {
        if (std::chrono::steady_clock::now() >= deadline)
        {
            std::_Exit(2);
        }

        CancelSynchronousIo(thread.native_handle());
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    thread.join();
}

i32 Client::run()
{
    std::thread reader([this]
    {
        read_input();
        read_done_ = true;
    });
    std::thread writer([this]
    {
        write_output();
        write_done_ = true;
    });

    try
    {
        io_.run();
    }
    catch (const std::exception& error)
    {
        stop(error.what());
    }

    io_done_ = true;
    output_cv_.notify_all();
    join(reader, read_canceled_, read_done_);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(1);

    while (!write_done_ && std::chrono::steady_clock::now() < deadline)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    join(writer, write_canceled_, write_done_);
    return result_;
}

void Client::read_input()
{
    std::array<char, 4096> bytes{};
    Str line;

    while (!read_canceled_)
    {
        DWORD count = 0;
        const bool read = ReadFile(GetStdHandle(STD_INPUT_HANDLE), bytes.data(),
            static_cast<DWORD>(bytes.size()), &count, nullptr) != 0;

        if (!read || count == 0)
        {
            if (!line.empty() && !submit(std::move(line)))
            {
                return;
            }

            asio::post(io_, [this]
            {
                eof_.expires_after(std::chrono::seconds(1));
                eof_.async_wait([this](const asio::error_code& error)
                {
                    if (!error)
                    {
                        stop("");
                    }
                });
            });
            return;
        }

        for (DWORD i = 0; i < count; ++i)
        {
            if (bytes[i] == '\n')
            {
                if (!line.empty() && line.back() == '\r')
                {
                    line.pop_back();
                }

                if (!submit(std::move(line)))
                {
                    return;
                }

                line.clear();
            }
            else if (line.size() >= max_bytes)
            {
                request_stop("stdin_line_size");
                return;
            }
            else
            {
                line.push_back(bytes[i]);
            }
        }
    }
}

void Client::write_output()
{
    while (!write_canceled_)
    {
        Str line;
        {
            std::unique_lock lock(output_mutex_);
            output_cv_.wait_for(lock, std::chrono::milliseconds(50), [this]
            {
                return !outputs_.empty() || io_done_ || write_canceled_;
            });

            if (outputs_.empty())
            {
                if (io_done_)
                {
                    return;
                }

                continue;
            }

            line = std::move(outputs_.front());
            outputs_.pop_front();
            output_bytes_ -= line.size();
        }

        usize offset = 0;

        while (offset < line.size() && !write_canceled_)
        {
            DWORD count = 0;

            if (!WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), line.data() + offset,
                static_cast<DWORD>(line.size() - offset), &count, nullptr) || count == 0)
            {
                request_stop("stdout_closed");
                return;
            }

            offset += count;
        }
    }
}

bool Client::submit(Str line)
{
    {
        std::lock_guard lock(input_mutex_);

        if (inputs_.size() >= max_count || input_bytes_ + line.size() > queue_bytes)
        {
            request_stop("stdin_backpressure");
            return false;
        }

        input_bytes_ += line.size();
        inputs_.push_back(std::move(line));
    }

    asio::post(io_, [this] { commands(); });
    return true;
}

void Client::commands()
{
    try
    {
        while (!stopped_ && (!ready_ || authed_))
        {
            Str line;
            {
                std::lock_guard lock(input_mutex_);

                if (inputs_.empty())
                {
                    return;
                }

                line = std::move(inputs_.front());
                inputs_.pop_front();
                input_bytes_ -= line.size();
            }

            auto doc = Json::parse(line);
            if (!ready_)
            {
                connect(doc);
            }
            else
            {
                const auto cmd = doc.value("cmd", "");
                const Set<Str> actions = {"bag", "pickup", "abandon", "switch_weapon",
                    "select_tool", "melee", "use", "interact"};

                if (actions.contains(cmd))
                {
                    if (!doc.contains("action_seq"))
                    {
                        if (action_seq_ == std::numeric_limits<u64>::max())
                        {
                            throw std::runtime_error("action_sequence_exhausted");
                        }

                        doc["action_seq"] = std::to_string(++action_seq_);
                    }
                    else
                    {
                        action_seq_ = std::max(action_seq_, id(doc.at("action_seq"), true));
                    }
                }

                send(command(doc));
            }
        }
    }
    catch (const std::exception& error)
    {
        stop(error.what());
    }
}

void Client::connect(const Json& ready)
{
    if (!ready.is_object() || ready.at("type") != "Ready"
        || number(ready.at("protocol_version"), 5, 5) != 5
        || ready.at("content_version").get<Str>() != hunter::content::version)
    {
        throw std::runtime_error("ready_identity_mismatch");
    }

    const auto port = static_cast<u16>(number(ready.at("port"), 1, 65535));
    instance_ = ready.at("instance").get<Str>();
    const auto token = ready.at("token").get<Str>();
    if (instance_.size() != 32 || token.size() != 64)
    {
        throw std::runtime_error("ready_credentials_size");
    }

    hunter::wire::Envelope msg;
    auto* hello = msg.mutable_hello();
    hello->set_protocol_version(5);
    hello->set_content_version(Str(hunter::content::version));
    hello->set_instance(instance_);
    hello->set_token(token);
    ready_ = true;
    handshake_.expires_after(std::chrono::seconds(5));
    handshake_.async_wait([this](const asio::error_code& error)
    {
        if (!error)
        {
            stop("handshake_timeout");
        }
    });
    socket_.async_connect(Tcp::endpoint(asio::ip::address_v4::loopback(), port),
        [this, msg = std::move(msg)](const asio::error_code& error)
    {
        if (error)
        {
            stop("connect_failed");
            return;
        }

        send(msg);
        read_header();
    });
}

void Client::send(const hunter::wire::Envelope& msg)
{
    const auto frame = hunter::encode_frame(msg, max_bytes);
    if (!frame)
    {
        stop(frame.error());
        return;
    }

    if (sends_.size() >= max_count || send_bytes_ + frame->bytes.size() > queue_bytes)
    {
        stop("send_backpressure");
        return;
    }

    send_bytes_ += frame->bytes.size();
    sends_.push_back(std::make_shared<hunter::Frame>(std::move(*frame)));

    if (sends_.size() == 1)
    {
        write_frame();
    }
}

void Client::write_frame()
{
    const auto frame = sends_.front();
    asio::async_write(socket_, asio::buffer(frame->bytes),
        [this, frame](const asio::error_code& error, usize)
    {
        if (error)
        {
            stop(error == asio::error::operation_aborted ? "" : "send_failed");
            return;
        }

        send_bytes_ -= frame->bytes.size();
        sends_.pop_front();

        if (!sends_.empty())
        {
            write_frame();
        }
    });
}

void Client::read_header()
{
    asio::async_read(socket_, asio::buffer(header_),
        [this](const asio::error_code& error, usize)
    {
        if (error)
        {
            stop(error == asio::error::eof || error == asio::error::operation_aborted
                ? "" : "receive_failed");
            return;
        }

        u32 size = 0;

        for (const auto byte : header_)
        {
            size = (size << 8) | byte;
        }

        if (size == 0 || size > max_bytes)
        {
            stop("server_frame_size");
            return;
        }

        read_body(size);
    });
}

void Client::read_body(usize size)
{
    body_.resize(size);
    asio::async_read(socket_, asio::buffer(body_),
        [this](const asio::error_code& error, usize)
    {
        if (error)
        {
            stop("incomplete_server_frame");
            return;
        }

        const auto msg = hunter::decode_frame(body_, max_bytes);
        if (!msg)
        {
            stop(msg.error());
            return;
        }

        if (!authed_)
        {
            if (!msg->has_hello_ack() || msg->hello_ack().protocol_version() != 5
                || msg->hello_ack().content_version() != hunter::content::version
                || msg->hello_ack().instance() != instance_)
            {
                stop("hello_ack_mismatch");
                return;
            }

            authed_ = true;
            handshake_.cancel();
        }
        else if (msg->has_hello_ack())
        {
            stop("duplicate_hello_ack");
            return;
        }

        try
        {
            if (!emit(response(*msg)))
            {
                stop("stdout_backpressure");
                return;
            }
        }
        catch (const std::exception& caught)
        {
            stop(caught.what());
            return;
        }

        commands();

        if (!stopped_)
        {
            read_header();
        }
    });
}

bool Client::emit(const Json& value)
{
    auto line = value.dump() + "\n";
    {
        std::lock_guard lock(output_mutex_);

        if (outputs_.size() >= max_count || output_bytes_ + line.size() > queue_bytes)
        {
            return false;
        }

        output_bytes_ += line.size();
        outputs_.push_back(std::move(line));
    }

    output_cv_.notify_one();
    return true;
}

void Client::request_stop(Str reason)
{
    asio::post(io_, [this, reason = std::move(reason)] { stop(reason); });
}

void Client::stop(const Str& reason)
{
    if (stopped_)
    {
        return;
    }

    stopped_ = true;

    if (!reason.empty())
    {
        result_ = 1;
        emit({{"type", "client_error"}, {"detail", reason.substr(0, 1024)}});
    }

    asio::error_code ignored;
    socket_.cancel(ignored);
    socket_.close(ignored);
    handshake_.cancel();
    eof_.cancel();
    work_.reset();
}
}

// 使用标准输入提供 Ready 与命令，标准输出仅包含可逐行解析的 JSON。
int main()
{
    Client client;
    return client.run();
}

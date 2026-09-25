// 使用构建生成的有限 schema 校验输出，不持有或修改脚本权威世界。
#include "common/Types.h"
#include "script/Schema.h"
#include "SchemaSpec.h"

#include <charconv>
#include <limits>

namespace hunter
{
namespace
{

// 读取规范十进制 ID，拒绝符号、前导零、浮点及超出契约的整数。
bool normalize_id(nlohmann::json& value, const nlohmann::json& spec)
{
    if (!value.is_string())
    {
        return false;
    }

    const auto& text = value.get_ref<const Str&>();

    if (text.empty() || (text.size() > 1 && text.front() == '0'))
    {
        return false;
    }

    u64 id = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), id);
    const auto min = std::stoull(spec.at("min").get<Str>());
    const auto max = std::stoull(spec.at("max").get<Str>());

    if (result.ec != std::errc{} || result.ptr != text.data() + text.size()
        || id < min || id > max)
    {
        return false;
    }

    value = id;
    return true;
}

// 校验整数边界或有限枚举，禁止无符号溢出与浮点值隐式截断。
bool valid_int(const nlohmann::json& value, const nlohmann::json& spec)
{
    if (!value.is_number_integer() || (value.is_number_unsigned()
        && value.get<u64>() > static_cast<u64>(std::numeric_limits<i64>::max())))
    {
        return false;
    }

    const auto number = value.get<i64>();
    if (number < spec.at("min").get<i64>() || number > spec.at("max").get<i64>())
    {
        return false;
    }

    if (spec.contains("values"))
    {
        for (const auto& item : spec.at("values"))
        {
            if (number == item.get<i64>())
            {
                return true;
            }
        }

        return false;
    }

    return true;
}

// 校验 UTF-8 字符串的字节限额与有限枚举。
bool valid_string(const nlohmann::json& value, const nlohmann::json& spec)
{
    if (!value.is_string())
    {
        return false;
    }

    const auto& text = value.get_ref<const Str&>();

    if (text.size() < spec.at("min").get<usize>() || text.size() > spec.at("max").get<usize>())
    {
        return false;
    }

    if (spec.contains("values"))
    {
        for (const auto& item : spec.at("values"))
        {
            if (text == item.get_ref<const Str&>())
            {
                return true;
            }
        }

        return false;
    }

    return true;
}

// 遍历已在构建期限定深度和形状的描述；对象精确匹配，数组逐项有界检查。
bool normalize(nlohmann::json& value, const nlohmann::json& spec)
{
    const auto& type = spec.at("type").get_ref<const Str&>();

    if (type == "id")
    {
        return normalize_id(value, spec);
    }

    if (type == "int")
    {
        return valid_int(value, spec);
    }

    if (type == "string")
    {
        return valid_string(value, spec);
    }

    if (type == "bool")
    {
        return value.is_boolean();
    }

    if (type == "array")
    {
        if (!value.is_array() || value.size() > spec.at("max").get<usize>())
        {
            return false;
        }

        for (auto& item : value)
        {
            if (!normalize(item, spec.at("item")))
            {
                return false;
            }
        }

        return true;
    }

    if (type == "object")
    {
        const auto& fields = spec.at("fields");

        if (!value.is_object() || value.size() != fields.size())
        {
            return false;
        }

        for (auto field = fields.begin(); field != fields.end(); ++field)
        {
            if (!value.contains(field.key()) || !normalize(value[field.key()], field.value()))
            {
                return false;
            }
        }

        return true;
    }

    return false;
}

// 将已校验的实体投影写入 Protobuf，不保留可独立修改的实体状态。
void encode_entity(const nlohmann::json& obj, wire::Entity& entity)
{
    entity.set_id(obj.at("id").get<u64>());
    entity.set_kind(obj.at("kind").get<Str>());
    entity.set_cfg_id(obj.at("cfg_id").get<u32>());
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

ScriptOut::ScriptOut(wire::Envelope value) : message(std::move(value))
{
    if (message.has_ack())
    {
        kind = "ack";
    }

    if (message.has_snapshot())
    {
        kind = "snapshot";
    }

    if (message.has_login_rsp())
    {
        kind = "login";
    }

    if (message.has_start_rsp())
    {
        kind = "start";
    }

    if (message.has_event())
    {
        kind = "event";
    }

    if (message.has_error())
    {
        kind = "error";
    }

}

ScriptOut::ScriptOut(Str name, const Str& payload) : kind(std::move(name))
{
    try
    {
        static const auto specs = nlohmann::json::parse(schema::outputs);
        auto obj = nlohmann::json::parse(payload, nullptr, false);
        if (!specs.contains(kind) || !normalize(obj, specs.at(kind)))
        {
            error = "output_schema";
            return;
        }

        message = encode_output(kind, obj);
    }
    catch (const std::exception&)
    {
        error = "output_schema";
    }
}

std::expected<void, Str> validate_output(const ScriptOut& out, usize max_bytes)
{
    if (!out.error.empty() || !schema::valid_output(out.message)
        || out.message.ByteSizeLong() > max_bytes)
    {
        return std::unexpected("output_schema");
    }

    return {};
}

nlohmann::json output_json(const ScriptOut& out)
{
    nlohmann::json result = {{"v", 4}};

    if (out.message.has_ack())
    {
        const auto& msg = out.message.ack();
        result["seq"] = std::to_string(msg.seq());
        result["match_id"] = std::to_string(msg.match_id());
        result["applied_tick"] = std::to_string(msg.applied_tick());
    }

    if (out.message.has_snapshot())
    {
        const auto& msg = out.message.snapshot();
        result["tick_id"] = std::to_string(msg.tick_id());
        result["seq"] = std::to_string(msg.seq());
        result["match_id"] = std::to_string(msg.match_id());
        result["phase"] = msg.phase();
        result["entities"] = nlohmann::json::array();

        for (const auto& entity : msg.entities())
        {
            nlohmann::json value;
            value["id"] = std::to_string(entity.id());
            value["kind"] = entity.kind();
            value["x"] = entity.x();
            value["y"] = entity.y();
            value["vx"] = entity.vx();
            value["vy"] = entity.vy();
            value["hp"] = entity.hp();
            value["max_hp"] = entity.max_hp();
            value["ammo"] = entity.ammo();
            value["reserve"] = entity.reserve();
            value["reload_ticks"] = entity.reload_ticks();
            value["grounded"] = entity.grounded();
            value["alive"] = entity.alive();
            value["facing"] = entity.facing();
            value["ai"] = entity.ai();
            value["cfg_id"] = std::to_string(entity.cfg_id());
            result["entities"].push_back(std::move(value));
        }
    }

    if (out.message.has_login_rsp())
    {
        const auto& msg = out.message.login_rsp();
        result["req_id"] = msg.req_id();
        result["player_id"] = std::to_string(msg.player_id());
        result["match_id"] = std::to_string(msg.match_id());
        result["phase"] = msg.phase();
    }

    if (out.message.has_start_rsp())
    {
        const auto& msg = out.message.start_rsp();
        result["req_id"] = msg.req_id();
        result["match_id"] = std::to_string(msg.match_id());
        result["phase"] = msg.phase();
    }

    if (out.message.has_event())
    {
        const auto& msg = out.message.event();
        result["match_id"] = std::to_string(msg.match_id());
        result["event_id"] = std::to_string(msg.event_id());
        result["tick_id"] = std::to_string(msg.tick_id());
        result["kind"] = msg.kind();
        result["actor_id"] = std::to_string(msg.actor_id());
        result["target_id"] = std::to_string(msg.target_id());
        result["x"] = msg.x();
        result["y"] = msg.y();
        result["amount"] = msg.amount();
    }

    if (out.message.has_error())
    {
        const auto& msg = out.message.error();
        result["code"] = msg.code();
        result["detail"] = msg.detail();
        result["req_id"] = msg.req_id();
        result["seq"] = std::to_string(msg.seq());
        result["match_id"] = std::to_string(msg.match_id());
    }

    return result;
}
}

// 依据 lua/contract.json 生成的常量执行共享输出校验，避免桥接与协议各自解释。
#include "common/Types.h"
#include "script/Schema.h"
#include "SchemaSpec.h"

#include <nlohmann/json.hpp>

#include <charconv>
#include <limits>
#include <span>
#include <string_view>

namespace hunter
{
namespace
{

// 对象必须精确包含契约字段，既不允许遗漏也不允许附加字段。
bool has_fields(const nlohmann::json& obj, std::span<const std::string_view> fields)
{
    if (!obj.is_object() || obj.size() != fields.size())
    {
        return false;
    }

    for (const auto field : fields)
    {
        if (!obj.contains(field))
        {
            return false;
        }
    }

    return true;
}

// 读取规范十进制并检查领域范围，拒绝符号、前导零和浮点转换。
std::expected<u64, Str> parse_id(const nlohmann::json& value, u64 min, u64 max)
{
    if (!value.is_string())
    {
        return std::unexpected("output_id_type");
    }

    const auto& text = value.get_ref<const Str&>();
    if (text.empty() || (text.size() > 1 && text.front() == '0'))
    {
        return std::unexpected("output_id_encoding");
    }

    u64 id = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), id);
    if (result.ec != std::errc{} || result.ptr != text.data() + text.size() || id < min || id > max)
    {
        return std::unexpected("output_id_range");
    }

    return id;
}

}

std::expected<ScriptMsg, Str> decode_output(const ScriptOut& out, usize max_json_bytes)
{
    if (out.payload.size() > max_json_bytes)
    {
        return std::unexpected("output_json_size");
    }

    try
    {
        const auto obj = nlohmann::json::parse(out.payload, nullptr, false);
        const bool snapshot = out.kind == "snapshot";
        if ((out.kind != "ack" && !snapshot) ||
            !(snapshot ? has_fields(obj, schema::snapshot_fields) :
                has_fields(obj, schema::ack_fields)))
        {
            return std::unexpected("output_fields");
        }

        const auto& version = obj["v"];
        if (!version.is_number_integer() || (version.is_number_unsigned() ?
            version.get<u64>() != static_cast<u64>(schema::version) :
            version.get<i64>() != schema::version))
        {
            return std::unexpected("output_version");
        }

        const auto& count = obj["count"];
        if (!count.is_number_integer() || (count.is_number_unsigned() &&
            count.get<u64>() > static_cast<u64>(std::numeric_limits<i64>::max())))
        {
            return std::unexpected("output_count_range");
        }

        const i64 count_value = count.get<i64>();
        if (count_value < schema::min_count || count_value > schema::max_count)
        {
            return std::unexpected("output_count_range");
        }

        auto seq = parse_id(obj["seq"], snapshot ? 0 : schema::min_ack_seq, schema::max_seq);
        if (!seq)
        {
            return std::unexpected(seq.error());
        }

        ScriptMsg msg;
        msg.snapshot = snapshot;
        msg.count = count_value;
        msg.seq = *seq;

        if (snapshot)
        {
            auto tick = parse_id(obj["tick_id"], 0, schema::max_tick);
            if (!tick)
            {
                return std::unexpected(tick.error());
            }

            msg.tick_id = *tick;
        }

        return msg;
    }
    catch (const std::exception&)
    {
        return std::unexpected("output_schema");
    }
}

}

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

}

std::expected<nlohmann::json, Str> decode_output(const ScriptOut& out, usize max_json_bytes)
{
    if (out.payload.size() > max_json_bytes)
    {
        return std::unexpected("output_json_size");
    }

    try
    {
        static const auto specs = nlohmann::json::parse(schema::outputs);
        auto obj = nlohmann::json::parse(out.payload, nullptr, false);

        if (!specs.contains(out.kind) || !normalize(obj, specs.at(out.kind)))
        {
            return std::unexpected("output_schema");
        }

        return obj;
    }
    catch (const std::exception&)
    {
        return std::unexpected("output_schema");
    }
}

}

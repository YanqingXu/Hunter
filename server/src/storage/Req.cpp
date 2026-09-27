// 实现数据库无关的请求校验和确定性编码，保留持久请求的原有字节及整数语义。
#include "common/Types.h"
#include "storage/Req.h"
#include "storage/Error.h"

#include <nlohmann/json.hpp>

namespace hunter::storage
{

using Json = nlohmann::json;

namespace req
{

void valid_id(u64 value)
{
    if (value == 0 || value > static_cast<u64>(max_value))
    {
        fail(Code::Invalid, "persistent_id_range");
    }
}

void ensure(bool ok, const char* message, Code code)
{
    if (!ok)
    {
        fail(code, message);
    }
}

i64 number(const Json& value, i64 low, i64 high)
{
    ensure(value.is_number_integer() && (!value.is_number_unsigned()
        || value.get<u64>() <= static_cast<u64>(max_value)), "hunter_integer");
    const auto result = value.get<i64>();
    ensure(result >= low && result <= high, "hunter_integer_range");
    return result;
}

i64 identity(u64 value)
{
    ensure(value > 0 && value <= static_cast<u64>(max_value), "hunter_identity");
    return static_cast<i64>(value);
}

Str text_value(const Json& value, usize limit)
{
    ensure(value.is_string(), "hunter_text_type");
    const auto result = value.get<Str>();
    ensure(!result.empty() && result.size() <= limit && result.find('\0') == Str::npos,
        "hunter_text_range");
    return result;
}

}

namespace
{

using req::ensure;
using req::identity;
using req::max_payload;
using req::number;
using req::text_value;
using req::valid_id;

// 检查持久文本最小约束，UTF-8 由规范 JSON 编码器进一步验证。
void valid_text(const Str& value)
{
    if (value.empty() || value.find('\0') != Str::npos)
    {
        fail(Code::Invalid, "persistent_text_empty_or_nul");
    }
}

// 校验闭集字段，禁止缺省参数掩盖未填写的经济配置。
void fields(const Json& value, std::initializer_list<const char*> names)
{
    ensure(value.is_object() && value.size() == names.size(), "hunter_payload_fields");

    for (const auto name : names)
    {
        ensure(value.contains(name), "hunter_payload_missing_field");
    }
}

// 校验有界列表，在任何写操作前限制工作量。
void array(const Json& value, usize limit)
{
    ensure(value.is_array() && value.size() <= limit, "hunter_array_limit");
}

}

Str encode_req(const CommitMatch& req)
{
    valid_id(req.match_id);
    valid_id(req.player_id);
    valid_id(req.expected_revision);
    valid_text(req.outcome);
    valid_text(req.content_key);
    Json items = Json::array();

    for (const auto& item : req.items)
    {
        if (item.cfg_id == 0 || item.count <= 0)
        {
            fail(Code::Invalid, "invalid_item_delta");
        }

        items.push_back({{"cfg_id", item.cfg_id}, {"count", item.count}});
    }

    try
    {
        return Json({{"v", 1}, {"match_id", req.match_id}, {"player_id", req.player_id},
            {"expected_revision", req.expected_revision}, {"outcome", req.outcome},
            {"content_key", req.content_key}, {"items", std::move(items)}}).dump();
    }
    catch (const Json::exception&)
    {
        fail(Code::Invalid, "invalid_request_utf8");
    }
}

Str encode_hunter(const HunterReq& req)
{
    identity(req.player_id);
    identity(req.expected_revision);
    text_value(req.op_id, 128);
    ensure(req.intent_json.size() <= max_payload, "hunter_intent_limit", Code::TooLarge);
    const auto value = hunter_payload(req);
    auto encoded = Json({{"v", 2}, {"player_id", req.player_id},
        {"expected_revision", req.expected_revision}, {"hunter_id", req.hunter_id},
        {"op_id", req.op_id}, {"kind", req.kind}, {"payload", value},
        {"intent", req.intent_json}}).dump();
    ensure(encoded.size() <= max_payload * 2 + 1024, "hunter_request_limit", Code::TooLarge);
    return encoded;
}

Json hunter_payload(const HunterReq& req)
{
    ensure(req.payload_json.size() <= max_payload, "hunter_payload_limit", Code::TooLarge);
    auto value = Json::parse(req.payload_json, nullptr, false);
    ensure(!value.is_discarded(), "hunter_payload_json");

    if (req.kind == "recruit")
    {
        fields(value, {"cfg_id", "level", "xp", "points", "currency_cost", "skills"});
        number(value["cfg_id"], 1, 2147483647);
        number(value["level"], 1, 1000000);
        number(value["xp"]);
        number(value["points"]);
        number(value["currency_cost"]);
        array(value["skills"], 64);
        Set<i64> skills;

        for (const auto& skill : value["skills"])
        {
            ensure(skills.insert(number(skill, 1, 2147483647)).second, "duplicate_skill");
        }

        ensure(req.hunter_id == 0, "recruit_requires_no_hunter");
    }
    else if (req.kind == "equip")
    {
        fields(value, {"items"});
        array(value["items"], 16);
        Set<i64> slots, items;

        for (const auto& item : value["items"])
        {
            fields(item, {"slot", "item_uid"});
            ensure(slots.insert(number(item["slot"], 1, 16)).second, "duplicate_slot");
            ensure(items.insert(number(item["item_uid"], 1)).second, "duplicate_item");
        }
    }
    else if (req.kind == "buy_skill" || req.kind == "remove_skill")
    {
        const auto key = req.kind == "buy_skill" ? "cost" : "refund";
        fields(value, {"cfg_id", key});
        number(value["cfg_id"], 1, 2147483647);
        number(value[key]);
    }
    else if (req.kind == "retire")
    {
        fields(value, {"max_level", "account_xp"});
        number(value["max_level"], 1, 1000000);
        number(value["account_xp"]);
    }
    else if (req.kind == "begin_raid")
    {
        fields(value, {"content_key"});
        text_value(value["content_key"], 256);
    }
    else if (req.kind == "recover_raid")
    {
        fields(value, {"match_id"});
        number(value["match_id"], 1);
    }
    else if (req.kind == "finish_raid")
    {
        fields(value, {"match_id", "outcome", "content_key", "level", "xp", "points",
            "account_xp", "currency_gain", "skills", "equipment", "items"});
        number(value["match_id"], 1);
        text_value(value["content_key"], 256);
        const auto outcome = text_value(value["outcome"], 16);
        ensure(outcome == "Extracted" || outcome == "Dead" || outcome == "Abandoned",
            "hunter_outcome");
        number(value["level"], outcome == "Extracted" ? 1 : 0, 1000000);

        for (const auto key : {"xp", "points", "account_xp", "currency_gain"})
        {
            number(value[key]);
        }

        array(value["skills"], 64);
        array(value["equipment"], 16);
        array(value["items"], 64);
        Set<i64> skills, items;

        for (const auto& skill : value["skills"])
        {
            fields(skill, {"cfg_id", "paid_cost", "source"});
            ensure(skills.insert(number(skill["cfg_id"], 1, 2147483647)).second,
                "duplicate_skill");
            number(skill["paid_cost"]);
            const auto source = text_value(skill["source"], 16);
            ensure(source == "recruit" || source == "purchased" || source == "loot",
                "hunter_skill_source");
        }

        for (const auto& item : value["equipment"])
        {
            fields(item, {"item_uid", "count"});
            ensure(items.insert(number(item["item_uid"], 1)).second, "duplicate_item");
            number(item["count"], 1, 2147483647);
        }

        for (const auto& item : value["items"])
        {
            fields(item, {"cfg_id", "count"});
            number(item["cfg_id"], 1, 2147483647);
            number(item["count"], 1, 2147483647);
        }

        if (outcome != "Extracted")
        {
            ensure(value["skills"].empty() && value["equipment"].empty()
                && value["items"].empty(), "dead_hunter_assets");

            for (const auto key : {"level", "xp", "points", "account_xp", "currency_gain"})
            {
                ensure(value[key] == 0, "dead_hunter_reward");
            }
        }
    }
    else
    {
        fail(Code::Invalid, "unknown_hunter_operation");
    }

    if (req.kind != "recruit")
    {
        identity(req.hunter_id);
    }

    return value;
}

}

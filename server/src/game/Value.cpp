// 在原生边界检查线程、写权限以及整数精度。
#include "game/Value.h"
#include "common/Types.h"
#include <charconv>
#include <stdexcept>
#include <limits>

namespace hunter
{
void require(bool valid, const Str& error)
{
    if (!valid)
    {
        throw std::invalid_argument(error);
    }
}

void Access::read() const
{
    require(owner == std::this_thread::get_id(), "wrong_thread");
    require(alive, "stale_session");
}

void Access::write() const
{
    read();
    require(writable, "readonly_state");
}

void range(i64 value, i64 low, i64 high)
{
    require(value >= low && value <= high, "value_out_of_range");
}

i32 read_integer(const nlohmann::json& value, i64 low, i64 high)
{
    require(value.is_number_integer() && (!value.is_number_unsigned()
        || value.get<u64>() <= static_cast<u64>(std::numeric_limits<i64>::max())),
        "invalid_integer");
    const auto number = value.get<i64>();
    range(number, low, high);
    return static_cast<i32>(number);
}

void read_fields(const nlohmann::json& value, std::initializer_list<const char*> names)
{
    require(value.is_object() && value.size() == names.size(), "invalid_fields");

    for (const auto name : names)
    {
        require(value.contains(name), "missing_field");
    }
}

u64 read_id(const Str& text)
{
    require(!text.empty() && (text.size() == 1 || text.front() != '0'), "invalid_id");
    u64 value = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), value);
    require(result.ec == std::errc{} && result.ptr == text.data() + text.size(), "invalid_id");
    return value;
}
}

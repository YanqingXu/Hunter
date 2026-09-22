// 在原生边界检查线程、写权限以及整数精度。
#include "game/Value.h"
#include "common/Types.h"
#include <charconv>
#include <stdexcept>

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

u64 read_id(const Str& text)
{
    require(!text.empty() && (text.size() == 1 || text.front() != '0'), "invalid_id");
    u64 value = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), value);
    require(result.ec == std::errc{} && result.ptr == text.data() + text.size(), "invalid_id");
    return value;
}
}

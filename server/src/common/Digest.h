// 计算内容身份所需的确定性 SHA-256，不解释配置或持有玩法数据。
#pragma once

#include "common/Types.h"
#include <string_view>

namespace hunter
{
// 对完整字节序列返回小写十六进制 SHA-256。
Str digest(StrView text);
}

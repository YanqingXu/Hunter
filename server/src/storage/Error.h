// 提供数据库无关的内部失败边界，由异步门面转换为拥有型完成结果。
#pragma once

#include "common/Types.h"
#include "storage/Model.h"

namespace hunter::storage
{

// 抛出有界业务错误，尚未进入后端的校验不附加驱动来源。
[[noreturn]] void fail(Code code, const Str& message);

}

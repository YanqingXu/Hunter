// 实现共享校验的有界错误，不依赖具体数据库。
#include "common/Types.h"
#include "storage/Error.h"

namespace hunter::storage
{

void fail(Code code, const Str& message)
{
    throw Error{code, 0, false, message.substr(0, 512), {}};
}

}

// 仅故障测试版本声明检查点；正式存储库不包含此入口或任何环境变量开关。
#pragma once

#include "common/Types.h"
#include "storage/Db.h"

namespace hunter::storage::test
{

// 在真实事务边界暂停进程或注入 SQLite 文件增长失败，由测试程序定义。
void point(const char* stage, Db& db);

}

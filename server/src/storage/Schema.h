// 管理独立存档版本及首次建库，拒绝未知或损坏文件而不覆盖。
#pragma once

#include "common/Types.h"
#include "storage/Db.h"

namespace hunter::storage
{

// 检查已有结构和完整性，配置 WAL/FULL 后仅对空库执行原子初始化。
void open_schema(Db& db);

// 返回持久化使用的系统时钟毫秒，不参与模拟或超时判断。
i64 wall_ms();

}

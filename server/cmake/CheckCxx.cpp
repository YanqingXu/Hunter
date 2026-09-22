// 验证目标工具链支持项目短类型及所需 C++23 标准库能力。
#include "common/Types.h"

#include <expected>
#include <thread>
#include <type_traits>

// 编译并链接最小能力探针；线程对象在函数退出时回收。
int main()
{
    static_assert(sizeof(i32) == 4 && sizeof(u64) == 8);
    static_assert(std::is_same_v<usize, decltype(sizeof(char))>);
    std::expected<i32, i32> value(1);
    std::jthread thread([]
    {
    });
    return *value - 1;
}

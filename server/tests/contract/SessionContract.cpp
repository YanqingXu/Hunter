// 验证替身身份、接收者与过期会话和世界的隔离，不代表多人运行验收。
#include "common/Types.h"
#include "core/Session.h"
#include <iostream>
#include <stdexcept>

namespace
{
// 断言公开行为，失败使测试进程返回非零。
void check(bool valid, const char* reason)
{
    if (!valid)
    {
        throw std::runtime_error(reason);
    }
}
}

// 使用非默认身份验证单人会话容量与明确路由。
int main()
{
    try
    {
        hunter::Session session;
        check(!session.enter(3, 99), "unauthenticated enter");
        check(!session.bind(0, 7), "zero session");
        check(session.bind(41, 73), "bind alternative identity");
        check(session.bind(41, 73), "idempotent binding");
        check(!session.bind(42, 73) && !session.bind(41, 74), "single player admission");
        check(session.enter(5, 101), "enter persistent match");
        auto ctx = session.resolve(41, 5, 101);
        check(ctx && ctx->player_id == 73, "server identity context");
        check(session.accepts(41) && !session.accepts(42), "recipient isolation");
        check(session.enter(6, 105), "persistent IDs may have gaps");
        check(!session.resolve(41, 5, 101), "old world rejected");
        session.clear();
        check(!session.resolve(41, 6, 105), "old session rejected");
        check(!session.accepts(41), "closed receiver rejected");
        std::cout << "session contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

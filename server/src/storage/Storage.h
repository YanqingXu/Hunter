// 对逻辑线程提供异步存档入口，工作线程独占 SQLite，完成通过 Asio 延后交付。
#pragma once

#include "common/Types.h"
#include "storage/Model.h"

#include <asio/io_context.hpp>

namespace hunter::storage
{

class Storage
{
public:
    using Done = Func<void(Rsp)>;

    // 在所属逻辑线程创建；io 必须在该线程运行且活过 Closed 和所有回调。
    Storage(asio::io_context& io, StorageCfg cfg, u64 instance);

    // 保底停止并等待工作退出；正常流程应先 stop 并驱动 io 至 Closed。
    ~Storage();

    // 连接、线程和关联表不能复制或转移到其他逻辑线程。
    Storage(const Storage&) = delete;

    // 连接、线程和关联表不能复制赋值。
    Storage& operator=(const Storage&) = delete;

    // 异步打开显式 UTF-8 文件路径；每个对象只允许一次已接受的打开操作。
    Expect<Accepted, Error> open(Str path, Done done);

    // 读取拥有全部物品的永久玩家；超限返回错误而不截断，回调不得抛异常。
    Expect<Accepted, Error> load_player(u64 player_id, Done done);

    // 事务分配跨启动不复用的对局 ID，只有完成成功时才可使用。
    Expect<Accepted, Error> alloc_match(Done done);

    // 接收完整结算值；Accepted 不代表已保存，相同请求可安全重试。
    Expect<Accepted, Error> commit_match(CommitMatch req, Done done);

    // 查询已提交结果；不存在返回 NotFound，不恢复未完成对局。
    Expect<Accepted, Error> find_match(u64 match_id, Done done);

    // 独立关闭槽不受容量饱和影响；拒绝新任务，完成全部回调后关闭并通知。
    // 重复 stop 返回 Closed 错误；已接受写入不取消，调用方继续驱动 io。
    Expect<Accepted, Error> stop(Done done);

private:
    struct Impl;
    Ptr<Impl> impl_;
};

}

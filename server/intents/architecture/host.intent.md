---
id: SRV-001
status: active
target: ["hunter_server_desktop"]
depends_on: ["SRV-002", "SRV-003"]
verification: ["hunter_process_integration"]
---

# 平台宿主与生命周期

## 目标与非目标

Windows 宿主提供逐行 JSON 控制与可取消的标准管道，并将所有业务调度交给逻辑线程。
Android Service、Binder、AAR 条款保持 draft；本契约的 active 仅覆盖桌面宿主。

## 不变量、线程与所有权

逻辑线程独占 Asio、Socket 和 Luax；读写标准管道不阻塞逻辑线程。
跨线程命令和事件拥有自己的字符串，按数量与字节同时限额，唤醒合并。
控制容量内预留一条、512 字节的终态槽；饱和时发布明确故障，不丢弃已有控制响应。
Ready 只在脚本初始化与回环监听全部成功后发布。实例和令牌由系统随机源生成。

## 接口与值语义

stdin 接收 `{"cmd":"Start|Pause|Resume|Stop","req_id":"..."}`；req_id 最长 128 字节。
stdout 返回 `type/req_id/state/session`；Ready 额外包含 `port/instance/token/protocol_version`
与 `content_version`。stderr 仅用于诊断。Start 在 Ready 时幂等返回当前 Ready。
宿主状态为 Starting/Ready/Stopping/Stopped/Faulted，会话为 Idle/Running/Paused/Aborted。
断连后不续接旧局；需重新启动服务进程。

## 失败、取消与退出

输入非法、命令队列满、状态不适用时明确返回错误；输出队列满则故障停止。
Stop、EOF 和管道断开均停止监听、取消网络与定时器，并由所属线程释放脚本。
Stop 可重复调用；停止后拒绝新命令。管道阻塞时 Windows 取消同步 I/O，收尾有时限。
逻辑资源释放后，如系统在退出时限内仍未完成管道取消，以退出码 2 终止宿主进程。
输入超长或命令拒绝时先发布独立停止标志，诊断仅经有界输出线程交付；stderr 阻塞不得阻止停止。
脚本 shutdown 的 false、执行或关闭失败仍尽力释放 VM，返回 Faulted 和有界诊断，进程退出码为 1。
退出码由运行时故障标志决定，不依赖父进程是否及时读取 stdout/stderr。
Asio 回调抛出非预期 C++ 异常时，在逻辑对象仍存活期间收集脚本退出结果与日志并停止资源，
然后保留原异常报告 runtime_failed；次生收尾错误只能增加诊断，不替换首个故障。
命令行限额只接受完整无符号十进制：握手／退出时限为 1～60000 ms，命令数量为 1～65536，
发送字节为 1～16777216；拒绝符号、空白、尾随文本、零与溢出。

## 验证入口与依赖

`hunter_process_integration` 启动真实进程，覆盖 Ready、完整闭环、暂停、EOF 和连续十次释放。
依赖 SRV-002 的传输与 SRV-003 的调度；真机与 Unity 联调尚未验证。

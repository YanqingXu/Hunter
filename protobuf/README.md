# 框架探针协议

`hunter.proto` 是 C++ 与 C# 唯一协议来源。从服务端开发构建运行
`cmake --build server/build/win-dev --config Debug --target hunter_proto_csharp`，生成文件位于
`server/build/win-dev/generated/`；C# 文件位于其 `csharp/` 子目录，不提交生成产物。

每个 TCP 帧由四字节大端无符号长度和 Envelope 的 Protobuf 字节组成。长度不能为零，默认
不超过 64 KiB。一个连接只代表一个会话；握手失败、断开或脚本失败后重新启动服务进程。

| Envelope 字段 | 方向 | 含义 |
| --- | --- | --- |
| hello = 1 | 客户端 → 服务端 | 精确携带 Ready 的版本、内容版本、实例和令牌 |
| hello_ack = 2 | 服务端 → 客户端 | 握手成功，返回版本、内容版本和实例 |
| input = 3 | 客户端 → 服务端 | seq 正数且递增；value 为 [-1000,1000] 的框架计数增量 |
| ack = 4 | 服务端 → 客户端 | 脚本实际处理的 seq 与处理后计数 |
| snapshot = 5 | 服务端 → 客户端 | 脚本 Tick、最后输入序号与计数；未发送快照可合并 |
| error = 6 | 服务端 → 客户端 | 明确协议错误码；坏帧和认证失败直接关闭连接 |

协议版本是 1，内容版本默认 `framework-v1`。计数值为 `sint64`，用 ZigZag 编码；序号和 Tick
使用 `uint64`。输入 `seq=1,value=-1` 的完整帧十六进制为 `000000061a0408011001`，C++ 与
Python 独立探针都检查这一黄金样例。该样例属于调度框架，不代表移动、战斗或 Unity 联调验收。

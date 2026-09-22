# 本机基础战斗协议

`hunter.proto` 是 C++ 与 C# 唯一协议来源。服务端构建生成两端代码，
C# 文件位于 `server/build/win-dev/generated/csharp/`，不手改或提交生成产物。

每个 TCP 帧由四字节大端无符号长度和 Envelope 的 Protobuf 字节组成。长度不能为零，默认
不超过 64 KiB。只监听回环地址；一个连接对应一个会话。协议版本为 3，内容版本来自导出数据摘要。
客户端先逐项回传 Ready 中的协议版本、内容版本、实例和临时令牌，完成 Hello 后进行本机登录。

| Envelope 字段 | 方向 | 含义 |
| --- | --- | --- |
| hello = 1 / hello_ack = 2 | 双向 | 本次启动实例的鉴权握手 |
| error = 6 | 服务端 → 客户端 | code/detail 及 req_id/seq/match_id 关联 |
| input = 7 | 客户端 → 服务端 | 本局移动、瞄准、跳跃、开火与换弹意图 |
| ack = 8 | 服务端 → 客户端 | seq、match_id 与 applied_tick；不表示射击一定命中 |
| snapshot = 9 | 服务端 → 客户端 | Tick、最后处理序号、局次、阶段及全部实体；未发送快照可合并 |
| login_req = 10 / login_rsp = 11 | 双向 | 本机会话登录，返回固定玩家与当前局状态 |
| start_req = 12 / start_rsp = 13 | 双向 | 首次开局或终局重开，成功后另发初始快照 |
| event = 14 | 服务端 → 客户端 | shot/hit/death/reload/end 一次性战斗事件 |
| pause = 15 | 服务端 → 客户端 | 暂停状态及已作废输入序号高水位 |

旧计数探针的 Envelope 编号 3、4、5 已保留，v1/v2 客户端在握手时拒绝，不转换为玩法输入。
`FrameInput.value=2`、`InputAck.count=2`、`Snapshot.count=3` 的编号和名称均保留。
输入 `seq=1,match_id=1,move_x=-1,aim_x=1000`
的完整帧黄金样例为 `0000000b3a0908011801200128d00f`。

`seq` 在整个连接内单调递增且非零，重开不归零；每个输入携带 `match_id`，旧局输入不能作用于新局。
`move_x` 为 -1/0/1，瞄准分量范围为 [-1000,1000] 且不同时为零；
客户端不能提交权威位置、伤害或命中目标。
坐标为整数毫米，以脚底中心为实体原点、X 向右、Y 向上；快照速度使用毫米每 Tick。
玩法以 60 Hz 模拟、20 Hz 发送快照。

`Entity.cfg_id = 16` 是新增的 `uint32` 配置 ID，合法范围为 1～2147483647；
客户端按 `kind=player` 查询内容 `players[cfg_id]`，按 `kind=enemy` 查询 `monsters[cfg_id]`。
`Entity.id` 是局内动态身份，不能用来推断出生点或配置；数组顺序也不表示玩家身份。
没有枪械的怪物继续输出零值 `ammo/reserve/reload_ticks`。其余字段编号、含义及输入黄金帧不变。
协议客户端 JSON 的 `cfg_id` 和脚本桥接一样使用十进制字符串，C# 生成字段为 `uint`。

移动与开火保持到下一条输入改变；跳跃／换弹按每次输入的按下事件处理。
同 Tick 移动／瞄准取最后输入，跳跃／换弹和短按开火锁存一次，仍由落地、冷却和弹药限制。
Snapshot.seq 包括已处理但业务拒绝的序号，不代表动作一定成功。事件 ID 在连接内单调增长。
shot 坐标为射线终点或交点，hit/death 为目标脚底坐标；reload 的 amount=0 表示开始，
amount>0 表示换弹完成及实际补弹量。end 的具体结果读取紧随其后的终态快照。

LoginReq 和 StartReq 的 `req_id` 为 1～128 字节。StartReq 的 `after_match_id` 首局为 0，
重开填写结束局的 ID；重复成功请求返回同一局，不能重置活动战斗。阶段为
Unauthenticated/Lobby/Playing/Dead/Cleared。Dead/Cleared 允许重开。

宿主 Pause 清除排队命令和持续/边沿动作；PauseState 的 `discard_through_seq` 标识作废范围。
暂停中的新玩法命令明确拒绝；恢复后客户端重新发送当前输入，不重放作废动作。
断连或致命框架错误中止当前会话，需重新启动服务进程；没有账号注册、远程登录或断线续局。

网络使用 Protobuf，进程内 Luax 桥接使用 `server/lua/contract.json` 的严格 JSON schema。
桥接版本为 3，所有 ID、Tick、序号使用规范十进制字符串，完整 uint64 不经浮点转换。
内部世界状态也升级为 v3，但组件结构不等于网络快照；旧内部快照拒绝导入，不提供迁移。
C++/脚本契约测试覆盖这些边界，生成 C# 类型不等于已完成 Unity 联调。

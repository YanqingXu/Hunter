# 本机验证 DEMO 协议 v5

`hunter.proto` 是 C++ 与 C# 唯一协议来源。服务端构建生成两端代码，
C# 文件位于 `server/build/win-dev/generated/csharp/`，不手改或提交生成产物。

每个 TCP 帧由四字节大端无符号长度和 Envelope 的 Protobuf 字节组成。长度不能为零，默认
不超过 64 KiB。只监听回环地址；一个连接对应一个会话。协议版本为 5，
内容标识为 `gameplay-v4:<SHA-256>`，由选中源表的规范化内容生成。
客户端先逐项回传 Ready 中的协议版本、内容版本、实例和临时令牌，完成 Hello 后进行本机登录。

| Envelope 字段 | 方向 | 含义 |
| --- | --- | --- |
| hello = 1 / hello_ack = 2 | 双向 | 本次启动实例的鉴权握手 |
| error = 6 | 服务端 → 客户端 | code/detail 及 req_id/seq/match_id 关联 |
| input = 7 | 客户端 → 服务端 | 本局横纵移动、瞄准、跳跃、开火、换弹、奔跑与姿态意图 |
| ack = 8 | 服务端 → 客户端 | seq、match_id 与 applied_tick；不表示射击一定命中 |
| snapshot = 9 | 服务端 → 客户端 | Tick、最后处理序号、局次、阶段及全部实体；未发送快照可合并 |
| login_req = 10 / login_rsp = 11 | 双向 | 本机会话登录，返回固定玩家与当前局状态 |
| start_req = 12 / start_rsp = 13 | 双向 | 校验免费配装后开局，返回冻结配装并另发初始快照 |
| event = 14 | 服务端 → 客户端 | shot/hit/death/reload/end 一次性战斗事件 |
| pause = 15 | 服务端 → 客户端 | 暂停状态及已作废输入／动作序号高水位 |
| action_req = 16 / action_rsp = 17 | 双向 | 背包、拾取、放弃、切枪、选工具、近战、使用、场景交互 |
| save_req = 18 / save_rsp = 19 | 双向 | 保存状态、幂等重试、历史结果和仓库分页 |

旧计数探针的 Envelope 编号 3、4、5 已保留；所有旧协议客户端在握手时拒绝。
`FrameInput.value=2`、`InputAck.count=2`、`Snapshot.count=3` 的编号和名称均保留。
输入 `seq=1,match_id=1,move_x=-1,aim_x=1000`
的既有字段编码黄金样例为 `0000000b3a0908011801200128d00f`；实际玩法输入还须携带 world_id。

`seq` 在整个连接内单调递增且非零，重开不归零；每个输入携带 `world_id/match_id`，旧局输入不能作用于新局。
`move_x/move_y` 为 -1/0/1，瞄准分量范围为 [-1000,1000]；零向量使用前向，
匍匐时按移动朝向裁剪到局部前方及上方。`run/prone` 是明确目标意图，重发不会反复切换；
客户端不能提交权威位置、伤害或命中目标。
坐标为整数毫米，以脚底中心为实体原点、X 向右、Y 向上；快照速度使用毫米每 Tick。
玩法以 60 Hz 模拟、20 Hz 发送快照。

`Entity.cfg_id = 16` 是新增的 `uint32` 配置 ID，合法范围为 1～2147483647；
客户端按 `kind=player` 查询内容 `players[cfg_id]`，按 `kind=enemy` 查询 `monsters[cfg_id]`。
`Entity.id` 是局内动态身份，不能用来推断出生点或配置；数组顺序也不表示玩家身份。
没有枪械的怪物继续输出零值 `ammo/reserve/reload_ticks`。其余字段编号、含义及输入黄金帧不变。
协议客户端 JSON 保留既有实体／战利品 `cfg_id` 十进制字符串；新配装与装备配置 ID 为 JSON 整数，
C# 配置字段为 `uint`。所有 uint64 身份、序号和 Tick 在 JSON 中仍使用十进制字符串。

Entity 追加姿态、有效宽高、体力、血段、双枪独立弹药与冷却、工具实例和次数、使用剩余 Tick、
当前装备和梯子 ID。Snapshot 追加场景消耗状态、飞行物及已处理 `action_seq`；最多 32 场景、16 飞行物。
地图和场景静态几何取同一内容文件，不能从快照反推另一套地图配置。

移动与开火保持到下一条输入改变；跳跃／换弹按每次输入的按下事件处理。
同 Tick 移动／瞄准取最后输入，跳跃／换弹和短按开火锁存一次，仍由落地、冷却和弹药限制。
Snapshot.seq 包括已处理但业务拒绝的序号，不代表动作一定成功。事件 ID 在连接内单调增长。
shot 坐标为射线终点或交点，hit/death 为目标脚底坐标；reload 的 amount=0 表示开始，
amount>0 表示换弹完成及实际补弹量。end 的具体结果读取紧随其后的终态快照。

LoginReq 和 StartReq 的 `req_id` 为 1～128 字节。StartReq 的 `after_match_id` 首局为 0，
重开填写结束局的 ID；先完整校验 Loadout，再分配持久局号。缺省采用内容默认配装，
相同请求重发返回同一局，改变参数返回 `request_conflict`。StartRsp 回显服务器接受的配装。
血段只接受 25／50 且总和 150；两个武器槽、四个常规工具槽、四个消耗品槽。
枪槽从 1 开始，常规工具槽 1～4、消耗品槽 5～8，开局后不可修改配装。
阶段包括 Unauthenticated/Lobby/Preparing/Playing/Settling/Finished；玩家结果独立为
Alive/Dead/Extracted/Abandoned。只有保存状态 Committed 后才允许重开。

ActionReq.kind 追加 SWITCH_WEAPON/SELECT_TOOL/MELEE/USE/INTERACT，保留 BAG/PICKUP/ABANDON 编号。
`slot` 选择装备，`target_id` 选择场景，`item_id` 选择地面物品。
每个请求带连接内非零递增 `action_seq`，与 FrameInput.seq 分开；ActionRsp 回显相同序号。
最近 128 条响应可重放，相同序号参数不同返回冲突；淘汰后的旧序号返回 `stale_action`，不再次执行。
动作业务错误的 Error.seq 对应 action_seq，由 req_id 区分关联。
梯子交互仅用于进入；梯上拒绝拾取和其他玩法动作，只按 move_y 移动。
向端点移动且站立空间足够时自动离梯，空间不足继续攀爬，不补执行旧动作。
免费装备、消费品及补给不进入奖励背包，死亡／放弃不发奖励；存档仍为 SQLite V1。

宿主 Pause 清除排队命令和持续/边沿动作；PauseState 的 `discard_through_seq` 和
`discard_action_seq` 标识各自作废范围。已开始的前摇、恢复、装填和飞行计时冻结。
暂停中的新玩法命令明确拒绝；恢复后客户端重新发送当前输入，不重放作废动作。
断连或致命框架错误中止当前会话，需重新启动服务进程；没有账号注册、远程登录或断线续局。

网络使用 Protobuf，进程内 Luax 桥接使用 `server/lua/contract.json` 的严格 JSON schema。
Host 契约、上下文和内部状态版本为 6，效果输出版本为 5；所有 ID、Tick、序号使用
规范十进制字符串，完整 uint64 不经浮点转换。
内部组件结构不等于网络快照；旧内部快照拒绝导入，不提供迁移，已提交 SQLite V1 结果原样可读。
C++/脚本契约测试覆盖这些边界，生成 C# 类型不等于已完成 Unity 联调。

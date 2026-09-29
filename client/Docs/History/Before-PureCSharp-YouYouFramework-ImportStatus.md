> 历史记录：移除 xLua 之前的迁移与验证；不代表当前版本。当前入口见 [纯 C# 迁移](../PureCSharp-Migration.md)。

# YouYouFramework 导入范围与验证

更新：2026-09-29。

本次已按要求迁入课程原版，而后进行 2D 接入。**框架在 `Assets/YouYouFramework`，原入口在 `Assets/YouYouFramework/GameEntry.cs`，全部底层管理器在 `Assets/YouYouFramework/Managers`。** 不需要再到旧 Packages 中找。

最初的 `YouYouFramework-1.0.0.zip` 本身是精简抽取版，所以此前确实缺少课程原管理器。此次迁移改用同一套资料里的 `Client/Assets/YouYouFramework`，连同实际依赖一起补齐；精简包已从活动 Packages 移出并保留备份。

## 实际迁入内容

- 原框架 **177 个 C# 文件**，原类型、命名空间、方法签名、配置资产与 `.meta`。
- `GameEntry` 的 **19 个原管理器**：Logger、Event、Time、Fsm、Procedure、DataTable、Socket、Http、Data、Localization、Pool、Scene、Resource、Download、UI、Lua、Audio、Input、Task。
- 原资源 AB / 依赖加载 / 缓存 / 引用计数 / 版本校验、原下载与断点续传、原 Socket 协议和 HTTP 签名、FlatBuffers 表、原 UI 分组 / 层级 / 遮罩 / 冻结 / LuaForm、任务、日志与生命周期。
- 原 `YouYouScript` 业务与生成类型、XLua 源码 / 绑定 / 本机库、FMOD、PathologicalGames、FingerGestures、DOTween、LitJson、protobuf、zlib 等依赖。当前项目的 Odin 版本保留。
- 原表、Lua 与 PB、FMOD Banks、UI 与 Reporter 资源。原九个 MMO 流程保留，2D 默认入口使用独立离线启动配置。

没有用空管理器或简化服务替代原 API。兼容修复包括 Unity 2022 的编译条件、独立启动、异步表回调线程、资源与 Lua 释放、任务回收、下载重试和日志落盘。原接口签名审计覆盖多组编译宏：**1579 项原公开 / protected 声明，缺失 0 项**。审计工具见 [ApiAudit](../../Tests/YouYouFull/ApiAudit/Program.cs)，报告见 [api-preservation.json](../../TestResults/YouYouFullMigration/api-preservation.json)。

## 2D 如何使用

`Assets/_Project/Modules/YouYou2D/Runtime/GameServices2D.cs` 创建真实 `YouYou.GameEntry`，接入层的 `Framework` 属性也是原 `GameEntry`。业务调用 `GameEntry.Event/Time/UI/Resource/...`，不再使用 `Framework.Context`。

正式关卡与示例 HUD 通过原资源索引、真实 AssetBundle 和原 UI 表的 9001 / 9002 行加载。原表数据、Lua Main、FMOD Banks 就绪后才允许进入世界。地图实体继续使用已有 `BigWorld.Pooling`；原池系统完整保留并实际管理框架资源、UI 与类对象。横版技能、Rigidbody2D 和 Cinemachine 相机仍沿用项目已有实现。

打开 **Tools → BigWorld → 正式关卡 → 打开边境试炼**，然后 Play。修改表、Lua、UI 或资源目录后，运行 **Tools → BigWorld → 2D 框架 → 生成原框架运行内容**。Windows 发布菜单会自动生成原格式资源包、VersionFile、AssetInfo 和完整程序 ZIP。

## 验证记录

原功能验证报告在 `TestResults/YouYouFull`，2D 示例 / 相机报告在 `TestResults/YouYouFramework/Adaptation2D`，正式玩法报告在 `TestResults/VerticalSlice`。独立管理器验证实际运行原 Lua / FMOD、配置表、UI、场景切换、资源包与本机 HTTP / TCP / 下载服务，不只检查类型存在。

2026-09-29，Solid 地形接缝修复后的两套 2D 验证均通过：

| 验证套件 | 本次结果 | 主要覆盖与报告 |
| --- | --- | --- |
| 正式玩法 Gameplay | 37 项通过，失败为空 | 真实物理移动、守卫巡逻/战斗、检查点、死亡重试、暂停、通关及原框架接入；[报告](../../TestResults/VerticalSlice/play-checks.json) |
| 框架示例 Framework2D | 42 项通过，失败为空 | 19 个原管理器、真实 AB / Lua / 表 / UI、Reporter PNG / GUISkin 实际加载与引用归还、地图与池、技能、Cinemachine 相机和同进程重启；[报告](../../TestResults/YouYouFramework/Adaptation2D/play-checks.json) |

两套分别记录，不汇总重复检查。原框架的新 runner 也已完成 Network 64 项、Content 75 项、Bundles 71 项复验，三份报告均为 0 失败；Content 销毁测试宿主及相机的用例记录 1 条 FMOD Listener 警告，另外两份为 0 警告。Scenes 保留原阶段 50 项通过记录。Network 较此前 63 项多出的基础检查与执行宏组合有关，不应作为新增独立网络能力计数。

各阶段包含重复的基础检查，不能把数字相加当作独立测试总量。完整过程和接口对应表见 [迁移说明](../YouYouFramework-Migration.md)。Windows 和源码产物见 `Builds/latest-windows.json`、`Builds/Source`。

课程线上账号、网关、数据库/CDN 和完整 3D MMO 世界资源不随这次 2D 客户端接入部署；相关管理器和协议代码保留，不能把本机功能验证解读为旧游戏线上业务已恢复。

2026-09-29 最终复验：地图 Solid 3 项、Play 7 项、Streaming 24 项全部通过，三个 Unity 进程均以 0 正常退出；构建场景设置恢复完成。证据见 [地图汇总](../../TestResults/Map2D/native-migration/Run-20260929-113834-933-b118da353f4741649d3eb3321c048eb9/summary.json)。

Windows x64 / Mono 发布成功：0 错误、12 条旧 API 或未使用成员等编译警告。实际程序完成原框架和关卡双就绪标记，收到正常窗口关闭请求后以 0 退出，未记录运行时异常。见 [构建报告](../../Builds/latest-windows.json) 和 [程序运行报告](../../TestResults/VerticalSlice/standalone-smoke.json)。

最终增加原事件释放回归：CommonEvent / SocketEvent 关闭时逐节点清除委托，阻止派发中的后续回调继续执行。测试保留真实 Lua 事件节点跨越关闭，确认 LuaEnv 可以释放并再次初始化；xLua 的存活回调检查保持原样。进一步加入异步分阶段关闭、外部回调存活拒绝关闭及释放后的显式恢复、重复请求和关闭期间销毁宿主的回归。Content 75 项、Bundles 71 项均通过；2D 示例的恢复场景也通过，Framework2D 共 42 项。

最终分阶段关闭版本连续 30 次完成真实 Windows 程序启动、框架与关卡就绪、正常窗口关闭，均以 0 退出且无运行异常；30 次使用同一个游戏程序集。见 [连续启停验证](../../TestResults/YouYouFullMigration/lua-shutdown-final.json)。修复前的失败诊断另行保留，不作为本版通过证据。

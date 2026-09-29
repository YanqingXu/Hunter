> 历史记录：移除 xLua 之前的迁移与验证；不代表当前版本。当前入口见 [纯 C# 迁移](../PureCSharp-Migration.md)。

# 原版 YouYouFramework 完整迁移与 2D 接入

记录日期：2026-09-29。先验证完整课程框架，再把本项目的横版玩法接到原接口。第一阶段验证数据与 2D 项目运行结果分别记录，不把“代码存在”当成“功能已运行”。

## 来源和保留范围

迁移来源是课程工程 `Client/Assets/YouYouFramework`，以及它实际依赖的 `YouYouScript`、`XLua`、原插件和 `Download` 内容。最初指定的 `Reusable/YouYouFramework/YouYouFramework-1.0.0.zip` 是抽取版；它与完整课程源码的范围不同。

当前 `Assets/YouYouFramework` 保留 **177 个原框架 C# 文件**，包括 `GameEntry`、`ManagerBase`、Core、组件、管理器、编辑器工具、`YouYouAssets` 配置及 `YouYouAssetsScript` 配置类型。完整运行入口为 `YouYou.GameEntry`，2D 代码不再通过旧抽取包的 `YouYou.Framework.FrameworkEntry/ClientContext` 提供服务。

保留的是原类型、原公开接口和真实实现；为 Unity 2022、独立初始化、资源发布和正确释放做了兼容修复。文件内容不是逐字不变，也没有用同名空类或始终成功的占位方法代替原管理器。

原 `GameEntry` 实际创建以下 19 个管理器：

| 原访问入口 | 实际类型 |
| --- | --- |
| `GameEntry.Logger` | `LoggerManager` |
| `GameEntry.Event` | `EventManager` |
| `GameEntry.Time` | `TimeManager` |
| `GameEntry.Fsm` | `FsmManager` |
| `GameEntry.Procedure` | `ProcedureManager` |
| `GameEntry.DataTable` | `DataTableManager` |
| `GameEntry.Socket` | `SocketManager` |
| `GameEntry.Http` | `HttpManager` |
| `GameEntry.Data` | `DataManager` |
| `GameEntry.Localization` | `LocalizationManager` |
| `GameEntry.Pool` | `PoolManager` |
| `GameEntry.Scene` | `YouYouSceneManager` |
| `GameEntry.Resource` | `AddressableManager`，包含原 `ResourceManager` 和 `ResourceLoaderManager` |
| `GameEntry.Download` | `DownloadManager` |
| `GameEntry.UI` | `YouYouUIManager` |
| `GameEntry.Lua` | `LuaManager` |
| `GameEntry.Audio` | `AudioManager` |
| `GameEntry.Input` | `InputManager` |
| `GameEntry.Task` | `TaskManager` |

`AddressableManager` 是课程自己的资源管理类型，其真实加载链是原 AssetBundle 系统；这个名字不表示改成了 Unity Addressables 包。

## 接口审计与实际验证

[API 审计工具](../../Tests/YouYouFull/ApiAudit/Program.cs) 对原源码与迁入源码的公开、protected 声明做去除空白/注释后的规范化比较，并覆盖多组宏定义。首轮 [API 审计报告](../../TestResults/YouYouFullMigration/api-preservation.json) 记录 **177 个源文件、1579 项声明、缺失 0 项**。这是原接口保留证据，不是每个业务场景都已兼容的证明；新增入口、生命周期和离线初始化方法不计作缺失。

原管理器的行为通过独立场景中的实际 `GameEntry` 验证。以下包含原阶段记录与 2026-09-29 新 runner 的复验结果，数量包含各阶段共用的初始化检查，不能相加当作独立用例数：

| 阶段 | 已通过检查数 | 实际覆盖 |
| --- | ---: | --- |
| Bundles | 71 | 构建真实小 AssetBundle；原压缩版本/资源索引解析、原异步任务加载、引用计数、缓存释放、同路径更换字节后重新加载、随包内容隔离旧缓存与 MD5 篡改拒绝 |
| Network | 64 | 本机真实 HTTP/TCP 监听；GET、带原签名的 POST 与 503 重试；原协议序列化、压缩、加密及分片收发；下载、Range 续传、忽略 Range 的处理和最终 MD5 |
| Scenes | 50 | 用原 FlatBuffers schema 建临时表，原 `Scene.LoadScene` 执行真实 additive 加载、旧场景卸载、回切、进度事件；恢复原表 |
| Content | 75 | 原压缩 FlatBuffers 表、表重载；原表驱动 UI 预制体与缓存；FMOD 原 Bank 和音效；原 xLua Main、数据初始化与 protobuf；同进程再次初始化和关闭 |

Network、Content、Bundles 已由新版临时导入 runner 完成复验，均为 0 失败。Content 的“关闭中销毁宿主”用例同时销毁其测试相机，FMOD 在该帧记录 1 条 Listener 缺失警告；Network、Bundles 为 0 警告。Scenes 50 项是原阶段通过记录。Network 由此前 63 项变为 64 项与宏组合下的基础检查数量有关，不表示多出一套独立网络业务验证。

报告位于 [TestResults/YouYouFull](../../TestResults/YouYouFull)。公共探针还检查原事件退订、真实计时器、FSM 生命周期、串行/并行任务组、类池复用、日志写盘、19 个管理器重建及静态表和 Lua 环境清理。原批量表加载实际包含 **16 张表**；存在某张生成结构或 `.bytes` 文件不等于它已被原批量入口加载。

网络验证使用测试自己创建的本机服务，不依赖课程远程账号/CDN。场景验证使用真实专用场景和原表结构，未用空回调模拟场景完成。FMOD 和 xLua 使用真实原生库。各阶段不等于旧 MMO 的账号、选角、战斗和线上热更已端到端恢复。

2026-09-29，在 Solid Tilemap 接缝修复后的代码上，两套项目接入验证分别完成：

| 2D 验证套件 | 本次结果 | 运行证据 |
| --- | --- | --- |
| Gameplay | 37 项通过，失败为空 | 原框架接入下的真实物理移动、守卫巡逻/战斗、检查点、重试、暂停、通关；[正式玩法报告](../../TestResults/VerticalSlice/play-checks.json) |
| Framework2D | 42 项通过，失败为空 | 19 个原管理器、真实 AB / Lua / 表 / UI、Reporter PNG / GUISkin 实际加载与引用归还、技能与地图池、Cinemachine 相机、同进程重启；[框架示例报告](../../TestResults/YouYouFramework/Adaptation2D/play-checks.json) |

以上两套不合计成独立测试总量。Reporter 新检查通过原 `AssetCategory.Reporter` 加载 `log_icon.png` 和 `reporterScrollerSkin.guiskin`，确认真实 `Texture2D` / `GUISkin` 类型，并通过原资源池各归还一次引用。

复验入口为 `YouYouFullValidation.RunCore/RunContent/RunBundles/RunNetwork/RunScenes`，也可在关闭当前工程编辑器后运行：

```powershell
& ./Tools/Verify-YouYouFull.ps1 -Stage Network
```

`Content`、`Scenes` 使用原编辑器直接读取模式，需要 `DISABLE_ASSETBUNDLE`；`Bundles` 必须关闭此宏。验证脚本会自动准备对应宏、重启 Unity 后运行，并在结束时恢复原宏状态。探针源码位于 `Tests/YouYouFull`，每次临时复制到 Assets 后运行并归档，不进入 Player。验证工具不把原联网启动场景加入正式 Build Settings；Scenes 阶段临时加入的两张夹具场景会在报告结束时恢复。

## 原目录和依赖

| 目录 | 保留原因 |
| --- | --- |
| `Assets/YouYouFramework` | 全部原框架源文件及配置；入口和原工具仍引用其中的配置路径 |
| `Assets/YouYouScript` | 原业务、协议、生成表、角色/相机/UI 等原类型依赖，不能只复制 Managers 后删掉 |
| `Assets/XLua` | 运行源码、生成绑定、`Gen/link.xml`、编辑器工具及原资源 |
| `Assets/Download/DataTable` | 原 zlib 压缩 FlatBuffers 数据 |
| `Assets/Download/xLuaLogic` | 原 Lua Main、共享初始化、protobuf 与业务脚本 |
| `Assets/Download/Audio` | 运行用原 FMOD Bank `.bytes` |
| `Assets/Download/UI`、`Reporter` | 原表驱动 UI、LuaForm、面板和依赖资源 |
| `Assets/Plugins` | FMOD、xLua 本机库、Google.Protobuf、LitJson、zlib、FingerGestures、PathologicalGames 与项目已有 Odin 等 |
| `Assets/ThirdPlugins` | 原 DOTween、SuperScrollView、MTE、TextMesh Pro 资源等 |
| `Assets/_Project/Modules/YouYou2D` | 本项目 2D 宿主、资源编目、世界会话和构建接入 |

原 FMOD 编辑器 Bank 输入位于 `Plugins/FMOD/Editor/BankSource`，避免课程机器的绝对磁盘路径；它不代替运行用 `Download/Audio/*.bytes`。Odin 保留项目已有版本。插件路径与 `.meta` 一起保留，原代码、表和生成工具中的固定路径未统一改名到 `_Project`。

## 2D 启动与原流程

`GameServices2D` 创建禁用的原 `GameEntry` 物体，绑定原 `ParamsSettings`、UI 根 Canvas、四个 UI 分组与池配置，调用 `Configure(..., autoLaunchProcedure: false)` 后激活。`GameEntry.Initialize()` 创建全部原管理器；`Lua.Init()` 在资源和表就绪后由宿主显式调用。

原九个流程状态仍然保留：Launch、CheckVersion、Preload、ChangeScene、LogOn、SelectRole、EnterGame、WorldMap、GameLevel。2D 宿主不自动进入 Launch，以免默认请求课程旧账号服务或执行旧 MMO 热更/选角流程。课程原流程仍可由专门宿主显式启用，前提是准备其真实服务、配置和资源。

2D 宿主从随包内容读取原压缩 `VersionFile.bytes` 和 `AssetInfo.bytes`，初始化原资源清单，再通过原加载链加载原配置表、2D 补充 UI 表、FMOD Banks 和 Lua。`IsReady` 只在这些步骤完成后置为 true；初始化失败由 `InitializationError` 表示，不发布成功状态。

2D 宿主显式启用新增的 `InitializeLocalManifest(bytes, streamingAssetsOnly: true)`，只接受当前随包内容，并按原清单核对大小与 MD5，避免旧课程或旧构建的可写缓存覆盖本版资源。缺包或校验不符会记录 `OfflineLoadError`，不请求课程 CDN。单参数离线清单入口与原 CDN 初始化接口仍保留各自原有加载行为。内容版本摘要包含所有包的名称与 MD5，任何包变化都会参与版本生成。

`GameAssetCatalog` 是编辑期资源编目与路径生成输入。发布版按记录的资源路径进入原 `GameEntry.Resource.ResourceLoaderManager`，不提供第二套轻量资源实现。构建生成的 `NativeUIForms.bytes` 保留全部原 UI 行并追加：

| 旧资源键 | 原表编号 | 预制体 |
| --- | ---: | --- |
| `ui.game` | 9001 | `Assets/_Project/Game/Prefabs/GameHud.prefab` |
| `ui.demo` | 9002 | `Assets/_Project/Examples/Framework2D/Prefabs/FrameworkHud2D.prefab` |

两个 HUD 继承原 `YouYou.UIFormBase`，使用原 protected 生命周期、分组、Canvas 层级及缓存。原 UI 加载器新增对完整 `Assets/...prefab` 路径的识别，课程原短路径仍按 `Assets/Download/UI/UIPrefab/...prefab` 解析。

`WorldSession2D` 先等待服务和出生地形，再调用原 `OpenUIForm(int, ..., callback)`；原窗口首次 callback 早于 `OnOpen`，所以会多等待一帧后才开启玩家模拟并发布 WorldReady。关闭/重新开启世界时会过滤旧加载回调。`HudResourceKey` 保留序列化兼容，新增 `HudFormId` 可直接指定原表编号。

2D 暂停由宿主控制 `Time.timeScale`，原框架事件和时间管理器继续负责各自服务。地图和技能实体仍使用项目既有 `BigWorld.Pooling` 共享池；原 `GameEntry.Pool` 保留，用于原 UI/资源及原对象池能力。正常退出先停止世界、等待地图实体和共享池完成异步清理，再等待 `GameEntry.ShutdownAsync()` 关闭原管理器；不要直接销毁正在清理的池驱动来替代 `ShutdownAsync()`。

新增的异步关闭先停止驱动并清除事件及待处理回调，退出当前 Unity 调用栈后才释放 Lua、原对象池与日志。同步 `Shutdown()` 继续保留。xLua 的存活回调检查保持不变：外部仍持有 Lua 委托时，关闭任务会失败并保留尚未释放的管理器和环境，调用者释放自己的委托后可以再次关闭。不会强制清空 xLua 的桥接表，也不会在后台无限重试。相关约束见 [xLua 官方 FAQ](https://tencent.github.io/xLua/public/v1/guide/faq.html)。

2D 回归中还修复了 Solid Tilemap 的内部格子接缝：在静态刚体上使用 `CompositeCollider2D` 合并边界，并在格子或区块变化后提交几何，避免连续碰撞角色在平地巡逻/移动时卡住。OneWay 单向平台继续使用原来的独立 Tilemap 与 `PlatformEffector2D`。这是地图碰撞兼容修复；具体玩法回归和最终构建结果仍以相应最新报告为准。

## 内容生成和打包

在当前平台执行 **Tools → BigWorld → 2D 框架 → 生成原框架运行内容**，对应 `BigWorld.YouYou2D.Editor.NativeFramework2DBuild.PrepareContent()`。它保留原表数据，生成 2D UI 补充表，修复既有 HUD 的原基类 Canvas 配置，构建真实 AssetBundle 并写入原二进制压缩清单。

输出位于 `Assets/StreamingAssets`：根目录为 `VersionFile.bytes`、`AssetInfo.bytes`；资源包为 `download/datatable.assetbundle`、`download/xlualogic.assetbundle`、`download/audio.assetbundle`、`download/ui.assetbundle`、`download/reporter.assetbundle` 和 `youyou2d/project.assetbundle`。构建缓存位于 `Library/YouYou2DContent/<平台>`。

改动表、Lua、UI、角色或音频内容后重新生成。正式 Windows 打包流程自动调用指定 Windows 目标的内容生成，然后构建 Player；完整说明和命令见 [Build.md](../Build.md)。发布版使用真实 Bundle 分支，`DISABLE_ASSETBUNDLE` 只用于原编辑器直接读取验证。`Download` 文件夹不是 Player 可直接读取的运行目录。

2D 正式入口保持 `Assets/_Project/Game/Scenes/BorderTrial.unity`；框架整合示例保持 `Assets/_Project/Examples/Framework2D/Scenes/Framework2DDemo.unity`。迁入原框架不替换这些玩法资产，也不把原 MMO 启动场景设为默认发布入口。

## 能力边界

完整保留原管理器与接口，意味着可以继续使用原框架和依赖，不意味着原 MMO 的所有业务已适配横版玩法。课程服务器、账号、数据库、网关部署，完整 3D 世界/角色/战斗资源，以及相应运营配置仍需要单独准备和验证。`YouYouScript` 中相关代码的存在不是这些服务或资源已可用的证明。

本阶段重点验证 Windows x64、Unity 2022.3.62f3c1 和当前 Mono 配置。原目录中含其他平台插件不等于相应 Player 已验证；Android、iOS、WebGL 和 IL2CPP 需要各自的构建、原生库及 AOT 验证。具体 2D 玩法和相机结果以相应测试报告为准，不从 API 审计推断。



2026-09-29 最终复验：地图 Solid 3 项、Play 7 项、Streaming 24 项全部通过，三个 Unity 进程均以 0 正常退出；构建场景设置恢复完成。证据见 [地图汇总](../../TestResults/Map2D/native-migration/Run-20260929-113834-933-b118da353f4741649d3eb3321c048eb9/summary.json)。

Windows x64 / Mono 发布成功：0 错误、12 条旧 API 或未使用成员等编译警告。实际程序完成原框架和关卡双就绪标记，收到正常窗口关闭请求后以 0 退出，未记录运行时异常。见 [构建报告](../../Builds/latest-windows.json) 和 [程序运行报告](../../TestResults/VerticalSlice/standalone-smoke.json)。

最终增加原事件释放回归：CommonEvent / SocketEvent 关闭时逐节点清除委托，阻止派发中的后续回调继续执行。测试保留真实 Lua 事件节点跨越关闭，确认 LuaEnv 可以释放并再次初始化；xLua 的存活回调检查保持原样。进一步加入异步分阶段关闭、外部回调存活拒绝关闭及释放后的显式恢复、重复请求和关闭期间销毁宿主的回归。Content 75 项、Bundles 71 项均通过；2D 示例的恢复场景也通过，Framework2D 共 42 项。

最终分阶段关闭版本连续 30 次完成真实 Windows 程序启动、框架与关卡就绪、正常窗口关闭，均以 0 退出且无运行异常；30 次使用同一个游戏程序集。见 [连续启停验证](../../TestResults/YouYouFullMigration/lua-shutdown-final.json)。修复前的失败诊断另行保留，不作为本版通过证据。

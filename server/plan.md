# Hunter Server 首版规划

编制日期：2026-09-22。状态：设计基线；P0＋Windows 基础框架已进入实现与验证。
当前能力及执行证据见 [构建说明](README.md) 与 [验证记录](VERIFICATION.md)。

本计划面向比赛用单人 PvE 撤离 Demo。服务端采用 **C++23 / Standalone Asio / Luax**，与 Unity 客户端一起打包为 Android APK，在同一手机上以两个进程运行。Windows 保留开发、联调和自动化测试宿主。

本文承接 [项目说明](../README.md) 与 [参赛计划](../docs/2026-TapTap-GameJam-Plan.md)，记录本轮已经确认的技术方向。正式发布目标由旧文档中的 Windows 改为 Android，脚本接入由前序草案中的普通 Lua 改为 Luax，PC 进程管理保留为开发能力；后续排期需同步修订。本计划不表示框架已可运行，也不表示参赛资格或平台兼容性已通过验证。

## 1. 首版目标与范围

### 1.1 已确认的决策

| 事项 | 首版决策 |
| --- | --- |
| 发布平台 | Android；客户端与服务端在同一个 APK 内，首次运行不下载必需内容 |
| 开发平台 | Windows PC，保留独立服务端程序，支持 Unity Editor 联调 |
| 运行规模 | 一个客户端、一个服务端进程、一场活动对局、一名玩家 |
| 通信 | 游戏消息通过 `127.0.0.1` TCP + Protobuf；Android 生命周期控制通过 Binder |
| 基础框架 | C++23、Standalone Asio、CMake；不自行重写 Reactor、Poller 或线程池 |
| 脚本方向 | Luax Runtime + Bind；完整游戏模拟放在脚本层 |
| 脚本组织 | `main.lua` 入口与独立功能模块，各模块可使用已授权的宿主接口 |
| 双向调用 | C++ 可调用脚本入口，脚本可调用 C++ Host API；跨线程通过队列调度 |
| 热更新 | 开发构建支持局内逻辑更新，状态结构固定；发布构建关闭更新入口 |
| 存档 | SQLite，由服务端单一写入；结算、去重与累计结果事务提交 |
| 开发方式 | 参考 game-net-core，采用 intent → 契约 → 测试 → 实现流程 |

首个完整用例：断网安装并启动 APK → 自动启动本地服务端 → 进入灰盒地图 → 战斗并击杀 Boss → 拾取 → 撤离 → 提交结算 → 退出重进仍可读取已确认结果，重复请求不重复发奖。

比赛期不安排跨设备联机、账号、匹配、分布式服务、多房间调度、云存档、远程数据库、iOS 发布、复杂永久经济系统或状态结构迁移热更新。移动端触控由客户端负责，权威判定保留在服务端脚本。

### 1.2 Luax 接入基线

已检查的候选提交为 [`d8a8160420074f8e590d94910d1af5a5f5a93245`](https://github.com/YanqingXu/luax/tree/d8a8160420074f8e590d94910d1af5a5f5a93245)。接入从该候选开始，记录源码、编译器、Runtime identity 和 Host API identity；不跟随远端 `main` 自动升级。

- Luax 是独立语言与运行时，使用 C++23，不提供 Lua C ABI，不按 Lua 5.5 或 `lua_State` 方案嵌入。
- 已有 typed Host、Bind、只读 namespace、结构化异步和代际句柄能力；Asio 调度器、业务模块组织及 Android 宿主仍需 Hunter 实现。
- 当前公开边界不直接传递普通脚本 table，传统 `require/package` 不能假定可用。
- 开发与测试链接 `Luax::DevelopmentRuntime` 和 `Luax::Bind`；发布服务端链接 compiler-free `Luax::Runtime` 和 `Luax::Bind`，加载离线生成且验证通过的 LUXB Bundle。
- 该候选仍为 Core Preview。已检查资料未提供 Android 验证证据；NDK 编译、ARM64 运行和签名 Bundle 加载属于 P0/P1 的前置验收。验证未通过时先形成具体阻塞记录，不默默换用另一种脚本运行时。

## 2. 架构与所有权

```text
Android APK
  ├─ Unity 客户端进程
  │    ├─ 输入、触控、表现与 UI
  │    ├─ Android 桥接层 ── Binder Messenger ───────────┐
  │    └─ Protobuf ── 127.0.0.1 / TCP ─────────────┐  │
  ├─ :server 进程                                 │  │
  │    ├─ ServerService + JNI ◀───────────────────────┘
  │    └─ libhunter_server.so                      │
  │         ├─ C++ / Asio / 协议 / Tick ◀───────────┘
  │         ├─ Luax：main + 游戏模块
  │         └─ SQLite 存档工作线程
  └─ 脚本 Bundle、配置、地图及必要运行依赖
```

### 2.1 模块职责

| 模块 | 职责与状态归属 |
| --- | --- |
| PlatformHost | Android Service/JNI 或 Windows 启动入口、路径、就绪、暂停与退出；不含玩法 |
| ServerRuntime | 装配组件、服务生命周期、固定 Tick、命令队列与故障收敛 |
| Transport/Protocol | TCP 连接、长度分帧、握手、Protobuf 编解码、输入封装与有界发送 |
| ScriptRuntime | Luax Runtime/Isolate、模块入口、Host 注册、执行预算与错误转换 |
| ScriptScheduler | 入口投递、AwaitRequest 适配、操作关联、完成事件及取消处理 |
| Game scripts | 对局、实体、运动碰撞、枪械、伤害、AI、掉落、拾取、死亡、撤离与奖励计算 |
| Storage | SQLite 事务、去重、存档版本与完成通知；不决定游戏奖励 |
| ReloadController | 开发脚本版本装载、状态快照校验、候选验证与安全切换 |

实体、位置、血量、局内物品和玩法计时只在脚本中维护一份权威状态。C++ 可保存发送快照、操作记录和持久化结果，不建立第二套可独立修改的世界模型。

### 2.2 线程与执行规则

- 原生逻辑线程创建并独占 Luax Runtime/Isolate，同时运行单线程 Asio `io_context`。Android UI、Service 主线程和 Binder 回调只投递命令，不执行脚本或磁盘操作。
- 网络回调完成分帧与校验后入队；玩法命令在 Tick 边界处理，不从收包栈直接进入玩法函数。
- 存档线程独占 SQLite 连接，只接收和返回拥有自身数据的任务，不持有 Isolate、脚本对象或借用字符串。
- 默认模拟 60 Hz，快照 20 Hz，单次调度最多补算 4 个 Tick；持续超时记录耗时并保留事件循环响应，不无限追赶。最终预算用 Android 真机确认。
- 游戏时间由 Tick 推进。暂停、后台和更新停止游戏时间；网络、操作 deadline 与退出超时使用单调时钟。
- 消息、完成事件、发送缓存均按条数和字节限制。旧的未发送快照可以合并；控制、结算和操作完成事件不得静默丢弃。工作启动前预留完成容量，容量不足明确拒绝。
- Lua/Host 回调中的再次脚本调用统一延后到当前执行片段结束。禁止同一 Runtime 同步重入，禁止其他线程直接 `call/dispatch/resume`。

## 3. main 与功能模块

### 3.1 预期目录

以下是目标目录；当前实现范围以 intent 和验证记录为准，目录存在不表示功能完成：

```text
server/
├─ plan.md
├─ AGENTS.md                   # 本目录的 intent 开发约定
├─ intents/                    # architecture / modules / usecases
├─ rules/                      # 线程、所有权、脚本边界、验证规则
├─ src/                        # common / core / net / script / storage，各模块内并置头文件与实现文件
├─ platform/                   # android / desktop，各宿主内并置头文件与实现文件
├─ lua/
│  ├─ main.lua                 # 组装、生命周期、入口分发
│  ├─ modules.json             # 显式模块依赖及构建顺序
│  ├─ framework/               # 分发、状态、Tick 定时器
│  └─ game/                    # world / movement / combat / ai / loot / settlement
├─ tools/                      # 脚本组装、离线编译、Bundle 与开发控制工具
└─ tests/                      # unit / contract / integration / scripts
```

C++ 文件按模块组织：例如 `src/storage/` 同时放置该模块的 `.h`／`.hpp` 与 `.cpp`，平台适配文件放在对应 `platform/android/` 或 `platform/desktop/` 中。对外接口与内部实现通过模块职责和可见性区分，不另设 `include/` 目录，也不按文件类型拆分目录。

`src/common/Types.h` 集中定义服务端通用短类型，所有自有 C++ 代码统一使用，第三方库保持上游定义。映射与外部契约边界见 [短类型命名规则](rules/type_naming_rules.md)；`common/` 仅承载实际共用定义，不承接玩法或跨模块可变状态。

跨端 `.proto` 仍以仓库根目录 `protobuf/` 为唯一来源；配置源表仍在 `design/`，导表工具仍在 `export/`。不在 `server/` 创建重复协议或另一套手工地图。

### 3.2 首版模块装配方式

为保留多文件开发，同时避开尚未提供的动态模块接口，首版采用**构建期组装为一个游戏 ModuleHandle**：

1. `modules.json` 声明模块名、文件和依赖，构建工具拒绝重名、缺失依赖与依赖环。
2. 每个功能文件返回模块工厂，显式接收依赖并返回函数表；脚本内部允许 table 和函数引用，世界状态不藏在模块闭包中。
3. 构建工具按依赖顺序包装并连接源文件，`main.lua` 最后组装功能模块。模块导出表只存在于脚本内部，不穿过 Host 边界。
4. 生成的入口适配层以 Luax 支持的顶层函数导出生命周期入口；初始化返回标量状态，不返回模块 table。
5. C++ 在模块首次执行前注入同一套受保护 Host namespace。今后若拆分为多个 ModuleHandle，必须逐模块注册能力，不依赖全局继承。

构建产物记录模块清单与源文件行号映射，使错误能定位到原始文件。采用显式包装和依赖注入，不做替换 `require` 文本的源码补丁。模块装配语法先用固定 Luax 候选编译验证，再进入玩法实现。

### 3.3 脚本入口与数据边界

首版入口采用以下逻辑契约；函数名和消息标识在 P0 intent 中冻结：

| 入口 | 数据与行为 |
| --- | --- |
| `init(context_json)` | 读取初始化上下文，创建模块内部纯数据状态；返回成功或错误 |
| `on_event(event_id, payload_json)` | 处理输入、交互、宿主完成通知；字段 schema 显式定义 |
| `tick(tick_id, dt_seconds)` | 按固定步长推进一次模拟，通过 Host API 提交输出 |
| `export_state()` | 返回带状态版本的快照字符串，仅用于开发更新与测试 |
| `import_state(snapshot_json)` | 导入独立快照，拒绝结构不符或非法字段 |
| `validate_state()` | 校验当前状态，不产生外部副作用 |
| `shutdown(reason)` | 清理脚本侧逻辑，不承担最后时刻存档保证 |

Protobuf 在 C++ 端编解码。首版复杂跨界数据使用有版本和大小限制的 JSON 字符串，在脚本中通过 Luax `json` 显式转换；简单参数使用整数、字符串、布尔或 buffer。JSON 只是进程内桥接格式，不增加第二套网络协议。

禁止把 Protobuf 原生 JSON 映射直接当作脚本 schema。桥接层明确字段名、必填项和数值含义；int64 保持精确，超出有符号范围的 uint64 领域 ID 使用十进制字符串，直接 typed 调用则使用注册的 TaggedScalar。热点批量数据需要优化时，在相同契约下改用明确编码的 buffer，并先测量收益。

## 4. C++ 与 Luax 双向通信

“任意功能模块可以通信”指模块获得能力后可在合法执行片段中调用宿主，不指任何线程都能直接访问 VM。

| 方向 | Luax 能力 | Hunter 补充职责 |
| --- | --- | --- |
| 脚本 → C++ 同步 | `registerHostFunction`、`Luax::Bind`、`registerHostNamespace` | 提供快速、非阻塞的宿主函数和明确错误 |
| C++ → 脚本 | `findFunction`、`call`、`dispatch` | 保存有效入口句柄，在所属线程调度 |
| 脚本 → 宿主异步 | `AsyncReturn`、`AwaitRequest` | 适配 Asio 定时器和存档任务，维护操作登记表 |
| 完成线程 → 脚本 | `AsyncCompletionEncoder`、`resume/cancel` | 有界队列、操作关联、deadline、取消与代次检查 |

初始 Host namespace 按职责暴露 `net`、`cfg`、`diagnostics`；`cfg` 按命名规则冻结，
`storage` 在存档阶段接入。使用 Luax 标准库日志时，在模块首次执行前配置其服务。
脚本不接触 Socket、SQLite 连接、任意文件路径或裸 C++ 指针。

- 同步调用只返回当前结果；网络和存档不得阻塞逻辑线程。
- 异步操作返回 ID 或挂起当前执行；后台只返回 detached 数据，所属线程关联后才通知或恢复脚本。队列与关联信息不是 Luax 自动提供的服务。
- 首版 Tick、输入入口及更新钩子必须同步完成；战斗长时行为使用状态机和到期 Tick。存档主要通过操作 ID + 完成事件驱动，不挂起整场对局。
- 桥接契约测试验证 Luax 结构化异步，但跨更新保留活动 Continuation 不属于首版。更新遇到活动 Continuation 或结算事务时返回 `Busy`。
- Runtime 卸载后旧 FunctionHandle/ContinuationHandle 不复用。所有跨线程请求包含服务实例、操作 ID 和必要的代次信息。
- 普通 gameplay 调用中的发包和存档请求先放入调用级输出缓冲；脚本入口成功返回且输出校验通过后才执行。失败时丢弃未提交输出并中止本局，不继续使用可能已部分修改的状态。
- 更新导出、导入、校验和试运行使用受限宿主能力，不允许真实发包或持久化。异常只记录有限诊断，不退回无预算执行。

## 5. Android 宿主与随包发布

### 5.1 构建与启动

构建目标为 `hunter_server_core`、`hunter_server_android` 和 `hunter_server_desktop`。Android JNI 属于宿主适配层，不向 Luax 增加 Lua C ABI。

- 首版默认 `arm64-v8a`、最低 Android 8.0。P0 根据客户端实际 Unity 版本锁定兼容 NDK、SDK、Gradle 和 C++23 标准库；检查 `std::expected` 等所需能力。
- Android 宿主以 AAR 交付，包含 Service、Manifest、桥接层和 `libhunter_server.so`，客户端集成到 Unity。核对 C++ 运行库打包方式和原生依赖，避免冲突或漏包。
- `ServerService` 使用 `android:process=":server"`、`android:exported="false"`，采用 Bound Service，不使用跨应用共享进程或后台常驻策略。
- Unity 绑定 Service 后，由 Service 启动原生逻辑线程，初始化资源、Luax 和 SQLite，再绑定 `127.0.0.1:0`。
- Binder Messenger 交付实际端口、本次启动标识、临时握手令牌和版本；Unity 通过 TCP 校验后进入游戏。仅通过完整握手的客户端可提交玩法命令。
- Manifest 声明 Socket 所需的 `INTERNET` 权限；首版无远端连接、局域网发现和运行时下载入口。应用初始化需区分主进程与 `:server`，不能在服务进程初始化第二套 Unity。

控制通道包含 `Start/Ready/Pause/Resume/Stop/Error`，开发构建增加 `Reload`。请求使用关联 ID，返回明确状态；Android 回调不等待 native 线程完成磁盘工作。Windows 对等控制通过标准输入/输出实现，日志使用独立通道。

### 5.2 资源与存档

- 随包提供完整脚本 Bundle、配置、地图及运行依赖。首次启动将资源复制到应用内部临时版本目录，校验成功后启用；不使用可被系统清理的 cache 目录存放存档。
- 资源目录只读使用，更新目录与存档目录分离。SQLite 由服务端独占，客户端通过消息查询结果，不直接打开存档。
- 发布流程在 PC 离线编译、构建和签名 LUXB；APK 只携带验证公钥与匹配的 identity，不携带签名私钥或开发编译器。
- 检查原生库依赖、ABI、APK 打包及 16 KB 页面兼容性，不能以 Windows 构建通过代替 Android 验证。

### 5.3 移动端生命周期

- 切后台或锁屏时暂停游戏模拟，保留绑定；回到前台先确认服务实例及对局仍有效，再清除旧持续输入并恢复，不补算后台时间。
- 暂停时不运行持续战斗，不申请后台保活或唤醒锁。生命周期状态同时由 Android 桥接层驱动，不依赖 Unity 渲染帧持续运行。
- 服务端死亡或被回收时终止当前局，返回入口并重新建立服务实例；不把新进程误认作旧局，也不隐式恢复未完成战斗。
- 正常退出停止接收新玩法任务，完成已接受的事务收敛，关闭监听、VM 和数据库，再解除绑定。客户端死亡通过 Binder 死亡通知、连接断开及宿主状态处理。
- 不依赖 `onDestroy` 或最后一个退出回调保存关键结果。系统强杀后以已提交 SQLite 事务恢复。
- Android 可以保留已停止组件的缓存进程。退出验收要求模拟、监听和数据库资源释放，不要求 PID 立即消失。

## 6. 开发期局内热更新

热更新只修改玩法逻辑，保持 Host API、状态结构、协议、地图和配置身份一致。发布包固定脚本版本并移除触发入口。

### 6.1 状态约束

- 所有可变世界状态集中为一个可显式导出和导入的数据模型；模块函数及依赖表与世界状态分离。
- 实体之间保存 ID，定时器保存到期 Tick、事件名和参数，不保存函数、userdata、循环引用或协程栈。
- 快照包含 Tick、实体 ID 分配器、输入去重状态、冷却、AI 状态、局内物品和可序列化随机状态。不得假定内建 random userdata 能自动跨 VM 序列化。
- 导出/导入由脚本使用明确 JSON schema 完成，C++ 只搬运有界快照并核对版本。首版不实现通用 Lua 对象图复制器。

### 6.2 切换流程

1. PC 开发工具将完整候选脚本放入独立版本目录；Android 调试包通过开发工具写入应用私有目录，再经 Binder `Reload` 触发，不开放远程更新服务器。
2. 逻辑线程在 Tick 边界检查安全点。活动 Continuation、未完成结算或已有更新均返回 `Busy`，不能强行迁移其句柄。
3. 暂停游戏时间，导出当前状态并创建候选 Isolate；装载完整脚本、注册能力，核对接口与状态版本。
4. 导入状态，执行校验与一个无外部副作用的试运行；试运行覆盖输入和宿主输出格式检查。
5. 试运行成功后销毁试运行候选，重新创建候选并导入原始快照，避免试运行修改、隐藏闭包或随机状态遗留。
6. 安全点切换活动脚本实例，增加代次，刷新入口句柄，通知客户端脚本版本并发送完整快照，再释放旧实例。
7. 切换前失败时丢弃候选，原实例继续；切换后的新逻辑故障按普通脚本故障中止本局，不承诺撤销已发送消息或已提交事务。

更新全过程设置资源与执行预算。暂停期间输入有界排队，恢复时按原顺序处理；游戏时间不追赶。旧代次异步结果必须有明确处置，不能恢复到新实例。

## 7. 结算与异常边界

- C++ 开局时分配稳定对局 ID；脚本计算结果，Storage 将结果记录、去重键及累计存档变更放入同一事务。
- 同 ID 同内容重试返回已保存结果；同 ID 不同内容返回冲突，不能再次计奖。
- `Accepted` 仅表示任务接收，`Committed` 才表示持久化成功。结算 UI 以 `Committed` 或查询到的已提交记录为准。
- 提交失败保留明确错误，不伪装成功。重新启动读取最后已提交存档，未完成局不发奖、不恢复。
- 保存损坏时报告诊断并保留原文件，不静默覆盖；具体重置入口由客户端与策划共同验收。
- C++ 调用脚本使用资源预算与结构化错误；脚本错误、非法输出、过期句柄和超时均有日志及关联 ID。
- 状态机明确区分宿主 `Starting/Ready/Stopping/Stopped/Faulted` 与对局 `Idle/Running/Paused/Settling/Finished/Aborted`。热更新是宿主调度屏障，不借助散落的布尔值改变终态。

## 8. Intent 驱动的开发约定

借鉴 [game-net-core 的 intent 流程](https://github.com/YanqingXu/game-net-core/blob/main/intents/README.md)，保留设计契约与可验证性，不照搬其大规模性能及发布治理。

每份正式 intent 必须包含：目标、非目标、不变量、线程与所有权、接口与值语义、失败/取消/退出行为、验证入口和依赖。元数据采用 `id/status/target/depends_on/verification`；状态为 `draft/active/deferred`，完成进度由本路线表和证据记录单独维护。

首批契约：

| ID | 主题 | 关键不变量 |
| --- | --- | --- |
| SRV-001 | 平台宿主与生命周期 | 同包启动、服务实例唯一、回收后不误续旧局 |
| SRV-002 | 传输与协议 | 只监听回环、完整握手、分帧和队列有界 |
| SRV-003 | Tick 与调度 | VM 单线程拥有、无重入、暂停不推进游戏时间 |
| SRV-004 | Luax Host 桥接 | 类型明确、无跨线程借用、完成事件可关联 |
| SRV-005 | 脚本模块和世界状态 | main 组装、单一权威状态、构建依赖可复现 |
| SRV-006 | 开发热更新 | 安全点切换、失败保留旧实例、不迁移活动 continuation |
| SRV-007 | 存档与结算 | 事务去重、提交后才确认、不恢复未完成局 |
| SRV-008 | PvE 端到端 | 真机真实双进程完成战斗、撤离和重启读取 |

工作流程：先更新 intent 和相关规则 → 明确接口 → 增加有意义的契约测试 → 实现 → 运行针对性验证 → 记录证据。文档调整本身不要求增加实现镜像测试。

active intent 的构建目标与验证入口必须真实存在；未实现能力保持 draft 或 deferred。CI 校验索引、依赖、目标与测试入口，不能用未来设计声明当前能力已经可用。核心 PR 写明所属线程、释放者、重入边界、跨线程通道及验证结果。

## 9. 实施路线与验收

当前已实现 P0 的 Windows 工程、Luax 探针及桌面运行闭环；Android 仅提供探针入口，
没有 NDK／真机证据，P0 整体尚未完成，P1～P5 尚未开始。详见 [验证记录](VERIFICATION.md)。
原参赛日程仅作时间约束；新增 Android 宿主、Luax 适配和热更新后，先用 P0/P1 实测工时再重排，
不直接沿用 PC 排期。

| 阶段 | 前置 | 主要交付 | 完成标准 |
| --- | --- | --- | --- |
| P0：工程与 Luax 验证 | 无 | intent 模板、CMake、固定依赖、PC/NDK 编译、模块组装与桥接探针 | C++ 调脚本、脚本调 Host、异步完成、源码与签名 Bundle 两条加载路径可验证；Android ARM64 能加载运行 |
| P1：Android 运行闭环 | P0 | AAR、Service、Binder、TCP 握手、就绪/暂停/退出、资源目录 | 真机离线安装后自动启动双进程，锁屏和返回正确，退出释放运行资源 |
| P2：脚本动作切片 | P1 | main、world、movement、输入、碰撞、快照、地图导出与触控联调 | 手机完成灰盒移动/跳跃/攻击，结果由脚本权威产生，持续运行无队列增长 |
| P3：热更新切片 | P2 | 快照 schema、候选验证、安全点切换、开发控制 | 移动中更新逻辑保留状态；错误脚本、结构不符或 Busy 情况可正确处理 |
| P4：PvE 与持久化闭环 | P3 | 战斗、AI、Boss、掉落、拾取、撤离、SQLite | 完整撤离可玩；成功、死亡、重复提交和重启读取结果正确 |
| P5：冻结与发布 | P4 | 故障回归、性能预算、发行 Bundle、Release APK | 干净手机离线可玩，开发入口移除，原生依赖与资源完整，必测项通过 |

每阶段记录提交、Luax pin、工具链、脚本/内容摘要、测试命令、真机型号/系统及失败项。S 主责服务端、脚本框架和工具，C 主责 Unity、Android 桥接集成和整包，D 确认玩法及失败体验；接口与 APK 用 C/S 联合验收。

### 必须验证的场景

- **协议**：拆包、粘包、超限帧、版本错误、无效握手、重复输入、慢读与队列饱和。
- **桥接**：正常双向调用、wrong_thread、重入拒绝、参数错误、table 直接跨界拒绝、过期句柄、异步重复完成与取消。
- **模拟**：固定种子和输入可复现，移动/碰撞边界、暂停时冷却与撤离计时停止，死亡与撤离互斥。
- **热更新**：语法错误、预算超限、状态不符、连续更新、待结算 Busy、旧完成事件到达，以及 Tick、弹药、随机和输入序号连续。
- **存档**：提交前/后强杀、写入失败、损坏存档、重复结果、覆盖安装后读取既有结果。
- **移动端**：首次断网安装、快速前后台切换、锁屏、Activity 重建、任一进程被杀、低内存回收和资源初始化中断。
- **发布**：ARM64 原生依赖、4 KB/16 KB 页面环境、无开发工具机器运行、无私钥与运行时编译器、开发更新入口关闭。
- **稳定性**：连续 10 局和 10 次启停，观察 CPU、内存、GC、Tick 耗时、发热及监听/数据库资源释放。

## 10. 参考依据

- [Luax 候选 README：语言定位、Host API、Bundle 与当前成熟度](https://github.com/YanqingXu/luax/blob/d8a8160420074f8e590d94910d1af5a5f5a93245/README.md)
- [Luax Isolate 公共接口](https://github.com/YanqingXu/luax/blob/d8a8160420074f8e590d94910d1af5a5f5a93245/include/luax/Isolate.hpp)
- [Luax detached 异步完成边界](https://github.com/YanqingXu/luax/blob/d8a8160420074f8e590d94910d1af5a5f5a93245/docs/architecture/bind-detached-async-completions.md)
- [Luax 值类型](https://github.com/YanqingXu/luax/blob/d8a8160420074f8e590d94910d1af5a5f5a93245/include/luax/Value.hpp) 与 [Host 契约测试](https://github.com/YanqingXu/luax/blob/d8a8160420074f8e590d94910d1af5a5f5a93245/tests/contract/luax_host_function_contract.cpp)
- [Android 进程与线程](https://developer.android.com/guide/components/processes-and-threads)、[Bound Service](https://developer.android.com/develop/background-work/services/bound-services)
- [Android 应用专属存储](https://developer.android.com/training/data-storage/app-specific)、[16 KB 页面兼容性](https://developer.android.com/guide/practices/page-sizes)

Luax 能力以指定提交为基线。Hunter 已构建并运行 Windows 模块装配、Host 桥接、结构化异步及
签名 Bundle 契约；这些能力由 Hunter 接入层与固定 Runtime 共同提供。Android 适配和更新控制
仍是待实现设计，不能由 Windows 测试推断其已可用。

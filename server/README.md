# Hunter Server

技术栈为 **C++23 / Standalone Asio 1.36.0 / Luax / Protobuf / CMake**。
构建显式定义 `ASIO_STANDALONE`，使用独立 Asio 头文件，不依赖 Boost。

当前交付 Windows 基础战斗切片：本机 TCP 鉴权、会话登录、开局、权威跑跳／碰撞、
射线枪械／换弹、普通怪 AI、死亡与清怪重开，以及暂停恢复、有界背压和资源收尾。
C++ 持有唯一世界状态和对象生命周期，Luax 编写玩法规则；不包含撤离结算、热更新、Unity 接入或 Android Service。
独立 SQLite 存储底座已实现，提供永久存档与事务结算接口，尚未接入当前对局或宿主启动流程。
Entity/Unit/Player/Monster/Item/Weapon/World 各有 C++ 类及同名小写 Lua 模块；
实体按 ID 管理，玩家输入和枪械状态与怪物 AI 分离，支持不同配置怪物共存。
默认灰盒数值保持不变；多配置、实体增删及分配边界由真实 Luax 契约验证。
设计见 [规划](plan.md)，契约见 [intents](intents/README.md)，实际结果见 [验证记录](VERIFICATION.md)。

## 构建与测试

需要 CMake 3.28+、支持 C++23 `std::expected/std::jthread` 的 x64 MSVC、Windows SDK、
Python 3.10+ 和 Git。本机验证使用 VS 2026；CI 使用 Windows 2025 镜像上的 MSVC。
从仓库根目录进入 `server`，执行：

```powershell
Set-Location server
cmake --preset win-dev -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax
cmake --build --preset win-dev --parallel 8
ctest --preset win-dev
```

本地 Luax 覆盖必须是固定提交的干净检出；其他机器可换成自己的路径，或省略覆盖参数让
FetchContent 按固定 Git 提交获取。访问私有依赖需要 Git 已有读取权限。CI 的 Luax 若为私有仓库，
需要仓库 secret `LUAX_READ_TOKEN`；本轮没有配置远程 secret，也没有运行远程 CI。

源码锁定在 `cmake/Deps.cmake`：Luax `d8a8160`、Standalone Asio 1.36.0、Protobuf 33.0、
Abseil 20250512.1、nlohmann/json 3.12.0，全部固定完整提交；公共归档另验 SHA-256。
SQLite 3.53.4 使用固定官方 amalgamation，校验归档及 `sqlite3.c/sqlite3.h` 摘要；
Windows 与未来 Android 构建编译同一源码，不依赖系统 SQLite。
可用 `FETCHCONTENT_SOURCE_DIR_<大写依赖名>` 指向干净的同版本源码。
依赖缓存位于构建目录，首次构建需要准备依赖；服务运行不访问远程地址。

生成产物为 `build/win-dev/generated/game.lua`、源映射、C++ Protobuf 与
`generated/csharp/Hunter.cs`。`generated/SchemaSpec.h` 从 `lua/contract.json` 生成字段及边界，
Luax 输出提交与 Protobuf 转换共用同一校验器。C# 产物尚未进入 Unity 联调。`hunter_tools` 可单独构建
本机 `protoc`、`luaxc`、`luax-bundle`。头文件与实现按模块并置，公共包含根为 `src`。

## 桌面控制与协议

```powershell
./build/win-dev/Release/hunter_server_desktop.exe `
    --source build/win-dev/generated/game.lua
```

stdin 每行一个 JSON 对象；`req_id` 为非空字符串，最多 128 字节。标准输出只有控制 JSON，
诊断写 stderr，调用方须同时读取两个通道。输入示例：

```json
{"req_id":"start-1","cmd":"Start"}
{"req_id":"pause-1","cmd":"Pause"}
{"req_id":"resume-1","cmd":"Resume"}
{"req_id":"stop-1","cmd":"Stop"}
```

Start 成功返回 `type=Ready`，包含 `port/instance/token/protocol_version/content_version`。
后续控制返回 `Rsp` 或 `Error`，关联原始 `req_id`。错误与状态的精确定义见
[宿主契约](intents/architecture/host.intent.md)。一个进程承载一次服务实例，Stop 后退出；
重复 Start 在 Ready 状态返回同一个实例。标准输入 EOF、输出管道断开也触发收尾。
正常停止返回退出码 0；脚本 shutdown 返回 false、抛错或原生关闭失败会保留有界诊断，
进入 Faulted 并返回 1，同时继续释放资源。管道取消超时返回 2。

客户端连接 `127.0.0.1:<port>`；每帧为四字节大端正文长度加 Protobuf `hunter.wire.Envelope`。
第一次消息必须为 Hello，逐项回传 Ready 的版本、实例和令牌，5 秒内完成握手。
正式定义位于根目录 `protobuf/hunter.proto`，不能另建私有协议来源。
网络协议保持 v3，Host 契约、上下文和内部状态为 v4，内容为 v2。
旧协议客户端和旧内部状态拒绝接入／导入，不提供状态迁移；客户端仍按 `kind/cfg_id` 选择配置。

协议为 v3，Hello 后发送 LoginReq，再发送 StartReq 才进入游戏。StartReq 携带上局 ID，
首局为 0；重复请求不重置活动世界。清怪或死亡后可在同一连接重开。旧计数协议不再提供。
FrameInput 携带局 ID、连接内单调的非零 uint64 序号、左右／停止、二维瞄准、跳跃、开火和换弹。
InputAck 仅确认操作已处理；命中、伤害和死亡以事件与快照为准，客户端不提交这些结果。
60 Hz 固定模拟、20 Hz 快照；整数毫米坐标，脚底中心，X 向右、Y 向上。
高频输入、确认、事件和快照在 C++ 直接处理；Lua 不编解码这些消息的 JSON。
低频 JSON 和测试客户端中的 ID、序号与 Tick 使用十进制字符串。

当前只接纳首个 TCP 客户端；额外连接关闭。首次客户端无效握手、超限输入、断连或脚本错误
都会终止当前会话并关闭监听；需要新进程和新实例重新开始。
暂停清空尚未应用的命令及移动／开火／跳跃／换弹意图，并通知作废序号范围。
暂停中新输入返回 paused；恢复等待新输入，不回放旧动作、不补算暂停时间。

默认 60 Hz、最多补算 4 Tick；单 Tick 最多处理 64 条输入，一次定时回调共享 4 ms 输入工作预算，
其余输入保留 FIFO。脚本入口不可中途抢占，因此该预算是让出事件循环的软预算；回调最多额外
完成一个输入入口和一个 Tick 入口，各入口仍受独立执行预算约束。
消息和 JSON 上限 64 KiB，主要队列各 256 条／1 MiB，默认限制集中在 `Cfg`。
测试参数只接受完整正十进制：`--handshake-ms/--stop-ms` 为 1～60000 毫秒，
`--queue-count` 为 1～65536 条，`--send-bytes` 为 1～16777216 字节；负号、零、尾随内容和
越界值均拒绝。异步探针适配器独立测试真实 continuation、定时器和后台完成；游戏入口保持同步。

## 灰盒配置与协议客户端

共享源为 `../design/combat_demo.json`；构建自动运行 `../export/combat.py`，生成
`generated/content.json` 与 `ContentSpec.h`。内容摘要进入 Ready／Hello，客户端应使用同一导出数据。
源配置包含毫米地图、实心平台、玩家与怪物出生点以及枪械数值；数据校验失败会阻止构建。
具体导出和单位约定见 [导表说明](../export/README.md)。

开发构建同时生成 `hunter_client.exe`。分别运行桌面服务和客户端，向客户端第一行输入服务端
Ready 的完整 JSON；握手成功后逐行发送以下命令。客户端持续输出快照和事件，EOF 关闭连接。

```json
{"cmd":"login","req_id":"login-1"}
{"cmd":"start","req_id":"start-1","after_match_id":"0"}
{"cmd":"input","seq":"1","match_id":"1","move_x":1,"aim_x":1000,"aim_y":0,"jump":true,"fire":false,"reload":false}
{"cmd":"input","seq":"2","match_id":"1","move_x":0,"aim_x":1000,"aim_y":0,"jump":false,"fire":true,"reload":false}
```

这些命令在客户端 stdin 使用 JSON，真实 TCP 始终传输 Protobuf。移动与开火持续到下一条输入改变；
跳跃与换弹为按下动作。暂停通过服务端宿主控制通道执行。结束后以当前局 ID 请求新局，序号继续递增。
客户端 EOF／对端正常 EOF 返回 0；协议错误或连接重置输出 client_error 并返回 1，均有界收尾。
`hunter_process_integration` 自动管理两个真实进程，包含死亡／清怪／重开场景。

## 生产 Bundle 模式

开发 CTest 的 `hunter_bundle_contract` 会生成公开测试向量签名的测试制品；只用于验证。
其中 `game.luxb` 是正式游戏模块，`entities.luxb` 包装实体测试入口；后者不用于游戏运行或发行。
使用它们验证生产 Runtime：

```powershell
$devBuild = (Resolve-Path build/win-dev).Path
cmake --preset win-bundle -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax `
    "-DHUNTER_HOST_PROTOC=$devBuild/_deps/protobuf-build/Release/protoc.exe" `
    "-DHUNTER_TEST_BUNDLE=$devBuild/bundle-test/game.luxb" `
    "-DHUNTER_TEST_ENTITY_BUNDLE=$devBuild/bundle-test/entities.luxb" `
    "-DHUNTER_TEST_POLICY=$devBuild/bundle-test/policy.json"
cmake --build --preset win-bundle --parallel 8
ctest --preset win-bundle
./build/win-bundle/Release/hunter_server_desktop.exe `
    --bundle "$devBuild/bundle-test/game.luxb" --policy "$devBuild/bundle-test/policy.json"
```

生产模式编译链接 `Luax::Runtime`，拒绝源码加载；不能用运行参数切换到开发 Runtime。
未显式指定实体测试 Bundle 时，默认从游戏测试 Bundle 同目录读取 `entities.luxb`。
CTest 检查真实链接参数不含 Compiler、AST、DevelopmentRuntime，并验证错误公钥、身份和篡改拒绝。
生产运行时仅接受可信交付的公钥 policy 和签名 Bundle。正式离线签名步骤见
[工具说明](tools/README.md)；私钥不进入可执行文件、运行资源或仓库。

`cmake --install build/win-bundle --config Release --prefix build/stage` 只安装桌面可执行文件。
正式发行需要另外交付可信制品、公钥策略及系统运行库；本轮不宣称干净机器发行包已验收。

## 独立持久化模块（SRV-007）

使用方链接 `hunter_storage` 并包含 `storage/Storage.h`，命名空间为 `hunter::storage`。
`Db` 封装资源、`Schema` 固定 V1 结构、`Store` 执行业务事务，`Storage` 提供异步门面；
这些模块没有 Luax、World、Protobuf 依赖，也不会自行创建桌面游戏存档。

在逻辑线程构造 `Storage(io, StorageCfg{}, instance)`，instance 必须为非零关联 ID。
调用 `open(UTF-8 文件路径, done)`；父目录由宿主准备，首次成功打开才初始化玩家 1。
收到 `Opened` 后才能提交业务请求。打开失败需关闭该对象，使用新对象重试。
同一个存档文件由一个 Storage 实例拥有。

| 方法 | 成功完成值 | 含义 |
| --- | --- | --- |
| `open(path, done)` | `Opened` | 文件、配置、schema 和完整性检查通过 |
| `load_player(player_id, done)` | `PlayerSave` | 身份、revision、最后提交的 match_id 和全部永久物品 |
| `alloc_match(done)` | `MatchId` | 已事务提交的稳定 ID，允许空洞、不复用 |
| `commit_match(req, done)` | `MatchResult` | 已 Committed，`replayed` 表示返回先前的同一结果 |
| `find_match(match_id, done)` | `MatchResult` | 已提交的原始结果，缺失返回 `NotFound` |
| `stop(done)` | `Closed` | 已接受任务及其回调排空，数据库连接已关闭 |

每个入口返回 `std::expected<Accepted, Error>`；拒绝不会产生回调或磁盘副作用。
`Accepted` 仅表示受理，最终 `Rsp{key, result}` 通过 Asio 延后交付。`key` 保留 instance/op，
成功的 `MatchResult` 才代表持久化成功；`result_json` 含 v1、完整结算字段、前后 revision、
提交毫秒时间及带永久 UID 的奖励。读取或重试原样返回这个 JSON，不根据当前配置重新发奖。

`CommitMatch` 提供 `match_id/player_id/expected_revision/outcome/content_key/items`。
items 为 `{cfg_id, count}` 正数增量，可为空；每行产生独立物品，顺序是请求内容的一部分。
存储层生成固定格式的 request_json；同 ID 同内容先于 revision 校验返回旧结果，不同内容冲突。
请求、奖励、玩家 revision 和结果在一个事务内提交。SQLite INTEGER 的上限也是 ID/revision
上限，JSON 直接编码整数，不经过浮点数；永久 item_uid 不使用 World 实体 ID。

`Error.code` 区分参数、容量、未就绪、未找到、内容冲突、revision 冲突、溢出、版本、损坏、
锁超时及写失败；`sqlite_code` 保留 SQLite 扩展码。`commit_unknown=true` 表示不能确认
COMMIT 结果，调用方通过查询或相同请求重试确认，不能展示保存成功。

默认限制为 64 个未交付操作、请求 1 MiB、完成 4 MiB、单结果 1 MiB。读取和结算按最大结果
预留完成槽，因此默认最多同时接受四个大结果操作；超限显式失败，不截断存档。
计费覆盖固定任务和拥有的请求／返回数据，不包含调用方回调捕获的外部对象或 SQLite 缓存。

io_context 由创建 Storage 的同一线程运行，必须活过所有完成；回调不得抛异常。
stop 使用独立控制槽，普通队列饱和也能关闭；已接受事务不取消，调用方持续驱动 io 至 Closed。
析构仅作为等待磁盘工作退出的保底路径，不执行“保存全部”；旧完成拥有独立关联和数据，
回调捕获的对象仍须由调用方保证有效。正常业务不依赖析构或退出回调保存结算。

数据库使用 `user_version=1`、WAL、FULL、外键和 2000 ms 锁等待。仅空库从 0 初始化，
已有库必须匹配 V1 完整结构并通过 quick_check／外键检查；不自动重置异常或高版本库。
物品表增加正数约束和延迟外键，结果表约束 JSON 合法性及 revision 增量。

```powershell
cmake --build --preset win-dev --target hunter_storage_contract hunter_storage_crash --parallel 8
ctest --preset win-dev -R hunter_storage
```

`hunter_storage_contract` 覆盖事务、重启、容量和线程；`hunter_storage_crash_integration`
由父进程在精确事务检查点强杀并重新读取，同时验证真实 SQLITE_FULL 回滚。
注入点只编译到测试专用 `hunter_storage_fault`，正式库没有故障开关。
Runtime 启动读档、World 对局 ID、Lua 奖励和网络结果查询均留待下一轮接入。

## Android 探针与后续边界

本轮没有安装 NDK。preset 接受客户端后续锁定的 `ANDROID_NDK_HOME`，目标为
`arm64-v8a`、API 26；原生探针使用独立可执行文件和静态 C++ 运行库，不定义最终 AAR 的运行库策略。

```powershell
cmake --preset android-probe -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax `
    "-DHUNTER_HOST_PROTOC=$devBuild/_deps/protobuf-build/Release/protoc.exe"
cmake --build --preset android-probe --target hunter_android_probe
py -3 tools/android_probe.py --exe build/android-probe/hunter_android_probe `
    --bundle "$devBuild/bundle-test/game.luxb" --policy "$devBuild/bundle-test/policy.json" `
    --evidence build/android-probe/device-evidence.json
```

缺少 NDK、adb 或设备时必须报告未验证，不能跳过后算成功。探针核对真实签名加载、Host 调用和
Asio Tick，并记录设备与制品摘要；它不替代 AAR、Binder、Unity、APK、16 KB 页面真机和生命周期验收。
P0 整体仍待 Android 证据，P1 及以后按规划推进。

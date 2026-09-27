# Hunter Server

技术栈为 **C++23 / Standalone Asio 1.36.0 / Luax / Protobuf / CMake**。
构建显式定义 `ASIO_STANDALONE`，使用独立 Asio 头文件，不依赖 Boost。

当前增量 [SRV-014](intents/usecases/hunt.intent.md) 接入怪物能力、狂暴与召唤、玩家天赋和
手动复活、赏金第二波、区域线索与特殊视角、场景读条和炸药桶，以及 SQLite V2 永久猎人。
保留分段回血与体力、走跑匍匐、双枪与穿透、医疗、投掷、梯子、补给和幂等结算。
C++ 持有唯一世界、物品和计时状态，Luax 编写玩法规则；热更新、Unity 与 Android Service
属于独立后续工作。仅接纳一个客户端、一名玩家和一个活动世界。永久出战使用持有资产；
免费配装必须显式 `test_mode=true`，命令行测试客户端的普通 start 会补上该标志。
Entity/Unit/Player/Monster/Item/Weapon/World 各有 C++ 类及同名小写 Lua 模块；
实体按 ID 管理，玩家输入和枪械状态与怪物 AI 分离，支持不同配置怪物共存。
正式内容从 [根目录 Excel 与显式来源清单](../design/DEMO内容说明.md) 生成；
旧首版 Excel 和灰盒 JSON 仅用于回归。
设计见 [规划](plan.md)，契约见 [intents](intents/README.md)，实际结果见 [验证记录](VERIFICATION.md)。

协议为 v6、内容为 v5、Host／内部状态为 v8、SQLite 为 V2。实现与验证状态见
[本轮任务表](V1_HUNT_TASKS.md)，永久接口见 [猎人接口](HUNTER_API.md)。新技能、怪物能力、
区域和经济数值仍须策划完整填写并显式选入；正式内容保持原 19 张表，`career=false` 时
允许免费招募、查询，但拒绝永久出战及退役。独立机制夹具不作为生产数值。

此前 [SRV-013](intents/modules/cfg.intent.md) 已完成 Windows 实现与验证：源表导出独立 Lua，
由 Lua 加载配置、解析关联并决定配装、拾取、消耗、掉落和状态语义。C++ 保留权威状态、
网络、存储及有界原子修改接口，不再持有完整配置文档。本轮任务与实际进度见
[Lua 配置迁移记录](V1_LUA_CFG_TASKS.md)。开发完整回归 32/32、Bundle 31/31，两种联调包
解压运行通过；此前验收记录继续保留。

## 构建与测试

### VS2026 单项目开发入口

日常开发打开本目录的 **[HunterServer.slnx](HunterServer.slnx)**，解决方案只有一个
`HunterServer` 项目。全新检出或删除 `build/` 后，先按下面的命令执行一次 CMake 配置，
生成对应工程。也可打开 `build/win-dev/HunterServer.Dev.slnx`，它指向同一个项目。
`build/win-dev/HunterServer.slnx` 是 CMake 的完整构建解决方案，包含第三方库和测试等目标。

项目中的头文件与实现文件并列展示，目录对应源码位置：

```text
HunterServer（唯一项目）
├─ src/          common、core、game、net、script、storage
├─ platform/     desktop/Main.cpp 与 Android 探针
├─ lua/          main.lua、game/、framework/ 与模块契约
├─ protobuf/     仓库根目录的协议定义
├─ generated/    自动生成的协议、配置身份与聚合脚本
├─ cmake/        构建配置与 VS 工程模板
├─ tools/        离线工具与协议客户端源码
├─ tests/        契约、集成测试与夹具
├─ intents/      设计契约
└─ rules/        编码规则
```

1. 选择 `x64` 和 `Debug`（断点调试）或 `Release`；也支持 `RelWithDebInfo`、`MinSizeRel`。
2. 按 **Ctrl+Shift+B** 编译，构建桌面服务及所需第三方库、协议和 Lua 产物。
3. 按 **F5** 调试或 **Ctrl+F5** 运行，在控制台输入 `{"req_id":"start-1","cmd":"Start"}`，
   收到 `Ready` 后可连接客户端；输入 `{"req_id":"stop-1","cmd":"Stop"}` 正常退出。
4. 开发存档位于 `build/win-dev/ide/saves/<配置名>.sqlite`；脚本、工作目录和程序路径已配置。
   此入口把客户端握手等待和单次脚本执行限时设为 60 秒，方便手动联调与断点调试；
   如需修改启动参数，使用项目属性的“调试”页。普通启动的脚本限时仍为 50 毫秒；
   `--script-ms` 仅在开发 Runtime 可用，范围为 1～60000 毫秒。

该项目使用 [VS Makefile 项目类型](https://learn.microsoft.com/cpp/build/reference/creating-a-makefile-project)，
将“生成／重新生成／清理”交给同一个 CMake 构建树。
IntelliSense 的包含路径与宏从实际编译目标生成。可执行文件仍位于
`build/win-dev/<配置名>/hunter_server_desktop.exe`；测试、Android 探针和工具源码便于查阅，
不会因显示在项目中而自动加入桌面程序。需要协议客户端时执行
`cmake --build --preset win-dev --target hunter_client`，完整测试仍使用下方命令。

新增／删除文件会在后续构建时触发 CMake 更新目录，VS 提示工程变化时重新加载；也可手动
执行 `cmake --preset win-dev` 立即刷新。新增需要编译的 `.cpp` 仍须加入 `CMakeLists.txt`
中的对应目标；不要直接维护 `build/` 下生成的 `.vcxproj` 或 `.filters`。
“清理／重新生成”会清理共享构建树的当前配置产物与共用生成文件，后者在下次构建时恢复；
源码、依赖源码、其他配置的可执行文件及开发存档保留。

### 命令行配置与完整测试

需要 CMake 3.28+、支持 C++23 `std::expected/std::jthread` 的 x64 MSVC、Windows SDK、
Python 3.10+ 和 Git。VS2026 生成器需要支持它的 CMake，建议在 VS2026 Developer PowerShell
使用 VS 自带版本；旧的 PATH 版本可能无法识别 `Visual Studio 18 2026`。
本机验证使用 VS 2026；CI 使用 Windows 2025 镜像上的 MSVC。
从仓库根目录进入 `server`，执行：

```powershell
Set-Location server
cmake --preset win-dev -G "Visual Studio 18 2026" -A x64
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

生成产物为 `build/win-dev/generated/cfg/` 原字段 Lua 表及清单、聚合的 `generated/game.lua`、
源映射、客户端共享 `generated/content.json`、仅含身份的 `generated/ContentId.h`、C++ Protobuf 与
`generated/csharp/Hunter.cs`。`generated/SchemaSpec.h` 从 `lua/contract.json` 生成字段及边界，
Luax 输出提交与 Protobuf 转换共用同一校验器。C# 产物尚未进入 Unity 联调。`hunter_tools` 可单独构建
本机 `protoc`、配置校验用 `luax`、`luaxc`、`luax-bundle`。头文件与实现按模块并置，公共包含根为 `src`。

## 桌面控制与协议

```powershell
./build/win-dev/Release/hunter_server_desktop.exe `
    --source build/win-dev/generated/game.lua --storage sqlite --save ./saves/demo.sqlite
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
Ready 之前必须完成存档打开和玩家加载；省略 `--save` 使用 `%LOCALAPPDATA%/Hunter/save.sqlite`。
`--storage` 默认 `sqlite`，当前仅实现此后端；未知或未实现的后端明确失败，不回退 SQLite。
只有选择 SQLite 时才准备默认存档路径及父目录，`--save` 始终表示 SQLite 文件路径。
首次启动创建 SQLite V2；旧 V1 在事务中迁移并保留 UID、局号高水位和历史结果原文。
损坏、陌生版本、不可写路径返回失败，禁止自动清档。
后续控制返回 `Rsp` 或 `Error`，关联原始 `req_id`。错误与状态的精确定义见
[宿主契约](intents/architecture/host.intent.md)。一个进程承载一次服务实例，Stop 后退出；
重复 Start 在 Ready 状态返回同一个实例。标准输入 EOF、输出管道断开也触发收尾。
正常停止返回退出码 0；脚本 shutdown 返回 false、抛错或原生关闭失败会保留有界诊断，
进入 Faulted 并返回 1，同时继续释放资源。管道取消超时返回 2。

客户端连接 `127.0.0.1:<port>`；每帧为四字节大端正文长度加 Protobuf `hunter.wire.Envelope`。
第一次消息必须为 Hello，逐项回传 Ready 的版本、实例和令牌，5 秒内完成握手。
正式定义位于根目录 `protobuf/hunter.proto`，不能另建私有协议来源。
网络协议为 v6，Host 契约、上下文和内部状态为 v8，内容为 v5，SQLite 为 V2。
旧协议客户端和旧内部状态拒绝接入／导入，不提供状态迁移；客户端仍按 `kind/cfg_id` 选择配置。

Hello 后发送 LoginReq，再发送 StartReq。首个 StartReq 的 after_match_id 为 0，后续使用
当前局 ID；先完整校验配装再分配局号，重复成功请求不重建世界，同请求改变配装返回冲突。
缺省配装由服务端展开，StartRsp 返回接受后冻结的配装。局 ID 来自 SQLite，允许跳号，跨启动不复用。
登录与开局响应包含世界和控制实体身份。FrameInput 必须携带对应 world_id/match_id、
连接内单调的非零 uint64 序号、横纵移动、瞄准、跳跃、开火和换弹，以及明确的 run/prone 意图。
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

## Excel 配置与协议客户端

正式共享源由 `../design/demo_sources.json` 选择根目录工作簿、工作表和记录；
清单不保存数值覆盖。构建自动运行 `../export/gameplay.py --source`，每张表生成独立 Lua，
保留原英文字段和数字主键。`generated/cfg/Manifest.json` 显式声明数据模块；组装器区分
`data` 与 `factory`，通过依赖注入将数据交给 `game.cfg`，运行时不调用 `require/dofile`。
Python 只做工作簿读取、字段类型、记录选择和序列化，字段映射、单位、引用、默认配装及
玩法语义在 Lua 中处理。正式发布前使用固定 Luax strict 执行同一个配置入口；全部通过才
发布数据表、清单和客户端派生制品，失败保留上一份有效输出。

共享 `generated/content.json` 和 `ContentId.h` 从 Lua 的规范内容派生；后者仅含身份，
不内嵌数值。服务端由脚本加载配置并注册内容身份及必要原生结构边界，启动不读取共享 JSON。
内容摘要进入 Ready／Hello，客户端应使用同一派生数据；数值和语义不变时摘要保持一致。
修改允许的源表数值后重新导出、组装脚本或签名 Bundle 即可改变玩法，不必重编服务端核心；
协议客户端自身绑定内容身份，交付时应同步生成和构建客户端。
唯一生产地图来源为地图表；旧怪物和 Boss 使用独立记录及 Attack 招式表，不读取天赋 Skill。
所选记录缺值、单位或引用非法会阻止构建；未选草稿不进入内容摘要，禁止回退旧工作簿。
具体导出和单位约定见 [导表说明](../export/README.md)。

配装先由只读 Lua 入口规范化并完整校验，再申请 SQLite 局号。拾取、叠堆、工具扣次与清槽、
补给和掉落拆分由 Lua 决定，C++ 核对实例、容量及数据范围后原子修改。状态导入先由 Lua
验证配置语义，再由 C++ 检查独立候选；失败不替换活动世界或改变句柄代际。测试配置通过
隔离 Lua 夹具组装，不经运行时上下文注入；上下文只含版本和快照频率。

开发构建同时生成 `hunter_client.exe`。分别运行桌面服务和客户端，向客户端第一行输入服务端
Ready 的完整 JSON；握手成功后逐行发送以下命令。客户端持续输出快照和事件，EOF 关闭连接。

```json
{"cmd":"login","req_id":"login-1"}
{"cmd":"start","req_id":"start-1","after_match_id":"0"}
{"cmd":"input","seq":"1","world_id":"1","match_id":"1","move_x":1,"aim_x":1000,"aim_y":0,"jump":true,"fire":false,"reload":false}
{"cmd":"bag","req_id":"bag-1","world_id":"1","match_id":"1"}
{"cmd":"pickup","req_id":"pick-1","world_id":"1","match_id":"1","item_id":"1"}
{"cmd":"switch_weapon","req_id":"switch-1","world_id":"1","match_id":"1","action_seq":"3","slot":2}
{"cmd":"use","req_id":"use-1","world_id":"1","match_id":"1","action_seq":"4","slot":6}
{"cmd":"interact","req_id":"scene-1","world_id":"1","match_id":"1","action_seq":"5","target_id":1}
{"cmd":"status","req_id":"save-1"}
{"cmd":"retry","req_id":"retry-1","match_id":"1"}
{"cmd":"result","req_id":"result-1","match_id":"1"}
{"cmd":"stash","req_id":"stash-1","revision":"0","cursor":"0","limit":128}
{"cmd":"abandon","req_id":"abandon-1","world_id":"1","match_id":"1"}
```

这些命令在客户端 stdin 使用 JSON，真实 TCP 始终传输 Protobuf。移动与开火持续到下一条输入改变；
跳跃与换弹为按下动作，run/prone 为目标状态。示例中的身份与场景 ID 必须替换成实际配置或响应值。
枪槽从 1 开始，常规工具槽为 1～4，消耗品槽为 5～8；melee/select_tool/use/interact 均走 ActionReq。
动作使用独立单调 action_seq，缓存最近 128 个响应；相同请求重发返回原响应，改变参数返回冲突，
缓存淘汰后的旧序号仍拒绝。生成客户端省略 action_seq 时自动递增，重试必须显式保留原序号。
PauseState 同步输入和动作的作废水位；暂停冻结前摇和恢复计时，恢复不补执行旧动作。
只有保存状态 Committed 后才能重开，输入和动作序号继续递增。
客户端 EOF／对端正常 EOF 返回 0；协议错误或连接重置输出 client_error 并返回 1，均有界收尾。
`hunter_process_integration` 自动管理两个真实进程，包含死亡／清怪／重开场景。

Boss 前摇、恢复和冷却由 Attack 表驱动，快照提供 ai/attack_ticks。Boss 出生实例死亡仅解锁
出口；回到出生地附近出口自动读条 180 Tick。快照提供资格、累计／剩余 Tick 和取消原因。
正伤害、死亡、离区清零，暂停冻结。拾取必须在 1500 mm 内，整份成功或整份拒绝。
地面 ItemId、配置 cfg_id 和永久 item_uid 分离；实体／物品引用须带本局 world_id/match_id。
免费武器、工具、消费品拾取及补给进入局内装备槽，不进入奖励背包或永久仓库。
进入梯子使用 interact；梯上只处理纵向移动，拾取及其他玩法动作均拒绝。
向梯顶或梯底移动到端点且站立空间足够时自动离梯，空间不足则保持梯上；旧动作不会补执行。
快照还提供有效体型、体力、血段、独立枪弹、工具次数、使用剩余 Tick、场景和飞行物。
既有完整玩法规则及历史验收见 [SRV-012](intents/usecases/gameplay.intent.md) 和
[玩法记录](V1_GAMEPLAY_TASKS.md)；当前配置及规则迁移进度见
[SRV-013 任务表](V1_LUA_CFG_TASKS.md)。

对局阶段 Preparing → Playing → Settling → Finished；玩家状态独立为 Alive/Dead/Extracted/
Abandoned。撤离只冻结背包奖励，死亡／放弃冻结空奖励；只有数据库 Committed 才到账。
保存状态 Idle/Saving/Failed/Unknown/Committed，Failed/Unknown 保留原请求；retry 先查询，
查无记录才重试冻结请求，重复返回同一结果。保存查询与回调在暂停时仍运行。
重启后先从 stash 的 last_match_id 取最近提交 ID，再用 result 查询；不恢复未结算战斗。

仓库每页默认／最多 128 条，首次 revision/cursor 为 0，后续带返回的 revision/next_cursor。
next_cursor=0 表示结束；stale_revision_or_page 要求从第一页重查。常见业务错误包含
stale_match、invalid_state、already_picked、out_of_range、bag_full、result_not_found，均关联 req_id。
完整自动流程见 `hunter_demo_integration`；故障流程见 `hunter_runtime_crash_integration`。

## 生产 Bundle 模式

开发 CTest 的 `hunter_bundle_contract` 会生成公开测试向量签名的测试制品；只用于验证。
其中 `game.luxb` 是正式游戏模块，`entities.luxb` 包装实体测试入口，`fixture.luxb` 使用独立旧内容
夹具；后两者不用于游戏运行或发行。
使用它们验证生产 Runtime：

```powershell
$devBuild = (Resolve-Path build/win-dev).Path
cmake --preset win-bundle -G "Visual Studio 18 2026" -A x64 `
    "-DHUNTER_HOST_PROTOC=$devBuild/_deps/protobuf-build/Release/protoc.exe" `
    "-DHUNTER_HOST_LUAX=$devBuild/_deps/luax-build/Release/luax.exe" `
    "-DHUNTER_TEST_BUNDLE=$devBuild/bundle-test/game.luxb" `
    "-DHUNTER_TEST_ENTITY_BUNDLE=$devBuild/bundle-test/entities.luxb" `
    "-DHUNTER_TEST_POLICY=$devBuild/bundle-test/policy.json"
cmake --build --preset win-bundle --parallel 8
ctest --preset win-bundle
./build/win-bundle/Release/hunter_server_desktop.exe `
    --bundle "$devBuild/bundle-test/game.luxb" --policy "$devBuild/bundle-test/policy.json"
```

生产模式编译链接 `Luax::Runtime`，拒绝源码加载；不能用运行参数切换到开发 Runtime。
未显式指定测试 Bundle 时，默认从游戏测试 Bundle 同目录读取 `entities.luxb` 和 `fixture.luxb`。
自定义测试目录时同时保留 `host-v3.luxb`／`host-v3.json`，用于验证旧 Host 契约拒绝。
CTest 检查真实链接参数不含 Compiler、AST、DevelopmentRuntime，并验证错误公钥、身份和篡改拒绝。
生产运行时仅接受可信交付的公钥 policy 和签名 Bundle。正式离线签名步骤见
[工具说明](tools/README.md)；私钥不进入可执行文件、运行资源或仓库。

`cmake --install build/win-bundle --config Release --prefix build/stage` 只安装桌面可执行文件。
正式发行需要另外交付可信制品、公钥策略及系统运行库；本轮不宣称干净机器发行包已验收。

可用 `tools/package_demo.py` 生成带 SHA-256 清单的可移动联调包：

```powershell
python tools/package_demo.py --build build/win-dev --output build/delivery/hunter-v1-source.zip
python tools/package_demo.py --build build/win-bundle `
    --bundle build/win-dev/bundle-test/game.luxb --policy build/win-dev/bundle-test/policy.json `
    --test-signature --output build/delivery/hunter-v1-bundle.zip
```

包内 START.txt 给出启动命令；内容、身份元数据、协议、C# 和客户端一起交付。
源包附 `cfg/Manifest.json`、19 张原字段 Lua 表和聚合文件，供联调核对；运行仍加载聚合的
`game.lua`。Bundle 包的配置与玩法共同位于签名制品内，不包含散配置、编译器或私钥。
打包前反向核对规范内容与身份头、实际 Lua 加载结果、生成源码与映射；Bundle 另验签并
核对确切源码摘要和当前 Host 契约，任何错配都不替换已有包。可用 `--luax` 和
`--bundle-tool` 显式指定固定离线工具，它们仅供构建校验，不进入联调包。
测试签名包只用于联调；当前进度见 [V1_LUA_CFG_TASKS.md](V1_LUA_CFG_TASKS.md)，
[V1_GAMEPLAY_TASKS.md](V1_GAMEPLAY_TASKS.md) 和
[V1_TASKS.md](V1_TASKS.md) 保留此前版本的实际完成记录。

## 独立持久化模块（SRV-007）

使用方链接 `hunter_storage` 并包含 `storage/Storage.h`，命名空间为 `hunter::storage`。
比赛阶段使用 SQLite V2，Runtime 已接入启动读档、持久局号、猎人资产及原子幂等结算。
本次拆分 `Storage` 异步门面和同步业务 `Backend`，保留队列、Asio 完成投递及关闭屏障。
`SqliteBackend` 封装 `Db/Schema/Store/HunterStore`，持有连接、SQL、迁移和恢复策略；
公共请求校验与确定性编码不依赖 SQLite，模块仍不依赖 Luax、World 或 Protobuf。
构建保留 `hunter_storage` 目标，源集按公共部分和 SQLite 实现分组；尚无 MySQL 实现。
仅使用自定义后端时可链接不依赖 SQLite 的 `hunter_storage_common`，并调用四参数构造
`Storage(io, limits, instance, factory)`；默认三参数构造和 SQLite 装配由 `hunter_storage` 提供。

在逻辑线程构造 `Storage(io, StorageCfg{}, instance)`，instance 必须为非零关联 ID。
调用 `open(OpenCfg{"sqlite", UTF-8 文件路径}, done)`；`OpenCfg.backend` 默认 `sqlite`，
`StorageCfg` 只描述容量限制。旧 `open(path, done)` 保留为 SQLite 兼容入口。
父目录由宿主准备，SQLite 首次成功打开才初始化玩家 1。
收到 `Opened` 后才能提交业务请求。打开失败需关闭该对象，使用新对象重试。
同一个 SQLite 存档文件由一个 Storage 实例拥有；全库未完成出战回退仅属于此后端的打开策略，
不属于公共 `open` 契约。`BackendFactory` 在存档线程创建后端，打开、业务调用、
关闭及销毁均在该线程执行；公共层不持有数据库连接或构造 SQL。

| 方法 | 成功完成值 | 含义 |
| --- | --- | --- |
| `open(cfg, done)` / `open(path, done)` | `Opened` | 所选后端完成打开；后者固定选择 SQLite |
| `load_player(player_id, done)` | `PlayerSave` | 身份、revision、最后提交的 match_id 和可用仓库物品 |
| `alloc_match(done)` | `MatchId` | 已事务提交的稳定 ID，允许空洞、不复用 |
| `commit_match(req, done)` | `MatchResult` | 已 Committed，`replayed` 表示返回先前的同一结果 |
| `find_match(match_id, done)` | `MatchResult` | 已提交的原始结果，缺失返回 `NotFound` |
| `load_hunters(player_id, done)` | `HunterResult` | 账号、猎人、技能、装备和可用仓库的完整档案 |
| `apply_hunter(req, done)` | `HunterResult` | 猎人操作及其幂等记录已原子提交 |
| `find_hunter_op(player_id, op_id, intent_json, done)` | `HunterResult` | 按原始意图查询已经提交的操作结果 |
| `stop(done)` | `Closed` | 已接受任务及其回调排空，数据库连接已关闭 |

每个入口返回 `std::expected<Accepted, Error>`；拒绝不会产生回调或磁盘副作用。
`Accepted` 仅表示受理，最终 `Rsp{key, result}` 通过 Asio 延后交付。`key` 保留 instance/op，
成功的 `MatchResult` 才代表持久化成功；`result_json` 含 v1、完整结算字段、前后 revision、
提交毫秒时间及带永久 UID 的奖励。读取或重试原样返回这个 JSON，不根据当前配置重新发奖。

`CommitMatch` 提供 `match_id/player_id/expected_revision/outcome/content_key/items`。
items 为 `{cfg_id, count}` 正数增量，可为空；每行产生独立物品，顺序是请求内容的一部分。
存储层生成固定格式的 request_json；同 ID 同内容先于 revision 校验返回旧结果，不同内容冲突。
请求、奖励、玩家 revision 和结果在一个事务内提交。正有符号 64 位整数的上限也是 ID/revision
上限，JSON 直接编码整数，不经过浮点数；永久 item_uid 不使用 World 实体 ID。

`Error.code` 区分参数、容量、未就绪、未找到、内容冲突、revision 冲突、溢出、版本、损坏、
锁超时及写失败；`Unsupported` 表示后端未知或尚未实现，不自动回退或创建 SQLite 文件。
`native_code` 与 `backend` 替代原 `sqlite_code`，分别保存后端原始码及来源；通用校验错误
可无后端来源。业务只依据 `Code` 和 `commit_unknown` 分支，不依赖驱动错误码。
`commit_unknown=true` 表示不能确认 COMMIT 结果，调用方通过查询或相同请求重试确认，
不能展示保存成功。

默认限制为 64 个未交付操作、请求 1 MiB、完成 4 MiB、单结果 1 MiB。读取和结算按最大结果
预留完成槽，因此默认最多同时接受四个大结果操作；超限显式失败，不截断存档。
计费覆盖固定任务和拥有的请求／返回数据，不包含调用方回调捕获的外部对象或 SQLite 缓存。

io_context 由创建 Storage 的同一线程运行，必须活过所有完成；回调不得抛异常。
stop 使用独立控制槽，普通队列饱和也能关闭；已接受事务不取消，调用方持续驱动 io 至 Closed。
析构仅作为等待磁盘工作退出的保底路径，不执行“保存全部”；旧完成拥有独立关联和数据，
回调捕获的对象仍须由调用方保证有效。正常业务不依赖析构或退出回调保存结算。

SQLite 使用 `user_version=2`、WAL、FULL、外键和 2000 ms 锁等待。空库初始化为 V2，
合法 V1 在事务内升级 V2；已有库必须匹配对应版本完整结构并通过 quick_check／外键检查，
不自动重置异常或高版本库。V2 在玩家、物品和结算表之外增加账号成长、猎人、技能、装备、
出战和永久操作表；保留正数、JSON、revision、外键及活动出战唯一性约束。

```powershell
cmake --build --preset win-dev --target hunter_storage_contract `
    hunter_storage_backend_contract hunter_storage_crash hunter_hunter_store_contract --parallel 8
ctest --preset win-dev -R '^hunter_(storage_(contract|backend_contract|crash_integration)|hunter_store_contract)$'
```

`hunter_storage_contract` 覆盖事务、重启、容量和线程；`hunter_storage_crash_integration`
由父进程在精确事务检查点强杀并重新读取，同时验证真实 SQLITE_FULL 回滚。
`hunter_storage_backend_contract` 使用无数据库测试后端检查工厂、调用和销毁的线程归属，
以及完成延后与关闭排空；测试结果以 [验证记录](VERIFICATION.md) 为准。
`hunter_storage_cfg_integration` 随桌面集成验证显式及默认 SQLite 选择、未知后端拒绝和文件保留。
注入点只编译到测试专用 `hunter_storage_fault`，正式库没有故障开关。
Runtime 已接入启动读档、持久对局 ID、冻结奖励与网络查询。SQLite V2 仍限定一局一名玩家。
`hunter_runtime_fault` 只供测试，在相同桌面 Runtime／TCP 链路注入事务检查点，不能发行。

赛后由独立服务端访问 MySQL 新库，不导入比赛 SQLite 存档。本次只建立后端替换边界，
MySQL 驱动、连接配置及实现尚未提供；账号体系、客户端远程连接与部署另行安排。
后续 MySQL 必须通过共有业务契约，并独立实现 DDL、版本管理、并发 revision 和局号分配、
幂等竞争、即时外键下的结算写入顺序、JSON 原文及操作 ID 的精确比较、断连与提交未知处理。
共享数据库的出战恢复需校验实例归属和失效状态，不能照搬 SQLite 启动时回退全部活动局。
具体边界见 [存储契约](intents/modules/storage.intent.md) 和 [规划](plan.md)；
[db.md](db.md) 保留为早期 SQLite V1 设计稿，不代表当前实现状态。

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
Android 和 Unity 证据仍待补齐；Windows 服务端通过不代表完整 Demo 发布验收通过。

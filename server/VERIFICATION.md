# 服务端验证记录

## 单人撤离持久化首版（2026-09-25）

本轮实现 SRV-011 / V1-T01—T13；详见 [实施记录](V1_TASKS.md)。Windows x64 Release，
VS 2026 / MSVC 14.50、配套 CMake 4.3.1；沿用固定 Luax、Protobuf、Asio 和 SQLite。
基线提交 `083ac56`。改动前重新构建并运行开发全套 17/17（含 Bundle 制品契约）；
不把旧验证记录当成本轮证据。改动前未单独重跑生产 Runtime，以下记录新版本实际结果。

| 验证 | 实际结果与证据 |
| --- | --- |
| 最终开发／生产构建 | 两种模式成功，无 C++ 编译警告；`build/delivery-dev-build.log`、`delivery-prod-build.log` |
| 开发完整 CTest | **24/24，164.13 秒**；`build/final-dev-tests.log` |
| Bundle 完整 CTest | **23/23，167.87 秒**；`build/final-bundle-tests.log` |
| 最终契约描述同步、重新签名 | schema/bundle **2/2，5.23 秒**；`build/delivery-bundle.log` |
| 最终开发增量回归 | script/pve/process/demo **4/4，147.23 秒**；`build/delivery-dev-tests.log` |
| 最终 Bundle 增量回归 | 上述场景及真实生产链接检查 **5/5，147.06 秒**；`build/delivery-prod-tests.log` |
| 解压后的两种联调包 | 各一局真实战斗、拾取、撤离、保存、重启查询通过；`build/package-smoke.log` |

完整 CTest 通过后，同步内部状态字段的契约描述，将耐久场景加强为**同一 Runtime 连续
十局完整撤离，再独立启动十次查询持久结果**，并让诊断阻塞夹具显式使用临时存档。
重新生成签名制品后运行上表的最终增量回归；没有用旧制品替代最终检查。
随后仅补充交付文档与 C++ 控制结构空行，逐行确认非空代码行未改变，`git diff --check` 通过。

真实流程使用正式 Excel 内容和生成的协议客户端，只发送移动、跳跃、射击、换弹、拾取等
合法意图，不提供传送、改血或客户端奖励入口。验证永久 UID 唯一、revision 增长、重复结果
逐字节一致、重启读取一致。另覆盖同一会话十次主动放弃、空奖励、旧世界拒绝、未完成局
不结算、300+ 物品分页、旧 revision、损坏／未知版本原文件保留。

`hunter_runtime_crash_integration` 在同一个桌面 Runtime/TCP 链路进入真实撤离后，分别于
事务前、事务内、提交后回调前强杀；重启核对奖励整体不存在或完整存在以及 MatchId 不复用。
另外注入一次写失败／提交未知，暂停期间查询并按冻结请求重试；提交幂等不重复发奖。
事务内 Stop 和断连后 Stop 必须等待已接受事务及回调排空。故障钩子仅链接测试宿主，
正式包不包含测试宿主。孤立 Storage 的 SQLITE_FULL、容量、线程和强杀契约仍保留。

玩法风险由原生 raid 契约和真实 Luax pve 契约覆盖：替身身份、拾取距离、满包无部分变化、
重复拾取、掉落一次、Boss 前摇躲避、仅解锁出口、暂停冻结、伤害／离区取消和末 Tick 死亡
优先。原战斗、对象生命周期、脚本限制、背压与耐久契约继续运行。

版本：网络 **v4**、内容 **v3**、Host／内部状态 **v5**、SQLite **V1**。
内容身份：`demo-v3:79b1b12379ed4969c2bad2bdcec1577d0a53eee3e1451f3b4b031b59c9a5fde4`。
首版工作簿来源与结构差异见 [内容说明](../design/demo/README.md)。

联调包位于 `build/delivery/`，每包有逐文件 SHA-256 manifest；打包后检查 ZIP CRC、
逐文件内容及独立目录真实运行。SHA-256：

- `hunter-v1-source.zip`：`0b99b632544f84151779d5fdc3ac8258024364526abc3174023f790d55ac1668`
- `hunter-v1-bundle.zip`：`48d713e8db269641387bd637730a1faf63c30f34d92c461ece43ece02db288e0`

Bundle 使用公开测试向量签名，仅用于本地验证；正式发行须使用发行密钥。
Unity、Android NDK／ARM64、Service/JNI/Binder、真机与离线整包本轮未验证，SRV-008
继续延期。本轮不声明完整 Demo 发布完成，也不提供多人或局内崩溃续玩。

---

日期：2026-09-23。结果来自本轮实现与审查修复后的本机工作区验证。
本记录区分实现、实际运行结果和后续平台验收；active intent 不等于完整路线阶段完成。

## 原生对象单继承（2026-09-23）

在当前怪物五态与边界实现之上，将 Unit 改为公开继承 Entity，Player/Monster 公开继承 Unit。
实体继承链只保留 Entity 的 Access*，Weapon 仍组合持有；Actor 的 variant 值存储、ID、
槽位代次、固定阶段移除、状态 v4 schema 和七类 Luax 显式契约保持。
创建、候选导入、快照和句柄解析已适配基类视图，导入仍绑定目标 World::access 后才发布。
base.md、plan.md 与 SRV-010 同步记录继承结构和借用边界。

新增原生回归覆盖创建及导入后的基类引用、同一状态和门禁、具体类及 Weapon/Item setter、
只读拒绝、运动字段正确提交、末参数非法时完全不写入，以及网络快照可见性。
真实 Luax 夹具验证导入后各类 setter、Player 句柄不能传入 Unit 方法，并逐类检查
Entity/Unit/Player/Monster/Weapon 旧句柄在导入和重开后失效。

| 命令／检查 | 实际结果 |
| --- | --- |
| 迁移前两种 preset 的 object/entity/game 契约 | 各 **3/3 通过**，1.59／0.80 秒，基于已有二进制 |
| `cmake --build --preset win-dev --parallel 8` | 开发构建成功；最终构建无 C++ 警告 |
| `ctest --preset win-dev --output-on-failure` | **17/17 通过，66.28 秒** |
| 最终开发构建及 `ctest --preset win-dev -R '^hunter_(object\|entity\|bundle)_contract$'` | 补齐五类失效断言后 **3/3 通过，8.23 秒**，重新生成签名测试制品 |
| `cmake --build --preset win-bundle --parallel 8` | 生产模式构建成功，无 C++ 警告 |
| `ctest --preset win-bundle --output-on-failure` | **16/16 通过，49.64 秒**，含最终夹具、耐久、进程和生产链接验证 |
| 四个类的头文件与 `lua/contract.json` 对照 | **45 个契约方法签名保持**，完整头文件与方案示例一致 |
| 修改范围的 100 列、Tab、行尾空白及 `git diff --check` | 通过；原生源码无旧 unit/entity 数据成员访问 |

开发全套通过后，仅追加五类旧句柄失效断言和实现签名换行；随后重建开发版并定向验证
object/entity/bundle，生产完整套件消费这批最终签名制品。构建和性能／耐久测试顺序运行。
初次开发编译出现继承字段同名参数警告，已用实现局部参数 new_x/new_y/new_facing 消除，
公开头文件签名、参数顺序、范围检查和先校验后写入的顺序保持不变。

迁移前后均使用同一 Windows x64 Release 工具链、正式配置、默认预算和 600 Tick，
每次运行默认及 64 实体两种场景，采样时没有同时执行本次构建或测试：

| 模式／实体数 | 迁移前 p50／p95（µs） | 迁移后 p50／p95（µs） | 前后帧数／编码字节 |
| --- | ---: | ---: | ---: |
| 开发／3 | 281.9／343.9 | 284.7／335.6 | 200／26,916 |
| 开发／64 | 5,917.2／6,896.9 | 5,883.5／6,393.6 | 200／490,558 |
| Bundle／3 | 281.1／336.0 | 282.7／328.4 | 200／26,916 |
| Bundle／64 | 5,895.7／6,802.2 | 5,885.1／6,416.1 | 200／490,558 |

四次前后场景均完成并通过状态验证；帧数和编码字节一致。耗时为本机参考采样，不据此宣称
继承本身带来性能提升或 Android 性能保证。

日志在忽略目录 `build/`：`inheritance-baseline-{dev,bundle}-tests.log`、
`inheritance-dev-build.log`、`inheritance-dev-final-build.log`、`inheritance-dev-tests.log`、
`inheritance-dev-final-tests.log`、`inheritance-bundle-build.log`、`inheritance-bundle-tests.log`，
以及 `inheritance-perf-{before,after}-{dev,bundle}.jsonl`。
本次未修改生产 Lua 调用方式或类契约，保留工作区原有怪物改动；未运行 Android 或 Unity 验证。

## 怪物五态与原生边界（2026-09-23）

补齐 spawn/patrol/chase/attack/dead 生命周期，保持 C++ 权威状态与安全校验、Lua 玩法决策。
原生创建和导入共用出生 ID、配置、整数坐标、巡逻范围、障碍和连续支撑校验；
冷却按配置限制，死亡清零并禁止恢复活动状态。Lua 完成出生初始化、巡逻端点转向与截步、
追击步长截断、范围判定及冷却攻击，原有玩家射击先于怪物攻击的顺序保持不变。

新增原生与真实 Luax 用例覆盖非连续出生 ID、非法配置与导入失败原子性、五态往返、
出生端点、窄区间和区外回归、检测与攻击的精确边界、追击防越位与同 X 竖直分离、
冷却递减，以及 Playing 局内死怪持续静止、禁止复活和重新写入速度。

| 命令／检查 | 实际结果 |
| --- | --- |
| `cmake --build --preset win-dev --parallel 8` | 开发构建成功 |
| `ctest --preset win-dev --output-on-failure` | **17/17 通过，60.62 秒** |
| `cmake --build --preset win-bundle --parallel 8` | 生产模式构建成功 |
| `ctest --preset win-bundle --output-on-failure` | 首轮 **14/16 通过，67.48 秒**；耐久与进程集成失败 |
| `ctest --preset win-bundle --rerun-failed --output-on-failure` | 原代码与预算复测 **2/2 通过，44.71 秒** |

开发套件重新生成签名测试 Bundle，生产套件消费同一批制品。
生产首轮 64 实体耐久在 Tick 731 触发 50 ms 脚本墙钟预算，进程集成在开局响应阶段出现
receive_failed；检查时主机仍有大量并行编译进程。未调整代码或超时预算，单独重跑两项均通过；
其余 14 项保留首轮通过证据，不将本次结果表述为一次完整生产套件全绿。
日志在忽略目录 `build/monster-dev-build.log`、`build/monster-dev-tests.log`、
`build/monster-bundle-build.log`、`build/monster-bundle-tests.log` 与 `build/monster-bundle-retest.log`。
本次验证使用现有 Windows 构建目录；Android 与 Unity 验证边界保持不变。

## 独立 SQLite 持久化底座（2026-09-22）

SRV-007 已实现独立的 hunter_storage：SQLite 3.53.4、Schema V1 四表、永久玩家／物品、
持久对局 ID、原子结算与去重、异步容量和正常关闭。本次不改 Runtime、World、Lua 或协议；
当前游戏不会自动存档，玩法结算、启动读档及 Android 真机验证仍未接入。

依赖使用官方 amalgamation，归档 SHA-256 为
`1e71ddf93849c6a6ecf58b827c0692073d2dd7ee40196158068f7b29f422e87d`；
下载时另核对官方 SHA3-256，CMake 还校验 sqlite3.c/sqlite3.h 内容摘要。
SQLite 直接作为 C 静态库构建，存储目标不链接 Luax、World 或 Protobuf。

本轮实际覆盖：

- 空库原子初始化、重复打开、未知版本、陌生结构、非空无版本库、损坏字节保留、
  外键失效拒绝、文件打开失败；检查 WAL/FULL/foreign_keys/busy_timeout/user_version 生效。
- 同请求原样重放、奖励变化／revision 变化／数组顺序变化冲突、revision 拒绝、未分配 ID、
  空奖励、重复 cfg 的独立 UID、读档与查询一致；连续 10 次提交和重新打开不重复发奖。
- 大于 2^53 的 match_id 和 item_uid 保持整数精度，i64 序列／revision 耗尽拒绝；
  负奖励、非法 UTF-8、无符号范围溢出和不存在玩家均有明确错误。
- 数量、请求总字节、完成总字节及单结果上限；大存档读取和大结果重放不截断或改变原数据；
  真实连接／门面错误线程拒绝、完成在所属线程延后交付、关闭饱和队列、旧对象销毁后的独立完成。
- Windows 父进程在 before_txn、in_txn、after_commit 确切检查点强杀子进程；
  重启验证玩家、物品、结果同时提交或同时不存在，原请求重试只产生一次奖励。
- 使用 SQLite max_page_count 注入真实 SQLITE_FULL，验证扩展错误码、全事务回滚及随后重试；
  另一个真实连接持有写锁，验证 2000 ms 等待后 Busy，未发生部分修改。

测试注入点仅存在于 hunter_storage_fault 测试副本，正式 hunter_storage 无故障开关。
这些证据覆盖 Windows 进程强杀与 SQLite 写满，不代表硬件掉电、真实磁盘介质 I/O 故障、
Android 系统强杀或完整撤离闭环已通过。无法确认 COMMIT 的错误会返回 commit_unknown，
该不确定提交分支本轮未用实际磁盘 I/O 故障触发。

| 命令／检查 | 实际结果 |
| --- | --- |
| `cmake --preset win-dev`、`cmake --preset win-bundle` | 两种配置成功，启用 C 并校验固定 SQLite 源码 |
| `cmake --build --preset win-dev --parallel 8` | 完整开发构建成功 |
| `ctest --preset win-dev --output-on-failure` | **17/17 通过，71.06 秒** |
| `cmake --build --preset win-bundle --parallel 8` | 完整生产模式构建成功 |
| `ctest --preset win-bundle --output-on-failure` | **16/16 通过，50.25 秒**，含生产链接检查 |
| 两种 preset 重建 `hunter_storage_contract hunter_storage_crash` | 最终源代码定向构建成功 |
| 两种 preset 执行 `ctest -R 'hunter_storage\|hunter_intent_contract'` | 各 **3/3 通过，3.39 秒** |
| 实际存储契约链接图 | 两种配置均无 Luax、game/script、协议或故障副本依赖 |
| 自有源码 UTF-8、100 列、Tab、行尾空白及 `git diff --check` | 通过；中文说明与短类型已复核 |

完整回归后，最终审查修正了 schema 检查中 LIKE 的下划线通配符问题，并确保交付完成前释放
请求缓冲容量；补充额外用户表拒绝用例。随后对最终代码重建两种模式，并定向重跑上述 3 项。
开发全套完成后生产套件消费同一批签名制品；最终存储定向测试不依赖 Luax 或 Bundle。

日志在忽略目录 `build/storage/`：`dev-build.log`、`dev-tests.log`、`bundle-configure.log`、
`bundle-build.log`、`bundle-tests.log`、两种模式的 `*-final-build.log`／`*-final-tests.log`，
以及 `source-scan.json`。CTest 原始输出也在各构建目录 `Testing/Temporary/LastTest.log`。
此前各节保留对应实现增量的历史证据，不替代本节当前存储状态。

## 原生对象与小写 Lua 配对（2026-09-22）

本节是 SRV-010 的当前验证结果；后续各节保留为迁移前的历史记录，不代表当前状态归属。
C++ World 统一保存 Entity、Unit、Player、Monster、Item、Weapon 数据和生命周期；Lua
保留玩法规则及代际句柄。Host、上下文及内部状态为 v4，网络仍为 v3，内容仍为 v2。

| 命令／检查 | 结果 |
| --- | --- |
| `cmake --build --preset win-dev --parallel 8` | 成功，生成原生 schema、七类绑定及开发工具 |
| `ctest --preset win-dev --output-on-failure` | **15/15 通过，59.31 秒** |
| `cmake --build --preset win-bundle --parallel 8` | 成功，保持 compiler-free Runtime |
| `ctest --preset win-bundle --output-on-failure` | **14/14 通过，44.76 秒**；生产链接检查通过 |
| `hunter_endurance_contract` | 两种 Runtime 各运行默认和 64 实体场景 1,800 Tick，通过 |
| `test_assemble.py` | 20 项通过；包含文件、模块和依赖引用的小写约束 |
| 最终原生输入积压暂停回归 | 开发／生产进程套件均补跑通过，31.05／30.15 秒 |
| 关闭后重开原生会话的补验 | 两种配置重建成功；相关开发 7/7、生产 5/5 通过 |

开发套件生成签名制品后由生产套件消费。开发测试期间另有生产构建并行，测试总时间仅为
本次运行记录；下方最终性能采样在构建及 CTest 全部结束后单独执行。

新增和保留的实际覆盖：

- 真实 ClassBuilder 注册七类，显式访问组合组件；Lua 修改直接反映到原生序列化和快照。
  错误类型、只读身份、删除、导入替换、重开后的陈旧句柄均被拒绝；线程及关闭边界受控。
  同一 Script 关闭后重新打开会得到新的空 World，重新登录／开局／关闭在两种模式均通过。
- Item 默认集合为空，最多 64 个实例；创建、查找、数量修改、销毁、完整状态往返及
  非法候选拒绝通过。数量为 1～2147483647，配置 ID 只校验规范十进制及正 int32 范围。
  实体及 Item 的容量／uint64 ID 耗尽不消费新 ID；局内删除后不复用身份。
- C++ 独立候选验证完整 v4 状态后才替换，旧 v3 状态拒绝；原有混合怪物、枪械、运动碰撞、
  输入锁存、伤害、死亡、暂停恢复、终态及重开回归通过。
- FrameInput 原生锁存并生成 Ack，快照直接从 World 生成，net.event 使用六个标量参数；
  script_frames 直接校验和编码 Protobuf。正式高频路径不构造／解析 JSON。
  JSON 适配器仍供低频控制与边界夹具使用，output_json 仅用于诊断和测试。
- 调用失败丢弃暂存输出并中止会话；新增真实签名夹具验证先暂存合法事件，再由 pcall
  捕获非法原生事件时，仍不能提交先前事件。快照合并、可靠消息顺序与真实慢读背压保留。
- 编译时冻结四项 Hunter 契约摘要；合法签名且旧 Host 元数据与旧 policy 互相匹配的制品，
  仍在进入脚本前以 host_contract_mismatch 拒绝。该夹具复用当前源码，仅改契约元数据，
  验证宿主的身份检查独立于 Lua 自身的版本断言。

### 性能实测

Windows x64 Release，同一固定 Luax 提交。默认场景为 1 玩家＋2 怪物；64 实体场景复制
第一种出生配置为 63 个不同 spawn_id，玩家不发送输入，Item 为空。每组 600 Tick，
统计 Script.tick 加输出校验／Protobuf 编码的合计耗时，不含初始化、登录、开局和状态导出。
字节数包括四字节帧头，不含 TCP/IP 开销；两种模式均使用实际协议编码，未使用网络模拟计数。
基线在迁移前采样，使用更宽松的指令／原生工作和截止时间预算；最终样本使用默认 Cfg，
因此这是本机同场景的参考比较，不是严格隔离变量的性能结论。

| 场景／模式 | p50（µs） | p95（µs） | 最大（µs） | 帧数 | 编码字节 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 默认，迁移前开发版 | 138.9 | 277.5 | 736.6 | 200 | 26,916 |
| 默认，当前开发版 | 277.4 | 321.5 | 740.2 | 200 | 26,916 |
| 默认，当前生产 Bundle | 283.2 | 329.7 | 692.3 | 200 | 26,916 |
| 64 实体，迁移前开发版 | 4,206.9 | 6,838.2 | 7,722.3 | 200 | 490,558 |
| 64 实体，当前开发版 | 5,764.1 | 6,273.3 | 8,117.3 | 200 | 490,558 |
| 64 实体，当前生产 Bundle | 5,684.2 | 6,220.8 | 6,796.7 | 200 | 490,558 |

网络帧数与字节数保持一致。开发版 p50 在默认场景约增加 100%，64 实体约增加 37%；
原生绑定调用与回收检查点有成本，本轮不宣称性能提升。固定候选的复杂原生属性读取存在
运行问题，因此使用显式类方法；运动以一次多标量读取／提交减少调用次数，规则仍在 Lua。

另对两场景各跑 6,000 Tick，均完成且状态有效；64 实体 p50 为 9,968.4 µs、p95 为
10,870.6 µs、最大 28,336.0 µs，不能据此宣称所有 Tick 都在 16.67 ms 内。两场景 Lua
峰值内存均为 3,660,111 字节（约 3.49 MiB），完成 GC 周期分别为 84／302。原生状态
迁移后，固定候选自动 GC 缺少表写入检查点，原始实现会耗尽 16 MiB；当前每 Tick 添加
128 次有界临时表写入推进自动 GC，保留原内存预算，并通过 endurance 契约防止回归。
6,000 Tick 样本采于最终生产身份校验与注释整理前，玩法及 GC 路径与最终样本相同。

复现最终样本（在 server 目录）：

```powershell
build/win-dev/Release/hunter_perf.exe build/win-dev/generated/game.lua `
    build/win-dev/generated/content.json 600
build/win-bundle/Release/hunter_perf.exe build/win-dev/bundle-test/game.luxb `
    build/win-bundle/generated/content.json build/win-dev/bundle-test/policy.json 600
```

日志和原始数据位于忽略的 build 目录：final-dev-build.log、final-dev-tests.log、
final-bundle-build.log、final-bundle-tests.log、perf-before.jsonl、perf-final-dev.jsonl、
perf-final-bundle.jsonl、perf-endurance.jsonl。长期运行可将 600 改为 6000。
补验日志为 final-dev-process.log、final-bundle-process.log 和 reopen-{dev,bundle}-{build,tests}.log。
性能测量不提供 Android 性能、发热或硬实时保证。Android 探针仅同步 v4 控制上下文，未编译或运行真机。
本轮未增加背包、掉落、拾取、存档、物品网络消息或 Android 宿主，未修改固定 Luax 源码。

## 基础对象与 ID 化验证（2026-09-22）

本节记录 SRV-005／009 的最新基础实现增量，以及 SRV-002／004 的 v3 协议和桥接同步。
沿用固定 Luax 提交、依赖和 Windows 工具链；默认地图、出生位置、运动与战斗数值已逐项
对照修改前的共享源配置，全部保持一致。规划前的玩法、组装、schema、导表基线为 4/4 通过。

| 命令／检查 | 结果 |
| --- | --- |
| `cmake --build --preset win-dev --parallel 8` | 成功，重新生成脚本、内容、schema、C++／C# 协议和实体测试夹具 |
| `ctest --preset win-dev --output-on-failure` | 13/13 通过；包含正式游戏及隔离实体夹具的签名制品生成 |
| `cmake --preset win-bundle ...` | 使用同轮生成的 protoc、game.luxb、entities.luxb 和 policy，配置成功 |
| `cmake --build --preset win-bundle --parallel 8` | 成功，继续链接 compiler-free Runtime |
| `ctest --preset win-bundle --output-on-failure` | 12/12 通过；包含真实进程及生产链接检查 |

本轮新增与保留的验收证据：

- Entity/Unit/Player/Monster 构造纯数据组件；玩家输入、备用弹药与枪械状态不再出现在怪物中。
  Weapon 与 AI 共用 Damage，网络快照通过独立投影生成。默认移动、射线、换弹、暂停、
  同 Tick 先射击后近战、死亡／清怪和重开的原有断言继续通过。
- 真实 Luax 混编两种怪物，检查各自生命、速度、感知、体型、伤害与近战间隔；射线越过矮怪
  命中远处高怪。另验证不同枪械的射程、弹匣、射速、换弹与伤害，以及独立玩家配置。
- `hunter_entity_contract` 在源码和签名 Runtime 中直接调用正式模块：标记后不能参与
  查找、伤害、移动、近战和快照；删除压紧索引但不改变幸存实体身份或出生关联；同出生点
  再次生成取得新 ID。玩家位于遍历数组中部仍可正确查找，旧局引用不能命中新局实体。
- 独立测试夹具验证 64 实体容量、删除后不复用 ID、完整 uint64 最大值、容量／ID 耗尽失败
  不修改状态及分配器。批量创建拆为多个入口，保留默认 50 ms 和原有资源预算；字段校验
  改为逐项存在性加数量检查，避免组件增加后重复嵌套遍历。63 实体与最大分配高水位可完整往返。
- 移动、换弹与 AI 冷却期间导出／导入后状态和后续输出保持一致；旧状态版本、额外字段、
  错误玩家引用、重复／遗漏索引、配置／出生引用、非法生死关系和落后的分配高水位均被拒绝。
- `Entity.cfg_id=16` 经 Luax、严格 JSON、Protobuf 和 C++ 协议客户端完整传递；缺失、零值、
  越界、非规范十进制及错误类型被拒绝。真实进程拒绝 v1／v2 握手；输入黄金帧保持不变。
- 导表工具 13 项测试覆盖独立 ID 空间、配置引用和按实际体型检查的出生／巡逻边界；schema
  工具 6 项测试覆盖 v3 及 cfg_id 描述。源码与生产模式均保留真实启停、战斗、故障及背压回归。

日志位于两套构建目录的 `foundation-build.log`、`foundation-tests.log`；生产配置另有
`foundation-configure.log`。开发测试先生成新 Bundle，生产测试顺序消费；`entities.luxb`
只用于隔离测试，不加入正式游戏清单或发行资源。测试签名仍使用公开测试向量。

本轮未运行远程 CI、Unity、Android NDK／真机或发行包验收。Android 探针仅同步 v3 桥接
参数；物品、装备、技能、撤离、SQLite、热更新控制和 Android 宿主仍不属于本轮实现。

## 基础战斗切片最终验证（2026-09-22）

本节为基础对象增量前的 SRV-009 历史结果；下方框架记录同样保留为历史证据。使用已有锁定依赖重新生成协议、
内容、schema 和脚本，再完整构建；未修改第三方源码或更新依赖版本。

| 命令／检查 | 结果 |
| --- | --- |
| `cmake --preset win-dev -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax` | 配置成功 |
| `cmake --build --preset win-dev --parallel 8` | 成功；包含服务端、协议客户端、C++／C# 协议、共享内容和玩法契约 |
| `ctest --preset win-dev --output-on-failure` | **12/12 通过，38.44 秒** |
| `cmake --preset win-bundle ...` | 指定上述开发构建的 protoc、签名测试 Bundle 与 policy；配置成功 |
| `cmake --build --preset win-bundle --parallel 8` | 成功；使用 compiler-free Runtime |
| `ctest --preset win-bundle --output-on-failure` | **11/11 通过，30.95 秒** |

两套顺序运行，开发测试先生成新签名 Bundle，生产测试再使用同一制品。生产链接检查通过，
不含 Compiler／AST／DevelopmentRuntime；签名制品仍使用公开测试向量，不能用于正式发行。

新增和扩展的实际覆盖：

- 两种 Runtime 均执行同一 `hunter_game_contract`：登录和开局幂等、移动与扫掠碰撞、落地／顶板、
  空中跳跃拒绝、枪械最近目标／墙优先／射程端点／射速／换弹、同 Tick 点射和 uint64 序号精度。
- 普通怪巡逻／追击／近战、窄巡逻区高速端点限制、区外逐步返回、死亡和清怪只裁定一次，
  死怪不能在同 Tick 再攻击；重开重置实体和弹药、保持连接序号，旧局请求不重置新局。
- 状态导出／导入后继续重放得到相同结果；非法字段、浮点坐标、错误实体 ID、内容不一致、
  暂停持有动作与终态非零速度均被拒绝。导入导出仅验证状态契约，不代表已有热更新或存档。
- 两种模式各完成十次真实服务进程启停，并经正式 C++ 协议客户端跑完死亡、清怪与第三局重开。
  保留拆包粘包、错误凭据／版本、超时、额外客户端、慢读背压、宿主管道与退出故障回归。
- 握手前暂停不触发未连接套接字发送；暂停期间和积压中输入明确作废，恢复不回放移动／开火。
  CLI 拒绝错误内容版本，服务端退出且 CLI stdin 仍打开时也有界结束；TCP 重置返回明确错误。
- 共享配置工具内部 **12 项测试**覆盖字段／数值／ID／出生碰撞／巡逻支撑及发布失败回滚。
  schema、组装、签名和 intent 工具继续通过；内部用例不重复计入 CTest 项数。

日志位于各配置的 `build/win-dev/` 与 `build/win-bundle/`：
`combat-configure.log`、`combat-build.log`、`combat-tests.log` 及 `Testing/Temporary/LastTest.log`。
现有 Android 原生探针只同步了 v2 上下文和登录／开局调用，未安装 NDK、编译 Android 或运行真机。
Unity 仍为占位；没有客户端画面、APK、完整撤离／Boss／掉落／存档或干净 PC 发行验收声明。

## 已实现

- SRV-001～005 当前桌面范围：宿主控制、回环 TCP／Protobuf、Tick、Luax 桥接、模块与制品工具。
- **Standalone Asio 1.36.0**：独立头文件，显式 `ASIO_STANDALONE`；没有 Boost.Asio 依赖。
  已核对生成的 `hunter_server_core.vcxproj` 编译定义及实际 TCP／定时器运行测试。
- 同一脚本的开发源码路径和 compiler-free 生产签名 Bundle 路径。
- C++／C# 协议生成、intent 检查、Windows CI 配置、Android ARM64 原生探针入口。
- SRV-009 增加本机登录、开局、共享灰盒、权威移动／射击／普通怪以及死亡和清怪重开。
- SRV-005／009 增量完成 Luax 组件构造、实体／配置 ID 管理、独立枪械与伤害和 v3 快照配置引用。
- SRV-007 已实现独立 SQLite 存档与事务接口，实际证据见本文首节；玩法与宿主尚未接入。
- SRV-006、008 保持 deferred。完整撤离、热更新、AAR／Service／Binder 未实现。

## 本机工具链与依赖

| 项目 | 实测或锁定值 |
| --- | --- |
| 宿主 | Windows x64，系统版本 10.0.19045 |
| 编译器 | MSVC 19.51.36256.0，VS 18 2026，工具目录 14.51.36231 |
| Windows SDK | 10.0.26100.0 |
| CMake | 4.3.1-msvc1；Visual Studio 18 2026 generator |
| Python | 3.14.4 |
| 构建 | Release；开发／生产模式分开构建目录 |
| Luax | d8a8160420074f8e590d94910d1af5a5f5a93245，干净本地检出 |
| Asio | 1.36.0，231cb29bab30f82712fcd54faaea42424cc6e710 |
| Protobuf | 33.0，a79f2d2e9fadd75e94f3fe40a0399bf0a5d90551 |
| Abseil | 20250512.1，76bb24329e8bf5f39704eb10d21b9a80befa7c81 |
| nlohmann/json | 3.12.0，55f93686c01528224f448c19128836e7df245f72 |
| SQLite | 3.53.4，官方 amalgamation 归档和源码摘要锁定 |

公共归档摘要在 `cmake/Deps.cmake`。Luax 从指定干净源码检出构建，未修改上游。
Protobuf 的公开解析头需要 `utf8_validity` 包含路径，Hunter 目标显式链接此已有依赖；
没有修改第三方源码。上游构建出现 MSVC C4819／C4715 警告，本轮不宣称第三方零警告。

## 首次实现验证（历史结果）

命令在 `server/` 执行；首次从空构建目录配置并构建依赖，随后针对修正增量重建。
完整参数见 [README](README.md)。

| 命令／检查 | 结果 |
| --- | --- |
| `cmake --preset win-dev -G "Visual Studio 18 2026" -A x64 -DFETCHCONTENT_SOURCE_DIR_LUAX=G:/github/luax` | 配置成功；C++23 expected/jthread 编译检查成功 |
| `cmake --build --preset win-dev --parallel 8` | 成功，包括本机 protoc、luaxc、luax-bundle |
| `ctest --preset win-dev --output-on-failure` | **9/9 通过**，16.52 秒 |
| `cmake --preset win-bundle ...`，指定已构建 protoc、测试 Bundle 与公钥 policy | 配置成功；关闭本机工具构建，使用生产 Runtime |
| `cmake --build --preset win-bundle --parallel 8` | 成功 |
| `ctest --preset win-bundle --output-on-failure` | **8/8 通过**，11.17 秒 |
| 自有 C++ 行宽、短类型搜索及 `git diff --check` | 已检查；标准入口签名保留语言要求的类型 |

开发 CTest 覆盖 core、net、script、async、process、assemble、bundle、intent checker 和 intent。
生产 CTest 覆盖 core、net、script、process、production link、assemble、intent checker 和 intent。
Bundle 工具契约内部 7 项、组装器内部 7 项用例通过；不是额外计入 CTest 数量。

实际日志保存在忽略的构建目录：

- `build/win-dev/final-build.log`、`final-tests.log`、`Testing/Temporary/LastTest.log`。
- `build/win-bundle/final-build.log`、`final-tests.log`、`Testing/Temporary/LastTest.log`。
- `build/win-dev/bundle-test/` 保存测试 Bundle、公钥、策略与来源摘要；不保存测试 seed。

### 核心覆盖

- 两种模式各完成 **10 次真实桌面进程闭环**：Ready、TCP 握手、输入／快照、暂停恢复、Stop，
  并检查退出后端口关闭。进程测试并非仅调用内存模拟对象。
- 拆包、粘包、非法／超限帧、错误版本／内容／实例／令牌、握手超时、第二连接、重复输入、
  输入队列饱和、发送容量不足；发送队列单元测试覆盖未发送快照合并与整批拒绝。
- 完整回归后另补真实慢读场景：客户端先正常接收确认，再停止 TCP 读取，用小接收窗口和
  持续输入触发 `send_backpressure`，明确排除输入队列饱和，随后确认断连和正常 Stop。
  两种模式的更新进程套件分别重跑，日志为各构建目录的 `process-final.log`。
- 固定步长、补算上限、丢弃墙钟积压、暂停后重新计时、容量按条数和字节计费。
- 初始化失败、EOF、重复停止、阻塞 stdout 收尾；日志通过有界事件通道离开逻辑线程，写 stderr。
- 真实 Luax 双向调用、参数与 table 边界、错误线程、重入、过期句柄、状态导入导出、执行／原生工作／
  内存／JSON 预算，以及脚本捕获 Host 错误后仍禁止提交输出。
- 后台 detached 完成、定时器、预留容量、重复完成、取消与截止时间竞争、关闭后旧票据拒绝。
- 实际离线工具签名，校验公钥、代次、九项身份及版本；实际生产 Runtime 拒绝错误公钥、
  identity、错误策略类型和 Bundle 篡改。CMake 实际链接片段不含 Compiler／AST／DevelopmentRuntime。

## 规则审查修复后的复验（2026-09-22）

[审查报告](RULE_AUDIT.md) 的 A01～A11 及调度 REVIEW 均已修复。本节是修复后的最终结果，
上方首次实现记录不替代本次复验。行为修复与布局、命名、注释修正分别核对，未修改第三方依赖。

| 命令／检查 | 最终结果 |
| --- | --- |
| `cmake --build --preset win-dev --parallel 8` | 增量构建成功 |
| `cmake --build --preset win-bundle --parallel 8` | 增量构建成功 |
| `ctest --preset win-dev --output-on-failure` | **10/10 通过，28.77 秒** |
| `ctest --preset win-bundle --output-on-failure` | **9/9 通过，14.37 秒** |
| 新能力探针 `cmake/CheckCxx.cpp` | 两种配置的 `HUNTER_HAS_CXX23_TYPES=1`；使用项目短类型 |
| Standalone Asio | 两种生成工程均含 `ASIO_STANDALONE`；TCP 与 timer 真实测试通过 |
| 编码规则复核 | 24 个 C++、10 个 Lua 文件未发现超 100 列、Tab、行尾空白、控制结构空行或短类型直接包含违规；命名命中均为保留的外部 API |
| 中文说明与差异 | 自有代码文件、具名函数及生成模板已复核；全部 74 个文件可按 UTF-8 解码；`git diff --check` 通过，未跟踪文件另行检查 |

两套 CTest 顺序执行，先由开发套件生成测试 Bundle，再由生产 Runtime 验证同一制品。
各自仍包含 **10 次真实桌面进程闭环**及端口释放检查；生产链接检查通过，不含开发 Runtime／编译器。
新增 `hunter_schema_contract` 同时进入两种配置。内部工具用例分别为组装／发布 17 项、
schema 6 项、Bundle／identity 13 项、intent 检查器 6 项，全部通过，不重复计入 CTest 总数。

本次补充的关键覆盖：

- stderr 阻塞／正常读取两个场景均先触发超长输入错误，进程在测试时限内非零退出。
- 两种模式各拒绝 36 个非法数值参数、接受 8 个上下界；构造失败和停止失败状态不依赖输出消费者。
- 真实脚本退出 false、抛错、日志移交、重复关闭、资源释放后重新打开、错误线程关闭拒绝；
  真实宿主 Stop 和断连的退出失败均表现为 Faulted／非零返回。
- 同一共享解码器拒绝零 Ack、上下界外计数、uint64→i64 回绕、超范围 Tick、浮点版本和附加字段；
  真实 Runtime 与 Protobuf 批次均拒绝整批，合法极值保持精度。
- 昂贵合法输入积压时 Pause 在断言时限内响应，Resume 后 256 条输入完整、按序执行。
  每 Tick 条数和单次回调工作预算可配置，剩余输入保留 FIFO。
- 源码／映射、policy／provenance 的第二项发布失败恢复旧字节；暂存写失败不改变制品；
  回滚失败保留恢复清单、备份与锁。拒绝内部锁文件名冲突及 Windows 尾随点／空格别名。
  最终 Bundle 暂存／发布失败没有半文件，提交后清理失败只告警，已发布 Bundle 仍可验证。
- schema 头文件生成失败保留旧文件；intent 的开发／工具条件入口缺失时检查失败，
  生产模式不要求本来不存在的开发专属测试。

源码故障注入（阻塞 stderr、退出异常、昂贵入口、非法脚本输出）在开发 Runtime 执行；生产模式
验证共享原生边界、正常签名脚本及生产加载拒绝，不能把开发注入用例宣称为生产逐项重跑。
原生 Isolate／Runtime 关闭错误和事件循环非预期 C++ 异常的显式收尾分支已审阅并通过编译，
尚未通过真实故障注入触发。输出管道被取消后不保证全部诊断字节交付，但故障退出码保持独立。
4 ms 是不可抢占脚本入口之间的调度软预算；不承诺硬实时。多路径制品发布的崩溃、掉电及
并发读者原子性不在当前保证内，恢复要求见 [工具说明](tools/README.md)。

最终日志与扫描保存在忽略的 `build/audit-fix/`：`dev-build-final.log`、
`bundle-build-final.log`、`dev-tests-final.log`、`bundle-tests-final.log`、`scan.json`、
`comment-encoding.json` 和 `inventory.json`。完整测试输出仍在各配置的
`Testing/Temporary/LastTest.log`；工具定向故障注入材料在 `build/audit-fix/tools/`。
本次使用已配置目录增量构建，没有再次从空目录编译依赖，也未执行远程 CI 或 Android 测试。

## 尚未验证与阶段状态

- 未安装或选定 Android NDK、SDK，未编译 Android 探针，未连接 ARM64 真机。
  API 26、16 KB 链接选项只是构建入口，不能当作设备兼容证据。
- 没有 Unity 工程／客户端运行，没有 C# 集成、AAR／Binder／APK、前后台及强杀验收。
- Windows CI 配置已提交到工作区，但远程 CI 未执行；私有 Luax 访问需要相应读取凭据。
- 没有干净 PC 发行包、长时间性能／发热或 Unity 实机玩法测试；测试公钥不得作为发行公钥。

Windows 基础框架及 SRV-009 共用玩法切片已实现；P0 的 Android 证据、Android 宿主、
热更新、存档玩法接入及完整撤离阶段仍待完成；独立 SQLite 底座已实现，不能由 Windows
通过替代 Android 真机验收。

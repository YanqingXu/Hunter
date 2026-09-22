# 服务端验证记录

日期：2026-09-22。结果来自本轮实现与审查修复后的本机工作区验证。
本记录区分实现、实际运行结果和后续平台验收；active intent 不等于完整路线阶段完成。

## 基础战斗切片最终验证（2026-09-22）

本节为 SRV-009 的最新结果；下方框架记录保留为历史证据。使用已有锁定依赖重新生成协议、
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
- SRV-006～008 保持 deferred。完整撤离、SQLite、热更新、AAR／Service／Binder 未实现。

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
热更新、存档及完整撤离阶段仍待完成，不能由 Windows 通过替代。

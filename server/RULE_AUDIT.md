# Server 全量规则审查

审查日期：2026-09-22。对象为本次审查时的工作区，包含尚未提交的实现。

**当前结论：A01～A11 和补充调度 REVIEW 已修复，通过本次规则复核与 Windows 回归。**
下方保留修复前发现、旧行号和复现证据，供核对原因；其中“未通过”与旧测试数量均指原始审查。
修复后的实现与验证边界见本节及 [验证记录](VERIFICATION.md)，不将 Windows 结果外推为 Android 验收。

## 修复闭环（2026-09-22）

| 条目 | 修复内容 | 回归与复核入口 |
| --- | --- | --- |
| A01 | 输入线程不再写 stderr；输入错误先请求故障停止，有界输出线程独立取消 | `hunter_process_integration` 阻塞／持续读取 stderr 对照与超长控制行 |
| A02 | 从 `contract.json` 生成边界，Script 与 Protocol 共用 `decode_output`；拒绝零 Ack、越界及无符号回绕、多余字段 | `hunter_script_contract`、`hunter_net_contract`、`hunter_schema_contract` |
| A03 | `Script::shutdown` 返回退出结果，保留有界错误与日志；宿主显式收尾并以独立故障标志决定非零退出；事件循环异常也先收尾 | 脚本 false／抛错／日志／重复关闭／重新打开／错误线程；真实 Stop 与断连退出 |
| A04 | 数值参数完整解析，检查正值和公开上界，合法后才写入 Cfg | 两种模式各检查 36 个非法参数及 8 个合法边界 |
| A05 | 源码／映射与 policy／provenance 完整暂存、加锁发布、捕获失败后恢复；Bundle 排他发布完整文件；提交后清理失败明确告警 | 第二份输出失败、暂存失败、回滚失败留证、锁冲突及 Bundle 实际编译签名回归 |
| A06 | 修正控制结构分组空行，以及单行局部声明直接用于 if 的紧贴例外 | 全量自有 C++ 扫描后人工复核，包括新增代码 |
| A07 | 自有声明统一为 `rsp/msg/ctx/req` 词根；外部 API 名称保持原样 | 声明与引用搜索、两种模式真实编译 |
| A08 | lambda 花括号和函数体按现有规则展开 | C++ 布局复核 |
| A09 | 自有测试 Lua 模板补中文职责说明；C++23 能力探针改为实际文件并使用短类型 | 源映射回归、中文注释复核、两种构建的 `HUNTER_HAS_CXX23_TYPES=1` |
| A10 | 两处简单 check 调用保持单行，并复查修改后行宽 | 自有 C++／Lua 100 列检查；复杂签名和回调按语义分组 |
| A11 | 如实描述 intent 检查器；统一 Tick／seq 范围；加入 `dev:`／`tools:` 验证选择及缺失入口回归 | `hunter_intent_checker_contract`、两种模式的 `hunter_intent_contract` |
| 调度 REVIEW | 每 Tick 最多 64 条输入，一次回调共享 4 ms 工作预算，剩余 FIFO 保留 | 昂贵合法输入积压期间 Pause 响应、Resume 后 256 条输入按序完整执行 |

行为修复为 A01～A05 与调度限制；排版、命名、注释修正另行检查，未借格式调整改变协议或规则。
新增生成器遇到不支持的 schema 漂移直接失败，不静默采用默认边界。发布锁文件名为保留名称，
不能作为制品目标；Windows 文件名别名也不能绕过目标检查。

复验只证明这里列明的契约和场景，不宣称任意故障下所有诊断都能交付：阻塞管道仍会按期限取消。
原生 Isolate／Runtime 关闭失败和事件循环非预期 C++ 异常的处理分支已实现并审阅，未通过真实
故障注入触发。制品多路径发布不提供并发读者、进程崩溃或掉电原子性；回滚失败保留恢复材料。
调度预算不可抢占单次脚本入口，不是硬实时承诺。Android、Unity 和远程 CI 仍未验证。

修复后的最终结果：开发与生产两套 Windows 增量构建成功；
`ctest --preset win-dev --output-on-failure` 为 **10/10，28.77 秒**，
`ctest --preset win-bundle --output-on-failure` 为 **9/9，14.37 秒**。
各自包含 10 次真实进程闭环。最新扫描覆盖 74 个文件（24 个 C++、10 个 Lua、12 个 Python），
未发现本次审查列明的布局、命名或注释问题。最终日志和快照在 `build/audit-fix/`，
详见 [修复后的验证记录](VERIFICATION.md#规则审查修复后的复验2026-09-22)。

## 原始审查结论（修复前）

原结论未通过：确认 1 项 P1、4 项 P2 行为问题，以及排版、命名、测试模板注释和文档问题。
当时的 CTest 全部通过，但故障探针证明覆盖不足；原审查阶段没有修改实现或既有测试。

## 范围、依据与方法

- 清点 `rg --files server` 返回的 67 个文件：21 个 C++、10 个 Lua、9 个 Python、
  3 个 CMake、3 个 JSON、21 个 Markdown。共审查 43 个代码文件及其契约、构建与说明。
- 排除 `build/` 中第三方源码、生成代码、缓存与产物；8 个 Lua 占位文件只检查说明和清单隔离，
  不将其视为已实现模块。项目维护的生成模板仍按规则检查。
- 依据 `AGENTS.md`、`rules/README.md` 及全部五份规则、`plan.md`、SRV-001～008。
  根目录 Protobuf 和 Windows CI 只用于交叉核对接口与构建边界。
- 采用逐文件阅读、遮蔽字符串及注释后的辅助搜索、Unicode 行宽检查、现有构建／CTest，
  以及真实进程故障探针。辅助正则的命中经过人工复核，不能替代 C++ 语法分析或并发证明。
- P1 表示应优先修复的退出／可用性问题；P2 表示确定的边界或失败保证问题；P3 表示编码规范
  与文档问题。优先级与规则的 MUST／SHOULD／REVIEW 是两个不同维度。

## 行为与契约发现

### A01 · P1：stderr 阻塞可使错误输入无法触发停止

位置：`platform/desktop/Main.cpp:120`、`:129`、`:278`。

`read_commands()` 在命令入队失败或输入超长时，先同步写 `std::cerr`，再调用
`runtime.stop()`。输出线程也可能正在向同一 stderr 管道写日志。stderr 满时，输入线程停在
错误日志写入，逻辑线程收不到 Stop；主线程又先执行无时限的 `runtime.join()`，因此到不了
`cancel_and_join()` 的取消和超时处理。

复核：合法初始化脚本输出一条 60,000 字节诊断；父进程不读取 stderr，随后发送 66,000 字节
控制行。配置 `--stop-ms 100`，等待 2.001 秒仍未退出，只能由探针终止进程。同样输入、持续
读取 stderr 的对照组在约 0.002 秒退出。两个流程都使用当前真实桌面可执行文件。

依据：SRV-001 的有时限管道收尾；`coding_principles_rules.md` 的 CC-12 失败释放边界。
修复方向：输入错误先保证停止请求可达；诊断统一走可取消、有界的输出路径，避免错误日志阻塞
停止本身。新增同时阻塞 stderr 与制造输入错误的进程回归用例。

### A02 · P2：输出校验接受违反正式 schema 的 Ack

位置：`src/script/Script.cpp:82`、`src/net/Protocol.cpp:29`、`:107`；
权威定义：`lua/contract.json:26`、`:37`，SRV-005 不变量。

脚本输出只验证 seq 能解析为 u64、count 能解析为 i64，没有执行 Ack 的 seq > 0 和
count ∈ [-1,000,000,000, 1,000,000,000] 约束。快照 Tick 的有符号范围也未在传输转换层收紧。
`Script::valid_output` 与 `script_frames` 还分别维护不同程度的字段校验，前者允许多余字段，
后者才按字段数量拒绝，说明同一 schema 已出现不同解释。

复核：替换探针的 `on_event`，输出 `{"v":1,"seq":"0","count":1000000001}`；入口返回
true 后，真实 TCP 客户端收到了 **seq=0、count=1000000001** 的 Ack。完整提交链路没有拒绝。
正常框架脚本自身会限制状态，这不等于 Host 边界已经履行非法输出拒绝契约。

依据：SRV-002 整批校验、SRV-004 输出事务、SRV-005 非法输出终止；
`R-REFACTOR-07` 同一知识的权威来源；CC-11 显式跨界 schema。
修复方向：以版本化契约统一数值范围与字段校验，覆盖零序号、越界计数、越界 Tick 和多余字段，
确保失败前整批不进入发送队列。

### A03 · P2：shutdown 失败被当作正常停止

位置：`src/script/Script.cpp:599`、`:607`；`src/core/Runtime.cpp:562`。

脚本 `shutdown` 的返回值／执行错误、Isolate 关闭和 Runtime 关闭结果均被直接丢弃。
退出脚本产生的诊断也没有在 `script.reset()` 前移交宿主。脚本正常返回 false 同样没有经过
`commit()` 的返回值检查。

复核：七入口脚本初始化成功，`shutdown` 抛出 `audit_shutdown_failure`。Stop 仍返回
`state=Stopped`，进程退出码为 0，stderr 为空，没有任何错误证据。

依据：SRV-004「入口错误返回有界诊断」；`plan.md` 第 7 节脚本错误有日志和关联信息；
CC-05 不吞失败。修复方向：继续完成资源释放，同时保存并交付有界退出错误，明确终态与退出码
语义；对 false、抛错及关闭失败分别建立必要的回归验证。

### A04 · P2：数值参数接受负号和尾随垃圾

位置：`platform/desktop/Main.cpp:199` 至 `:213`。

`std::stoul/stoull` 未检查完整消费位置，允许前导负号，再直接赋值到容量／时限字段。
例如 `5junk` 被读作 5；`--queue-count -1` 被读作无符号最大值；Windows 上
`--stop-ms -1` 会成为 4,294,967,295 毫秒，而不是参数错误。队列的字节限制仍然存在，
但条数和时间约束不再是调用方输入的合法正值。

复核：同时传入 `--handshake-ms 5junk --queue-count -1`，实际进程正常发布 Ready。

依据：CC-11 接口数值边界、`R-REFACTOR-14` 外部输入运行时校验，以及 Cfg 的容量／时限语义。
修复方向：完整解析无符号十进制，检查目标类型范围、零值语义及允许的配置范围后再修改 Cfg。

### A05 · P2：组装失败后仍发布半套制品

位置：`tools/assemble.py:132` 至 `:138`。

工具先直接写最终源码，再写行号映射。第二次写入失败时，源码已创建或覆盖，未回滚。
`bundle.py:140` 的最终文件创建／写入也存在失败后留下文件的同类风险，当前测试仅覆盖编译
失败，不覆盖发布写入失败；该 Bundle 风险本轮只做代码审查，未注入磁盘故障。

复核：将 `--map` 指向已存在的目录。组装器退出码为 1，但新的最终源码仍存在，大小为
7,989 字节，映射未生成。

依据：SRV-005「构建失败返回非零，不发布部分输出」；CC-05 失败后的可观察状态。
修复方向：先在构建目录内完整准备、校验临时产物，再发布；两个输出需要约定并实现成套发布
与失败恢复，不能只把首次写入换成一次 rename 就宣称满足整套原子性。

## 排版、命名与注释发现

### A06 · P3／MUST：控制结构空行不合规，确认 18 处

依据：`formatting_rules.md` 的 `R-FORMATTING-03-04`。
其中 16 处缺少前置分组空行，2 处在明确要求紧贴的单行局部声明与 if 之间多留空行。

| 文件 | 行号 | 原因 |
| --- | --- | --- |
| `src/core/Runtime.cpp` | 34 | 前置随机调用是多行初始化，不适用单行例外 |
| 同上 | 41 | 前置为 `source.read()` 调用 |
| 同上 | 195 | 前置声明 cmd，条件并未使用 cmd |
| 同上 | 267 | 前置声明 out，条件调用 flush_logs，未使用 out |
| 同上 | 633 | 前置为锁声明，条件未使用 lock |
| `src/script/Script.cpp` | 37 | 前置为 stream.read 调用 |
| 同上 | 170 | 前置为成员赋值 |
| 同上 | 295 | 前置为已有 result 的赋值，不是局部声明 |
| 同上 | 336 | 前置为 allow_output 赋值 |
| 同上 | 480 | 前置为 busy 赋值 |
| `src/script/Async.cpp` | 99 | 前置为 deadline.cancel 调用 |
| 同上 | 187 | 前置为锁声明，条件未使用 lock |
| 同上 | 194 | 前置为 packet.emplace 调用 |
| `platform/desktop/Main.cpp` | 179 | 前置声明 name，条件未使用 name |
| 同上 | 268 | 前置 diagnostic 为多行初始化 |
| `platform/android/Probe.cpp` | 67 | 前置为 shutdown 调用 |
| `src/net/Protocol.cpp` | 163 | iter 是单行声明且直接用于 if，按例外应紧贴 |
| `src/core/Runtime.cpp` | 469 | due 是单行声明且直接用于 if，按例外应紧贴 |

不能把上述例外解释为「只要上一句定义了变量就可以不空行」。此次检查未发现省略控制体
花括号、分支压成一行或拆开 else 分支的问题。条件编译和字符串中的布局仍需保持语义。

### A07 · P3／MUST：7 个自有声明未使用统一词根

依据：`readability_rules.md` 的 `R-READABILITY-01`。以下均为项目自有标识符，
不是第三方 API 名称或已冻结协议键。

| 文件位置 | 当前名称 | 应采用的词根 |
| --- | --- | --- |
| `src/core/Runtime.cpp:180` | `response` | `rsp` |
| `src/script/Script.cpp:46` | 参数 `message` | `msg` |
| `src/script/Script.cpp:247` | 参数 `context` | `ctx` |
| `src/script/Script.cpp:302` | 参数 `message` | `msg` |
| `src/script/Async.cpp:303` | 局部 `request` | `req` |
| `tests/contract/ScriptContract.cpp:99` | 局部 `context` | 使用 ctx 词根并表达探针角色 |
| `tests/contract/AsyncContract.cpp:15` | 参数 `message` | `msg` |

`asio::io_context`、`luax::HostCallContext`、`RuntimeConfig`、第三方 `.message()`／
`.request` 保留外部名称；它们没有计入违规数。修正时同步所有引用，不改冻结的接口字符串。

### A08 · P3：5 处单行 lambda 偏离基础花括号风格

位置：`src/core/Runtime.cpp:113`、`:278`、`:279`，`src/script/Script.cpp:293`，
`tests/contract/AsyncContract.cpp:109`。

依据：`formatting_rules.md` 的 C++ 基本风格要求函数左花括号另起一行。
上述 lambda 将花括号和函数体压在声明行；规则只对匿名函数的职责注释按需处理，
未提供对应的花括号排版豁免。应按同一基础风格展开，不改变捕获和调用时机。

### A09 · P3／MUST：自有源码模板漏掉中文说明，能力探针还遗漏短类型

位置：`tests/contract/ScriptContract.cpp:47` 至 `:53`，
`tests/contract/AsyncContract.cpp:48` 至 `:49`；
`tests/scripts/test_assemble.py:30` 至 `:31`；`CMakeLists.txt:18` 至 `:20`。

`fixture()` 拼出的七个合法脚本入口没有函数前中文说明，生成文件也没有职责说明；
异步探针拼出的 `run/nested` 同样没有说明。组装测试创建的合法模块文件没有文件职责说明。
模板所在 C++／Python 函数有注释，不等于其输出的自有代码已具备所需注释。

CMake 内嵌的能力检查源码同样没有中文文件／函数说明，且 `std::expected<int, int>` 的
模板实参没有沿用项目短类型；这两个 int 不属于 main 的标准入口签名。函数体也被压在一行。
该源码由项目维护，不能因为最终写入构建目录，就与第三方生成代码一并豁免。

依据：`R-READABILITY-02/08/09`，尤其是「项目维护的生成模板遵守相同要求」。
能力探针还适用 `R-TYPES-04` 的自有生成模板条款与 C++ 基础布局约定；调整需保持真实类型
和工具链能力检查语义，并补齐直接头文件依赖。
应修改源模板及受影响的映射预期；故意制造语法错误的负例不做会改变输入含义的格式整理。
正式 `tools/assemble.py` 的生成文件和七个转发入口已有中文说明，此处没有遗漏。

### A10 · P3／SHOULD：至少两处简单调用可保持单行

依据：`R-FORMATTING-03-03`「能清楚地写在一行时保持单行」。

| 文件 | 起始行 | 合并后长度（含缩进，Unicode 码点） |
| --- | --- | --- |
| `tests/unit/NetTest.cpp` | 32 | 94 |
| `tests/unit/CoreTest.cpp` | 40 | 97 |

两处均为普通 `check(...)` 调用，合并不改求值、名称、类型或生命周期，当前未记录保留折行的
理由。这是 SHOULD 偏差，不是超过 100 列；复杂条件、语义分组和过长签名不能机械合并。

## 文档与验证覆盖

### A11 · P3：两处说明超过实现或与正式边界不一致

- `intents/README.md:22` 声称检查器还会检查「验证源码」。实际
  `tools/verify_intents.py` 只读取目标名单和 CTest 注册名称，不核对命令引用的源码是否存在、
  测试是否断言了对应契约。本轮逐项阅读并运行了真实入口，但不能把这一人工结果归给检查器。
- `intents/modules/tick.intent.md:27` 写「完整 uint64 Tick」，而 SRV-004／005、
  `lua/contract.json:28` 和 `Script.cpp:526` 都将 Tick 限为非负 i64 范围；输入 seq 才支持
  完整 uint64。应同步文字，不能让调用方按较宽的契约发送 Tick。

另外，SRV-004 元数据未列出正文提到的异步测试，SRV-005 元数据未列出 Bundle 测试。
两项测试实际存在且开发 CTest 已运行，因此不判为虚假 active；但自动检查无法从这两份
metadata 发现这些入口被移除。后续应表达构建模式差异后补齐，而不是把仅开发模式存在的名称
直接强塞进生产构建的无条件校验。

## 各规则维度结果

| 维度 | 本轮判断与边界 |
| --- | --- |
| C++23／CMake／目标 | 两种 Windows 构建通过；真实核心、桌面、脚本和测试目标存在 |
| Standalone Asio | `cmake/Deps.cmake:65` 显式定义 `ASIO_STANDALONE`；独立头文件，未引入 Boost.Asio |
| 依赖固定与本地覆盖 | 五项完整提交和公共归档 SHA-256 已记录；显式覆盖检查 Git HEAD 与干净状态 |
| 100 列、缩进、编码 | 21 个 C++ 和 10 个 Lua 文件未发现超 100 Unicode 码点、Tab 或行尾空白；67 文件可按 UTF-8 解码 |
| 控制体／函数布局 | 控制体花括号存在；分组空行见 A06，lambda 见 A08，单行表达建议见 A10 |
| 相邻函数块 | 手工 C++／Lua 的具名函数分组未发现缺失；C++ 排版不强制传播给 Python／CMake |
| 短类型 R-TYPES-01～05 | 21 个 C++ 文件使用 Types.h 并直接包含；原始类型命中仅为标准 main 签名等合法边界；CMake 内嵌探针的例外遗漏见 A09 |
| 命名 R-READABILITY-01 | 未通过，见 A07；没有对外部 API 名称做机械替换 |
| 中文注释 R-READABILITY-02/08/09 | 43 个实体代码文件有中文文件说明；具名 C++ 声明、Lua、Python、CMake 函数已核对；自有测试生成模板见 A09 |
| 函数组织／深度／封装 | Script.open 和测试驱动较长，按阶段串联具有理由；未把 60 行或三层嵌套当作强制拆分阈值 |
| 条件与副作用 | 未以折行、布尔项数量直接判错；容量预留等带副作用的短路顺序不能作为纯格式修改调整 |
| 模块与世界状态 | 头源模块内并置；世界状态由 Lua main 持有；C++ 仅有传输去重、Tick 调度和确认缓存，未发现第二份可变玩法模型 |
| 线程与所有权 | Runtime 逻辑线程独占 io_context／网络／VM；异步票据传 detached 数据；真实 wrong_thread／重入／过期测试通过 |
| Tick／暂停／容量 | 现有固定步长、最多 4 Tick、暂停恢复、双容量及慢读背压测试通过；CLI 限制解析见 A04 |
| 输出与失败保证 | 未通过：schema 见 A02，退出失败见 A03，工具发布见 A05 |
| 生命周期与管道 | 正常 Stop／EOF／阻塞 stdout 测试通过；stderr 与错误输入组合失败，见 A01 |
| Bundle 与生产依赖 | 源码／签名路径测试通过，公钥／篡改／identity 拒绝通过；实际生产链接检查通过 |
| intent 与阶段 | 8 份契约索引和实际入口校验通过；006～008 deferred、Android 宿主 draft；文档／元数据限制见 A11 |
| 重构纪律 | 本轮没有修改实现，行为保持型重构条款不伪报为已执行；R-REFACTOR-07 的实际 schema 漂移见 A02 |

补充 REVIEW：`Runtime::step()` 会在一个 Tick 内排空全部积压输入，每个脚本入口单独获得预算。
默认队列虽有界，但本轮没有测量昂贵合法入口叠加时的事件循环响应延迟；不能只凭
`max_catchup=4` 证明调度总耗时受控。此项作为后续性能／调度验证，不混入已复现缺陷数量。

## 实际验证与证据

命令在 `server/` 执行，除审查探针命令以仓库根目录为工作目录。

| 命令／检查 | 本轮结果 |
| --- | --- |
| `cmake --build --preset win-dev --parallel 8` | 增量构建成功 |
| `cmake --build --preset win-bundle --parallel 8` | 增量构建成功 |
| `ctest --preset win-dev --output-on-failure` | 9/9 通过，17.98 秒 |
| `ctest --preset win-bundle --output-on-failure` | 8/8 通过，12.73 秒 |
| 两种模式的 process integration | 各包含 10 次完整进程闭环及真实慢 TCP 读者 |
| `py -3 server/build/audit/probes.py` | 真实复现 A01～A05；探针成功运行不表示被审查行为通过 |
| UTF-8／Unicode 行宽／短类型／中文注释辅助检查 | 已运行并人工排除字面量、预处理及外部名称误报 |
| `git diff --check` | 通过；未跟踪代码另外检查，不能仅凭 git diff 覆盖全部新文件 |

本轮证据位于忽略的 `build/audit/`：`inventory.json` 保存审查前文件 SHA-256；
`scan.py/scan.json` 保存辅助扫描及候选；`probes.py/probes.json` 保存复现入口与结果；
`dev-build.log/bundle-build.log` 保存构建输出。完整 CTest 日志在各构建目录
`Testing/Temporary/LastTest.log`。源码发生变化后，应按新行号和摘要重新确认结论。

本次没有重新执行干净目录全量构建、远程 CI、Android 编译／真机、Unity 联调、长时间性能
和内存诊断；此前文档中的相应历史记录没有当作本轮执行结果。Android 仍未验证，P0 整体
仍未完成。上述为修复前记录；A01～A11 与调度 REVIEW 的当前处理结果见文首修复闭环。

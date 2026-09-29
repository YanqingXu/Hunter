# 对象池稳定性测试记录

验证日期：2026-09-21。环境：Windows x64、Unity 2021.3.8f1、Unity Test Framework 1.1.31。

本轮针对当前源码新增压力测试并重新运行完整测试集。61 项核心测试、14 项编辑器 Play Mode 测试、同一组 14 项 Windows Mono Player 测试全部通过。本轮没有发现需要修改对象池运行时实现的问题。

## 测试结果

| 测试 | 实际执行量 | 结果 |
| --- | --- | --- |
| Edit Mode 完整回归 | 原有 52 项 + 新增 9 项 | 61 / 61 通过 |
| 热缓存借还 | 预跑 1 万次后，再测量 100 万次 | 同一实例复用，旧凭证不能访问或归还新借用，重复归还不改变状态 |
| 固定种子混合操作 | 6 个种子 × 3 万次，共 18 万次 | 独立存活账本、池计数、作用域账目、全局配额一致，最终完全排空 |
| 容量与队列饱和 | 100 轮，1,000 次成功借用，4,800 个异步请求 | 1,600 次队列满、1,600 次取消、1,600 次超时均符合预期；每轮关闭后配额归零 |
| 极小调度预算 | 64 个池，各预热 2 个实例，每帧仅 1 个维护步骤 | 所有池都能完成预热和关闭，无饥饿；预算没有超限 |
| Unity 生命周期回归 | 原有 12 项 + 新增 2 项 | 编辑器 14 / 14；Windows Mono Player 14 / 14 |
| 真实 GameObject 反复切换 | 每次运行 200 轮作用域切换、1,600 次生成、1,600 次取消、40 次外部 Destroy | 初始化总数为 48；每轮缓存子节点数与空闲数一致，最终实例、配额、资源引用均归零 |
| 分区场景反复卸载 | 每次运行 30 轮场景创建/卸载，120 次生成 | 始终复用同一组 4 个实例，无重复初始化；关闭后实例销毁、引用归零 |

两个新增 Unity 测试均在编辑器和实际构建出的 Windows Mono 程序中各执行一次；上表中的循环次数按单次运行列出。运行采用 `-batchmode -nographics`。

## 随机压力的检查方法

种子为 `1、7、42、101、2026、65537`。每个种子使用 4 个池和 6 个持续替换的作用域，交错执行同步借用、缓存借用、异步借用、归还、取消、短期限超时、关闭作用域、预热、预热计划更新、外部失效和内存压力回收。发生错误时会输出种子和操作编号，便于复现。

测试适配器另行维护真正创建但尚未确认销毁的对象集合，交叉核对池快照；还检查作用域持有数、请求数、全局驻留数量及内存估值。每隔 32 次操作检查状态，每次抽查已归还凭证的有效性，并定期重试释放故障。

18 万次混合操作共创建 84,864 个测试对象，实际注入异常如下，所有种子的最终关闭均成功：

| 故障阶段 | 注入次数 |
| --- | --- |
| 创建 | 2,818 |
| 一次性初始化 | 2,282 |
| 归还重置 | 1,215 |
| 发出销毁请求 | 2,004 |
| 确认销毁完成 | 1,828 |

适配器将销毁确认推迟至少两个调度帧，并单独检测重复发出已经成功的销毁请求。最终检查所有独立存活集合为空、全局驻留和内存估值为零、待处理请求结束、资源释放恰好一次。这里的内存数值是配额估值，不是进程实际内存测量。

随机负载不保证每次都打满容量，因此另设饱和测试：4 个池的单池上限为 4，全局上限为 10 个实例和 1,000 字节估值，每池队列上限为 8。每轮固定填满全局容量并向每池提交 12 个请求，逐个核对返回状态，再关闭作用域检查所有配额恢复及元数据槽位不持续增长。

## 分配量与性能边界

最终 Edit Mode 运行中，100 万次同步热缓存借还耗时约 **3.829 秒**。计时前预跑 1 万次；循环内没有 NUnit 断言、诊断快照或新建测试对象，仍包含实例身份、重置结果、旧凭证和重复归还检查。

**分配量更正：** 原记录使用 `GC.GetAllocatedBytesForCurrentThread()` 得到“0 字节”，但后续已知分配对照发现该接口在本机 Unity Mono 中仍返回零，因此撤回以此作为零分配证据的结论。已修正旧测试，在校准失败时输出 `unavailable`。后续通过 Unity `GC.Alloc` 计数器重新验证了热复用路径，并测出异步流程存在分配；详见 [Performance.md](Performance.md)。原始 XML 保留历史输出，正确性测试结果不受影响。

这是本机 Mono 编辑器环境下、固定容量、单个普通 C# 对象的结果。不能推导为整个服务、异步 Task、业务回调、Prefab 初始化或诊断接口均零分配，也不能作为目标设备帧耗时保证。核心功能压力测试将工作时间预算放宽至 100 ms，以验证调度数量与状态收敛；没有据此评估默认 2 ms 预算下的游戏帧率。

此次属于有限次数的行为与压力验证，没有做数小时驻留测试、真实大世界资源内存采样、GPU 首次使用测试或目标移动设备性能测试。IL2CPP 仍受先前记录的本机 Unity/C++ 工具链构建问题阻塞，本轮没有重试；详见 [Validation.md](Validation.md)。

## 源码与原始证据

- 新增核心压力测试：[PoolStabilityTests.cs](../../../Tests/Pooling/EditMode/PoolStabilityTests.cs)。
- 新增 Unity 循环测试位于：[PrefabPoolTests.cs](../../../Tests/Pooling/PlayMode/PrefabPoolTests.cs)。
- 最终结果：[EditMode.xml](../../../TestResults/Pooling/Stability/EditMode.xml)、[PlayMode.xml](../../../TestResults/Pooling/Stability/PlayMode.xml)、[Windows-Mono.xml](../../../TestResults/Pooling/Stability/Windows-Mono.xml)。
- 日志：[EditMode.log](../../../TestResults/Pooling/Stability/EditMode.log)、[PlayMode.log](../../../TestResults/Pooling/Stability/PlayMode.log)、[Windows-Mono-build.log](../../../TestResults/Pooling/Stability/Windows-Mono-build.log)、[Windows-Mono-run.log](../../../TestResults/Pooling/Stability/Windows-Mono-run.log)。
- 源码对应关系：[SourceHashes.csv](../../../TestResults/Pooling/Stability/SourceHashes.csv)，19 个运行时、编辑器、示例及测试源码文件均与验证工程中的副本 SHA-256 一致。

首轮随机测试在最后的覆盖检查失败：请求期限较长，没有真正触发超时；此前的计数与排空检查均已通过。已将随机请求期限调整为 5–25 ms 的测试时钟时间，使超时路径确实执行，再运行完整回归。首轮结果保留在 [EditMode-first-attempt.xml](../../../TestResults/Pooling/Stability/EditMode-first-attempt.xml)，没有将未覆盖的路径计为通过。

本轮继续使用 `Temp/PoolingValidation` 独立工程。测试源码仍放在源工程 `Assets` 外，源工程 `Assets` 中 `.asmdef` 数量仍为 0。临时验证工程的测试程序集仅用于 NUnit 和 Unity Test Runner，不影响源工程默认程序集结构。

## 在当前电脑复跑

1. 将最新 `Assets/_Framework/Pooling` 下的 `.cs` 文件同步到 `Temp/PoolingValidation/Assets/Pooling`，将 `Tests/Pooling` 下的测试同步到验证工程的 `Assets/Pooling/Tests`。保留验证工程自己的测试程序集与结果回调脚本。
2. 使用 Unity 2021.3.8f1 打开该独立工程，在 Test Runner 中运行全部 Edit Mode 和 Play Mode 测试，或以 `-runTests -testPlatform EditMode/PlayMode` 分别批量执行；测试命令不添加 `-quit`。
3. Windows Player 使用 `-runTests -testPlatform StandaloneWindows64 -testSettingsFile <验证工程>/Mono.json` 构建。验证工程的构建辅助脚本只构建并退出；确认日志出现 `Build Finished, Result: Success.` 后，运行 `Player/PoolingTests.exe -batchmode -nographics`，读取新生成的 `Player/PlayerResults.xml`。

同一验证工程一次只运行一个 Unity 进程。若清除了 `Temp`，需先重新建立含 Unity Test Framework 1.1.31 的独立测试工程；原始测试源码和结果保存在 `Tests` 与 `TestResults` 中。

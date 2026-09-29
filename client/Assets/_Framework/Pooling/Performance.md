# 对象池性能测试记录

测试日期：2026-09-21。当前结论：同步热缓存复用开销较低，但大批异步请求与大作用域关闭存在明显的完成延迟；本轮没有修改运行时实现或调整默认预算。

## 环境与结果范围

- CPU：Intel Core i5-10400F，6 核 12 线程；内存约 16 GB。
- Unity 2021.3.8f1，Windows x64 Mono，**Development 测试构建**，`-batchmode -nographics`，无 Deep Profiling。
- 9 个基准场景先在编辑器验证，再在独立 Player 进程中连续执行 3 次；每次均完成 9 / 9 项正确性与清理检查。没有按某个固定耗时阈值判定“性能通过”。
- 沿用默认调度预算：每 Tick 最多 2 次创建、4 次销毁请求、64 个调度步骤，时间目标 2 ms。池容量及请求队列容量按测量负载单独设置；1024 请求测试不是使用默认 64 的队列上限。
- 用户原有 Unity 编辑器保持打开。三次纳入统计的 Player 运行没有与本轮启动的测试编辑器同时执行；这些结果不是独占机器的实验室数据。

下表合并三次 Player 运行的原始样本。普通对象与 Prefab 的“一次”均指一对借用/归还操作。异步行指完整入队、Tick 调度、读取完成结果及归还流程的平均单请求成本。

| 负载 | 合并样本数 | 中位耗时 | P95 耗时 | 托管分配事件 |
| --- | ---: | ---: | ---: | ---: |
| 普通 C# 对象热缓存借还 | 90 批，每批 50,000 对 | 1.129 μs / 对 | 1.238 μs / 对 | 0 次 / 对 |
| 轻量 GameObject 热缓存生成/归还 | 90 批，每批 2,000 对 | 7.795 μs / 对 | 9.647 μs / 对 | 0 次 / 对 |
| 1 个空闲池，64 个驻留对象 | 12,000 个 Tick | 10.1 μs / Tick | 14.8 μs / Tick | 0 次 / Tick |
| 64 个空闲池，共 4,096 个驻留对象 | 12,000 个 Tick | 11.3 μs / Tick | 16.5 μs / Tick | 0 次 / Tick |
| 256 个空闲池，共 16,384 个驻留对象 | 12,000 个 Tick | 11.8 μs / Tick | 20.3 μs / Tick | 0 次 / Tick |
| 每批 64 个异步缓存请求 | 90 批 | 7.675 μs / 请求 | 17.681 μs / 请求 | 8 次 / 请求 |
| 每批 1024 个异步缓存请求 | 90 批 | 69.224 μs / 请求 | 76.925 μs / 请求 | 8 次 / 请求 |
| 关闭持有 1,000 个普通对象的作用域 | 45 次关闭 | 1.327 ms / 次关闭 | 1.712 ms / 次关闭 | 0 次 / 关闭区间 |
| 关闭持有 10,000 个普通对象的作用域 | 45 次关闭 | 33.020 ms / 次关闭 | 35.744 ms / 次关闭 | 0 次 / 关闭区间 |

批量借还及异步请求行的中位数/P95，是各批次“总耗时除以操作数”后的分布，不是逐次调用的尾延迟。Tick 行测量单次 Tick；关闭行测量一次完整关闭期间连续调用 Tick 的累计墙钟耗时，**没有计入真实帧间等待**。分位数使用最近秩方法。

GameObject 测试模板只有 Transform 和两个轻量生命周期探针，无模型、动画、粒子或物理组件。计时包含类型化参数准备、OnEnable、重置、停用、层级调整，以及活动场景和常驻缓存场景之间的迁移。创建、首次组件扫描及预热在计时区间之外。

空闲池数量增加后每 Tick 成本变化较小，是因为每次只执行有界的 64 个调度步骤；不能据此解释为每个 Tick 都完整扫描了所有池和所有实例。

## 默认预算下的完成延迟

| 负载 | 完成所需 Tick 中位数 | 假设每帧 1 次 Tick、60 FPS 的时间换算 |
| --- | ---: | ---: |
| 64 个异步缓存请求 | 6 | 约 0.10 秒 |
| 1024 个异步缓存请求 | 96 | 约 1.60 秒 |
| 关闭持有 1,000 个对象的作用域 | 63 | 约 1.05 秒 |
| 关闭持有 10,000 个对象的作用域 | 626 | 约 10.43 秒 |

时间换算用于说明默认 `PoolDriver.Update` 每帧调用一次 Tick 时的调度影响，不是实际运行了 60 FPS 游戏场景得到的延迟。作用域关闭立即让凭证逻辑失效，但关闭任务要等所有责任结算才能完成；场景卸载如果等待该任务，也会受到这些调度次数影响。

关闭测试把对象归还到足够大的缓存，没有销毁这些实例，没有清理复杂业务资源，创建新作用域和建立借用均不计入关闭区间。整个服务随后另行关闭，验证所有驻留计数归零。因此实际大世界分区退出成本可能更高。

三次运行中，1024 请求场景记录到的最慢单次 Tick 为 **17.403 ms**，超过 2 ms 时间目标。2 ms 是调度步骤之间检查的软目标，单个步骤、GC 或系统调度可以使实测值超出。尚未通过时间线分析确认这次峰值具体由哪一项造成，不能宣称每帧始终低于 2 ms。

## 分配计量的更正

此前稳定性测试使用 `GC.GetAllocatedBytesForCurrentThread()` 得到“0 字节”。本轮加入对照后发现：在该 Unity 编辑器及 Windows Mono Player 中，实际创建并保留 **100 个 1 KB 数组**，该接口仍返回增量 **0**。因此旧记录不能作为零分配证据。

本轮改用 Unity `ProfilerRecorder` 的 `GC.Alloc` 事件计数，限定当前线程，按帧汇总并在动作结束后停止记录；已知分配对照准确记录到 **100 次**。在本机该标记的 `UnitType` 为 `TimeNanoseconds`，其 `Value` 也不能解释为分配字节数；本轮只报告 `Count` 对应的**分配次数**，字节数在原始数据中以 `-1` 表示不可用。相关 API 的采样选项见 [Unity 官方文档](https://docs.unity3d.com/ja/2021.3/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.html)，汇总后停止并读取次数的方法参照 [官方 GC.Alloc 示例](https://docs.unity3d.com/ja/2020.3/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)。

校准后的记录器在普通对象和轻量 Prefab 热复用、空闲 Tick、已建立借用的关闭区间内均未观察到托管分配事件。异步流程平均每个请求有 **8 次托管分配事件**，不能把整个对象池称为“零 GC”。这 8 次涵盖被测完整异步流程，没有进一步逐个归因到某个构造函数。

性能测试先执行一次完整预跑；计时与分配计数分别重复执行同样的动作，计时时记录器处于停止状态，以减少记录分配事件对耗时的干扰。所有结果输出、CSV 构造、NUnit 断言以及诊断快照均在计时区间之外；热循环保留失败时抛异常的轻量检查。

同时修正了旧百万次稳定性测试：先用已知分配校准该接口，无法计量时输出 `unavailable`。该项已单独复跑通过；原先的凭证、实例和关闭正确性结果不受影响。

## 后续优化应关注什么

1. **异步请求队列。** 每请求成本从 64 请求批次的约 7.7 μs，上升到 1024 请求批次的约 69.2 μs。结合源码，`ManagedPool.RequestStep` 会逐次遍历待处理列表以选出请求，完成后还会移除列表元素；这是值得进一步分析和优化的路径。不能仅靠增大队列上限提升吞吐。
2. **作用域批量清理的调度次数。** 默认每 Tick 64 个步骤对大作用域偏保守；应在真实场景帧预算内评估清理批量与优先级。`PoolScope.StepClose` 每次从 HashSet 重新找首个借用，也需要关注大集合逐渐清空时的扫描成本。这里是源码与测量共同提示的优化方向，尚未做单独归因实验。
3. **目标业务 Prefab 的回调。** 本轮模板很轻，真实 AI、Animator、粒子和物理清理应单独接入 Profiler。同步缓存生成也会在调用帧执行这些工作，不能因为已有缓存就假设成本为零。

尚未测试冷创建/销毁对照、发布构建、IL2CPP、移动设备、渲染帧率或真实大世界资源内存。本轮是可复现的 CPU 与托管分配事件基准，不是上线性能验收。

## 源码、结果与复跑

- 基准源码：[PoolPerformanceTests.cs](../../../Tests/Pooling/PlayMode/PoolPerformanceTests.cs)。所有新增方法都有中文说明。
- 汇总：[Summary.csv](../../../TestResults/Pooling/Performance/Summary.csv)、[Summary.json](../../../TestResults/Pooling/Performance/Summary.json)、[Runs.json](../../../TestResults/Pooling/Performance/Runs.json)。
- 三次 Player 结果：[第 1 次](../../../TestResults/Pooling/Performance/Windows-Mono-1.xml)、[第 2 次](../../../TestResults/Pooling/Performance/Windows-Mono-2.xml)、[第 3 次](../../../TestResults/Pooling/Performance/Windows-Mono-3.xml)。
- 编辑器与构建：[Editor.xml](../../../TestResults/Pooling/Performance/Editor.xml)、[Windows-Mono-build.log](../../../TestResults/Pooling/Performance/Windows-Mono-build.log)。
- 旧计量接口的保护验证：[LegacyCounterGuard.xml](../../../TestResults/Pooling/Performance/LegacyCounterGuard.xml)。
- 源码核对：[SourceHashes.csv](../../../TestResults/Pooling/Performance/SourceHashes.csv)，当前 20 个源码文件与验证副本一致。
- 每个原始采样点存放在 `TestResults/Pooling/Performance/PlayerSamples-1`、`PlayerSamples-2`、`PlayerSamples-3`；每行含耗时、分配事件数、Tick 数与最大 Tick 耗时。

未校准的首次编辑器结果保留为 `Editor-uncalibrated.xml`，不用于分配结论。一次与验证编辑器启动重叠的 Player 运行保留为 `Windows-Mono-overlap-excluded.xml` 和对应采样目录，未纳入三次最终统计；为该次重新执行了独立运行。

沿用 [Stability.md](Stability.md) 的独立验证工程，将最新测试同步到对应目录后，增加过滤参数 `-testFilter BigWorld.Pooling.Tests.PoolPerformanceTests` 即可只执行性能基准。构建后运行 Player 三次，每次先保存 `PlayerResults.xml` 和 `PerformanceSamples` 再启动下一次，避免覆盖。临时验证工程保留测试程序集；源工程 `Assets` 仍没有 `.asmdef`。

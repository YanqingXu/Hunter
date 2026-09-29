# 对象池验证记录

## 性能基准与计量更正

已补充 9 个性能基准场景，并在 Windows Mono Development Player 中独立执行 3 次。热缓存借还、多池 Tick、异步批量请求及大作用域关闭的耗时与分配次数见 [Performance.md](Performance.md)。旧稳定性测试中的“0 字节”来自未通过已知分配对照的计数接口，已更正并加校准保护；不能据此认定整个服务零分配。

## 当前版本的稳定性复测

2026-09-21 已针对当前源码新增并运行稳定性测试：**61 / 61 核心测试、14 / 14 编辑器 Play Mode 测试、14 / 14 Windows Mono Player 测试通过**。覆盖百万次热缓存借还、18 万次混合操作与故障注入、100 轮容量/队列饱和、64 池极小预算调度、200 轮作用域切换和 30 轮场景卸载。完整方法、分配量测量范围、原始结果及复跑说明见 [Stability.md](Stability.md)。

本轮只新增测试和验证记录，没有修改对象池运行时实现；源工程仍使用默认程序集。

## 移除程序集定义后的结构

对象池的 6 个 `.asmdef` 及对应 `.meta` 已移除。运行时代码使用 Unity 默认的 `Assembly-CSharp`，编辑器工具保留在 `Editor` 文件夹并使用 `Assembly-CSharp-Editor`。原测试源码移至项目根目录 `Tests/Pooling`，不参与源工程的日常编译。

移除后已在 Unity 2021.3.8f1 的独立工程 `Temp/PoolingDefaultAssemblyCheck` 中完成导入与编译，并通过实际类型检查确认 `PoolService` 位于 `Assembly-CSharp`、`PoolMonitorWindow` 位于 `Assembly-CSharp-Editor`。该验证工程未使用 `.asmdef`，也未安装测试框架，编译通过。结果见 [DefaultAssemblies.txt](../../../TestResults/Pooling/DefaultAssemblies.txt)，日志见 [DefaultAssemblies.log](../../../TestResults/Pooling/DefaultAssemblies.log)。

下面的 52 项核心测试、12 项生命周期测试及 Player 测试结果记录的是此前实现的行为验证，不表示当前工程仍保留原来的测试程序集配置。

## 原实现的行为验证

验证日期：2026-09-21。源工程：Unity 2021.3.8f1，Windows x64。

使用 `Temp/PoolingValidation` 下的独立测试工程运行，避免占用当前已经打开的 Unity 工程。测试工程复制了交付的对象池源码及测试程序集，使用 Unity Test Framework 1.1.31；没有修改源工程的场景、包配置、构建后端或 Unity 安装文件。

| 检查 | 结果 |
| --- | --- |
| Unity 实际导入与 C# 编译 | 通过 |
| Edit Mode 核心状态测试 | 52 / 52 通过 |
| Editor Play Mode 生命周期测试 | 12 / 12 通过 |
| Windows x64 Mono Player 构建 | 成功 |
| Windows x64 Mono Player 中执行生命周期测试 | 12 / 12 通过 |
| Windows x64 IL2CPP Player 构建 | 未通过，见下方构建限制 |

最终 XML 与构建日志保存在项目根目录 `TestResults/Pooling`：

- [EditMode.xml](../../../TestResults/Pooling/EditMode.xml)
- [PlayMode.xml](../../../TestResults/Pooling/PlayMode.xml)
- [Windows-Mono.xml](../../../TestResults/Pooling/Windows-Mono.xml)
- [Windows-Mono-build.log](../../../TestResults/Pooling/Windows-Mono-build.log)
- [Windows-Mono-run.log](../../../TestResults/Pooling/Windows-Mono-run.log)
- [IL2CPP-build.log](../../../TestResults/Pooling/IL2CPP-build.log)

## 已覆盖的主要行为

核心测试覆盖：重复归还、旧凭证及空槽位复用、池重建隔离、作用域与祖先立即失效、共享借用隔离、正常关闭和释放故障重试、后台取消、共享加载的单独取消、队列上限、超时、创建及初始化异常、销毁确认前的驻留计数、不同实例的目标预热、计划去重及目标更新、全局预算、压力淘汰、容量缩小、重入防护、配置校验和统计分类。

Play Mode 与 Windows Player 运行相同的 12 项测试，覆盖：

1. 预热不激活玩法，实际建立不同实例。
2. 类型化参数与 Transform 在 OnEnable 前就绪。
3. 归还、状态清理、复用与旧凭证失效。
4. 延迟销毁、驻留配额与资源引用释放时序。
5. 准备期间取消，不激活、不交付。
6. 一个清理组件异常后，仍清理其余组件并停用实例。
7. 外部 Destroy 后凭证失效，归还仍能修正账目。
8. 分区关闭后迁移到常驻缓存，再卸载场景。
9. 排队期间父节点销毁，生成明确失败。
10. 两个池共享资源时分别持有引用。
11. 缓存根节点损坏时，作用域等待销毁完成。
12. Resources 资源缺失时返回加载失败，并能关闭释放引用。

## IL2CPP 构建限制

已实际尝试 Windows x64 IL2CPP 构建。构建在 Unity 安装目录的原生运行时代码失败，首个关键错误为：

```text
Editor/Data/il2cpp/libil2cpp/utils/Il2CppHashMap.h(71)
error C2039: 'hash_compare': is not a member of 'stdext'
```

随后出现 `MetadataCache.cpp` 的关联模板编译错误。C# 编译已完成；不能据此宣称 IL2CPP 验证通过。需要先处理本机 Unity 2021.3.8f1 与 C++ 工具链的构建兼容问题，再重跑 Player 测试。未为绕过此问题修改 Unity 自带头文件或切换源工程构建设置。

## 尚需项目场景验证

当前工程尚无大世界玩法和目标设备场景，因此没有宣称完成首次进图、连续跨分区、爆发战斗、背包反复开关等性能压测。上述测试是行为验证，不代表帧耗时、GPU 首帧成本、真实资源内存或整个服务零 GC 的性能结论。默认容量和估值需在接入业务资源后校准。

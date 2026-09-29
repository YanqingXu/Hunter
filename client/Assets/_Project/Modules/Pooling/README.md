# BigWorld 对象池

按《Unity 大世界客户端对象池设计文档》1.0 实现，兼容当前工程 Unity 2021.3.8f1。不依赖 Addressables、第三方异步库或 DI 框架。

## 目录与入口

| 目录 | 内容 |
| --- | --- |
| `Core` | 纯 C# 核心代码：`DataPool<T>`、版本凭证、作用域、配置、全局服务与调度 |
| `Unity` | `PrefabPool<TArgs>`、资源适配边界、`PoolDriver`、配置与预热清单资产 |
| `Editor` | `Window > BigWorld > Pool Monitor`；配置检查按钮 |
| `Samples` | 带类型化参数的角色组件与生成示例 |
| 项目根目录 `Tests/Pooling/EditMode` | 身份、容量、取消、异常、关闭、预热与预算测试源码 |
| 项目根目录 `Tests/Pooling/PlayMode` | 真实 Unity 激活、销毁、场景迁移、资源引用测试源码 |

当前不使用 `.asmdef`。`Core`、`Unity` 和 `Samples` 按 Unity 默认规则编译到 `Assembly-CSharp`；`Editor` 中的监控代码编译到 `Assembly-CSharp-Editor`，不会进入游戏构建。目录和命名空间继续用于组织代码，模块之间不再需要配置程序集引用。核心代码仍未使用 Unity API，但不再通过程序集配置强制限制依赖。

## 最短接入流程

1. 场景中创建**根 GameObject**，挂载 `PoolDriver`，保持启用。它会创建一个常驻、未激活的缓存节点并在 `Update` 中驱动服务。
2. 业务在初始化阶段注册所需的池；缓存返回的池引用。
3. 为分区、窗口、战斗建立 `PoolScope`，通过该作用域预热和借用。
4. 持有 `PoolLease<T>`，通过 `TryReturn()` 或 `Dispose()` 归还。定时器、协程和异步回调必须捕获**当次凭证**。
5. 卸载场景前 `await scope.CloseAsync()`；退出会话时 `await driver.ShutdownAsync()`。

不要提前禁用或销毁驱动，也不要把缓存节点移进业务场景。异步关闭需要驱动继续工作。

### 普通 C# 对象

```csharp
using System.Collections.Generic;
using BigWorld.Pooling;

var lists = driver.Service.RegisterData(
    () => new List<int>(32),
    new DelegateResetPolicy<List<int>>(
        list => list.Clear(),
        list => list.Capacity <= 4096),
    new PoolSettings {
        MaxIdle = 16, MaxBorrowed = 64, MaxResident = 80,
        IdleTimeoutSeconds = 0,
        EstimatedBytesPerInstance = 1024
    }.Freeze());

var result = lists.RentOrCreate(driver.Service.RootScope);
if (result.Succeeded)
{
    using (var lease = result.Lease)
    {
        lease.Value.Add(123);
    }
}
```

`Reset` 必须解除引用和业务关系；`CanRetain` 在重置后判断超大容器是否值得保留。即使最终不缓存，重置也会执行。需要显式释放原生资源的普通对象，可传入 `destroy` 回调；该回调应只在正常返回时表示释放请求成功，并支持其故障语义下的明确重试。

### 预制体

组件实现显式生命周期；本次业务参数通过泛型接口传入，无须装箱为 `object`：

```csharp
public readonly struct SpawnArgs
{
    public readonly int EntityId;
    public SpawnArgs(int entityId) { EntityId = entityId; }
}

public sealed class Actor : UnityEngine.MonoBehaviour,
    BigWorld.Pooling.Unity.IPooledLifecycle,
    BigWorld.Pooling.Unity.IPooledInitializer<SpawnArgs>
{
    public int EntityId { get; private set; }
    public void InitializeOnce() { /* 缓存稳定组件引用；不要启动玩法。 */ }
    public void InitializeForUse(SpawnArgs args) { EntityId = args.EntityId; }
    public void OnRelease() { EntityId = 0; /* 退订事件、取消任务、解除目标引用。 */ }
}
```

注册、预热、生成：

```csharp
using BigWorld.Pooling;
using BigWorld.Pooling.Unity;
using UnityEngine;

var region = driver.Service.CreateScope("Forest/001");
var source = new SharedPrefabResource(actorPrefab);
var actors = driver.Service.Register(new PrefabPool<SpawnArgs>(
    driver.Service,
    new PoolKey(PoolKind.Prefab, "actor.common", "default"),
    new PoolSettings {
        MaxIdle = 16, MaxBorrowed = 32, MaxResident = 48,
        EstimatedBytesPerInstance = 64 * 1024
    }.Freeze(),
    source.Acquire(),
    driver.CacheRoot));

var warm = await actors.PrewarmAsync(region, targetReady: 16);
// warm.Reason 可能为 CapacityLimited、TimedOut 等；预热不提供独占保证。
var result = await actors.SpawnAsync(region,
    SpawnTransform.At(Vector3.zero, Quaternion.identity),
    new SpawnArgs(1001));

// 完成任务后到 continuation 执行前，作用域仍可能已经关闭。
if (result.Succeeded && result.Lease.TryGet(out var instance))
{
    // 使用 instance，保存 result.Lease 并在业务结束时归还。
}

await region.CloseAsync();
// 从这里开始才允许卸载该分区场景。
```

`SpawnTransform.Under(parent)` 使用局部位置零、单位旋转和单位缩放；`At(position, rotation, scene)` 使用世界位置。`default(SpawnTransform)` 也是有效输入。请求中的父节点在排队期间被销毁时，该次生成失败，不会意外生成到其他位置。

普通对象提供 `TryRentCached / RentOrCreate / RentAsync`；预制体提供 `TrySpawnCached / SpawnOrCreate / SpawnAsync`。缓存接口绝不创建，但本次准备仍可能有成本。`SpawnOrCreate` 只有构造池时显式启用 `allowSynchronousCreation` 才可使用，适合加载阶段，独立计入同步创建统计。

## 配置与预热计划

通过 `Create > BigWorld > Pooling > Pool Catalog` 创建配置资产，通过 `Warm Plan` 创建预热清单。

- 配置顺序：`Defaults → DataCategory/PrefabCategory → 单项 Overrides → PoolConfig`。
- `Fields` 决定哪些字段覆盖；未选择的字段继承上一层。
- 每项填写稳定 `Id` 和 `Variant`，在直接 Prefab 引用与 Resources 路径之间二选一。
- 运行时以明确类型注册，避免依赖反射创建泛型：

```csharp
var catalogSession = catalog.Open(driver);
var fx = catalogSession.RegisterPrefab<MyFxArgs>("fx.hit.basic");
await System.Threading.Tasks.Task.WhenAll(warmPlan.Submit(region));
// 或显式准备配置中的基础目标：
await System.Threading.Tasks.Task.WhenAll(
    driver.Service.PrewarmDefaultsAsync(driver.Service.RootScope));
```

同一个 `PoolCatalogSession` 内，相同资源的变体共享加载源，但各池持有独立资源引用。自定义注册时可重复使用同一个 `SharedPrefabResource`，为各池分别 `Acquire()`。

预热 `TargetReady` 是 `Borrowed + Idle` 的目标总量。已有借用计入目标，待销毁对象不计入。任务只产生足够数量的不同实例；不能达到空闲保留上限以外的目标时，会返回部分完成及原因。完成后不自动补货。

计划以“作用域 + PlanId”登记。同一作用域多个计划按最大需求合并，独立作用域需求相加，与池基础目标取最大值。更新同一计划会取消它的旧预热任务并提交新目标；移除计划或关闭作用域会使其待完成任务失效，已经完成的共享缓存保留。

容量说明：

- `InitialStorageCapacity` 只预留容器空间。
- `MaxBorrowed` 限制借出和准备中的业务对象。
- `MaxIdle` 限制空闲保留量。
- `MaxResident` 包括创建预留、准备、借出、空闲、等待销毁、销毁中和释放故障。
- `Reconfigure` 可在运行中更新配置。调小容量限制后续借用并逐步释放空闲对象，不抢占已有借用。
- 队列支持 `WaitOrFail` 和 `Fail`。通用内核没有自动替换活动实例的策略，替换须由业务正常结束旧借用后重新请求。

## 线程、生命周期与关闭

所有池操作在创建 `PoolService` 的线程执行。Unity 项目必须在主线程创建服务和调用接口。后台线程可以触发 `CancellationToken`，也可以调用有界 `service.Post(action)`；`Post` 返回 `false` 表示队列已满，需要调用方处理。

生命周期回调不能同步重入同一个池的借用、创建、配置或关闭操作。需要继续工作时 `Post` 到下一次调度。无效凭证重复归还只返回 `false`，不会重复清理。

凭证校验覆盖池归属、槽位、版本、状态、作用域及实例有效性。作用域关闭立即使其整个子树的访问失效，实际归还按预算处理。原始对象引用无法被撤销，归还后不得继续使用。

预热只调用一次 `InitializeOnce`，不调用本次业务初始化、不激活对象。生成时先设置层级和 Transform，再执行 `InitializeForUse`，重新检查作用域和取消，最后激活并交付。不要在 `Awake` 中覆盖本次生成参数。

归还会尽力执行全部生命周期组件，然后清理缓存的粒子、拖尾、音效、刚体速度，再停用并迁回常驻缓存场景。任一步骤异常都会让整实例退役。AI、Animator、网络对象及项目事件系统的状态由业务 `OnRelease` 负责；本地回池不能代替网络反生成。

组件列表只在实例创建时扫描。当前基线不支持动态增删池生命周期组件；需要动态结构时应退役旧实例，或扩展明确的重新登记协议。

关闭与故障恢复：

- `scope.CloseAsync()`：等该作用域的请求、借用及必要销毁确认完成；不关闭共享池。
- `pool.CloseAsync()`：停止准入，等其他开放作用域正常归还，不擅自回收它们。
- `service.CloseAsync()`：先使根作用域及所有子作用域失效，再排空池并释放资源。
- 释放失败保持 `Closing`、保留实例及计数，并让池/服务关闭任务失败。修复故障后调用对应 `RetryCloseAsync()`；旧失败任务保持失败。
- 运行中的池可使用 `RetryFailedReleases()` 明确重试失败释放；已失败的关闭仍需要 `RetryCloseAsync()`。
- `driver.ShutdownAsync()` 成功后才销毁驱动；重试使用 `RetryShutdownAsync()`。
- 池关闭后，可 `UnregisterClosedPool(key)` 再注册新池；旧凭证不能操作新池。
- 应用退出只发出尽力关闭信号，不宣称已经完成异步排空。

自定义适配器的有效性查询应当是无副作用、不会抛异常的轻量查询。创建函数不得在取得实例后、返回核心登记前执行可能失败的业务初始化；这部分放到 `InitializeOnce`。销毁请求和销毁确认分开实现，明确哪些步骤可重试。

## 资源适配与预算

内置资源路径：直接 Prefab 引用、`Resources.LoadAsync<GameObject>` 共享加载，随后 `Object.Instantiate`。一个请求取消不会取消共享加载。即使全部请求取消，池关闭也会等已开始的加载结算后再释放资源引用。

默认销毁通过 `Object.Destroy` 发出请求，必须在后续调度帧确认 Unity 对象失效后才返还配额。资源系统直接实例化的项目应实现 `IPrefabInstanceProvider`，自行保存实例句柄，并用该资源系统的释放及完成确认 API；不要同时调用 Unity Destroy 和句柄释放。

`Resources` 的 GameObject 不能用 `Resources.UnloadAsset` 单独卸载，内置适配器在最后一个引用归还后解除持有。实际资源卸载时机由项目资源系统控制。直接 Prefab 引用本身也可能被场景或配置资产继续持有。这里不调用 `GC.Collect()` 或全局资源清扫。

一个 `PoolService` 共享一份每帧创建、销毁、维护数量限制和耗时目标。控制、作用域回收、池维护、业务请求、预热和压力回收交错执行，轮询池的进度跨帧保留。请求有上限、期限及优先级老化。单次不可拆分的回调或 Unity 操作可能超过耗时目标。

全局驻留数量与单实例增量估值限制新增创建；压力释放选择低保留优先级池的较旧空闲实例。活动实例不参与压力淘汰。共享资产估值单独展示，不加入每实例累计值；值为零表示尚未提供资产估值。默认估值仅为初始配置，需按资源类型和目标设备校准。

## 调试与测试

在 Play Mode 打开 `Window > BigWorld > Pool Monitor`，查看各池计数、等待原因、缓存命中、三类创建统计、关闭故障，以及各作用域持有的槽位与版本。

编程错误抛异常；预期借用失败通过 `PoolStatus` 返回。预热通过 `WarmEndReason` 返回。生命周期异常保存在最近 128 条诊断记录中，结果携带诊断编号；`PoolDriver` 会向 Unity Console 输出原始异常。

测试源码保留在项目根目录 `Tests/Pooling`，位于 `Assets` 之外，不参与日常编译，也不会直接显示在当前工程的 Test Runner 中。需要回归测试时，在独立测试工程中导入对象池及测试源码，再配置测试程序集运行；现有测试结果和独立验证工程的说明见 `Validation.md`。不要直接将含 NUnit 的测试脚本复制回普通运行时目录。

当前版本已通过 61 项核心测试、14 项 Play Mode 测试和 Windows Mono Player 中的同组 14 项测试。新增压力覆盖百万次复用、18 万次混合操作、容量饱和及反复场景卸载，具体测试边界与复跑方法见 [Stability.md](Stability.md)。

另有 9 个性能基准，Windows Mono 测试构建独立运行三次，结果见 [Performance.md](Performance.md)。当前需要关注大批异步请求的分配和排队成本，以及默认预算下大作用域关闭的完成延迟。此前未校准接口得出的“0 字节”已更正；性能报告使用校准后的 Unity 分配事件计数。

同步缓存借还按稳定容量下不持续分配托管内存的目标编写。首次创建、容量增长、异步 Task、业务回调和调试快照均可能分配；尚不能把整个服务称为零 GC。真实大世界场景的帧分布、GPU 首次使用和设备内存仍需按设计文档的场景矩阵压测。

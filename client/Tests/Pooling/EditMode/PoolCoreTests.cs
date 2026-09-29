using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BigWorld.Pooling.Tests
{
    public sealed class PoolCoreTests
    {
        private sealed class Clock : IPoolClock { public double Time; public double Now => Time; }
        private sealed class Box { internal int Id; internal bool Valid = true; internal int Data; }
        private sealed class Adapter : PoolAdapter<Box>
        {
            internal int Created, ResetCount, Destroyed, Released;
            internal bool FailCreate, FailInitialize, FailReset, FailDestroy, FailConfirm, FailRelease;
            internal bool Retain = true, Confirm = true;
            internal ResourceState LoadState = ResourceState.Ready;
            internal Action<Box> OnInitialize, OnReset;
            internal Action OnCreate;
            public override ResourceState ResourceState => LoadState;
            public override Exception ResourceError => new Exception("load failure");
            /// <summary>
            /// 模拟启动尚未完成的资源加载，供取消、等待和关闭时序测试控制。
            /// </summary>
            public override void BeginLoad() { LoadState = ResourceState.Loading; }
            /// <summary>
            /// 执行创建观察回调，按开关模拟创建异常，或产生具有独立编号的新测试对象。
            /// </summary>
            public override Box Create() { OnCreate?.Invoke(); if (FailCreate) throw new Exception("create"); return new Box { Id = ++Created }; }
            /// <summary>
            /// 执行初始化观察回调，并按开关模拟一次性初始化失败。
            /// </summary>
            public override void InitializeOnce(Box value) { OnInitialize?.Invoke(value); if (FailInitialize) throw new Exception("initialize"); }
            /// <summary>
            /// 同时检查测试对象引用和有效标记，用于模拟 Unity 实例被外部销毁。
            /// </summary>
            public override bool IsValid(Box value) => value != null && value.Valid;
            /// <summary>
            /// 记录重置次数、清空测试数据并执行观察回调，可按开关模拟清理失败。
            /// </summary>
            public override void Reset(Box value) { ResetCount++; value.Data = 0; OnReset?.Invoke(value); if (FailReset) throw new Exception("reset"); }
            /// <summary>
            /// 返回测试控制的缓存保留结果，以覆盖拒绝缓存和退役路径。
            /// </summary>
            public override bool CanRetain(Box value) => Retain;
            /// <summary>
            /// 按开关模拟销毁请求失败，成功时累计实际发出请求的次数。
            /// </summary>
            public override void RequestDestroy(Box value) { if (FailDestroy) throw new Exception("destroy"); Destroyed++; }
            /// <summary>
            /// 模拟释放确认开关与异常，且只在后续调度帧允许完成，用于验证延迟配额结算。
            /// </summary>
            public override bool IsDestroyComplete(Box value, long issuedFrame, long currentFrame)
            { if (FailConfirm) throw new Exception("confirm"); return Confirm && currentFrame > issuedFrame; }
            /// <summary>
            /// 按开关模拟资源释放失败，成功时累计释放次数以检查关闭幂等性。
            /// </summary>
            public override void ReleaseResources() { if (FailRelease) throw new Exception("release"); Released++; }
        }
        private Clock clock;
        private PoolService service;
        private PoolScope scope;
        private Adapter adapter;
        private DataPool<Box> pool;
        /// <summary>
        /// 生成容量可调整、空闲过期关闭且实例估值固定的测试配置。
        /// </summary>
        private static PoolSettings Settings(int idle = 4, int borrowed = 4, int resident = 8) => new PoolSettings
        { InitialStorageCapacity = 8, MaxIdle = idle, MaxBorrowed = borrowed, MaxResident = resident, IdleTimeoutSeconds = 0, EstimatedBytesPerInstance = 100 };
        /// <summary>
        /// 为每个核心测试新建可控时钟、服务、分区作用域和可注入异常的对象池。
        /// </summary>
        [SetUp] public void Setup()
        {
            clock = new Clock(); service = new PoolService(new PoolFrameBudget { MaxCreatesPerFrame = 2, MaxDestroyRequestsPerFrame = 2, MaxMaintenanceStepsPerFrame = 60, MaxWorkMilliseconds = 100 }, clock);
            scope = service.CreateScope("region"); adapter = new Adapter(); pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(), Settings().Freeze(), adapter));
        }
        /// <summary>
        /// 在限定步数内持续驱动服务，直到条件成立；超出上限则使测试失败，避免无限等待。
        /// </summary>
        private void Pump(Func<bool> done, int limit = 2000)
        { for (int i = 0; i < limit && !done(); i++) service.Tick(); Assert.IsTrue(done(), "Operation did not settle within the bounded pump."); }
        /// <summary>
        /// 检查核心计数非负且驻留总量等于各状态之和。
        /// </summary>
        private static void AssertCounts(PoolSnapshot snapshot)
        {
            Assert.GreaterOrEqual(snapshot.ResidentSlots, 0); Assert.GreaterOrEqual(snapshot.Borrowed, 0); Assert.GreaterOrEqual(snapshot.Idle, 0);
            Assert.AreEqual(snapshot.CreateReserved + snapshot.Preparing + snapshot.Borrowed + snapshot.Returning + snapshot.Idle + snapshot.PendingDestroy + snapshot.DestroyIssued + snapshot.DestroyFaulted, snapshot.ResidentSlots);
        }
        /// <summary>
        /// 验证预留元数据容量不会创建业务对象或占用驻留配额。
        /// </summary>
        [Test] public void MetadataCapacityDoesNotCreateInstances()
        { Assert.AreEqual(0, adapter.Created); Assert.AreEqual(0, pool.GetSnapshot().ResidentSlots); }
        /// <summary>
        /// 验证同一凭证的副本多次归还时，只有第一次改变状态并执行重置。
        /// </summary>
        [Test] public void CopiedLeaseReturnsExactlyOnce()
        {
            var lease = pool.RentOrCreate(scope).Lease; var copy = lease; Assert.IsTrue(copy.TryReturn()); Assert.IsFalse(lease.TryReturn());
            Assert.AreEqual(1, adapter.ResetCount); Assert.AreEqual(1, pool.GetSnapshot().Idle);
        }
        /// <summary>
        /// 验证槽位再次借出后，旧凭证既不能取出对象，也不能归还新的借用。
        /// </summary>
        [Test] public void OldLeaseCannotTouchNewBorrow()
        {
            var old = pool.RentOrCreate(scope).Lease; var value = old.Value; old.Dispose(); var current = pool.RentOrCreate(scope).Lease;
            Assert.AreSame(value, current.Value); Assert.IsFalse(old.TryGet(out _)); Assert.IsFalse(old.TryReturn()); Assert.AreEqual(1, pool.GetSnapshot().Borrowed);
        }
        /// <summary>
        /// 验证默认凭证取值和归还失败，强制访问 Value 会报告凭证无效。
        /// </summary>
        [Test] public void DefaultLeaseIsHarmless()
        { var lease = default(PoolLease<Box>); Assert.IsFalse(lease.TryGet(out _)); Assert.IsFalse(lease.TryReturn()); Assert.Throws<InvalidOperationException>(() => { var ignored = lease.Value; }); }
        /// <summary>
        /// 验证释放后复用空槽位仍保留版本隔离，旧凭证无法影响新借用。
        /// </summary>
        [Test] public void ReusedEmptySlotPreservesVersion()
        {
            adapter.Retain = false; var old = pool.RentOrCreate(scope).Lease; old.Dispose(); Pump(() => pool.GetSnapshot().ResidentSlots == 0);
            var current = pool.RentOrCreate(scope).Lease; Assert.IsFalse(old.TryReturn()); Assert.IsTrue(current.TryGet(out _)); Assert.AreEqual(1, pool.GetSnapshot().MetadataSlots);
        }
        /// <summary>
        /// 验证仅缓存借用在缺少空闲对象时直接失败，不会偷偷创建实例。
        /// </summary>
        [Test] public void CacheMissNeverCreates()
        { Assert.IsFalse(pool.TryRentCached(scope, out _)); Assert.AreEqual(0, adapter.Created); }
        /// <summary>
        /// 验证即使策略拒绝缓存，对象也已完成必要重置后才进入销毁等待。
        /// </summary>
        [Test] public void ResetPrecedesRetentionDecision()
        {
            adapter.Retain = false; var lease = pool.RentOrCreate(scope).Lease; lease.Value.Data = 7; var value = lease.Value; lease.Dispose();
            Assert.AreEqual(0, value.Data); Assert.AreEqual(1, adapter.ResetCount); Assert.AreEqual(1, pool.GetSnapshot().PendingDestroy);
        }
        /// <summary>
        /// 验证作用域关闭立即禁止访问和新请求，而实际清理由后续预算调度完成。
        /// </summary>
        [Test] public void ClosingScopeInvalidatesImmediatelyButCleanupIsBudgeted()
        {
            var lease = pool.RentOrCreate(scope).Lease; var task = scope.CloseAsync();
            Assert.IsFalse(lease.TryGet(out _)); Assert.AreEqual(PoolStatus.ScopeClosed, pool.RentOrCreate(scope).Status); Assert.IsFalse(task.IsCompleted);
            Pump(() => task.IsCompleted); Assert.AreEqual(ScopeState.Closed, scope.State); Assert.AreEqual(0, pool.GetSnapshot().Borrowed);
        }
        /// <summary>
        /// 验证父作用域开始关闭后，子作用域及其凭证立即逻辑失效，并最终一起排空。
        /// </summary>
        [Test] public void ClosingParentInvalidatesDescendantsImmediately()
        {
            var child = scope.CreateChild("child"); var lease = pool.RentOrCreate(child).Lease; var close = scope.CloseAsync();
            Assert.IsFalse(child.IsOpen); Assert.IsFalse(lease.TryGet(out _)); Pump(() => close.IsCompleted); Assert.AreEqual(ScopeState.Closed, child.State);
        }
        /// <summary>
        /// 验证关闭一个作用域不会清理其他作用域在共享池中的活动借用。
        /// </summary>
        [Test] public void ClosingOneScopePreservesOtherBorrowers()
        {
            var other = service.CreateScope("other"); var first = pool.RentOrCreate(scope).Lease; var second = pool.RentOrCreate(other).Lease;
            var close = scope.CloseAsync(); Pump(() => close.IsCompleted); Assert.IsFalse(first.TryReturn()); Assert.IsTrue(second.TryGet(out _));
        }
        /// <summary>
        /// 验证单独关闭池会停止准入但继续等待开放作用域归还，不强行收回其对象。
        /// </summary>
        [Test] public void DirectPoolCloseWaitsForOpenScopeBorrow()
        {
            var lease = pool.RentOrCreate(scope).Lease; var task = pool.CloseAsync(); for (int i = 0; i < 4; i++) service.Tick();
            Assert.IsFalse(task.IsCompleted); Assert.IsTrue(lease.TryGet(out _)); Assert.AreEqual(PoolStatus.PoolClosed, pool.RentOrCreate(scope).Status);
            lease.Dispose(); Pump(() => task.IsCompleted); Assert.AreEqual(PoolState.Closed, pool.State); Assert.AreEqual(1, adapter.Released);
        }
        /// <summary>
        /// 验证全局关闭先清理全部作用域借用，再释放实例和池资源引用。
        /// </summary>
        [Test] public void GlobalCloseDrainsScopesBeforeResources()
        {
            pool.RentOrCreate(scope); var task = service.CloseAsync(); Pump(() => task.IsCompleted);
            Assert.AreEqual(PoolState.Closed, service.State); Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(1, adapter.Released);
        }
        /// <summary>
        /// 验证重复关闭作用域、池和服务时返回同一任务，而不是重复启动清理。
        /// </summary>
        [Test] public void CloseIsIdempotent()
        { Assert.AreSame(scope.CloseAsync(), scope.CloseAsync()); Assert.AreSame(pool.CloseAsync(), pool.CloseAsync()); Assert.AreSame(service.CloseAsync(), service.CloseAsync()); }
        /// <summary>
        /// 验证不能将另一个服务创建的作用域用于当前池借用。
        /// </summary>
        [Test] public void WrongServiceScopeIsProgrammingError()
        { Assert.Throws<ArgumentException>(() => pool.RentOrCreate(new PoolService().RootScope)); }
        /// <summary>
        /// 验证同一数据类型的不同变体池互不干扰，各自维护借用状态。
        /// </summary>
        [Test] public void VariantsAndPoolsDoNotShareLeases()
        {
            var other = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>("other"), Settings().Freeze(), new Adapter()));
            var a = pool.RentOrCreate(scope).Lease; var b = other.RentOrCreate(scope).Lease; a.Dispose();
            Assert.IsTrue(b.TryGet(out _)); Assert.AreEqual(1, other.GetSnapshot().Borrowed);
        }
        /// <summary>
        /// 验证创建初始化期间取消请求时不交付凭证，并最终释放已创建实例。
        /// </summary>
        [Test] public void CancellationDuringInitializationDoesNotDeliver()
        {
            var cancellation = new CancellationTokenSource(); adapter.OnInitialize = _ => cancellation.Cancel();
            var task = pool.RentAsync(scope, cancellationToken: cancellation.Token); Pump(() => task.IsCompleted);
            Assert.AreEqual(PoolStatus.Cancelled, task.Result.Status); Pump(() => pool.GetSnapshot().ResidentSlots == 0); Assert.AreEqual(0, scope.BorrowedCount);
        }
        /// <summary>
        /// 验证初始化回调关闭作用域时，实例不交付且作用域等待退役结算。
        /// </summary>
        [Test] public void ScopeClosingDuringInitializationDoesNotDeliver()
        {
            adapter.OnInitialize = _ => scope.CloseAsync(); var result = pool.RentOrCreate(scope);
            Assert.AreEqual(PoolStatus.ScopeClosed, result.Status); Pump(() => scope.State == ScopeState.Closed); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证取消一个等待者只结束其请求，其他等待者仍可使用相同加载结果。
        /// </summary>
        [Test] public void OneCancelledWaitDoesNotCancelSharedLoading()
        {
            adapter.LoadState = ResourceState.NotLoaded; var cancellation = new CancellationTokenSource();
            var a = pool.RentAsync(scope, cancellationToken: cancellation.Token); var b = pool.RentAsync(scope); service.Tick(); cancellation.Cancel();
            Pump(() => a.IsCompleted); Assert.AreEqual(PoolStatus.Cancelled, a.Result.Status); Assert.IsFalse(b.IsCompleted);
            adapter.LoadState = ResourceState.Ready; Pump(() => b.IsCompleted); Assert.IsTrue(b.Result.Succeeded);
        }
        /// <summary>
        /// 验证关闭中的池先结束借用请求，再等待迟到加载结算后释放资源引用。
        /// </summary>
        [Test] public void ClosingPoolWaitsForLateLoadBeforeReleasingResources()
        {
            adapter.LoadState = ResourceState.NotLoaded; var request = pool.RentAsync(scope); service.Tick(); var close = pool.CloseAsync();
            Pump(() => request.IsCompleted); Assert.IsFalse(close.IsCompleted); Assert.AreEqual(0, adapter.Released);
            adapter.LoadState = ResourceState.Ready; Pump(() => close.IsCompleted); Assert.AreEqual(0, adapter.Created); Assert.AreEqual(1, adapter.Released);
        }
        /// <summary>
        /// 验证等待队列满时返回 QueueFull，取消后请求及作用域待处理计数正确移除。
        /// </summary>
        [Test] public void QueueIsBoundedAndCancellationReleasesOwnership()
        {
            var s = Settings(); s.MaxPendingRequests = 1; pool.Reconfigure(s.Freeze()); adapter.LoadState = ResourceState.Loading;
            var cancel = new CancellationTokenSource(); var first = pool.RentAsync(scope, cancellationToken: cancel.Token);
            Assert.AreEqual(PoolStatus.QueueFull, pool.RentAsync(scope).Result.Status); cancel.Cancel(); Pump(() => first.IsCompleted);
            Assert.AreEqual(0, scope.PendingCount); Assert.AreEqual(0, pool.GetSnapshot().PendingRequests);
        }
        /// <summary>
        /// 验证请求超时通过 TimedOut 结果表达，而不是将 Task 置为取消状态。
        /// </summary>
        [Test] public void TimeoutsAreStructuredResults()
        {
            adapter.LoadState = ResourceState.Loading; var request = pool.RentAsync(scope, new RentOptions(timeoutSeconds: 2)); clock.Time = 3;
            Pump(() => request.IsCompleted); Assert.AreEqual(PoolStatus.TimedOut, request.Result.Status); Assert.IsFalse(request.IsCanceled);
        }
        /// <summary>
        /// 验证加载失败返回原始错误的诊断编号，并且不占用实例驻留配额。
        /// </summary>
        [Test] public void LoadFailureHasDiagnosticAndNoInstance()
        {
            adapter.LoadState = ResourceState.Failed; var request = pool.RentAsync(scope); Pump(() => request.IsCompleted);
            Assert.AreEqual(PoolStatus.LoadFailed, request.Result.Status); Assert.Greater(request.Result.DiagnosticId, 0); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证工厂创建失败后，池状态计数和全局创建预留完整回滚。
        /// </summary>
        [Test] public void CreationFailureReleasesReservation()
        {
            adapter.FailCreate = true; var result = pool.RentOrCreate(scope); Assert.AreEqual(PoolStatus.CreationFailed, result.Status);
            Assert.AreEqual(0, pool.GetSnapshot().ResidentSlots); Assert.AreEqual(0, service.ResidentSlots); AssertCounts(pool.GetSnapshot());
        }
        /// <summary>
        /// 验证一次性初始化失败时实例已经登记，随后由池承担清理、销毁和作用域结算。
        /// </summary>
        [Test] public void InitializeFailureRetiresRegisteredInstance()
        {
            adapter.FailInitialize = true; var result = pool.RentOrCreate(scope);
            Assert.AreEqual(PoolStatus.InitializationFailed, result.Status); Assert.AreEqual(1, pool.GetSnapshot().PendingDestroy);
            Assert.AreEqual(1, scope.RetiringCount); Pump(() => service.ResidentSlots == 0); Assert.AreEqual(1, adapter.Destroyed);
        }
        /// <summary>
        /// 验证创建和初始化回调执行时，其相应状态与全局驻留计数已经登记。
        /// </summary>
        [Test] public void CreationAndPreparationAreChargedBeforeCallbacks()
        {
            adapter.OnCreate = () => { Assert.AreEqual(1, pool.GetSnapshot().CreateReserved); Assert.AreEqual(1, service.ResidentSlots); };
            adapter.OnInitialize = _ => Assert.AreEqual(1, pool.GetSnapshot().Preparing);
            Assert.IsTrue(pool.RentOrCreate(scope).Succeeded);
        }
        /// <summary>
        /// 验证归还清理失败的实例不重新进入缓存，而是保留计数直到释放完成。
        /// </summary>
        [Test] public void ResetFailureDoesNotCacheInstance()
        {
            var lease = pool.RentOrCreate(scope).Lease; adapter.FailReset = true; Assert.IsTrue(lease.TryReturn());
            Assert.AreEqual(0, pool.GetSnapshot().Idle); Assert.AreEqual(1, pool.GetSnapshot().PendingDestroy); Pump(() => service.ResidentSlots == 0);
        }
        /// <summary>
        /// 验证销毁失败保留实例及配额，关闭任务失败后可明确重试，原失败任务保持失败。
        /// </summary>
        [Test] public void DestroyFailureRetainsQuotaAndFailedCloseCanRetry()
        {
            var lease = pool.RentOrCreate(scope).Lease; adapter.FailDestroy = true; var close = pool.CloseAsync(); lease.Dispose(); Pump(() => close.IsCompleted);
            Assert.IsTrue(close.IsFaulted); Assert.AreEqual(PoolState.Closing, pool.State); Assert.AreEqual(1, pool.GetSnapshot().DestroyFaulted); Assert.AreEqual(1, service.ResidentSlots);
            adapter.FailDestroy = false; var retry = pool.RetryCloseAsync(); Pump(() => retry.IsCompleted); Assert.IsFalse(retry.IsFaulted); Assert.IsTrue(close.IsFaulted); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证销毁确认失败后的重试只继续确认，不重复发出已经成功的销毁请求。
        /// </summary>
        [Test] public void ConfirmationRetryDoesNotIssueDestroyTwice()
        {
            var lease = pool.RentOrCreate(scope).Lease; adapter.FailConfirm = true; var close = pool.CloseAsync(); lease.Dispose(); Pump(() => close.IsCompleted);
            Assert.IsTrue(close.IsFaulted); Assert.AreEqual(1, adapter.Destroyed); adapter.FailConfirm = false;
            var retry = pool.RetryCloseAsync(); Pump(() => retry.IsCompleted); Assert.AreEqual(1, adapter.Destroyed);
        }
        /// <summary>
        /// 验证池资源释放失败时保持 Closing，并在故障修复和显式重试后才完成关闭。
        /// </summary>
        [Test] public void ResourceReleaseFailureKeepsPoolClosing()
        {
            adapter.FailRelease = true; var close = pool.CloseAsync(); Pump(() => close.IsCompleted); Assert.IsTrue(close.IsFaulted); Assert.AreEqual(PoolState.Closing, pool.State);
            adapter.FailRelease = false; var retry = pool.RetryCloseAsync(); Pump(() => retry.IsCompleted); Assert.AreEqual(PoolState.Closed, pool.State);
        }
        /// <summary>
        /// 验证根作用域尚未排空时发生释放故障，全局关闭仍能报告失败并正确重试。
        /// </summary>
        [Test] public void ServiceCloseCanRetryFaultBeforeRootFinishes()
        {
            pool.RentOrCreate(scope); adapter.FailDestroy = true; var close = service.CloseAsync(); Pump(() => close.IsCompleted); Assert.IsTrue(close.IsFaulted);
            adapter.FailDestroy = false; var retry = service.RetryCloseAsync(); Pump(() => retry.IsCompleted); Assert.IsFalse(retry.IsFaulted); Assert.AreEqual(PoolState.Closed, service.State);
        }
        /// <summary>
        /// 验证未确认销毁的对象继续占用驻留配额，并阻止其作用域过早报告关闭完成。
        /// </summary>
        [Test] public void DeferredDestroyBlocksResidentAdmissionAndScopeClose()
        {
            pool.Reconfigure(Settings(0, 1, 1).Freeze()); adapter.Confirm = false; var lease = pool.RentOrCreate(scope).Lease; lease.Dispose();
            Assert.AreEqual(PoolStatus.CapacityExceeded, pool.RentOrCreate(scope).Status); var close = scope.CloseAsync(); for (int i = 0; i < 10; i++) service.Tick();
            Assert.IsFalse(close.IsCompleted); Assert.AreEqual(1, service.ResidentSlots); adapter.Confirm = true; Pump(() => close.IsCompleted); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证外部使空闲实例失效后，缓存借用不会交付该对象，而会将其退役。
        /// </summary>
        [Test] public void ExternalInvalidationNeverDeliversDeadCachedObject()
        {
            var lease = pool.RentOrCreate(scope).Lease; var value = lease.Value; lease.Dispose(); value.Valid = false;
            Assert.IsFalse(pool.TryRentCached(scope, out _)); Assert.AreEqual(1, pool.GetSnapshot().PendingDestroy);
        }
        /// <summary>
        /// 验证外部销毁的借出实例无法访问，但凭证归还仍可结束记录并修正账目。
        /// </summary>
        [Test] public void ExternalInvalidBorrowCanStillBeReturned()
        { var lease = pool.RentOrCreate(scope).Lease; lease.Value.Valid = false; Assert.IsFalse(lease.TryGet(out _)); Assert.IsTrue(lease.TryReturn()); Pump(() => service.ResidentSlots == 0); }
        /// <summary>
        /// 验证预热把已有借用计入目标，并通过创建不同实例补足缺口。
        /// </summary>
        [Test] public void PrewarmCreatesDistinctInstancesAndCountsExistingReady()
        {
            var borrowed = pool.RentOrCreate(scope).Lease; var warm = pool.PrewarmAsync(scope, 4); Pump(() => warm.IsCompleted);
            Assert.AreEqual(3, warm.Result.Created); Assert.AreEqual(4, warm.Result.EndReady); Assert.AreEqual(4, adapter.Created);
            var ids = new HashSet<int> { borrowed.Value.Id }; for (int i = 0; i < 3; i++) ids.Add(pool.RentOrCreate(scope).Lease.Value.Id); Assert.AreEqual(4, ids.Count);
        }
        /// <summary>
        /// 验证超出空闲容量的预热目标返回部分完成，任务结束后不持续创建补货。
        /// </summary>
        [Test] public void WarmTargetAboveIdleCapacityCompletesPartiallyWithoutLooping()
        {
            var warm = pool.PrewarmAsync(scope, 100); Pump(() => warm.IsCompleted); Assert.AreEqual(WarmEndReason.CapacityLimited, warm.Result.Reason);
            Assert.AreEqual(4, adapter.Created); var lease = pool.RentOrCreate(scope).Lease; for (int i = 0; i < 10; i++) service.Tick(); Assert.AreEqual(4, adapter.Created);
        }
        /// <summary>
        /// 验证多个相同目标的预热请求共享已准备供给，不重复创建整批实例。
        /// </summary>
        [Test] public void ConcurrentWarmRequestsDoNotDoubleCountSupply()
        {
            var a = pool.PrewarmAsync(scope, 4); var b = pool.PrewarmAsync(scope, 4); Pump(() => a.IsCompleted && b.IsCompleted);
            Assert.AreEqual(4, adapter.Created); Assert.AreEqual(4, a.Result.Created + b.Result.Created);
        }
        /// <summary>
        /// 验证同作用域计划按最大需求合并，独立作用域求和，关闭作用域后仅移除自身需求。
        /// </summary>
        [Test] public void WarmPlanUpdatesUseScopeMaximumAndIndependentScopeSum()
        {
            var demand = new Dictionary<PoolKey, int> { [pool.Key] = 2 };
            service.SubmitWarmPlan(scope, "one", demand); service.SubmitWarmPlan(scope, "one", demand); service.SubmitWarmPlan(scope, "two", demand);
            Assert.AreEqual(2, service.GetWarmTarget(pool.Key)); var other = service.CreateScope("other"); service.SubmitWarmPlan(other, "one", demand);
            Assert.AreEqual(4, service.GetWarmTarget(pool.Key)); scope.CloseAsync(); Assert.AreEqual(2, service.GetWarmTarget(pool.Key));
        }
        /// <summary>
        /// 验证多个池同时预热或关闭时，创建和销毁请求共同遵守服务级每帧上限。
        /// </summary>
        [Test] public void GlobalCreateAndDestroyBudgetsAreSharedAcrossPools()
        {
            var other = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>("b"), Settings().Freeze(), new Adapter()));
            var a = pool.PrewarmAsync(scope, 4); var b = other.PrewarmAsync(scope, 4);
            while (!a.IsCompleted || !b.IsCompleted) { service.Tick(); Assert.LessOrEqual(service.Scheduler.CreatesThisFrame, 2); }
            var close = service.CloseAsync(); Pump(() => { Assert.LessOrEqual(service.Scheduler.DestroyRequestsThisFrame, 2); return close.IsCompleted; });
        }
        /// <summary>
        /// 验证将同一计划的目标调低后，旧的大目标任务被取消，不再继续创建多余对象。
        /// </summary>
        [Test] public void WarmPlanUpdateCancelsObsoleteHigherTarget()
        {
            var old = service.SubmitWarmPlan(scope, "plan", new Dictionary<PoolKey, int> { [pool.Key] = 4 })[0];
            var current = service.SubmitWarmPlan(scope, "plan", new Dictionary<PoolKey, int> { [pool.Key] = 2 })[0];
            Pump(() => old.IsCompleted && current.IsCompleted);
            Assert.AreEqual(WarmEndReason.Cancelled, old.Result.Reason); Assert.AreEqual(2, adapter.Created); Assert.AreEqual(2, service.GetWarmTarget(pool.Key));
        }
        /// <summary>
        /// 验证关闭并注销池后重新注册同一池键，旧凭证仍无法访问或归还新池的借用。
        /// </summary>
        [Test] public void RebuildingPoolDoesNotReviveOldLeases()
        {
            var old = pool.RentOrCreate(scope).Lease; old.Dispose(); var close = pool.CloseAsync(); Pump(() => close.IsCompleted);
            service.UnregisterClosedPool(pool.Key);
            var replacement = service.Register(new DataPool<Box>(service, pool.Key, Settings().Freeze(), new Adapter()));
            var current = replacement.RentOrCreate(scope).Lease; Assert.IsFalse(old.TryGet(out _)); Assert.IsFalse(old.TryReturn()); Assert.IsTrue(current.TryGet(out _));
        }
        /// <summary>
        /// 验证借用容量不足只会失败或等待，不会抢占活动借用，归还后等待者可继续取得对象。
        /// </summary>
        [Test] public void BorrowCapacityDoesNotForceRecycleActiveLeases()
        {
            pool.Reconfigure(Settings(1, 1, 2).Freeze()); var lease = pool.RentOrCreate(scope).Lease;
            Assert.AreEqual(PoolStatus.CapacityExceeded, pool.RentOrCreate(scope).Status); var waiting = pool.RentAsync(scope); service.Tick(); Assert.IsFalse(waiting.IsCompleted);
            lease.Dispose(); Pump(() => waiting.IsCompleted); Assert.IsTrue(waiting.Result.Succeeded);
        }
        /// <summary>
        /// 验证运行时降低容量不会立即使活动凭证失效，对象归还后按新配置逐步释放。
        /// </summary>
        [Test] public void ShrinkingCapacityKeepsActiveBorrowersAndTrimsIdle()
        {
            var a = pool.RentOrCreate(scope).Lease; var b = pool.RentOrCreate(scope).Lease; pool.Reconfigure(Settings(0, 1, 1).Freeze());
            Assert.IsTrue(a.TryGet(out _)); Assert.IsTrue(b.TryGet(out _)); a.Dispose(); b.Dispose(); Pump(() => service.ResidentSlots == 0);
        }
        /// <summary>
        /// 验证空闲过期使用注入的单调时钟，常规淘汰不会低于最小空闲保留量。
        /// </summary>
        [Test] public void ExpirationUsesInjectedMonotonicTimeAndPreservesMinimum()
        {
            var s = Settings(); s.MinIdle = 1; s.IdleTimeoutSeconds = 2; pool.Reconfigure(s.Freeze()); var warm = pool.PrewarmAsync(scope, 4); Pump(() => warm.IsCompleted);
            clock.Time = 3; Pump(() => pool.GetSnapshot().ResidentSlots == 1); Assert.AreEqual(1, pool.GetSnapshot().Idle);
        }
        /// <summary>
        /// 验证内存压力优先淘汰保留优先级较低池中的空闲对象。
        /// </summary>
        [Test] public void MemoryPressureEvictsLowerPriorityBeforeHigher()
        {
            var highSettings = Settings(); highSettings.RetentionPriority = 100;
            var high = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>("high"), highSettings.Freeze(), new Adapter()));
            var a = pool.PrewarmAsync(scope, 1); var b = high.PrewarmAsync(scope, 1); Pump(() => a.IsCompleted && b.IsCompleted);
            service.RequestMemoryPressure(100); Pump(() => service.EstimatedResidentBytes == 100); Assert.AreEqual(0, pool.GetSnapshot().ResidentSlots); Assert.AreEqual(1, high.GetSnapshot().Idle);
        }
        /// <summary>
        /// 验证普通内存压力处理不回收活动借用，凭证仍然有效。
        /// </summary>
        [Test] public void MemoryPressureNeverEvictsBorrowedInstances()
        { var lease = pool.RentOrCreate(scope).Lease; service.RequestMemoryPressure(0); for (int i = 0; i < 10; i++) service.Tick(); Assert.IsTrue(lease.TryGet(out _)); }
        /// <summary>
        /// 验证全局实例内存估值已经达到上限时，进一步同步创建会返回全局预算不足。
        /// </summary>
        [Test] public void GlobalAdmissionIncludesEstimatesAndPendingDestruction()
        {
            var limited = new PoolService(maxEstimatedResidentBytes: 100); var p = limited.Register(new DataPool<Box>(limited, PoolKey.ForData<Box>(), Settings().Freeze(), new Adapter()));
            Assert.IsTrue(p.RentOrCreate(limited.RootScope).Succeeded); Assert.AreEqual(PoolStatus.GlobalBudgetExceeded, p.RentOrCreate(limited.RootScope).Status);
        }
        /// <summary>
        /// 验证生命周期回调同步重入同一池会使初始化失败，但不破坏实例登记与最终释放。
        /// </summary>
        [Test] public void CallbackReentryFailsWithoutCorruptingAccounting()
        {
            adapter.OnInitialize = _ => pool.RentOrCreate(scope); var result = pool.RentOrCreate(scope);
            Assert.AreEqual(PoolStatus.InitializationFailed, result.Status); Pump(() => service.ResidentSlots == 0); AssertCounts(pool.GetSnapshot());
        }
        /// <summary>
        /// 验证归还重置回调里重复归还同一凭证只返回失败，不再执行第二次清理。
        /// </summary>
        [Test] public void DuplicateReturnInsideResetIsHarmless()
        { var lease = pool.RentOrCreate(scope).Lease; adapter.OnReset = _ => Assert.IsFalse(lease.TryReturn()); Assert.IsTrue(lease.TryReturn()); Assert.AreEqual(1, adapter.ResetCount); }
        /// <summary>
        /// 验证后台线程可以发出取消信号，但不能直接调用归属主线程的借用接口。
        /// </summary>
        [Test] public void BackgroundCancellationIsSafeAndBackgroundRentIsRejected()
        {
            var cancel = new CancellationTokenSource(); adapter.LoadState = ResourceState.Loading; var request = pool.RentAsync(scope, cancellationToken: cancel.Token);
            Task.Run(() => { cancel.Cancel(); Assert.Throws<InvalidOperationException>(() => pool.RentOrCreate(scope)); }).GetAwaiter().GetResult();
            Pump(() => request.IsCompleted); Assert.AreEqual(PoolStatus.Cancelled, request.Result.Status);
        }
        /// <summary>
        /// 验证运行时配置独立于可编辑设置，并拒绝不满足容量关系的配置。
        /// </summary>
        [Test] public void ConfigIsImmutableAndRejectsInvalidLimits()
        {
            var settings = Settings(); var config = settings.Freeze(); settings.MaxIdle = 1; Assert.AreEqual(4, config.MaxIdle);
            settings.MinIdle = 2; Assert.Throws<ArgumentException>(() => settings.Freeze());
        }
        /// <summary>
        /// 验证同一服务不能重复注册相同完整池键。
        /// </summary>
        [Test] public void DuplicateRegistrationIsRejected()
        { Assert.Throws<InvalidOperationException>(() => service.Register(new DataPool<Box>(service, pool.Key, Settings().Freeze(), new Adapter()))); }
        /// <summary>
        /// 验证预热创建、同步创建、异步创建和缓存命中分别统计，不混淆实际成本来源。
        /// </summary>
        [Test] public void StatisticsSeparateWarmSyncAsyncAndCacheHits()
        {
            var warm = pool.PrewarmAsync(scope, 1); Pump(() => warm.IsCompleted); var a = pool.RentOrCreate(scope).Lease;
            var b = pool.RentOrCreate(scope).Lease; var request = pool.RentAsync(scope); Pump(() => request.IsCompleted);
            var snapshot = pool.GetSnapshot(); Assert.AreEqual(1, snapshot.WarmCreates); Assert.AreEqual(1, snapshot.SyncCreates); Assert.AreEqual(1, snapshot.AsyncCreates); Assert.AreEqual(1, snapshot.CacheHits); Assert.AreEqual(3, snapshot.SuccessfulRents);
        }
    }
}

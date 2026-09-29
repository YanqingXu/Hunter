using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BigWorld.Pooling.Tests
{
    public sealed class PoolStabilityTests
    {
        private sealed class Clock : IPoolClock { internal double Time; public double Now => Time; }
        private sealed class Box { internal bool Valid = true; internal int Data, DestroyCalls; }
        private sealed class Adapter : PoolAdapter<Box>
        {
            internal readonly HashSet<Box> Live = new HashSet<Box>();
            internal readonly int[] Faults = new int[5];
            internal bool InjectFaults;
            internal int Created, Released, Violations;
            private readonly int[] calls = new int[5];

            /// <summary>按各阶段独立计数注入可复现的间歇异常，重试不会永久失败。</summary>
            private void MaybeFail(int stage, int interval)
            {
                if (++calls[stage] % interval == 0 && InjectFaults)
                { Faults[stage]++; throw new InvalidOperationException("Injected stage " + stage); }
            }
            /// <summary>记录真正创建的实例，形成独立于池内部计数的存活对象账本。</summary>
            public override Box Create()
            { MaybeFail(0, 31); var value = new Box(); Live.Add(value); Created++; return value; }
            /// <summary>模拟对象已创建之后的一次性初始化异常。</summary>
            public override void InitializeOnce(Box value) { MaybeFail(1, 37); }
            /// <summary>模拟外部使对象失效；查询本身无副作用。</summary>
            public override bool IsValid(Box value) => value != null && value.Valid;
            /// <summary>清除业务数据，同时模拟部分归还发生清理异常。</summary>
            public override void Reset(Box value) { value.Data = 0; MaybeFail(2, 41); }
            /// <summary>模拟销毁请求失败，并单独记录重复发出成功销毁请求的违规行为。</summary>
            public override void RequestDestroy(Box value)
            { MaybeFail(3, 43); if (++value.DestroyCalls != 1) Violations++; }
            /// <summary>模拟延迟两帧才完成销毁及确认异常，成功后移除独立账本中的对象。</summary>
            public override bool IsDestroyComplete(Box value, long issuedFrame, long currentFrame)
            {
                MaybeFail(4, 47);
                if (currentFrame < issuedFrame + 2) return false;
                if (!Live.Remove(value)) Violations++;
                return true;
            }
            /// <summary>记录资源释放次数，检查是否在仍有存活实例时提前释放。</summary>
            public override void ReleaseResources() { if (Live.Count != 0) Violations++; Released++; }
        }
        private sealed class Pending
        {
            internal Task<RentResult<Box>> Task;
            internal CancellationTokenSource Cancel;
        }

        /// <summary>创建限制清楚的压力测试配置；禁用时间淘汰，避免墙上时间影响随机序列。</summary>
        private static PoolConfig Config(int index = 0) => new PoolSettings
        {
            InitialStorageCapacity = 40, MaxIdle = 12, MaxBorrowed = 24, MaxResident = 40,
            MaxPendingRequests = 32, RequestTimeoutSeconds = 0.2, IdleTimeoutSeconds = 0,
            EstimatedBytesPerInstance = 64 + index * 32
        }.Freeze();

        /// <summary>以有限调度次数等待任务收敛；未完成或故障时直接失败，避免测试无限挂起。</summary>
        private static void Pump(PoolService service, Clock clock, Task task, int limit = 30000)
        {
            for (int i = 0; i < limit && !task.IsCompleted; i++) { clock.Time += 0.01; service.Tick(); }
            Assert.IsTrue(task.IsCompleted, "Bounded pump exhausted.");
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
        }

        /// <summary>执行不含断言框架和诊断快照分配的缓存借还循环，验证旧凭证无法触及下一次借用。</summary>
        private static void CachedLoop(DataPool<Box> pool, PoolScope scope, Box expected, int iterations)
        {
            var previous = default(PoolLease<Box>);
            for (int i = 0; i < iterations; i++)
            {
                if (!pool.TryRentCached(scope, out var current) || !ReferenceEquals(current.Value, expected))
                    throw new InvalidOperationException("Unexpected cache miss or replacement at " + i);
                if (previous.TryGet(out _) || previous.TryReturn() || current.Value.Data != 0)
                    throw new InvalidOperationException("Stale lease or dirty instance at " + i);
                current.Value.Data = i + 1;
                if (!current.TryReturn() || current.TryReturn())
                    throw new InvalidOperationException("Return was not exactly once at " + i);
                previous = current;
            }
        }

        /// <summary>百万次热缓存借还后检查复用、凭证隔离及关闭；仅在已知分配对照通过时输出线程分配字节数。</summary>
        [Test] public void MillionCachedCyclesKeepOneInstanceAndRejectStaleLeases()
        {
            var clock = new Clock(); var service = new PoolService(clock: clock);
            var adapter = new Adapter(); var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(), Config(), adapter));
            var first = pool.RentOrCreate(service.RootScope).Lease; var value = first.Value; first.Dispose();
            CachedLoop(pool, service.RootScope, value, 10000);
            long probeBefore = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[1024];
            bool allocationCounterWorks = GC.GetAllocatedBytesForCurrentThread() - probeBefore >= 1024;
            GC.KeepAlive(probe);
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
            CachedLoop(pool, service.RootScope, value, 1000000);
            watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine("Cached cycles=1000000 elapsed_ms=" + watch.Elapsed.TotalMilliseconds +
                " thread_allocated_bytes=" + (allocationCounterWorks ? allocated.ToString() : "unavailable") + " allocation_counter_valid=" + allocationCounterWorks);
            Assert.AreEqual(1, adapter.Created); Assert.AreEqual(1, pool.GetSnapshot().MetadataSlots);
            Assert.AreEqual(0, pool.GetSnapshot().Borrowed); Assert.AreEqual(1, pool.GetSnapshot().Idle);
            Pump(service, clock, service.CloseAsync());
            Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, adapter.Live.Count);
            Assert.AreEqual(1, adapter.Released); Assert.AreEqual(0, adapter.Violations);
        }

        /// <summary>收集已经完成的异步借用并释放取消源；失败结果也是正常完成，不作为任务异常处理。</summary>
        private static void Collect(List<Pending> pending, List<PoolLease<Box>> held, int[] outcomes)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var request = pending[i]; if (!request.Task.IsCompleted) continue;
                Assert.IsFalse(request.Task.IsFaulted, request.Task.Exception?.ToString());
                var result = request.Task.Result; outcomes[(int)result.Status]++;
                if (result.Succeeded) held.Add(result.Lease);
                request.Cancel.Dispose(); pending.RemoveAt(i);
            }
        }

        /// <summary>交叉核对池、全局、作用域与独立存活账本，检查容量、队列及每帧预算上限。</summary>
        private static void Audit(PoolService service, DataPool<Box>[] pools, Adapter[] adapters)
        {
            int resident = 0, borrowed = 0, pending = 0; long bytes = 0;
            for (int i = 0; i < pools.Length; i++)
            {
                var s = pools[i].GetSnapshot();
                foreach (int count in new[] { s.CreateReserved, s.Preparing, s.Borrowed, s.Returning, s.Idle, s.PendingDestroy, s.DestroyIssued, s.DestroyFaulted, s.PendingRequests })
                    Assert.GreaterOrEqual(count, 0);
                Assert.LessOrEqual(s.ResidentSlots, pools[i].Config.MaxResident);
                Assert.LessOrEqual(s.Borrowed, pools[i].Config.MaxBorrowed);
                Assert.LessOrEqual(s.Idle, pools[i].Config.MaxIdle);
                Assert.LessOrEqual(s.PendingRequests, pools[i].Config.MaxPendingRequests);
                Assert.AreEqual(adapters[i].Live.Count, s.PhysicalInstances, "Independent live-object ledger disagrees.");
                Assert.AreEqual(0, adapters[i].Violations);
                resident += s.ResidentSlots; borrowed += s.Borrowed; pending += s.PendingRequests;
                bytes += (long)adapters[i].Live.Count * pools[i].Config.EstimatedBytesPerInstance;
            }
            int scopeBorrowed = 0, scopePending = 0;
            foreach (var scope in service.Scopes) { scopeBorrowed += scope.BorrowedCount; scopePending += scope.PendingCount; }
            Assert.AreEqual(resident, service.ResidentSlots); Assert.AreEqual(bytes, service.EstimatedResidentBytes);
            Assert.AreEqual(borrowed, scopeBorrowed); Assert.AreEqual(pending, scopePending);
            Assert.LessOrEqual(resident, service.MaxResidentSlots); Assert.LessOrEqual(bytes, service.MaxEstimatedResidentBytes);
            Assert.LessOrEqual(service.Scheduler.CreatesThisFrame, 3);
            Assert.LessOrEqual(service.Scheduler.DestroyRequestsThisFrame, 2);
            Assert.LessOrEqual(service.Scheduler.MaintenanceStepsThisFrame, 96);
        }

        /// <summary>固定随机种子交错借还、取消、作用域关闭、预热和故障重试；每个种子执行三万次并验证最终完全排空。</summary>
        [TestCase(1)] [TestCase(7)] [TestCase(42)] [TestCase(101)] [TestCase(2026)] [TestCase(65537)]
        public void SeededMixedOperationsDrainEveryInstance(int seed)
        {
            var clock = new Clock(); var random = new Random(seed);
            var service = new PoolService(new PoolFrameBudget { MaxCreatesPerFrame = 3, MaxDestroyRequestsPerFrame = 2, MaxMaintenanceStepsPerFrame = 96, MaxWorkMilliseconds = 100 }, clock, 96, 10000);
            var pools = new DataPool<Box>[4]; var adapters = new Adapter[4]; var scopes = new PoolScope[6];
            var held = new List<PoolLease<Box>>(); var stale = new List<PoolLease<Box>>();
            var pending = new List<Pending>(); var warms = new List<Task<WarmResult>>();
            var outcomes = new int[Enum.GetValues(typeof(PoolStatus)).Length];
            for (int i = 0; i < pools.Length; i++)
            {
                adapters[i] = new Adapter { InjectFaults = true };
                pools[i] = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(i.ToString()), Config(i), adapters[i]));
            }
            for (int i = 0; i < scopes.Length; i++) scopes[i] = service.CreateScope("region-" + i);
            int operation = 0;
            try
            {
                for (; operation < 30000; operation++)
                {
                    var pool = pools[random.Next(pools.Length)]; int scopeIndex = random.Next(scopes.Length); var scope = scopes[scopeIndex];
                    switch (random.Next(12))
                    {
                        case 0:
                        case 1:
                            var result = pool.RentOrCreate(scope); outcomes[(int)result.Status]++;
                            if (result.Succeeded) held.Add(result.Lease);
                            break;
                        case 2:
                            if (pool.TryRentCached(scope, out var cached)) held.Add(cached);
                            break;
                        case 3:
                        case 4:
                            var cancel = new CancellationTokenSource();
                            pending.Add(new Pending { Cancel = cancel, Task = pool.RentAsync(scope, new RentOptions(random.Next(-5, 6), 0.005 * random.Next(1, 6)), cancel.Token) });
                            break;
                        case 5:
                            if (held.Count > 0)
                            {
                                int index = random.Next(held.Count); var lease = held[index];
                                if (lease.TryReturn()) stale.Add(lease);
                                Assert.IsFalse(lease.TryReturn()); held.RemoveAt(index);
                            }
                            break;
                        case 6:
                            if (pending.Count > 0) pending[random.Next(pending.Count)].Cancel.Cancel();
                            break;
                        case 7:
                            scope.CloseAsync(); scopes[scopeIndex] = service.CreateScope("region-" + operation);
                            break;
                        case 8:
                            warms.Add(pool.PrewarmAsync(scope, random.Next(1, 13), new WarmOptions(0.15)));
                            break;
                        case 9:
                            service.RequestMemoryPressure(random.Next(0, 8000));
                            break;
                        case 10:
                            if (held.Count > 0 && held[random.Next(held.Count)].TryGet(out var invalidated)) invalidated.Valid = false;
                            break;
                        case 11:
                            warms.AddRange(service.SubmitWarmPlan(scope, "nearby", new Dictionary<PoolKey, int> { [pool.Key] = random.Next(1, 10) }, new WarmOptions(0.15)));
                            break;
                    }
                    if (stale.Count > 0)
                    {
                        var old = stale[random.Next(stale.Count)]; Assert.IsFalse(old.TryGet(out _)); Assert.IsFalse(old.TryReturn());
                        if (stale.Count > 128) stale.RemoveAt(0);
                    }
                    clock.Time += 0.005;
                    if (operation % 2 == 0) service.Tick();
                    Collect(pending, held, outcomes);
                    if (operation % 32 == 0)
                    {
                        Audit(service, pools, adapters);
                        foreach (var item in pools) item.RetryFailedReleases();
                        for (int i = held.Count - 1; i >= 0; i--) if (!held[i].TryGet(out _)) { held[i].TryReturn(); held.RemoveAt(i); }
                        for (int i = warms.Count - 1; i >= 0; i--) if (warms[i].IsCompleted) { Assert.IsFalse(warms[i].IsFaulted); warms.RemoveAt(i); }
                    }
                }
                for (int i = 0; i < pools.Length; i++) { adapters[i].InjectFaults = false; pools[i].RetryFailedReleases(); }
                Pump(service, clock, service.CloseAsync()); Collect(pending, held, outcomes); Audit(service, pools, adapters);
                Assert.AreEqual(0, pending.Count); Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, service.EstimatedResidentBytes);
                Assert.AreEqual(0, service.Scopes.Count);
                foreach (var task in warms) Assert.IsTrue(task.IsCompleted && !task.IsFaulted);
                foreach (var lease in held) { Assert.IsFalse(lease.TryGet(out _)); Assert.IsFalse(lease.TryReturn()); }
                var totalFaults = new int[5]; int created = 0;
                foreach (var adapter in adapters)
                {
                    Assert.AreEqual(0, adapter.Live.Count); Assert.AreEqual(1, adapter.Released); created += adapter.Created;
                    for (int i = 0; i < totalFaults.Length; i++) totalFaults[i] += adapter.Faults[i];
                }
                foreach (int faults in totalFaults) Assert.Greater(faults, 0, "Each fault phase must actually be exercised.");
                TestContext.WriteLine("seed=" + seed + " operations=30000 created=" + created + " faults(create,init,reset,destroy,confirm)=" + string.Join(",", totalFaults) + " outcomes=" + string.Join(",", outcomes));
                Assert.Greater(outcomes[(int)PoolStatus.Cancelled], 0, "Cancellation path was not exercised.");
                Assert.Greater(outcomes[(int)PoolStatus.TimedOut], 0, "Timeout path was not exercised.");
            }
            catch (Exception ex) { throw new Exception("Reproduce with seed=" + seed + " operation=" + operation, ex); }
            finally { foreach (var request in pending) request.Cancel.Dispose(); }
        }

        /// <summary>连续一百轮打满全局容量和四个请求队列，核对拒绝、取消、超时及关闭后的配额恢复。</summary>
        [Test] public void HundredSaturatedQueueCyclesRecoverAllCapacity()
        {
            var clock = new Clock();
            var service = new PoolService(new PoolFrameBudget { MaxCreatesPerFrame = 3, MaxDestroyRequestsPerFrame = 2, MaxMaintenanceStepsPerFrame = 96, MaxWorkMilliseconds = 100 }, clock, 10, 1000);
            var pools = new DataPool<Box>[4]; var adapters = new Adapter[4];
            var config = new PoolSettings { MaxIdle = 0, MaxBorrowed = 4, MaxResident = 4, MaxPendingRequests = 8, EstimatedBytesPerInstance = 100 }.Freeze();
            for (int i = 0; i < pools.Length; i++)
            {
                adapters[i] = new Adapter();
                pools[i] = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(i.ToString()), config, adapters[i]));
            }
            var previous = default(PoolLease<Box>);
            for (int round = 0; round < 100; round++)
            {
                var scope = service.CreateScope("saturation-" + round);
                for (int i = 0; i < 10; i++)
                {
                    var result = pools[i / 4].RentOrCreate(scope); Assert.IsTrue(result.Succeeded);
                    Assert.IsFalse(previous.TryGet(out _)); Assert.IsFalse(previous.TryReturn());
                    if (i == 9) previous = result.Lease;
                }
                Assert.AreEqual(PoolStatus.CapacityExceeded, pools[0].RentOrCreate(scope).Status);
                Assert.AreEqual(PoolStatus.GlobalBudgetExceeded, pools[2].RentOrCreate(scope).Status);
                Assert.AreEqual(10, service.ResidentSlots); Assert.AreEqual(1000, service.EstimatedResidentBytes);
                using (var cancel = new CancellationTokenSource())
                {
                    var requests = new List<Task<RentResult<Box>>>();
                    foreach (var pool in pools)
                    {
                        for (int i = 0; i < 12; i++) requests.Add(pool.RentAsync(scope, new RentOptions(0, 0.05), i < 4 ? cancel.Token : default));
                        Assert.AreEqual(8, pool.GetSnapshot().PendingRequests);
                    }
                    Audit(service, pools, adapters); cancel.Cancel();
                    Pump(service, clock, Task.WhenAll(requests));
                    for (int i = 0; i < requests.Count; i++)
                    {
                        var expected = i % 12 < 4 ? PoolStatus.Cancelled : i % 12 < 8 ? PoolStatus.TimedOut : PoolStatus.QueueFull;
                        Assert.AreEqual(expected, requests[i].Result.Status, "round=" + round + " request=" + i);
                    }
                }
                Assert.AreEqual(10, scope.BorrowedCount); Assert.AreEqual(0, scope.PendingCount);
                Pump(service, clock, scope.CloseAsync()); Audit(service, pools, adapters);
                Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, service.EstimatedResidentBytes);
                Assert.AreEqual(1, service.Scopes.Count);
                foreach (var pool in pools) Assert.LessOrEqual(pool.GetSnapshot().MetadataSlots, 4);
            }
            Pump(service, clock, service.CloseAsync());
            foreach (var adapter in adapters) { Assert.AreEqual(0, adapter.Live.Count); Assert.AreEqual(1, adapter.Released); }
            TestContext.WriteLine("saturation_cycles=100 successful_rents=1000 queued_requests=3200 queue_full=1600 cancelled=1600 timed_out=1600");
        }

        /// <summary>每帧仅允许一个维护步骤时，验证六十四个池仍能轮流完成预热与关闭，不发生饥饿。</summary>
        [Test] public void SixtyFourPoolsProgressWithOneMaintenanceStepPerFrame()
        {
            var clock = new Clock(); var service = new PoolService(new PoolFrameBudget { MaxCreatesPerFrame = 1, MaxDestroyRequestsPerFrame = 1, MaxMaintenanceStepsPerFrame = 1, MaxWorkMilliseconds = 100 }, clock);
            var adapters = new List<Adapter>(); var tasks = new List<Task<WarmResult>>();
            for (int i = 0; i < 64; i++)
            {
                var adapter = new Adapter(); adapters.Add(adapter);
                var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(i.ToString()), Config(), adapter));
                tasks.Add(pool.PrewarmAsync(service.RootScope, 2, new WarmOptions(1000)));
            }
            Pump(service, clock, Task.WhenAll(tasks));
            foreach (var task in tasks) Assert.AreEqual(WarmEndReason.ReachedTarget, task.Result.Reason);
            Assert.AreEqual(128, service.ResidentSlots);
            var close = service.CloseAsync();
            for (int i = 0; i < 30000 && !close.IsCompleted; i++)
            {
                clock.Time += 0.01; service.Tick();
                Assert.LessOrEqual(service.Scheduler.MaintenanceStepsThisFrame, 1);
                Assert.LessOrEqual(service.Scheduler.CreatesThisFrame, 1); Assert.LessOrEqual(service.Scheduler.DestroyRequestsThisFrame, 1);
            }
            Assert.IsTrue(close.IsCompleted && !close.IsFaulted); Assert.AreEqual(0, service.ResidentSlots);
            foreach (var adapter in adapters) { Assert.AreEqual(0, adapter.Live.Count); Assert.AreEqual(1, adapter.Released); Assert.AreEqual(0, adapter.Violations); }
            TestContext.WriteLine("pools=64 instances=128 total_ticks=" + service.Scheduler.Frame);
        }
    }
}

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BigWorld.Pooling.Unity;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;
using Object = UnityEngine.Object;

namespace BigWorld.Pooling.Tests
{
    /// <summary>记录固定负载的耗时与托管分配量，不使用与机器性能相关的硬性通过阈值。</summary>
    [Category("PoolingPerformance")]
    public sealed class PoolPerformanceTests
    {
        private sealed class Box { internal int Value; }
        private sealed class Adapter : PoolAdapter<Box>
        {
            internal int Created;
            /// <summary>创建最小业务对象，仅用于隔离核心借还开销。</summary>
            public override Box Create() { Created++; return new Box(); }
            /// <summary>清空单个整数字段，保持回调没有主动分配。</summary>
            public override void Reset(Box value) { value.Value = 0; }
            /// <summary>普通对象没有外部资源，销毁时仅由内核解除引用。</summary>
            public override void RequestDestroy(Box value) { }
        }
        private struct RunInfo { internal int Ticks; internal double MaxTickMs; }
        private static bool legacyAllocationCounterWorks;
        private static bool recorderMeasuresBytes;
        [Serializable] private sealed class Summary
        {
            public string name;
            public int samples, unitsPerSample, gen0Collections;
            public double medianUsPerUnit, p95UsPerUnit, maxUsPerUnit, allocatedBytesPerUnit, allocationCallsPerUnit;
            public long totalAllocatedBytes, totalAllocationCalls;
            public double medianTicksPerSample, p95TicksPerSample, maxTickMs;
        }

        /// <summary>输出实际运行环境，避免将编辑器或开发构建的测量误认为发布版本结果。</summary>
        [OneTimeSetUp] public void DescribeEnvironment()
        {
            TestContext.WriteLine("POOL_PERF_ENV unity=" + Application.unityVersion + " editor=" + Application.isEditor +
                " development=" + Debug.isDebugBuild + " platform=" + Application.platform + " cpu=" + SystemInfo.processorType +
                " logical_cores=" + SystemInfo.processorCount + " memory_mb=" + SystemInfo.systemMemorySize);
            CalibrateAllocationCounters();
        }

        /// <summary>用保留在数组中的一百个 1 KB 数组校准计数器；不把运行时无效接口返回的零误判为零分配。</summary>
        private static void CalibrateAllocationCounters()
        {
            var retained = new byte[100][];
            using (var recorder = new ProfilerRecorder(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                Assert.IsTrue(recorder.Valid, "Unity GC.Alloc recorder is unavailable.");
                recorder.Start(); long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < retained.Length; i++) retained[i] = new byte[1024];
                long delta = GC.GetAllocatedBytesForCurrentThread() - before; recorder.Stop();
                long calls = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                long value = recorder.Count == 0 ? 0 : recorder.GetSample(0).Value;
                legacyAllocationCounterWorks = delta >= 102400;
                recorderMeasuresBytes = recorder.UnitType.ToString() == "Bytes" && value >= 102400;
                TestContext.WriteLine("POOL_PERF_ALLOCATION_CALIBRATION arrays=100 payload_bytes=102400 legacy_bytes=" + delta +
                    " recorder_calls=" + calls + " recorder_value=" + value + " recorder_unit=" + recorder.UnitType +
                    " legacy_valid=" + legacyAllocationCounterWorks + " recorder_bytes_valid=" + recorderMeasuresBytes);
                Assert.GreaterOrEqual(calls, 100, "GC.Alloc calibration must observe known allocations.");
                GC.KeepAlive(retained);
            }
        }

        /// <summary>生成固定容量配置，避免过期淘汰及短期限干扰性能测量。</summary>
        private static PoolConfig Config(int capacity, int pending = 64) => new PoolSettings
        {
            InitialStorageCapacity = capacity, MaxIdle = capacity, MaxBorrowed = capacity, MaxResident = capacity,
            MaxPendingRequests = pending, IdleTimeoutSeconds = 0, RequestTimeoutSeconds = 120, EstimatedBytesPerInstance = 32
        }.Freeze();

        /// <summary>同步建立指定数量的独立缓存实例，准备工作不计入被测区间。</summary>
        private static void Fill(DataPool<Box> pool, PoolScope scope, int count)
        {
            var leases = new PoolLease<Box>[count];
            for (int i = 0; i < count; i++)
            {
                var result = pool.RentOrCreate(scope);
                if (!result.Succeeded) throw new InvalidOperationException("Setup rent failed: " + result.Status);
                leases[i] = result.Lease;
            }
            foreach (var lease in leases) lease.Dispose();
        }

        /// <summary>驱动有限次数直到任务结束，记录真实 Tick 总数及最大单次 Tick 耗时。</summary>
        private static RunInfo Drain(PoolService service, Task task)
        {
            var info = new RunInfo();
            while (!task.IsCompleted && info.Ticks < 100000)
            {
                long start = Stopwatch.GetTimestamp(); service.Tick();
                double elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                info.MaxTickMs = Math.Max(info.MaxTickMs, elapsed); info.Ticks++;
            }
            if (!task.IsCompleted || task.IsFaulted) throw new InvalidOperationException("Measured task did not drain.", task.Exception);
            return info;
        }

        /// <summary>以最近秩方法取得已排序样本的分位数；批量操作的样本代表整批的平均单次开销。</summary>
        private static double Percentile(double[] sorted, double fraction) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];

        /// <summary>预跑后分别重复计时和分配采样，避免 GC.Alloc 记录器影响耗时；保存原始 CSV 与摘要。</summary>
        private static void Measure(string name, int unitsPerSample, int sampleCount, Func<RunInfo> action, Action prepare = null)
        {
            prepare?.Invoke(); action();
            var microseconds = new double[sampleCount]; var allocations = new long[sampleCount];
            var allocationCalls = new long[sampleCount];
            var tickCounts = new double[sampleCount]; var maxTickTimes = new double[sampleCount];
            long totalBytes = 0, totalCalls = 0; int gcDelta = 0;
            using (var recorder = new ProfilerRecorder(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    prepare?.Invoke();
                    int gcBefore = GC.CollectionCount(0); long started = Stopwatch.GetTimestamp();
                    var info = action(); long elapsed = Stopwatch.GetTimestamp() - started;
                    gcDelta += GC.CollectionCount(0) - gcBefore;
                    microseconds[i] = elapsed * 1000000.0 / Stopwatch.Frequency / unitsPerSample;
                    tickCounts[i] = info.Ticks; maxTickTimes[i] = info.MaxTickMs;
                    prepare?.Invoke(); recorder.Reset(); recorder.Start();
                    long bytesBefore = GC.GetAllocatedBytesForCurrentThread(); action();
                    long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore; recorder.Stop();
                    allocationCalls[i] = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                    allocations[i] = recorderMeasuresBytes ? (recorder.Count == 0 ? 0 : recorder.GetSample(0).Value) : legacyAllocationCounterWorks ? legacyBytes : -1;
                    totalBytes += allocations[i]; totalCalls += allocationCalls[i];
                }
            }
            var csv = new StringBuilder("sample,units,us_per_unit,allocated_bytes,allocation_calls,ticks,max_tick_ms\n");
            for (int i = 0; i < sampleCount; i++) csv.AppendFormat(CultureInfo.InvariantCulture,
                "{0},{1},{2:R},{3},{4},{5},{6:R}\n", i, unitsPerSample, microseconds[i], allocations[i], allocationCalls[i], tickCounts[i], maxTickTimes[i]);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../PerformanceSamples"));
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".csv"), csv.ToString());
            Array.Sort(microseconds); Array.Sort(tickCounts); Array.Sort(maxTickTimes);
            var summary = new Summary
            {
                name = name, samples = sampleCount, unitsPerSample = unitsPerSample, gen0Collections = gcDelta,
                medianUsPerUnit = Percentile(microseconds, 0.5), p95UsPerUnit = Percentile(microseconds, 0.95), maxUsPerUnit = microseconds[sampleCount - 1],
                allocatedBytesPerUnit = recorderMeasuresBytes || legacyAllocationCounterWorks ? (double)totalBytes / sampleCount / unitsPerSample : -1,
                totalAllocatedBytes = recorderMeasuresBytes || legacyAllocationCounterWorks ? totalBytes : -1,
                allocationCallsPerUnit = (double)totalCalls / sampleCount / unitsPerSample, totalAllocationCalls = totalCalls,
                medianTicksPerSample = Percentile(tickCounts, 0.5), p95TicksPerSample = Percentile(tickCounts, 0.95), maxTickMs = maxTickTimes[sampleCount - 1]
            };
            TestContext.WriteLine("POOL_PERF " + JsonUtility.ToJson(summary));
        }

        /// <summary>测量稳定容量下的普通对象缓存借还，排除首次创建与诊断快照开销。</summary>
        [Test] public void CachedDataRentReturn()
        {
            var service = new PoolService(); var adapter = new Adapter();
            var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(), Config(1), adapter)); Fill(pool, service.RootScope, 1);
            Measure("data_cached_pair", 50000, 30, () =>
            {
                for (int i = 0; i < 50000; i++)
                {
                    if (!pool.TryRentCached(service.RootScope, out var lease)) throw new InvalidOperationException("Cache miss.");
                    lease.Value.Value = i;
                    if (!lease.TryReturn()) throw new InvalidOperationException("Return failed.");
                }
                return default;
            });
            Assert.AreEqual(1, adapter.Created); Assert.AreEqual(1, pool.GetSnapshot().Idle);
            Drain(service, service.CloseAsync()); Assert.AreEqual(0, service.ResidentSlots);
        }

        /// <summary>测量不同数量的空闲池在默认帧预算下每次 Tick 的实际开销及线程分配。</summary>
        [TestCase(1)] [TestCase(64)] [TestCase(256)] public void IdleScheduler(int poolCount)
        {
            var service = new PoolService();
            for (int i = 0; i < poolCount; i++)
            {
                var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(i.ToString()), Config(64), new Adapter()));
                Fill(pool, service.RootScope, 64);
            }
            for (int i = 0; i < 200; i++) service.Tick();
            Measure("idle_tick_" + poolCount + "_pools", 1, 4000, () => { service.Tick(); return new RunInfo { Ticks = 1, MaxTickMs = service.Scheduler.WorkMilliseconds }; });
            Assert.AreEqual(poolCount * 64, service.ResidentSlots);
            Drain(service, service.CloseAsync()); Assert.AreEqual(0, service.ResidentSlots);
        }

        /// <summary>测量已预热缓存下的异步批量请求，包括入队、调度完成和归还；同时记录需要跨越的调度次数。</summary>
        [TestCase(64)] [TestCase(1024)] public void AsyncCachedBursts(int count)
        {
            var service = new PoolService(); var adapter = new Adapter();
            var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(), Config(count, count), adapter));
            Fill(pool, service.RootScope, count); var tasks = new Task<RentResult<Box>>[count];
            Measure("async_cached_burst_" + count, count, 30, () =>
            {
                for (int i = 0; i < count; i++) tasks[i] = pool.RentAsync(service.RootScope);
                var info = new RunInfo(); int completed = 0;
                while (completed < count && info.Ticks < 100000)
                {
                    long started = Stopwatch.GetTimestamp(); service.Tick();
                    info.MaxTickMs = Math.Max(info.MaxTickMs, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); info.Ticks++;
                    while (completed < count && tasks[completed].IsCompleted)
                    {
                        var result = tasks[completed++].Result;
                        if (!result.Succeeded || !result.Lease.TryReturn()) throw new InvalidOperationException("Async request failed: " + result.Status);
                    }
                }
                if (completed != count) throw new InvalidOperationException("Async burst did not complete.");
                return info;
            });
            Assert.AreEqual(count, adapter.Created); Assert.AreEqual(0, pool.GetSnapshot().PendingRequests);
            Drain(service, service.CloseAsync()); Assert.AreEqual(0, service.ResidentSlots);
        }

        /// <summary>测量已有大量借用的作用域关闭，准备借用不计时，归还缓存与分帧调度计入关闭成本。</summary>
        [TestCase(1000)] [TestCase(10000)] public void ScopeCloseWithOutstandingBorrows(int count)
        {
            var service = new PoolService(); var adapter = new Adapter();
            var pool = service.Register(new DataPool<Box>(service, PoolKey.ForData<Box>(), Config(count), adapter));
            Fill(pool, service.RootScope, count); PoolScope scope = null;
            Measure("scope_close_" + count, 1, 15, () => Drain(service, scope.CloseAsync()), () =>
            {
                scope = service.CreateScope("measured-region");
                for (int i = 0; i < count; i++) if (!pool.TryRentCached(scope, out _)) throw new InvalidOperationException("Setup cache miss.");
            });
            Assert.AreEqual(count, adapter.Created); Assert.AreEqual(0, pool.GetSnapshot().Borrowed);
            Drain(service, service.CloseAsync()); Assert.AreEqual(0, service.ResidentSlots);
        }

        /// <summary>在真实 Unity 生命周期下测量轻量预制体缓存生成与归还，包含参数准备、激活、停用和跨场景层级迁移。</summary>
        [UnityTest] public IEnumerator CachedPrefabSpawnReturn()
        {
            PoolProbe.Clear(); SecondReleaseProbe.Released = 0;
            var host = new GameObject("Performance template parent"); host.SetActive(false);
            var prefab = new GameObject("Performance prefab"); prefab.transform.SetParent(host.transform, false);
            prefab.AddComponent<PoolProbe>(); prefab.AddComponent<SecondReleaseProbe>();
            var cache = new GameObject("Performance persistent cache"); cache.SetActive(false); Object.DontDestroyOnLoad(cache);
            var service = new PoolService(); var source = new SharedPrefabResource(prefab);
            var pool = service.Register(new PrefabPool<int>(service, new PoolKey(PoolKind.Prefab, "performance"), Config(1), source.Acquire(), cache.transform, true));
            var first = pool.SpawnOrCreate(service.RootScope, default, 1); Assert.IsTrue(first.Succeeded); first.Lease.Dispose();
            yield return null;
            Measure("prefab_cached_pair", 2000, 30, () =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    if (!pool.TrySpawnCached(service.RootScope, default, i, out var lease) || !lease.TryReturn())
                        throw new InvalidOperationException("Prefab cache cycle failed.");
                }
                return default;
            });
            Assert.AreEqual(1, PoolProbe.Initialized); Assert.AreEqual(1, cache.transform.childCount);
            var close = service.CloseAsync();
            for (int i = 0; i < 120 && !close.IsCompleted; i++) { service.Tick(); yield return null; }
            Assert.IsTrue(close.IsCompleted && !close.IsFaulted); Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, source.ReferenceCount);
            Object.Destroy(host); Object.Destroy(cache); yield return null;
        }
    }
}

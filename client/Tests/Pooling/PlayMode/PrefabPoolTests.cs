using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using BigWorld.Pooling.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BigWorld.Pooling.Tests
{
    public sealed class PrefabPoolTests
    {
        private GameObject host, prefab, cache;
        private PoolService service;
        private PoolScope scope;
        private SharedPrefabResource source;
        private PrefabPool<int> pool;
        /// <summary>
        /// 生成预制体测试的固定借用与驻留容量配置，并允许调整空闲上限。
        /// </summary>
        private PoolSettings Settings(int maxIdle = 4) => new PoolSettings { MaxIdle = maxIdle, MaxBorrowed = 4, MaxResident = 8, IdleTimeoutSeconds = 0 };
        /// <summary>
        /// 创建未激活模板、常驻缓存节点及手动驱动的服务，注册带计数探针的预制体池。
        /// </summary>
        [UnitySetUp] public IEnumerator SetUp()
        {
            PoolProbe.Clear(); SecondReleaseProbe.Released = 0;
            host = new GameObject("Test template parent"); host.SetActive(false);
            prefab = new GameObject("Pooled probe"); prefab.transform.SetParent(host.transform, false); prefab.AddComponent<PoolProbe>(); prefab.AddComponent<SecondReleaseProbe>();
            cache = new GameObject("Persistent test cache"); cache.SetActive(false); Object.DontDestroyOnLoad(cache);
            service = new PoolService(new PoolFrameBudget { MaxCreatesPerFrame = 2, MaxDestroyRequestsPerFrame = 2, MaxMaintenanceStepsPerFrame = 60, MaxWorkMilliseconds = 100 });
            scope = service.CreateScope("region"); source = new SharedPrefabResource(prefab);
            pool = service.Register(new PrefabPool<int>(service, new PoolKey(PoolKind.Prefab, "test.prefab"), Settings().Freeze(), source.Acquire(), cache.transform, true));
            yield return null;
        }
        /// <summary>
        /// 恢复探针状态并等待服务排空，然后销毁测试模板和缓存，避免跨测试残留。
        /// </summary>
        [UnityTearDown] public IEnumerator TearDown()
        {
            PoolProbe.FailReset = false; PoolProbe.Preparing = null;
            if (service != null && service.State != PoolState.Closed)
            {
                var close = service.CloseAsync();
                for (int i = 0; i < 120 && !close.IsCompleted; i++) { service.Tick(); yield return null; }
                Assert.IsTrue(close.IsCompleted && !close.IsFaulted, "Test cleanup failed.");
            }
            if (host != null) Object.Destroy(host); if (cache != null) Object.Destroy(cache);
            yield return null;
        }
        /// <summary>
        /// 每个真实 Unity 帧推进一次服务，在限定帧数内等待任务结束并检查其未发生异常。
        /// </summary>
        private IEnumerator Pump(Task task, int frames = 120)
        {
            for (int i = 0; i < frames && !task.IsCompleted; i++) { service.Tick(); yield return null; }
            Assert.IsTrue(task.IsCompleted, "Pool operation timed out in test."); Assert.IsFalse(task.IsFaulted);
        }
        /// <summary>
        /// 验证真实 GameObject 预热只执行基础初始化，创建多个不同实例且不激活或执行本次玩法准备。
        /// </summary>
        [UnityTest] public IEnumerator PrewarmDoesNotActivateGameplayAndProducesDifferentInstances()
        {
            var warm = pool.PrewarmAsync(scope, 3); yield return Pump(warm);
            Assert.AreEqual(3, warm.Result.Created); Assert.AreEqual(3, PoolProbe.Initialized); Assert.AreEqual(0, PoolProbe.Enabled); Assert.AreEqual(0, PoolProbe.Prepared);
            Assert.AreEqual(3, cache.transform.childCount);
        }
        /// <summary>
        /// 验证业务参数、父节点和 Transform 在实例激活前已按请求设置完成。
        /// </summary>
        [UnityTest] public IEnumerator TypedArgumentsAndTransformsAreReadyBeforeOnEnable()
        {
            var parent = new GameObject("Spawn parent"); parent.transform.SetParent(host.transform, false); parent.transform.SetParent(null);
            var request = pool.SpawnAsync(scope, new SpawnTransform(parent.transform, new Vector3(1, 2, 3), Quaternion.identity, Vector3.one * 2), 42);
            yield return Pump(request); Assert.IsTrue(request.Result.Succeeded); var value = request.Result.Lease.Value;
            Assert.AreEqual(42, PoolProbe.ArgsAtEnable); Assert.AreEqual(new Vector3(1, 2, 3), value.transform.localPosition); Assert.AreEqual(Vector3.one * 2, value.transform.localScale);
            Assert.AreEqual(parent.transform, value.transform.parent); request.Result.Lease.Dispose(); Object.Destroy(parent);
        }
        /// <summary>
        /// 验证归还会停用并清理真实实例，复用不会重复基础初始化，旧凭证也不能回收新借用。
        /// </summary>
        [UnityTest] public IEnumerator ReturnAndReuseInvalidateOldLeaseAndResetState()
        {
            var old = pool.SpawnOrCreate(scope, default, 9).Lease; var value = old.Value; old.Dispose();
            Assert.IsFalse(value.activeSelf); Assert.AreEqual(0, value.GetComponent<PoolProbe>().Args); Assert.AreEqual(cache.transform, value.transform.parent);
            Assert.IsTrue(pool.TrySpawnCached(scope, default, 17, out var current)); Assert.AreSame(value, current.Value);
            Assert.AreEqual(1, PoolProbe.Initialized); Assert.AreEqual(17, PoolProbe.ArgsAtEnable); Assert.IsFalse(old.TryReturn()); current.Dispose(); yield return null;
        }
        /// <summary>
        /// 验证 Destroy 发出当帧仍占用驻留和资源引用，只有后续帧确认对象失效后才完成关闭。
        /// </summary>
        [UnityTest] public IEnumerator DestroyIsChargedUntilALaterFrameConfirmsIt()
        {
            var lease = pool.SpawnOrCreate(scope, default, 0).Lease; var value = lease.Value; var close = pool.CloseAsync(); lease.Dispose(); service.Tick();
            Assert.AreEqual(1, pool.GetSnapshot().ResidentSlots); Assert.IsFalse(close.IsCompleted); Assert.AreEqual(1, source.ReferenceCount);
            yield return null; yield return Pump(close); Assert.IsTrue(value == null); Assert.AreEqual(0, source.ReferenceCount); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证类型化准备回调发出取消时，实例不会进入 OnEnable 或交付给业务。
        /// </summary>
        [UnityTest] public IEnumerator CancellationDuringTypedPreparationNeverEnablesOrDelivers()
        {
            var cancel = new CancellationTokenSource(); PoolProbe.Preparing = () => cancel.Cancel();
            var request = pool.SpawnAsync(scope, default, 2, cancellationToken: cancel.Token); yield return Pump(request);
            Assert.AreEqual(PoolStatus.Cancelled, request.Result.Status); Assert.AreEqual(0, PoolProbe.Enabled);
            var close = scope.CloseAsync(); yield return Pump(close); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证一个组件清理抛异常后，其余生命周期组件仍被调用，整实例停用并退役。
        /// </summary>
        [UnityTest] public IEnumerator CleanupExceptionStillCallsOtherComponentsAndDisablesInstance()
        {
            var lease = pool.SpawnOrCreate(scope, default, 5).Lease; var value = lease.Value; PoolProbe.FailReset = true; lease.Dispose();
            Assert.AreEqual(1, SecondReleaseProbe.Released); Assert.IsFalse(value.activeSelf); Assert.AreEqual(0, pool.GetSnapshot().Idle);
            Assert.AreEqual(1, pool.GetSnapshot().PendingDestroy); PoolProbe.FailReset = false; var close = scope.CloseAsync(); yield return Pump(close);
        }
        /// <summary>
        /// 验证真实外部 Destroy 使访问失效，但仍可凭旧借用身份完成归还和计数结算。
        /// </summary>
        [UnityTest] public IEnumerator ExternalDestroyInvalidatesLeaseAndCanStillEndBorrow()
        {
            var lease = pool.SpawnOrCreate(scope, default, 5).Lease; Object.Destroy(lease.Value); yield return null;
            Assert.IsFalse(lease.TryGet(out _)); Assert.IsTrue(lease.TryReturn()); var close = scope.CloseAsync(); yield return Pump(close); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证分区关闭先将实例移回常驻缓存场景，随后卸载分区不会破坏共享缓存与复用。
        /// </summary>
        [UnityTest] public IEnumerator RegionCloseMovesReturnedObjectsBeforeSceneUnload()
        {
            var scene = SceneManager.CreateScene("Pool region test"); var parent = new GameObject("Region root"); SceneManager.MoveGameObjectToScene(parent, scene);
            var lease = pool.SpawnOrCreate(scope, SpawnTransform.Under(parent.transform), 5).Lease; var value = lease.Value;
            Assert.AreEqual(scene, value.scene); var close = scope.CloseAsync(); Assert.IsFalse(lease.TryGet(out _)); yield return Pump(close);
            Assert.AreEqual(cache.scene, value.scene); yield return SceneManager.UnloadSceneAsync(scene); Assert.IsTrue(value != null);
            var next = service.CreateScope("next region"); Assert.IsTrue(pool.TrySpawnCached(next, default, 6, out var reused)); Assert.AreSame(value, reused.Value); reused.Dispose();
        }
        /// <summary>
        /// 验证生成父节点在等待期间被销毁后，请求明确失败，不意外改为无父节点生成。
        /// </summary>
        [UnityTest] public IEnumerator ParentDestroyedWhileRequestWaitsDoesNotSpawnElsewhere()
        {
            var parent = new GameObject("Destroyed parent"); var request = pool.SpawnAsync(scope, SpawnTransform.Under(parent.transform), 7);
            Object.Destroy(parent); yield return null; yield return Pump(request); Assert.AreEqual(PoolStatus.InitializationFailed, request.Result.Status); Assert.AreEqual(0, PoolProbe.Enabled);
        }
        /// <summary>
        /// 验证共享同一预制体资源的两个池分别持有引用，一个池关闭不会使另一个池的借用失效。
        /// </summary>
        [UnityTest] public IEnumerator SharedAssetStaysOwnedUntilBothPoolsClose()
        {
            var other = service.Register(new PrefabPool<int>(service, new PoolKey(PoolKind.Prefab, "test.prefab", "other"), Settings().Freeze(), source.Acquire(), cache.transform, true));
            var first = pool.SpawnOrCreate(scope, default, 1).Lease; var second = other.SpawnOrCreate(scope, default, 2).Lease;
            var close = pool.CloseAsync(); first.Dispose(); yield return Pump(close); Assert.AreEqual(1, source.ReferenceCount); Assert.IsTrue(second.TryGet(out _));
            second.Dispose(); var closeOther = other.CloseAsync(); yield return Pump(closeOther); Assert.AreEqual(0, source.ReferenceCount);
        }
        /// <summary>
        /// 验证缓存根节点损坏时，实例不能假装归还成功，作用域必须等待退役销毁完成。
        /// </summary>
        [UnityTest] public IEnumerator MissingCacheRootMakesScopeWaitForDestroyInsteadOfPretendingToCache()
        {
            var lease = pool.SpawnOrCreate(scope, default, 1).Lease; Object.Destroy(cache); yield return null;
            var close = scope.CloseAsync(); service.Tick(); Assert.IsFalse(close.IsCompleted); Assert.AreEqual(0, pool.GetSnapshot().Idle);
            yield return null; yield return Pump(close); Assert.AreEqual(0, service.ResidentSlots);
        }
        /// <summary>
        /// 验证 Resources 路径不存在时返回加载失败，并且失败后的池仍可正常关闭释放引用。
        /// </summary>
        [UnityTest] public IEnumerator MissingResourcesAssetReturnsLoadFailureAndCanClose()
        {
            var missingSource = new SharedPrefabResource("PoolingTests/DefinitelyMissingPrefab");
            var missing = service.Register(new PrefabPool<int>(service, new PoolKey(PoolKind.Prefab, "missing"), Settings().Freeze(), missingSource.Acquire(), cache.transform));
            var request = missing.SpawnAsync(scope, default, 0); yield return Pump(request); Assert.AreEqual(PoolStatus.LoadFailed, request.Result.Status);
            var close = missing.CloseAsync(); yield return Pump(close); Assert.AreEqual(0, missingSource.ReferenceCount);
        }

        /// <summary>
        /// 连续两百轮满容量借用、排队取消、外部销毁和作用域关闭，检查真实实例与引用最终全部释放。
        /// </summary>
        [UnityTest] public IEnumerator TwoHundredScopeCyclesWithCancellationAndExternalDestroy()
        {
            pool.Reconfigure(new PoolSettings { MaxIdle = 8, MaxBorrowed = 8, MaxResident = 16, IdleTimeoutSeconds = 0 }.Freeze());
            var leases = new PoolLease<GameObject>[8];
            var requests = new Task<RentResult<GameObject>>[8];
            for (int round = 0; round < 200; round++)
            {
                var region = service.CreateScope("stress-region-" + round);
                for (int i = 0; i < leases.Length; i++)
                {
                    var result = pool.SpawnOrCreate(region, default, round * 8 + i);
                    Assert.IsTrue(result.Succeeded); leases[i] = result.Lease;
                    Assert.IsTrue(leases[i].Value.activeSelf);
                    Assert.AreEqual(round * 8 + i, leases[i].Value.GetComponent<PoolProbe>().Args);
                }
                using (var cancel = new CancellationTokenSource())
                {
                    for (int i = 0; i < requests.Length; i++) requests[i] = pool.SpawnAsync(region, default, -1, cancellationToken: cancel.Token);
                    cancel.Cancel(); yield return Pump(Task.WhenAll(requests));
                    foreach (var request in requests) Assert.AreEqual(PoolStatus.Cancelled, request.Result.Status);
                }
                if (round % 5 == 0)
                {
                    Object.Destroy(leases[0].Value); yield return null;
                    Assert.IsFalse(leases[0].TryGet(out _));
                }
                for (int i = 0; i < 4; i++) { Assert.IsTrue(leases[i].TryReturn()); Assert.IsFalse(leases[i].TryReturn()); }
                var close = region.CloseAsync();
                foreach (var lease in leases) Assert.IsFalse(lease.TryGet(out _));
                yield return Pump(close);
                foreach (var lease in leases) Assert.IsFalse(lease.TryReturn());
                var snapshot = pool.GetSnapshot();
                Assert.AreEqual(0, snapshot.Borrowed); Assert.AreEqual(0, snapshot.PendingRequests);
                Assert.AreEqual(0, snapshot.PendingDestroy + snapshot.DestroyIssued + snapshot.DestroyFaulted);
                Assert.AreEqual(snapshot.Idle, cache.transform.childCount);
                Assert.LessOrEqual(snapshot.ResidentSlots, 8); Assert.LessOrEqual(service.Scopes.Count, 2);
                Assert.AreEqual(1, source.ReferenceCount);
                for (int i = 0; i < cache.transform.childCount; i++) Assert.IsFalse(cache.transform.GetChild(i).gameObject.activeSelf);
            }
            yield return Pump(service.CloseAsync());
            Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, service.EstimatedResidentBytes);
            Assert.AreEqual(0, cache.transform.childCount); Assert.AreEqual(0, source.ReferenceCount);
            TestContext.WriteLine("scope_cycles=200 successful_spawns=1600 cancelled_requests=1600 external_destroys=40 initialized=" + PoolProbe.Initialized);
        }

        /// <summary>
        /// 三十轮创建并卸载分区场景，验证四个缓存实例始终可复用，场景迁移不丢对象、不增加初始化次数。
        /// </summary>
        [UnityTest] public IEnumerator ThirtySceneUnloadCyclesKeepTheSameCachedInstances()
        {
            var values = new GameObject[4]; var identities = new System.Collections.Generic.HashSet<int>();
            for (int round = 0; round < 30; round++)
            {
                var scene = SceneManager.CreateScene("Pool stress scene " + round);
                var parent = new GameObject("Region root"); SceneManager.MoveGameObjectToScene(parent, scene);
                var region = service.CreateScope("scene-" + round);
                for (int i = 0; i < values.Length; i++)
                {
                    var result = pool.SpawnOrCreate(region, SpawnTransform.Under(parent.transform), i);
                    Assert.IsTrue(result.Succeeded); values[i] = result.Lease.Value;
                    Assert.AreEqual(scene, values[i].scene);
                    if (round == 0) identities.Add(values[i].GetInstanceID());
                    else Assert.IsTrue(identities.Contains(values[i].GetInstanceID()));
                }
                yield return Pump(region.CloseAsync());
                foreach (var value in values) { Assert.AreEqual(cache.scene, value.scene); Assert.IsFalse(value.activeSelf); }
                yield return SceneManager.UnloadSceneAsync(scene);
                Assert.IsFalse(scene.isLoaded);
                foreach (var value in values) Assert.IsTrue(value != null);
                Assert.AreEqual(4, pool.GetSnapshot().Idle); Assert.AreEqual(4, PoolProbe.Initialized);
                Assert.AreEqual(4, cache.transform.childCount); Assert.LessOrEqual(service.Scopes.Count, 2);
            }
            yield return Pump(service.CloseAsync());
            Assert.AreEqual(0, service.ResidentSlots); Assert.AreEqual(0, source.ReferenceCount);
            foreach (var value in values) Assert.IsTrue(value == null);
            TestContext.WriteLine("scene_unload_cycles=30 successful_spawns=120 initialized=" + PoolProbe.Initialized);
        }
    }
}

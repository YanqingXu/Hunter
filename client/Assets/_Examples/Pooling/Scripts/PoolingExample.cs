using System;
using System.Collections;
using System.Threading.Tasks;
using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Pooling.Samples
{
    public sealed class PoolingExample : MonoBehaviour
    {
        [SerializeField] private PoolDriver driver;
        [SerializeField] private GameObject actorPrefab;
        private PoolScope scope;
        private PrefabPool<ActorSpawnArgs> pool;
        /// <summary>
        /// 演示创建分区作用域、注册类型化预制体池、预热和异步生成，并在交付后再次校验凭证。
        /// </summary>
        private async void Start()
        {
            try
            {
                scope = driver.Service.CreateScope("Example region");
                var source = new SharedPrefabResource(actorPrefab);
                pool = driver.Service.Register(new PrefabPool<ActorSpawnArgs>(driver.Service,
                    new PoolKey(PoolKind.Prefab, "sample.actor"), new PoolSettings { MaxIdle = 8, MaxBorrowed = 16, MaxResident = 24 }.Freeze(), source.Acquire(), driver.CacheRoot));
                var warm = await pool.PrewarmAsync(scope, 8);
                if (!scope.IsOpen) return;
                Debug.Log($"Warm complete: {warm.Reason}, ready={warm.EndReady}");
                var spawned = await pool.SpawnAsync(scope, SpawnTransform.At(Vector3.zero, Quaternion.identity), new ActorSpawnArgs(1));
                if (spawned.Succeeded && spawned.Lease.TryGet(out var actor))
                {
                    Debug.Log("Spawned " + actor.name);
                    StartCoroutine(ReturnLater(spawned.Lease)); // Capture this borrow's lease, never look up a future borrow by GameObject.
                }
            }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }
        /// <summary>
        /// 等待一秒游戏时间后归还捕获的原借用凭证；即使对象已被重新借出，旧凭证也不能回收新借用。
        /// </summary>
        private static IEnumerator ReturnLater(PoolLease<GameObject> lease)
        {
            yield return new WaitForSeconds(1); lease.TryReturn();
        }
        /// <summary>
        /// 返回示例分区的关闭任务；尚未创建作用域时直接完成，业务应等待它再卸载分区。
        /// </summary>
        public Task CloseRegionAsync() => scope != null ? scope.CloseAsync() : Task.CompletedTask;
        /// <summary>
        /// 示例宿主销毁时尽力发起作用域关闭；此回调不能等待排空，正常卸载应提前调用关闭接口。
        /// </summary>
        private void OnDestroy() { if (scope != null && scope.IsOpen) scope.CloseAsync(); }
    }
}

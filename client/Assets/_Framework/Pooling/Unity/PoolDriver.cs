using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BigWorld.Pooling.Unity
{
    [DefaultExecutionOrder(-2500), DisallowMultipleComponent]
    public sealed class PoolDriver : MonoBehaviour
    {
        [SerializeField] private PoolFrameBudget frameBudget = new PoolFrameBudget();
        [SerializeField] private int maxResidentSlots = 100000;
        [SerializeField] private int maxEstimatedResidentMegabytes = 1024;
        private bool quitting;
        public PoolService Service { get; private set; }
        public Transform CacheRoot { get; private set; }
        /// <summary>
        /// 要求驱动位于场景根节点，建立常驻未激活缓存节点与池服务，并订阅诊断及低内存通知。
        /// </summary>
        private void Awake()
        {
            if (transform.parent != null) throw new InvalidOperationException("PoolDriver must be on a root GameObject.");
            DontDestroyOnLoad(gameObject);
            var cache = new GameObject("Pool Cache (inactive)"); cache.SetActive(false); cache.transform.SetParent(transform, false); CacheRoot = cache.transform;
            Service = new PoolService(frameBudget, maxResidentSlots: maxResidentSlots, maxEstimatedResidentBytes: maxEstimatedResidentMegabytes * 1024L * 1024);
            Service.Diagnostic += OnDiagnostic;
            Application.lowMemory += OnLowMemory;
        }
        /// <summary>
        /// 每帧在 Unity 主线程推进一次池服务，使排队、预热、销毁确认和异步关闭持续进行。
        /// </summary>
        private void Update() { if (Service != null) Service.Tick(); }
        /// <summary>
        /// 收到低内存通知时，将当前估算驻留内存的一半作为渐进回收目标。
        /// </summary>
        private void OnLowMemory() => Service.RequestMemoryPressure(Service.EstimatedResidentBytes / 2);
        /// <summary>
        /// 将池诊断编号、池键、操作名及原始异常写入 Unity Console，便于定位失败请求。
        /// </summary>
        private static void OnDiagnostic(PoolDiagnostic diagnostic) => Debug.LogException(new Exception("Pool diagnostic #" + diagnostic.Id + " [" + diagnostic.Key + "] " + diagnostic.Operation, diagnostic.Exception));
        /// <summary>
        /// 等待作用域、实例、延迟销毁和资源引用全部结算后，再销毁驱动及缓存根节点。
        /// </summary>
        /// <returns>正常排空并发出驱动销毁请求的任务。</returns>
        /// <remarks>等待期间不能禁用或提前销毁驱动，否则逐帧销毁确认无法继续。</remarks>
        public async Task ShutdownAsync()
        {
            await Service.CloseAsync();
            // Keep the driver alive until all scopes, instances, delayed destroys and asset references have settled.
            if (this != null) Destroy(gameObject);
        }
        /// <summary>
        /// 对失败的服务关闭进行明确重试，仅在重试成功后销毁驱动。
        /// </summary>
        /// <returns>修复释放故障后重新尝试关闭的任务。</returns>
        /// <remarks>仅适用于先前关闭已失败的情况，原失败任务仍保留失败结果。</remarks>
        public async Task RetryShutdownAsync()
        {
            await Service.RetryCloseAsync();
            if (this != null) Destroy(gameObject);
        }
        /// <summary>
        /// 记录应用退出状态并尽力发起关闭；退出回调不能证明异步释放已经完成。
        /// </summary>
        private void OnApplicationQuit()
        {
            quitting = true;
            if (Service != null && Service.State == PoolState.Running) Service.CloseAsync();
        }
        /// <summary>
        /// 解除低内存和诊断事件订阅；若正常运行中被提前销毁，则报告尚未排空的生命周期错误。
        /// </summary>
        private void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            if (Service == null) return;
            Service.Diagnostic -= OnDiagnostic;
            if (!quitting && Service.State != PoolState.Closed) Debug.LogError("PoolDriver was destroyed before ShutdownAsync completed. Pool cleanup is incomplete.");
        }
    }
}

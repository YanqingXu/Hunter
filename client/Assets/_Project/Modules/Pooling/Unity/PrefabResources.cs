using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BigWorld.Pooling.Unity
{
    /// <summary>Implement this boundary for Addressables instance handles or a project's resource manager.
    /// CreateInactive must return the allocated instance before any fallible business initialization.</summary>
    public interface IPrefabInstanceProvider
    {
        ResourceState State { get; }
        Exception Error { get; }
        long SharedResourceEstimatedBytes { get; }
        /// <summary>
        /// 启动本提供器依赖的预制体资源加载；共享加载不应因单个借用请求取消而中止。
        /// </summary>
        void BeginLoad();
        /// <summary>
        /// 在主线程更新资源加载状态，使池可以判断资源是否已就绪或失败。
        /// </summary>
        void Poll();
        /// <summary>
        /// 在未激活缓存层级中创建实例并立即返回，业务初始化必须留到池内核登记之后。
        /// </summary>
        /// <param name="cacheRoot">保持未激活的常驻缓存父节点。</param>
        /// <returns>已建立但尚未执行业务准备的独立实例。</returns>
        /// <remarks>不得在创建之后、返回内核登记之前执行可能失败的业务初始化。</remarks>
        GameObject CreateInactive(Transform cacheRoot);
        /// <summary>
        /// 使用本资源系统对应的实例销毁或句柄释放接口发出请求，保持创建与释放路径配套。
        /// </summary>
        void RequestDestroy(GameObject instance);
        /// <summary>
        /// 判断先前发出的释放是否真正完成；只有返回成功后池才能返还驻留配额。
        /// </summary>
        /// <param name="instance">此前请求释放的实例，托管引用可能仍存在但 Unity 原生对象已失效。</param>
        /// <param name="issuedFrame">发出销毁请求时的服务调度帧编号。</param>
        /// <param name="currentFrame">本次确认时的服务调度帧编号。</param>
        /// <returns>是否已满足资源系统有效的释放完成契约。</returns>
        bool IsDestroyComplete(GameObject instance, long issuedFrame, long currentFrame);
        /// <summary>
        /// 释放该池持有的资源所有权；调用时必须已没有未结算实例或加载工作。
        /// </summary>
        void ReleaseResources();
    }

    /// <summary>One source may be shared by several pools/variants. Individual request cancellation never cancels this load.</summary>
    public sealed class SharedPrefabResource
    {
        private GameObject asset;
        private readonly GameObject directPrefab;
        private readonly string resourcesPath;
        private ResourceRequest request;
        private int references;
        public ResourceState State { get; private set; }
        public Exception Error { get; private set; }
        public int ReferenceCount => references;
        public long EstimatedBytes { get; }
        /// <summary>
        /// 使用直接预制体引用建立已就绪的共享资源源，并记录可选的共享资产估值。
        /// </summary>
        /// <param name="prefab">已持有的有效预制体引用。</param>
        /// <param name="estimatedBytes">共享资产估算字节数，零表示尚未提供估值，不作为每实例成本累计。</param>
        public SharedPrefabResource(GameObject prefab, long estimatedBytes = 0)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (estimatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            directPrefab = prefab; asset = prefab; State = ResourceState.Ready; EstimatedBytes = estimatedBytes;
        }
        /// <summary>
        /// 使用 Resources 路径建立尚未加载的共享资源源；实际加载推迟到首次请求时。
        /// </summary>
        /// <param name="resourcesPath">Resources 下不含文件扩展名的资源路径。</param>
        /// <param name="estimatedBytes">共享资产估算字节数，零表示尚未提供估值。</param>
        public SharedPrefabResource(string resourcesPath, long estimatedBytes = 0)
        {
            if (string.IsNullOrWhiteSpace(resourcesPath)) throw new ArgumentException("Resources path is required.");
            if (estimatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            this.resourcesPath = resourcesPath; EstimatedBytes = estimatedBytes;
        }
        /// <summary>
        /// 为一个池取得独立的资源提供器并增加引用计数；各提供器共享同一加载结果。
        /// </summary>
        /// <returns>独立持有一个共享资源引用的提供器，使用结束后必须归还其资源所有权。</returns>
        /// <remarks>在 Unity 主线程使用；多个池可共用同一源，但应各自 Acquire。</remarks>
        public IPrefabInstanceProvider Acquire()
        {
            references++; return new InstantiateProvider(this);
        }
        /// <summary>
        /// 仅在尚未加载时启动一次 Resources 异步请求；异常转换为共享加载失败状态。
        /// </summary>
        private void BeginLoad()
        {
            if (State != ResourceState.NotLoaded) return;
            try { request = Resources.LoadAsync<GameObject>(resourcesPath); State = ResourceState.Loading; }
            catch (Exception ex) { Error = ex; State = ResourceState.Failed; }
        }
        /// <summary>
        /// 在异步加载完成后读取 GameObject 结果，更新就绪或失败状态并清除加载请求引用。
        /// </summary>
        private void Poll()
        {
            if (State != ResourceState.Loading || !request.isDone) return;
            asset = request.asset as GameObject; request = null;
            State = asset != null ? ResourceState.Ready : ResourceState.Failed;
            if (asset == null) Error = new InvalidOperationException("Prefab load failed: Resources/" + resourcesPath);
        }
        /// <summary>
        /// 归还一个池的资源引用，最后一个引用释放后解除加载结果持有；加载未结算时禁止释放。
        /// </summary>
        private void Release()
        {
            if (State == ResourceState.Loading) throw new InvalidOperationException("Wait for the shared load to settle before releasing a pool reference.");
            if (--references != 0) return;
            // Resources.UnloadAsset does not support GameObjects. Drop ownership; a project-level unload policy controls actual memory reclamation.
            asset = directPrefab; Error = null;
            State = directPrefab != null ? ResourceState.Ready : ResourceState.NotLoaded;
        }
        private sealed class InstantiateProvider : IPrefabInstanceProvider
        {
            private readonly SharedPrefabResource source;
            private bool released;
            /// <summary>
            /// 绑定共享资源源；引用计数已由 Acquire 增加，本构造方法不重复增加。
            /// </summary>
            internal InstantiateProvider(SharedPrefabResource source) { this.source = source; }
            public ResourceState State => source.State;
            public Exception Error => source.Error;
            public long SharedResourceEstimatedBytes => source.EstimatedBytes;
            /// <summary>
            /// 确认提供器尚未释放后，请求共享资源源开始加载。
            /// </summary>
            public void BeginLoad() { Check(); source.BeginLoad(); }
            /// <summary>
            /// 在提供器仍持有资源时轮询共享加载，已释放提供器不再参与更新。
            /// </summary>
            public void Poll() { if (!released) source.Poll(); }
            /// <summary>
            /// 校验提供器与预制体状态，在未激活父节点下直接实例化，抑制准备前的 OnEnable。
            /// </summary>
            /// <param name="cacheRoot">保持未激活的常驻缓存父节点。</param>
            /// <returns>已建立但尚未执行业务准备的独立实例。</returns>
            /// <remarks>不得在创建之后、返回内核登记之前执行可能失败的业务初始化。</remarks>
            public GameObject CreateInactive(Transform cacheRoot)
            {
                Check();
                if (source.State != ResourceState.Ready || source.asset == null) throw new InvalidOperationException("Prefab resource is not ready.");
                // An inactive parent suppresses OnEnable even when the prefab's activeSelf is true.
                return Object.Instantiate(source.asset, cacheRoot, false);
            }
            /// <summary>
            /// 对仍存在的 Unity 实例调用延迟 Destroy；已经被外部销毁的实例无需再次请求。
            /// </summary>
            public void RequestDestroy(GameObject instance) { if (instance != null) Object.Destroy(instance); }
            /// <summary>
            /// 要求调度已进入后续帧且 Unity 对象已失效，才确认销毁完成。
            /// </summary>
            /// <param name="instance">此前请求释放的实例，托管引用可能仍存在但 Unity 原生对象已失效。</param>
            /// <param name="issuedFrame">发出销毁请求时的服务调度帧编号。</param>
            /// <param name="currentFrame">本次确认时的服务调度帧编号。</param>
            /// <returns>是否已满足资源系统有效的释放完成契约。</returns>
            public bool IsDestroyComplete(GameObject instance, long issuedFrame, long currentFrame) => currentFrame > issuedFrame && instance == null;
            /// <summary>
            /// 幂等归还本提供器占用的共享资源引用，成功后将提供器标记为已释放。
            /// </summary>
            public void ReleaseResources() { if (released) return; source.Release(); released = true; }
            /// <summary>
            /// 拒绝继续使用已经释放的资源提供器，避免在失去所有权后启动加载或实例化。
            /// </summary>
            private void Check() { if (released) throw new ObjectDisposedException(nameof(InstantiateProvider)); }
        }
    }
}

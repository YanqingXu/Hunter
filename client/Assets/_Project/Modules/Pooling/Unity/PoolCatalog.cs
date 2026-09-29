using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BigWorld.Pooling.Unity
{
    [Flags]
    public enum PoolOverrideFields
    {
        None = 0, InitialStorageCapacity = 1, BasePrewarmTargetReady = 2, MinIdle = 4, MaxIdle = 8,
        MaxBorrowed = 16, MaxResident = 32, IdleTimeoutSeconds = 64, RetentionPriority = 128,
        MaxPendingRequests = 256, RequestTimeoutSeconds = 512, OverflowPolicy = 1024,
        EstimatedBytesPerInstance = 2048, MaxCacheProbes = 4096, All = 8191
    }
    [Serializable]
    public sealed class PoolSettingsOverride
    {
        public PoolOverrideFields Fields;
        public PoolSettings Values = new PoolSettings();
        /// <summary>
        /// 仅将掩码选中的配置字段写入目标设置，其余字段继续继承上一层配置。
        /// </summary>
        /// <param name="target">接收当前层已勾选覆盖字段的可编辑配置对象。</param>
        public void Apply(PoolSettings target)
        {
            if (Values == null) throw new ArgumentException("Override values cannot be null.");
            if (Has(PoolOverrideFields.InitialStorageCapacity)) target.InitialStorageCapacity = Values.InitialStorageCapacity;
            if (Has(PoolOverrideFields.BasePrewarmTargetReady)) target.BasePrewarmTargetReady = Values.BasePrewarmTargetReady;
            if (Has(PoolOverrideFields.MinIdle)) target.MinIdle = Values.MinIdle;
            if (Has(PoolOverrideFields.MaxIdle)) target.MaxIdle = Values.MaxIdle;
            if (Has(PoolOverrideFields.MaxBorrowed)) target.MaxBorrowed = Values.MaxBorrowed;
            if (Has(PoolOverrideFields.MaxResident)) target.MaxResident = Values.MaxResident;
            if (Has(PoolOverrideFields.IdleTimeoutSeconds)) target.IdleTimeoutSeconds = Values.IdleTimeoutSeconds;
            if (Has(PoolOverrideFields.RetentionPriority)) target.RetentionPriority = Values.RetentionPriority;
            if (Has(PoolOverrideFields.MaxPendingRequests)) target.MaxPendingRequests = Values.MaxPendingRequests;
            if (Has(PoolOverrideFields.RequestTimeoutSeconds)) target.RequestTimeoutSeconds = Values.RequestTimeoutSeconds;
            if (Has(PoolOverrideFields.OverflowPolicy)) target.OverflowPolicy = Values.OverflowPolicy;
            if (Has(PoolOverrideFields.EstimatedBytesPerInstance)) { target.EstimatedBytesPerInstance = Values.EstimatedBytesPerInstance; target.MemoryCostIsEstimate = Values.MemoryCostIsEstimate; }
            if (Has(PoolOverrideFields.MaxCacheProbes)) target.MaxCacheProbes = Values.MaxCacheProbes;
        }
        /// <summary>
        /// 判断覆盖掩码是否包含指定字段，用于逐项应用配置覆盖。
        /// </summary>
        private bool Has(PoolOverrideFields field) => (Fields & field) != 0;
    }
    [Serializable]
    public sealed class PrefabPoolDefinition
    {
        public string Id;
        public string Variant;
        public GameObject Prefab;
        [Tooltip("Use either a direct prefab reference or a Resources path, without extension.")]
        public string ResourcesPath;
        public long SharedAssetEstimatedBytes;
        public bool AllowSynchronousCreation;
        public PoolSettingsOverride Overrides = new PoolSettingsOverride();
        public PoolKey Key => new PoolKey(PoolKind.Prefab, Id, Variant);
    }
    [Serializable]
    public sealed class WarmPlanItem
    {
        public string PoolId;
        public string Variant;
        [Min(0)] public int ExpectedConcurrentDemand;
    }
    [CreateAssetMenu(menuName = "BigWorld/Pooling/Pool Catalog", fileName = "PoolCatalog")]
    public sealed class PoolCatalog : ScriptableObject
    {
        public PoolSettings Defaults = new PoolSettings();
        public PoolSettingsOverride DataCategory = new PoolSettingsOverride();
        public PoolSettingsOverride PrefabCategory = new PoolSettingsOverride();
        public PrefabPoolDefinition[] Prefabs = Array.Empty<PrefabPoolDefinition>();
        /// <summary>
        /// 复制全局默认值，依次应用类型分类和单项覆盖，再校验并生成只读运行时配置。
        /// </summary>
        /// <param name="kind">选择普通对象或预制体分类配置。</param>
        /// <param name="item">可选的单项覆盖，优先级高于分类和默认配置。</param>
        /// <returns>合并并通过完整校验后的独立只读配置。</returns>
        public PoolConfig Resolve(PoolKind kind, PoolSettingsOverride item = null)
        {
            // Copy the authoring data; callers only receive an immutable validated snapshot.
            var settings = JsonUtility.FromJson<PoolSettings>(JsonUtility.ToJson(Defaults));
            (kind == PoolKind.Data ? DataCategory : PrefabCategory)?.Apply(settings);
            item?.Apply(settings); return settings.Freeze();
        }
        /// <summary>
        /// 校验分类配置、池键唯一性、资源来源二选一、估值和每项最终配置；不创建实例或启动加载。
        /// </summary>
        public void Validate()
        {
            var keys = new HashSet<PoolKey>(); Resolve(PoolKind.Data); Resolve(PoolKind.Prefab);
            foreach (var entry in Prefabs)
            {
                if (entry == null) throw new ArgumentException("Catalog contains a null entry.");
                if (!keys.Add(entry.Key)) throw new ArgumentException("Duplicate catalog key: " + entry.Key);
                bool hasPath = !string.IsNullOrWhiteSpace(entry.ResourcesPath);
                if ((entry.Prefab != null) == hasPath) throw new ArgumentException("Specify exactly one prefab source for " + entry.Key);
                if (entry.SharedAssetEstimatedBytes < 0) throw new ArgumentException("Negative asset memory estimate.");
                Resolve(PoolKind.Prefab, entry.Overrides);
            }
        }
        /// <summary>
        /// 校验配置资产和已初始化的驱动，并建立一个可共享资源来源的注册会话。
        /// </summary>
        public PoolCatalogSession Open(PoolDriver driver)
        { if (driver == null || driver.Service == null) throw new ArgumentException("Driver must be initialized."); Validate(); return new PoolCatalogSession(this, driver); }
    }

    /// <summary>Explicit registration preserves the initializer's concrete generic type for IL2CPP.</summary>
    public sealed class PoolCatalogSession
    {
        private readonly PoolCatalog catalog;
        private readonly PoolDriver driver;
        private readonly Dictionary<string, SharedPrefabResource> sources = new Dictionary<string, SharedPrefabResource>();
        /// <summary>
        /// 绑定配置目录与运行时驱动，并为同一次注册会话保存共享资源来源。
        /// </summary>
        internal PoolCatalogSession(PoolCatalog catalog, PoolDriver driver) { this.catalog = catalog; this.driver = driver; }
        /// <summary>
        /// 根据稳定标识和变体查找配置，以明确泛型参数注册预制体池；同会话相同资源共享加载源，注册失败时归还已取得的资源引用。
        /// </summary>
        /// <param name="id">配置目录中的稳定预制体池标识。</param>
        /// <param name="variant">同一资源的用途或变体，默认空字符串。</param>
        /// <returns>已注册的类型化预制体池。</returns>
        /// <remarks>泛型参数必须与组件实现的 IPooledInitializer 参数类型相匹配。在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public PrefabPool<TArgs> RegisterPrefab<TArgs>(string id, string variant = "")
        {
            driver.Service.CheckThread();
            var key = new PoolKey(PoolKind.Prefab, id, variant);
            if (driver.Service.HasPool(key)) throw new InvalidOperationException("Pool already registered: " + key);
            if (driver.Service.State != PoolState.Running) throw new InvalidOperationException("Service is closing.");
            foreach (var entry in catalog.Prefabs)
            {
                if (!entry.Key.Equals(key)) continue;
                var config = catalog.Resolve(PoolKind.Prefab, entry.Overrides);
                string sourceKey = entry.Prefab != null ? "direct:" + entry.Prefab.GetInstanceID() : "resources:" + entry.ResourcesPath;
                if (!sources.TryGetValue(sourceKey, out var source))
                {
                    source = entry.Prefab != null ? new SharedPrefabResource(entry.Prefab, entry.SharedAssetEstimatedBytes) : new SharedPrefabResource(entry.ResourcesPath, entry.SharedAssetEstimatedBytes);
                    sources.Add(sourceKey, source);
                }
                var provider = source.Acquire();
                try { return driver.Service.Register(new PrefabPool<TArgs>(driver.Service, key, config, provider, driver.CacheRoot, entry.AllowSynchronousCreation)); }
                catch { provider.ReleaseResources(); throw; }
            }
            throw new KeyNotFoundException("No prefab definition for " + key);
        }
    }
}

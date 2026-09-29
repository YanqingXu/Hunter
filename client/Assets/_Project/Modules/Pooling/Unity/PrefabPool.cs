using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigWorld.Pooling.Unity
{
    public interface IPooledLifecycle
    {
        /// <summary>
        /// 在新实例登记后显式执行一次基础初始化；预热也会调用，因此不能启动本次玩法行为。
        /// </summary>
        void InitializeOnce();
        /// <summary>
        /// 归还时解除目标引用、事件、定时器和异步任务等业务关系；停用 GameObject 不能替代这些清理。
        /// </summary>
        void OnRelease();
    }
    public interface IPooledInitializer<TArgs>
    {
        /// <summary>
        /// 在实例尚未激活时写入本次借用的类型化业务参数，避免使用 object 导致值类型装箱。
        /// </summary>
        /// <param name="args">本次借用的类型化业务参数，不应依赖激活回调补写。</param>
        /// <remarks>调用时对象保持未激活；不要在 Awake 中覆盖这里写入的生成数据。</remarks>
        void InitializeForUse(TArgs args);
    }

    public readonly struct SpawnTransform
    {
        private readonly bool initialized;
        private readonly Quaternion rotation;
        private readonly Vector3 scale;
        public Transform Parent { get; }
        public bool RequiresParent { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation => initialized ? rotation : Quaternion.identity;
        public Vector3 Scale => initialized ? scale : Vector3.one;
        public bool WorldSpace { get; }
        public Scene Scene { get; }
        /// <summary>
        /// 保存父节点、位置、旋转、局部缩放及目标场景，并记录是否要求父节点在排队结束后仍然存在。
        /// </summary>
        /// <param name="parent">目标父节点，可为空；非空父节点在准备时必须仍有效。</param>
        /// <param name="position">由 worldSpace 决定是世界位置还是局部位置。</param>
        /// <param name="rotation">由 worldSpace 决定是世界旋转还是局部旋转。</param>
        /// <param name="scale">实例的局部缩放，不受 worldSpace 影响。</param>
        /// <param name="worldSpace">是否按世界坐标设置位置和旋转。</param>
        /// <param name="scene">无父节点时的目标场景；未指定则使用活动场景。有父节点时以父节点场景为准。</param>
        public SpawnTransform(Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, bool worldSpace = false, Scene scene = default)
        {
            initialized = true; Parent = parent; RequiresParent = !ReferenceEquals(parent, null); Position = position;
            this.rotation = rotation; this.scale = scale; WorldSpace = worldSpace; Scene = scene;
        }
        /// <summary>
        /// 构造位于指定父节点下的默认局部姿态：位置为零、旋转为单位旋转、缩放为一。
        /// </summary>
        /// <param name="parent">目标父节点，可为空。</param>
        /// <returns>局部位置为零、单位旋转及单位缩放的生成设置。</returns>
        public static SpawnTransform Under(Transform parent) => new SpawnTransform(parent, Vector3.zero, Quaternion.identity, Vector3.one);
        /// <summary>
        /// 构造没有父节点的世界空间生成姿态；未指定场景时由生成流程选择当前活动场景。
        /// </summary>
        /// <param name="position">世界坐标位置。</param>
        /// <param name="rotation">世界旋转。</param>
        /// <param name="scene">无父节点生成时使用的场景，默认由准备流程选择活动场景。</param>
        /// <returns>无父节点、单位局部缩放的世界空间生成设置。</returns>
        public static SpawnTransform At(Vector3 position, Quaternion rotation, Scene scene = default) => new SpawnTransform(null, position, rotation, Vector3.one, true, scene);
    }
    public readonly struct SpawnRequest<TArgs>
    {
        public SpawnTransform Transform { get; }
        public TArgs Args { get; }
        /// <summary>
        /// 将生成姿态与本次类型化业务参数组合为一个不可变请求值。
        /// </summary>
        public SpawnRequest(SpawnTransform transform, TArgs args) { Transform = transform; Args = args; }
    }

    public sealed class PrefabPool<TArgs> : ManagedPool<GameObject, SpawnRequest<TArgs>>
    {
        private readonly PrefabAdapter<TArgs> prefabAdapter;
        public bool AllowSynchronousCreation { get; }
        /// <summary>
        /// 使用资源提供器和常驻未激活缓存根构造预制体池；同步创建必须由调用方明确启用。
        /// </summary>
        /// <param name="service">负责主线程归属、作用域和全局预算的服务。</param>
        /// <param name="key">种类必须为 Prefab 的完整池键。</param>
        /// <param name="config">已经校验的只读运行时配置。</param>
        /// <param name="provider">为本池持有资源引用并负责配套实例创建与释放的提供器。</param>
        /// <param name="cacheRoot">常驻且未激活的缓存根节点，不应随业务分区卸载。</param>
        /// <param name="allowSynchronousCreation">是否显式允许调用 SpawnOrCreate；常规游戏阶段建议通过异步调度创建。</param>
        public PrefabPool(PoolService service, PoolKey key, PoolConfig config, IPrefabInstanceProvider provider, Transform cacheRoot, bool allowSynchronousCreation = false)
            : this(service, key, config, new PrefabAdapter<TArgs>(provider, cacheRoot), allowSynchronousCreation) { }
        /// <summary>
        /// 建立使用有序空闲缓存的预制体池，校验池键种类，并保存同步创建权限及 Unity 适配器。
        /// </summary>
        private PrefabPool(PoolService service, PoolKey key, PoolConfig config, PrefabAdapter<TArgs> adapter, bool allowSynchronousCreation)
            : base(service, key, config, adapter, true)
        {
            if (key.Kind != PoolKind.Prefab) throw new ArgumentException("Prefab pools require a prefab key.");
            prefabAdapter = adapter; AllowSynchronousCreation = allowSynchronousCreation;
        }
        /// <summary>
        /// 将姿态和本次业务参数交给 Unity 适配器，在保持实例未激活的情况下完成准备。
        /// </summary>
        protected override void PrepareForUse(GameObject value, SpawnRequest<TArgs> args) => prefabAdapter.Prepare(value, args);
        /// <summary>
        /// 在内核完成准入及取消检查后激活实例，随后内核还会再次检查作用域与实例有效性。
        /// </summary>
        protected override void Activate(GameObject value) { value.SetActive(true); }
        /// <summary>
        /// 只使用空闲实例完成姿态设置、业务初始化和激活；缓存不足时返回失败，不创建 GameObject。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="transform">目标父节点、场景及生成姿态。</param>
        /// <param name="args">本次借用的类型化业务参数，不应依赖激活回调补写。</param>
        /// <param name="lease">成功时返回本次 GameObject 借用凭证，失败时为默认值。</param>
        /// <returns>是否成功准备并交付一个缓存实例。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public bool TrySpawnCached(PoolScope scope, SpawnTransform transform, TArgs args, out PoolLease<GameObject> lease)
        { var result = RentCached(scope, new SpawnRequest<TArgs>(transform, args)); lease = result.Lease; return result.Succeeded; }
        /// <summary>
        /// 在显式允许的加载阶段同步生成；未启用同步创建时抛出错误，容量及归属检查仍由内核执行。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="transform">目标父节点、场景及生成姿态。</param>
        /// <param name="args">本次借用的类型化业务参数，不应依赖激活回调补写。</param>
        /// <returns>同步生成结果，预期失败由状态表达。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 仅可用于构造时显式启用同步创建的池。</remarks>
        public RentResult<GameObject> SpawnOrCreate(PoolScope scope, SpawnTransform transform, TArgs args)
        {
            if (!AllowSynchronousCreation) throw new InvalidOperationException("Synchronous creation must be explicitly enabled for loading-stage pools.");
            return RentSynchronously(scope, new SpawnRequest<TArgs>(transform, args));
        }
        /// <summary>
        /// 提交包含生成姿态和类型化参数的有界请求，由主线程统一调度创建、准备及交付。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="transform">目标父节点、场景及生成姿态；排队期间仍会检查父节点有效性。</param>
        /// <param name="args">本次借用的类型化业务参数，不应依赖激活回调补写。</param>
        /// <param name="options">本次请求的优先级与超时选项；零超时表示使用池默认值。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>包含成功凭证或失败状态的任务；等待后仍须用 TryGet 确认有效性。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public Task<RentResult<GameObject>> SpawnAsync(PoolScope scope, SpawnTransform transform, TArgs args, RentOptions options = default, CancellationToken cancellationToken = default)
            => EnqueueRent(scope, new SpawnRequest<TArgs>(transform, args), options, cancellationToken);
    }

    internal sealed class PrefabAdapter<TArgs> : PoolAdapter<GameObject>
    {
        private sealed class Components
        {
            internal IPooledLifecycle[] Lifecycle;
            internal IPooledInitializer<TArgs>[] Initializers;
            internal ParticleSystem[] Particles;
            internal TrailRenderer[] Trails;
            internal AudioSource[] Audio;
            internal Rigidbody[] Bodies;
            internal Rigidbody2D[] Bodies2D;
        }
        private readonly IPrefabInstanceProvider provider;
        private readonly Transform cacheRoot;
        private readonly Dictionary<GameObject, Components> components = new Dictionary<GameObject, Components>();
        /// <summary>
        /// 保存实例资源提供器及缓存根，要求缓存根存在且当前未在层级中激活。
        /// </summary>
        internal PrefabAdapter(IPrefabInstanceProvider provider, Transform cacheRoot)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (cacheRoot == null || cacheRoot.gameObject.activeInHierarchy) throw new ArgumentException("A persistent inactive cache root is required.");
            this.cacheRoot = cacheRoot;
        }
        public override ResourceState ResourceState => provider.State;
        public override Exception ResourceError => provider.Error;
        public override long SharedResourceEstimatedBytes => provider.SharedResourceEstimatedBytes;
        /// <summary>
        /// 转交资源提供器启动加载，保持具体资源系统与池内核解耦。
        /// </summary>
        public override void BeginLoad() => provider.BeginLoad();
        /// <summary>
        /// 在主线程轮询提供器，结算共享资源加载状态。
        /// </summary>
        public override void PollResource() => provider.Poll();
        /// <summary>
        /// 确认缓存根仍有效且未激活，再由提供器在其下创建实例；一次性组件初始化由登记后的阶段执行。
        /// </summary>
        public override GameObject Create()
        {
            if (cacheRoot == null || cacheRoot.gameObject.activeInHierarchy) throw new InvalidOperationException("Cache root has been destroyed or activated.");
            return provider.CreateInactive(cacheRoot);
        }
        /// <summary>
        /// 使用 Unity 对象的空值语义检查原生实例是否仍存在，覆盖仅检查托管引用的默认实现。
        /// </summary>
        public override bool IsValid(GameObject value) => value != null;
        /// <summary>
        /// 保持实例未激活，扫描并缓存生命周期、类型化初始化和常见表现组件，再执行显式一次性初始化。
        /// </summary>
        public override void InitializeOnce(GameObject value)
        {
            value.SetActive(false);
            var lifecycle = new List<IPooledLifecycle>(); var initializers = new List<IPooledInitializer<TArgs>>();
            foreach (var component in value.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is IPooledLifecycle pooled) lifecycle.Add(pooled);
                if (component is IPooledInitializer<TArgs> initializer) initializers.Add(initializer);
            }
            var entry = new Components { Lifecycle = lifecycle.ToArray(), Initializers = initializers.ToArray(), Particles = value.GetComponentsInChildren<ParticleSystem>(true),
                Trails = value.GetComponentsInChildren<TrailRenderer>(true), Audio = value.GetComponentsInChildren<AudioSource>(true),
                Bodies = value.GetComponentsInChildren<Rigidbody>(true), Bodies2D = value.GetComponentsInChildren<Rigidbody2D>(true) };
            components.Add(value, entry);
            foreach (var item in entry.Lifecycle) item.InitializeOnce();
        }
        /// <summary>
        /// 校验父节点和场景，在未激活状态下迁移层级、设置姿态与缩放，最后向已缓存组件传入业务参数。
        /// </summary>
        internal void Prepare(GameObject value, SpawnRequest<TArgs> request)
        {
            var target = request.Transform;
            if (target.RequiresParent && target.Parent == null) throw new InvalidOperationException("Spawn parent was destroyed while waiting.");
            if (cacheRoot == null) throw new InvalidOperationException("Cache root was destroyed.");
            if (target.Parent != null && (target.Parent == cacheRoot || target.Parent.IsChildOf(cacheRoot))) throw new ArgumentException("Cannot spawn beneath the inactive cache root.");
            value.SetActive(false);
            var transform = value.transform; transform.SetParent(null, false);
            var scene = target.Parent != null ? target.Parent.gameObject.scene : target.Scene.IsValid() ? target.Scene : SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Spawn scene is not loaded.");
            if (value.scene != scene) SceneManager.MoveGameObjectToScene(value, scene);
            transform.SetParent(target.Parent, false);
            if (target.WorldSpace) transform.SetPositionAndRotation(target.Position, target.Rotation);
            else { transform.localPosition = target.Position; transform.localRotation = target.Rotation; }
            transform.localScale = target.Scale;
            foreach (var item in components[value].Initializers) item.InitializeForUse(request.Args);
        }
        /// <summary>
        /// 尽力清理全部生命周期组件、粒子、拖尾、音效和刚体，再停用并迁回缓存场景；汇总异常使整实例退役。
        /// </summary>
        public override void Reset(GameObject value)
        {
            List<Exception> errors = null;
            if (components.TryGetValue(value, out var entry))
            {
                foreach (var item in entry.Lifecycle) try { item.OnRelease(); } catch (Exception ex) { AddError(ref errors, ex); }
                foreach (var item in entry.Particles) try { if (item != null) item.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); } catch (Exception ex) { AddError(ref errors, ex); }
                foreach (var item in entry.Trails) try { if (item != null) item.Clear(); } catch (Exception ex) { AddError(ref errors, ex); }
                foreach (var item in entry.Audio) try { if (item != null) item.Stop(); } catch (Exception ex) { AddError(ref errors, ex); }
                foreach (var item in entry.Bodies) try { if (item != null && !item.isKinematic) { item.velocity = Vector3.zero; item.angularVelocity = Vector3.zero; item.Sleep(); } } catch (Exception ex) { AddError(ref errors, ex); }
                foreach (var item in entry.Bodies2D) try { if (item != null) { item.velocity = Vector2.zero; item.angularVelocity = 0; item.Sleep(); } } catch (Exception ex) { AddError(ref errors, ex); }
            }
            // These steps must still run when a business component throws.
            try { if (value != null) value.SetActive(false); } catch (Exception ex) { AddError(ref errors, ex); }
            try
            {
                if (value != null)
                {
                    if (cacheRoot == null) throw new InvalidOperationException("Cannot return instance: cache root is missing.");
                    value.transform.SetParent(null, false);
                    if (value.scene != cacheRoot.gameObject.scene) SceneManager.MoveGameObjectToScene(value, cacheRoot.gameObject.scene);
                    value.transform.SetParent(cacheRoot, false);
                    value.transform.localPosition = Vector3.zero; value.transform.localRotation = Quaternion.identity; value.transform.localScale = Vector3.one;
                }
            }
            catch (Exception ex) { AddError(ref errors, ex); }
            if (errors != null) throw new AggregateException("Pooled component cleanup failed; retiring the entire instance.", errors);
        }
        /// <summary>
        /// 仅在首次清理异常时创建错误列表，随后追加异常，保证其他组件仍有机会完成清理。
        /// </summary>
        private static void AddError(ref List<Exception> errors, Exception ex) { if (errors == null) errors = new List<Exception>(); errors.Add(ex); }
        /// <summary>
        /// 通过实例提供器发出正确的销毁或句柄释放请求，不混用资源系统的释放方式。
        /// </summary>
        public override void RequestDestroy(GameObject value) => provider.RequestDestroy(value);
        /// <summary>
        /// 由提供器确认释放完成后移除组件缓存；未确认前仍保留实例记录和资源依赖。
        /// </summary>
        /// <param name="value">正在等待释放确认的实例。</param>
        /// <param name="issuedFrame">释放请求发出时的服务调度帧编号。</param>
        /// <param name="currentFrame">当前服务调度帧编号。</param>
        /// <returns>是否可以清除实例持有并归还驻留配额。</returns>
        public override bool IsDestroyComplete(GameObject value, long issuedFrame, long currentFrame)
        {
            if (!provider.IsDestroyComplete(value, issuedFrame, currentFrame)) return false;
            components.Remove(value); return true;
        }
        /// <summary>
        /// 在池已排空后归还提供器持有的资源引用。
        /// </summary>
        public override void ReleaseResources() => provider.ReleaseResources();
    }
}

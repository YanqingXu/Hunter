using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigWorld.Pooling
{
    public readonly struct NoPoolArgs { }

    public sealed class DataPool<T> : ManagedPool<T, NoPoolArgs>, IObjectPool<T> where T : class
    {
        /// <summary>
        /// 使用对象工厂、重置策略和可选释放回调构造普通对象池；此时只准备元数据，不创建业务实例。
        /// </summary>
        public DataPool(PoolService service, PoolKey key, PoolConfig config, Func<T> factory, IResetPolicy<T> reset, Action<T> destroy = null)
            : this(service, key, config, new DataAdapter<T>(factory, reset, destroy)) { }
        /// <summary>
        /// 使用自定义适配器构造普通对象池，并校验池键属于数据对象类型；使用前仍需向服务注册。
        /// </summary>
        public DataPool(PoolService service, PoolKey key, PoolConfig config, PoolAdapter<T> adapter) : base(service, key, config, adapter)
        { if (key.Kind != PoolKind.Data) throw new ArgumentException("Data pools require a data key."); }
        /// <summary>
        /// 普通对象池没有逐次借用参数，因此这里不执行额外准备；归还时由重置策略清理对象。
        /// </summary>
        protected override void PrepareForUse(T value, NoPoolArgs args) { }
        /// <summary>
        /// 仅从已有空闲对象中尝试借用并登记到指定作用域；缓存不足时返回失败，不创建对象。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="lease">成功时返回本次借用凭证；失败时为默认凭证。</param>
        /// <returns>是否成功取得并准备好一个缓存实例；失败时不会创建对象。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public bool TryRentCached(PoolScope scope, out PoolLease<T> lease) { var result = RentCached(scope, default); lease = result.Lease; return result.Succeeded; }
        /// <summary>
        /// 优先复用空闲对象，必要时同步创建；容量不足或生命周期失败通过结构化结果返回。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <returns>借用状态、成功凭证及可选诊断编号。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 同步创建不受逐帧创建时间目标保障。</remarks>
        public RentResult<T> RentOrCreate(PoolScope scope) => RentSynchronously(scope, default);
        /// <summary>
        /// 提交有界异步借用请求，由服务主线程按优先级、期限及全局预算处理。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="options">本次请求的优先级与超时选项；零超时表示使用池默认值。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>最终借用结果；取消和超时以状态返回，不通过任务取消异常表达。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 await 恢复后仍应调用凭证 TryGet，确认作用域未在期间关闭。</remarks>
        public Task<RentResult<T>> RentAsync(PoolScope scope, RentOptions options = default, CancellationToken cancellationToken = default) => EnqueueRent(scope, default, options, cancellationToken);
    }

    public sealed class DelegateResetPolicy<T> : IResetPolicy<T> where T : class
    {
        private readonly Action<T> reset;
        private readonly Predicate<T> retain;
        /// <summary>
        /// 将必需的重置委托和可选保留条件组合成策略；未提供保留条件时默认允许缓存。
        /// </summary>
        public DelegateResetPolicy(Action<T> reset, Predicate<T> retain = null) { this.reset = reset ?? throw new ArgumentNullException(nameof(reset)); this.retain = retain; }
        /// <summary>
        /// 执行业务重置委托，清除对象持有的引用、事件或本次借用状态。
        /// </summary>
        public void Reset(T value) => reset(value);
        /// <summary>
        /// 在重置完成后判断对象是否适合继续缓存，例如拒绝保留容量过大的容器。
        /// </summary>
        public bool CanRetain(T value) => retain == null || retain(value);
    }

    internal sealed class DataAdapter<T> : PoolAdapter<T> where T : class
    {
        private readonly Func<T> factory;
        private readonly IResetPolicy<T> reset;
        private readonly Action<T> destroy;
        /// <summary>
        /// 保存普通对象的创建、重置和可选释放行为，供统一池内核调用。
        /// </summary>
        internal DataAdapter(Func<T> factory, IResetPolicy<T> reset, Action<T> destroy)
        { this.factory = factory ?? throw new ArgumentNullException(nameof(factory)); this.reset = reset ?? throw new ArgumentNullException(nameof(reset)); this.destroy = destroy; }
        /// <summary>
        /// 调用工厂创建一个独立对象；对象返回后由池内核登记并处理失败路径。
        /// </summary>
        public override T Create() => factory();
        /// <summary>
        /// 将归还对象交给重置策略处理；异常由池内核记录并使对象退役。
        /// </summary>
        public override void Reset(T value) => reset.Reset(value);
        /// <summary>
        /// 转交保留策略判断已经重置的对象是否值得继续缓存。
        /// </summary>
        public override bool CanRetain(T value) => reset.CanRetain(value);
        /// <summary>
        /// 调用可选释放委托处理对象占用的外部资源；未提供委托时由内核解除持有即可。
        /// </summary>
        public override void RequestDestroy(T value) { destroy?.Invoke(value); }
    }
}

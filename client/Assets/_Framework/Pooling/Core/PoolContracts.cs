using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigWorld.Pooling
{
    /// <summary>
    /// 对象池管理的对象种类，用于区分普通 C# 对象与 Unity 预制体实例。
    /// </summary>
    public enum PoolKind
    {
        /// <summary>普通 C# 对象池，例如复用临时数据、消息或业务上下文对象。</summary>
        Data,

        /// <summary>Unity 预制体实例池，负责 GameObject 的生成、激活、重置和销毁。</summary>
        Prefab
    }

    /// <summary>
    /// 对象池或池服务的生命周期状态；开始关闭后停止接收新的借用请求。
    /// </summary>
    public enum PoolState
    {
        /// <summary>正常运行；新请求仍需通过作用域、容量和资源等准入检查。</summary>
        Running,

        /// <summary>正在关闭，等待已有请求、借用、实例销毁和资源释放完成；释放故障时也保持此状态。</summary>
        Closing,

        /// <summary>关闭完成，池已解除对实例及其所持资源的持有，不能再借用。</summary>
        Closed
    }

    /// <summary>
    /// 作用域的生命周期状态；作用域用于集中管理一组业务请求与借用，例如一个地图分区。
    /// </summary>
    public enum ScopeState
    {
        /// <summary>自身尚未开始关闭；只有全部祖先作用域也开放时，才允许继续借用和访问对象。</summary>
        Open,

        /// <summary>正在关闭，借用访问立即失效；后续按预算结算请求、归还对象并等待子作用域及必要销毁完成。</summary>
        Closing,

        /// <summary>关闭完成，该作用域的请求、借用、待释放实例及子作用域均已结算。</summary>
        Closed
    }

    /// <summary>
    /// 池内一个槽位的状态。槽位是对象的管理记录，实例释放后仍可保留槽位和版本信息供后续复用。
    /// </summary>
    public enum SlotState
    {
        /// <summary>空槽位，不持有实例，也不占用驻留配额；可能尚未创建对象，或已经完成释放。</summary>
        Released,

        /// <summary>已预留创建所需的驻留配额，工厂尚未返回可登记的实例；用于避免创建过程中突破容量限制。</summary>
        CreateReserved,

        /// <summary>实例已登记，正在进行基础初始化或本次借用准备，尚未交付给业务使用。</summary>
        Preparing,

        /// <summary>实例已借出并关联所属作用域，业务通过本次借用凭证访问；归还前仍计入借用数量。</summary>
        Borrowed,

        /// <summary>正在结束本次借用并重置对象；处理后进入空闲缓存或退役销毁流程。</summary>
        Returning,

        /// <summary>实例处于空闲缓存中，可尝试再次借用；虽然没有被业务使用，仍占用驻留配额。</summary>
        Idle,

        /// <summary>实例已退役，正在等待调度器发出销毁请求；此时仍保留实例和驻留配额。</summary>
        PendingDestroy,

        /// <summary>销毁请求已成功发出，正在等待实际释放完成的确认；不能提前返还驻留配额。</summary>
        DestroyIssued,

        /// <summary>销毁请求或释放确认发生异常，继续保留实例与配额，等待显式重试。</summary>
        DestroyFaulted
    }

    /// <summary>
    /// 借用操作的结果或内部调度的等待原因；预期失败通过此状态返回，便于业务选择重试、跳过或降级。
    /// </summary>
    public enum PoolStatus
    {
        /// <summary>操作成功；用于借用结果时，结果中包含本次借用凭证。</summary>
        Success,

        /// <summary>仅缓存借用没有找到可用的空闲对象，不会因此创建新实例。</summary>
        CacheMiss,

        /// <summary>当前池的借用容量或驻留容量不足，暂时无法满足请求。</summary>
        CapacityExceeded,

        /// <summary>当前池的待处理请求队列已达到上限，无法继续排队。</summary>
        QueueFull,

        /// <summary>请求未在规定的等待期限内完成，已因超时结束。</summary>
        TimedOut,

        /// <summary>请求收到取消信号，并在成功交付借用前结束。</summary>
        Cancelled,

        /// <summary>请求所属作用域或其祖先已开始关闭，不再允许继续借用。</summary>
        ScopeClosed,

        /// <summary>对象池或池服务已开始关闭，不再允许新的借用。</summary>
        PoolClosed,

        /// <summary>所需资源尚未就绪；内部调度也用此状态表示等待本帧创建预算，异步请求可继续排队。</summary>
        ResourceNotReady,

        /// <summary>资源加载或资源状态轮询失败，当前请求无法通过资源就绪检查。</summary>
        LoadFailed,

        /// <summary>基础初始化、本次借用准备或激活失败，也包括准备期间实例失效的情况。</summary>
        InitializationFailed,

        /// <summary>实例工厂创建失败，例如创建过程抛出异常或返回空引用。</summary>
        CreationFailed,

        /// <summary>池服务的全局驻留数量或实例内存估值配额不足，暂时不能新增实例。</summary>
        GlobalBudgetExceeded
    }
    /// <summary>
    /// 一次预热任务结束的原因；未达到目标时也可能已经创建了部分实例，实际进度需查看 WarmResult。
    /// </summary>
    public enum WarmEndReason
    {
        /// <summary>已借出数量与空闲数量之和达到预热目标；这些实例不一定全部由本次预热创建。</summary>
        ReachedTarget,

        /// <summary>受到当前池的空闲容量或驻留容量限制，本轮预热无法继续达到目标，按已有进度结束。</summary>
        CapacityLimited,

        /// <summary>全局驻留数量或实例内存估值配额不足，提前结束本轮预热。</summary>
        GlobalBudgetLimited,

        /// <summary>未能在规定期限内达到预热目标，任务因超时结束。</summary>
        TimedOut,

        /// <summary>收到取消信号，本次预热停止；已经进入共享缓存的实例不会仅因该任务取消而自动销毁。</summary>
        Cancelled,

        /// <summary>发起预热的作用域或其祖先已开始关闭，本次预热不再继续。</summary>
        ScopeClosed,

        /// <summary>对象池或池服务已开始关闭，不再继续执行本次预热。</summary>
        PoolClosed,

        /// <summary>当前池的待处理请求队列已满，本次预热请求无法入队。</summary>
        QueueFull,

        /// <summary>资源加载、实例创建或基础初始化等步骤失败，任务以失败原因结束，可结合诊断编号定位问题。</summary>
        Failed
    }

    /// <summary>
    /// 异步借用在单池容量或全局驻留配额不足时的处理策略。
    /// 同步借用不会因本策略自动等待，预热任务则使用自身的结束规则。
    /// </summary>
    public enum OverflowPolicy
    {
        /// <summary>
        /// 保留已入队的异步请求，等待归还或销毁释放配额；超时、取消或关闭时结束等待。
        /// 请求队列已经满时仍会直接拒绝，不会无限排队。
        /// </summary>
        WaitOrFail,

        /// <summary>
        /// 调度处理请求时，若发现单池容量或全局配额不足，则结束请求并返回对应失败状态。
        /// 资源未就绪或本帧创建预算不足时仍允许等待，不代表调用异步接口时必定立即完成。
        /// </summary>
        Fail
    }

    /// <summary>
    /// 创建实例所需资源的加载状态，例如预制体资源是否可用；与实例槽位的状态分别管理。
    /// </summary>
    public enum ResourceState
    {
        /// <summary>尚未启动加载，需要创建实例时可以请求资源提供器开始加载。</summary>
        NotLoaded,

        /// <summary>资源正在加载，尚不能依赖加载结果创建实例，需要继续等待加载结算。</summary>
        Loading,

        /// <summary>所需资源已可用，可以进入实例创建流程；不代表池中已经有空闲实例。无需加载资源的普通对象也可直接处于此状态。</summary>
        Ready,

        /// <summary>资源加载失败，无法继续依赖该资源创建实例；具体错误由资源提供器或适配器提供。</summary>
        Failed
    }

    public readonly struct PoolKey : IEquatable<PoolKey>
    {
        public PoolKind Kind { get; }
        public string Id { get; }
        public string Variant { get; }
        /// <summary>
        /// 组合池种类、稳定标识和用途变体形成完整身份；标识不能为空，空变体按空字符串处理。
        /// </summary>
        public PoolKey(PoolKind kind, string id, string variant = "")
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A stable identity is required.", nameof(id));
            Kind = kind; Id = id; Variant = variant ?? "";
        }
        /// <summary>
        /// 以泛型对象类型的程序集限定名和变体生成数据池键，区分不同类型及用途。
        /// </summary>
        public static PoolKey ForData<T>(string variant = "") where T : class => new PoolKey(PoolKind.Data, typeof(T).AssemblyQualifiedName, variant);
        /// <summary>
        /// 按池种类、标识和变体比较完整身份，避免仅使用哈希值导致不同池串用。
        /// </summary>
        public bool Equals(PoolKey other) => Kind == other.Kind && Id == other.Id && Variant == other.Variant;
        /// <summary>
        /// 检查对象类型并比较完整池身份；非 PoolKey 对象返回不相等。
        /// </summary>
        public override bool Equals(object obj) => obj is PoolKey other && Equals(other);
        /// <summary>
        /// 为完整池键计算集合查找哈希；哈希冲突仍需通过 Equals 区分。
        /// </summary>
        public override int GetHashCode() { unchecked { return (((int)Kind * 397) ^ (Id?.GetHashCode() ?? 0)) * 397 ^ (Variant?.GetHashCode() ?? 0); } }
        /// <summary>
        /// 生成包含池种类、稳定标识及可选变体的可读文本，用于日志和监控。
        /// </summary>
        public override string ToString() => Kind + ":" + Id + (string.IsNullOrEmpty(Variant) ? "" : ":" + Variant);
    }

    internal interface ILeaseOwner<T> where T : class
    {
        /// <summary>
        /// 由实际拥有者校验槽位和版本，并仅在借用、作用域及实例仍有效时返回对象。
        /// </summary>
        /// <param name="slot">池内稳定槽位编号。</param>
        /// <param name="version">需要匹配的借用版本。</param>
        /// <param name="value">成功时返回有效对象；失败时为 null。</param>
        /// <returns>身份、作用域和实例是否均有效。</returns>
        bool TryGet(int slot, ulong version, out T value);
        /// <summary>
        /// 尝试结束匹配版本的当前借用；作用域关闭或实例已失效时仍应允许结算归还。
        /// </summary>
        /// <param name="slot">需要结束借用的槽位编号。</param>
        /// <param name="version">凭证记录的原借用版本。</param>
        /// <returns>是否首次成功开始结束这一次借用。</returns>
        bool TryReturn(int slot, ulong version);
    }

    public readonly struct PoolLease<T> : IDisposable where T : class
    {
        private readonly ILeaseOwner<T> owner;
        private readonly int slot;
        private readonly ulong version;
        /// <summary>
        /// 保存拥有者、槽位和本次借用版本，建立只指向这一次借用的值类型凭证。
        /// </summary>
        internal PoolLease(ILeaseOwner<T> owner, int slot, ulong version) { this.owner = owner; this.slot = slot; this.version = version; }
        /// <summary>
        /// 通过拥有者检查凭证是否仍有效；默认凭证、过期版本或已关闭作用域都不能取出对象。
        /// </summary>
        /// <param name="value">成功时返回该次借用的有效实例；失败时为 null。</param>
        /// <returns>借用、作用域和实例当前是否仍有效。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 取出的原始引用不能自动撤销，归还后不得继续使用。</remarks>
        public bool TryGet(out T value) { value = null; return owner != null && owner.TryGet(slot, version, out value); }
        public T Value => TryGet(out var value) ? value : throw new InvalidOperationException("This pool lease is no longer valid.");
        /// <summary>
        /// 尝试归还本次借用；凭证的副本、旧回调或重复归还不会影响后续借用。
        /// </summary>
        /// <returns>仅第一次成功开始归还时返回 true；过期或重复操作返回 false。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public bool TryReturn() => owner != null && owner.TryReturn(slot, version);
        /// <summary>
        /// 支持 using 模式结束借用；内部使用安全归还，重复 Dispose 不会重复清理。
        /// </summary>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 Dispose 结束的是当前版本借用，不直接代表底层实例已经物理销毁。</remarks>
        public void Dispose() { TryReturn(); }
    }

    public readonly struct RentResult<T> where T : class
    {
        public PoolStatus Status { get; }
        public PoolLease<T> Lease { get; }
        public long DiagnosticId { get; }
        public bool Succeeded => Status == PoolStatus.Success;
        /// <summary>
        /// 封装借用状态、成功凭证和可选诊断编号；预期失败通过状态表达，而不是取消异常。
        /// </summary>
        internal RentResult(PoolStatus status, PoolLease<T> lease = default, long diagnosticId = 0)
        { Status = status; Lease = lease; DiagnosticId = diagnosticId; }
    }

    public readonly struct WarmResult
    {
        public int TargetReady { get; }
        public int StartReady { get; }
        public int Created { get; }
        public int EndReady { get; }
        public WarmEndReason Reason { get; }
        public long DiagnosticId { get; }
        /// <summary>
        /// 记录一次预热的目标、开始与结束可用量、新建成功量、结束原因和可选诊断编号。
        /// </summary>
        internal WarmResult(int target, int start, int created, int end, WarmEndReason reason, long diagnosticId = 0)
        { TargetReady = target; StartReady = start; Created = created; EndReady = end; Reason = reason; DiagnosticId = diagnosticId; }
    }

    public readonly struct RentOptions
    {
        public int Priority { get; }
        public double TimeoutSeconds { get; }
        /// <summary>
        /// 构造借用排队选项并校验优先级及时间范围；超时为零时由池采用默认期限。
        /// </summary>
        public RentOptions(int priority = 0, double timeoutSeconds = 0)
        {
            if (priority < -1000 || priority > 1000) throw new ArgumentOutOfRangeException(nameof(priority));
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            Priority = priority; TimeoutSeconds = timeoutSeconds;
        }
    }

    public readonly struct WarmOptions
    {
        public double TimeoutSeconds { get; }
        /// <summary>
        /// 构造预热期限选项；零表示使用池默认超时，负值或非有限数值无效。
        /// </summary>
        public WarmOptions(double timeoutSeconds)
        {
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            TimeoutSeconds = timeoutSeconds;
        }
    }

    public interface IObjectPool<T> where T : class
    {
        /// <summary>
        /// 仅借用已有空闲对象并将凭证归属于指定作用域；缓存不足时不创建对象。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="lease">成功时返回本次借用凭证；失败时为默认凭证。</param>
        /// <returns>是否成功取得并准备好一个缓存实例；失败时不会创建对象。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        bool TryRentCached(PoolScope scope, out PoolLease<T> lease);
        /// <summary>
        /// 允许在当前线程同步创建实例的借用入口，仍须遵守作用域、池状态和容量限制。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <returns>借用状态、成功凭证及可选诊断编号。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 同步创建不受逐帧创建时间目标保障。</remarks>
        RentResult<T> RentOrCreate(PoolScope scope);
        /// <summary>
        /// 提交支持优先级、期限和取消的有界异步借用请求，完成后应再次通过凭证确认有效性。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="options">本次请求的优先级与超时选项；零超时表示使用池默认值。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>最终借用结果；取消和超时以状态返回，不通过任务取消异常表达。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 await 恢复后仍应调用凭证 TryGet，确认作用域未在期间关闭。</remarks>
        Task<RentResult<T>> RentAsync(PoolScope scope, RentOptions options = default, CancellationToken cancellationToken = default);
        /// <summary>
        /// 提交一次目标预热任务，目标表示池中已借出与空闲实例的总量，而非额外创建数量。
        /// </summary>
        /// <param name="owner">承接本次预热任务的开放作用域。</param>
        /// <param name="targetReady">期望的已借出与空闲实例总量。</param>
        /// <param name="options">本次任务的超时选项。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>一次性预热的完成或部分完成结果。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        Task<WarmResult> PrewarmAsync(PoolScope owner, int targetReady, WarmOptions options = default, CancellationToken cancellationToken = default);
        /// <summary>
        /// 取得池状态、驻留计数、请求和操作成本等诊断快照。
        /// </summary>
        PoolSnapshot GetSnapshot();
        /// <summary>
        /// 停止新借用和预热，等待现有借用正常归还以及资源释放；重复调用返回同一关闭过程。
        /// </summary>
        /// <returns>幂等的关闭任务；释放失败时任务失败而池继续保持 Closing。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 不会擅自回收开放作用域的活动借用，关闭期间需要持续驱动服务。</remarks>
        Task CloseAsync();
        /// <summary>
        /// 在关闭因释放故障失败后显式重试；不得用于重复启动仍在进行中的关闭。
        /// </summary>
        /// <returns>新的关闭尝试任务，原失败任务保持失败。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 仅允许在上一关闭任务已经失败后调用。</remarks>
        Task RetryCloseAsync();
    }

    public interface IResetPolicy<T> where T : class
    {
        /// <summary>
        /// 归还时先解除对象的业务状态和外部引用；即使随后不再缓存，也必须执行必要清理。
        /// </summary>
        void Reset(T value);
        /// <summary>
        /// 在重置后判断实例是否适合保留，例如拒绝缓存超大列表或失效对象。
        /// </summary>
        bool CanRetain(T value);
    }

    public interface IPoolClock { double Now { get; } }
    public sealed class StopwatchPoolClock : IPoolClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public double Now => watch.Elapsed.TotalSeconds;
    }

    /// <summary>All methods are called on the service's owner thread. A returned instance is registered before InitializeOnce.</summary>
    public abstract class PoolAdapter<T> where T : class
    {
        public virtual ResourceState ResourceState => ResourceState.Ready;
        public virtual Exception ResourceError => null;
        public virtual long SharedResourceEstimatedBytes => 0;
        /// <summary>
        /// 开始适配器需要的资源加载；默认实现无需加载，异步加载由后续主线程轮询推进。
        /// </summary>
        public virtual void BeginLoad() { }
        /// <summary>
        /// 在主线程结算资源加载进度；默认无操作，自定义实现不能因单个请求取消而破坏共享加载。
        /// </summary>
        public virtual void PollResource() { }
        /// <summary>
        /// 创建并返回尚未做业务初始化的独立实例，供内核先登记所有权和计数。
        /// </summary>
        public abstract T Create();
        /// <summary>
        /// 实例登记完成后执行一次性基础初始化；默认无操作，失败时实例由内核负责退役。
        /// </summary>
        public virtual void InitializeOnce(T value) { }
        /// <summary>
        /// 判断实例是否仍可使用；默认只检查引用非空，Unity 适配器需覆盖其原生对象有效性语义。
        /// </summary>
        /// <param name="value">需要检查是否仍可使用的实例。</param>
        /// <returns>实例当前是否有效。</returns>
        /// <remarks>有效性检查应当轻量、无副作用且不抛异常；Unity 实现需识别原生对象已销毁的情况。</remarks>
        public virtual bool IsValid(T value) => value != null;
        /// <summary>
        /// 解除本次借用的引用、事件及业务状态；失败会使整实例退役。
        /// </summary>
        public abstract void Reset(T value);
        /// <summary>
        /// 判断重置后的实例是否值得缓存；默认允许，池内核还会检查状态与容量。
        /// </summary>
        public virtual bool CanRetain(T value) => true;
        /// <summary>
        /// 发出释放实例或外部资源的请求；正常返回仅代表请求步骤成功，最终配额释放由确认步骤决定。
        /// </summary>
        public abstract void RequestDestroy(T value);
        /// <summary>
        /// 确认实例释放是否完成；纯 C# 默认可立即确认，延迟销毁或资源句柄应覆盖此行为。
        /// </summary>
        /// <param name="value">正在等待释放确认的实例。</param>
        /// <param name="issuedFrame">释放请求发出时的服务调度帧编号。</param>
        /// <param name="currentFrame">当前服务调度帧编号。</param>
        /// <returns>是否可以清除实例持有并归还驻留配额。</returns>
        public virtual bool IsDestroyComplete(T value, long issuedFrame, long currentFrame) => true;
        /// <summary>
        /// 在池中实例及加载工作结算后释放池持有的资源引用；默认没有额外资源需要处理。
        /// </summary>
        public virtual void ReleaseResources() { }
    }
}

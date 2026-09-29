using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigWorld.Pooling
{
    public abstract class PoolBase
    {
        public PoolService Service { get; }
        public PoolKey Key { get; }
        public PoolConfig Config { get; protected set; }
        public PoolState State { get; protected set; } = PoolState.Running;
        /// <summary>
        /// 保存池所属服务、完整身份和只读配置，拒绝缺失服务或配置的构造参数。
        /// </summary>
        protected PoolBase(PoolService service, PoolKey key, PoolConfig config)
        { Service = service ?? throw new ArgumentNullException(nameof(service)); Key = key; Config = config ?? throw new ArgumentNullException(nameof(config)); }
        /// <summary>
        /// 由具体池提供当前状态、计数和成本快照，供服务或编辑器诊断使用。
        /// </summary>
        public abstract PoolSnapshot GetSnapshot();
        /// <summary>
        /// 由具体池停止准入并等待正常排空，完成后才能注销池或释放其外部宿主。
        /// </summary>
        /// <returns>幂等的关闭任务；释放失败时任务失败而池继续保持 Closing。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 不会擅自回收开放作用域的活动借用，关闭期间需要持续驱动服务。</remarks>
        public abstract Task CloseAsync();
        /// <summary>
        /// 由具体池为已经失败的关闭建立明确的释放重试流程。
        /// </summary>
        /// <returns>新的关闭尝试任务，原失败任务保持失败。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 仅允许在上一关闭任务已经失败后调用。</remarks>
        public abstract Task RetryCloseAsync();
        /// <summary>
        /// 更新池的只读配置；降低容量应限制后续准入并渐进回收空闲对象，不抢占活动借用。
        /// </summary>
        /// <param name="config">已经校验的只读运行时配置。</param>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 新上限可以暂时小于当前驻留量，活动对象继续使用，后续归还和维护逐步收敛。</remarks>
        public abstract void Reconfigure(PoolConfig config);
        /// <summary>
        /// 由具体池提交有限预热任务，并将请求归属于给定作用域。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="targetReady">期望达到的 Borrowed + Idle 总量，不是额外创建数量。</param>
        /// <param name="options">本次预热的超时设置；零表示使用池默认期限。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>包含目标、起止可用量、新建成功数量和结束原因的预热结果。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 预热不提供独占配额，也不会在完成后自动补齐被借走的对象。</remarks>
        public abstract Task<WarmResult> PrewarmAsync(PoolScope scope, int targetReady, WarmOptions options = default, CancellationToken cancellationToken = default);
        /// <summary>
        /// 执行一次受调度控制的维护步骤，处理回收、销毁确认或关闭推进。
        /// </summary>
        internal abstract bool MaintenanceStep();
        /// <summary>
        /// 推进一个指定类别的请求；warm 为真处理预热，否则处理业务借用。
        /// </summary>
        internal abstract bool RequestStep(bool warm);
        /// <summary>
        /// 尝试退役一个符合条件的空闲实例；不以活动借用作为普通淘汰对象。
        /// </summary>
        internal abstract bool EvictOne();
        /// <summary>
        /// 根据作用域保存的完整借用身份尝试归还，不允许用原始对象引用替代版本凭证。
        /// </summary>
        /// <param name="slot">需要结束借用的槽位编号。</param>
        /// <param name="version">凭证记录的原借用版本。</param>
        /// <returns>是否首次成功开始结束这一次借用。</returns>
        internal abstract bool ReturnScoped(int slot, ulong version);
        internal abstract int ResidentCount { get; }
        internal abstract int IdleCount { get; }
        internal abstract long RetiringEstimatedBytes { get; }
        internal abstract Exception ReleaseFault { get; }
    }

    public sealed class PoolService
    {
        private sealed class WarmPlanRegistration
        {
            internal Dictionary<PoolKey, int> Demand;
            internal CancellationTokenSource Lifetime;
            /// <summary>
            /// 取消本次计划提交产生的未完成预热任务，并释放其关联取消源；已经缓存的实例不受影响。
            /// </summary>
            internal void Cancel() { Lifetime.Cancel(); Lifetime.Dispose(); }
        }
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<PoolKey, PoolBase> lookup = new Dictionary<PoolKey, PoolBase>();
        private readonly List<PoolBase> pools = new List<PoolBase>();
        private readonly List<PoolScope> scopes = new List<PoolScope>();
        private readonly Dictionary<PoolScope, Dictionary<string, WarmPlanRegistration>> plans = new Dictionary<PoolScope, Dictionary<string, WarmPlanRegistration>>();
        private readonly ConcurrentQueue<Action> posted = new ConcurrentQueue<Action>();
        private readonly Queue<PoolDiagnostic> diagnostics = new Queue<PoolDiagnostic>();
        private int postCount;
        private long nextScopeId, diagnosticId;
        private int scopeCursor;
        private TaskCompletionSource<bool> closed;
        private bool poolsClosing;
        private bool ticking;
        private bool pressureRequested;
        private long pressureTarget;
        internal int CallbackDepth;
        public IPoolClock Clock { get; }
        public PoolScheduler Scheduler { get; }
        public PoolScope RootScope { get; }
        public PoolState State { get; private set; } = PoolState.Running;
        public int MaxResidentSlots { get; }
        public long MaxEstimatedResidentBytes { get; }
        public int ResidentSlots { get; private set; }
        public long EstimatedResidentBytes { get; private set; }
        public IReadOnlyList<PoolBase> Pools => pools.AsReadOnly();
        public IReadOnlyList<PoolScope> Scopes => scopes.AsReadOnly();
        public event Action<PoolDiagnostic> Diagnostic;

        /// <summary>
        /// 在当前线程建立服务、统一调度器和会话根作用域，并设置全局驻留数量及估算内存上限。
        /// </summary>
        /// <param name="budget">全局帧预算，传 null 时使用默认值并复制保存。</param>
        /// <param name="clock">单调时钟，传 null 时使用 Stopwatch；可注入测试时钟。</param>
        /// <param name="maxResidentSlots">全部池共享的驻留槽位上限，包含预留及尚未释放的实例。</param>
        /// <param name="maxEstimatedResidentBytes">全部实例增量估值的总上限，单位为字节，不重复累计共享资产。</param>
        public PoolService(PoolFrameBudget budget = null, IPoolClock clock = null, int maxResidentSlots = 100000, long maxEstimatedResidentBytes = 1024L * 1024 * 1024)
        {
            if (maxResidentSlots < 1 || maxEstimatedResidentBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxResidentSlots));
            Clock = clock ?? new StopwatchPoolClock(); MaxResidentSlots = maxResidentSlots; MaxEstimatedResidentBytes = maxEstimatedResidentBytes;
            Scheduler = new PoolScheduler(this, budget ?? new PoolFrameBudget());
            RootScope = new PoolScope(this, ++nextScopeId, "Session", null); scopes.Add(RootScope);
        }
        /// <summary>
        /// 确保当前调用发生在服务创建线程；跨线程修改池状态属于编程错误。
        /// </summary>
        public void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Pool state belongs to its creating thread. Use Post for cross-thread work.");
        }
        /// <summary>
        /// 校验作用域非空且属于本服务；是否仍开放由具体操作另行检查。
        /// </summary>
        internal void ValidateScope(PoolScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (!ReferenceEquals(scope.Service, this)) throw new ArgumentException("Scope belongs to another service.", nameof(scope));
        }
        /// <summary>
        /// 在开放父级下创建具有新身份的作用域，未指定父级时挂到会话根作用域。
        /// </summary>
        /// <param name="name">便于诊断的作用域名称，不能为空。</param>
        /// <param name="parent">所属父作用域；为 null 时使用根作用域。</param>
        /// <returns>具有独立身份、不可在关闭后重新开放的新作用域。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public PoolScope CreateScope(string name, PoolScope parent = null)
        {
            CheckThread(); parent = parent ?? RootScope; ValidateScope(parent);
            if (State != PoolState.Running || !parent.IsOpen) throw new InvalidOperationException("Cannot create a scope beneath a closing scope/service.");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A scope name is required.", nameof(name));
            var scope = new PoolScope(this, checked(++nextScopeId), name, parent); scopes.Add(scope); return scope;
        }
        /// <summary>
        /// 注册属于本服务且正在运行的池，拒绝重复完整池键；注册完成后池才能提供借用。
        /// </summary>
        /// <param name="pool">已构造且属于当前服务的运行中池。</param>
        /// <returns>原池引用，便于注册后直接缓存类型明确的入口。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 同一完整键不能重复注册，重建前须先关闭并注销旧池。</remarks>
        public TPool Register<TPool>(TPool pool) where TPool : PoolBase
        {
            CheckThread();
            if (pool == null || !ReferenceEquals(pool.Service, this)) throw new ArgumentException("Pool must belong to this service.");
            if (string.IsNullOrEmpty(pool.Key.Id)) throw new ArgumentException("Pool key is uninitialized.");
            if (State != PoolState.Running || pool.State != PoolState.Running) throw new InvalidOperationException("Service/pool is closing.");
            if (lookup.ContainsKey(pool.Key)) throw new InvalidOperationException("Duplicate pool registration: " + pool.Key);
            lookup.Add(pool.Key, pool); pools.Add(pool); return pool;
        }
        /// <summary>
        /// 确认池键对应的已注册对象就是当前池，防止未注册实例或已被替换的池参与操作。
        /// </summary>
        internal void CheckRegistered(PoolBase pool)
        {
            if (!lookup.TryGetValue(pool.Key, out var registered) || !ReferenceEquals(registered, pool)) throw new InvalidOperationException("Register the pool before using it.");
        }
        /// <summary>
        /// 按完整池键获取并转换为指定池类型；键不存在或类型不匹配时抛出错误。
        /// </summary>
        /// <param name="key">池种类、稳定标识和用途变体组成的完整池键。</param>
        /// <returns>转换为指定泛型池类型的已注册池引用。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public TPool GetPool<TPool>(PoolKey key) where TPool : PoolBase
        { CheckThread(); return (TPool)lookup[key]; }
        /// <summary>
        /// 检查当前服务是否已登记给定池键，包括尚未注销的已关闭池。
        /// </summary>
        public bool HasPool(PoolKey key) { CheckThread(); return lookup.ContainsKey(key); }
        /// <summary>
        /// 仅移除已经完成关闭的池，使同一配置键可以注册新的运行时池身份。
        /// </summary>
        public void UnregisterClosedPool(PoolKey key)
        {
            CheckThread();
            var pool = lookup[key];
            if (pool.State != PoolState.Closed) throw new InvalidOperationException("A pool must finish closing before it is unregistered.");
            lookup.Remove(key); pools.Remove(pool);
        }
        /// <summary>
        /// 按对象类型及用途变体创建并注册普通对象池；未提供配置时使用默认设置。
        /// </summary>
        /// <param name="factory">每次调用应创建独立对象的工厂。</param>
        /// <param name="reset">负责解除业务状态并决定是否保留对象的策略。</param>
        /// <param name="config">只读配置；为 null 时采用默认池设置。</param>
        /// <param name="variant">同一对象类型的用途或变体标识。</param>
        /// <param name="destroy">可选外部资源释放回调；为 null 时仅解除池持有。</param>
        /// <returns>已完成注册的普通对象池。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public DataPool<T> RegisterData<T>(Func<T> factory, IResetPolicy<T> reset, PoolConfig config = null, string variant = "", Action<T> destroy = null) where T : class
            => Register(new DataPool<T>(this, PoolKey.ForData<T>(variant), config ?? new PoolSettings().Freeze(), factory, reset, destroy));
        /// <summary>
        /// 在全局数量和估算内存都允许时预留一个驻留槽位，防止创建过程临时透支配额。
        /// </summary>
        internal bool Reserve(long cost)
        {
            if (ResidentSlots >= MaxResidentSlots || cost > MaxEstimatedResidentBytes - EstimatedResidentBytes) return false;
            ResidentSlots++; EstimatedResidentBytes += cost; return true;
        }
        /// <summary>
        /// 在创建预留撤销或实例确认释放后归还一个全局槽位及其估算内存。
        /// </summary>
        internal void Release(long cost) { ResidentSlots--; EstimatedResidentBytes -= cost; }
        /// <summary>
        /// 在运行时配置变更后调整全局驻留内存估值，并使用受检运算避免数值溢出。
        /// </summary>
        internal void ChangeEstimate(long delta) { EstimatedResidentBytes = checked(EstimatedResidentBytes + delta); }
        /// <summary>
        /// 保存带唯一编号的原始异常并通知诊断监听者；仅保留最近 128 条，监听者异常不会破坏池账目。
        /// </summary>
        internal long Report(PoolKey key, string operation, Exception error)
        {
            var item = new PoolDiagnostic { Id = ++diagnosticId, Key = key, Operation = operation, Exception = error };
            if (diagnostics.Count == 128) diagnostics.Dequeue(); diagnostics.Enqueue(item);
            try { Diagnostic?.Invoke(item); } catch { /* Diagnostic listeners cannot corrupt lifecycle accounting. */ }
            return item.Id;
        }
        /// <summary>
        /// 复制当前保留的诊断记录，便于按结果中的诊断编号追踪原始异常。
        /// </summary>
        public PoolDiagnostic[] GetDiagnostics() { CheckThread(); return diagnostics.ToArray(); }
        /// <summary>
        /// 从任意线程向有界队列投递主线程工作；队列已满时返回失败，由调用方决定后续处理。
        /// </summary>
        /// <param name="action">稍后在服务拥有者线程执行的工作，不在投递线程内立即运行。</param>
        /// <returns>是否成功入队；队列满时返回 false 且不执行工作。</returns>
        /// <remarks>本接口允许后台线程调用；调用方必须处理入队失败，并确保需要执行的任务期间服务仍持续 Tick。</remarks>
        public bool Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Interlocked.Increment(ref postCount) > 4096) { Interlocked.Decrement(ref postCount); return false; }
            posted.Enqueue(action); return true;
        }
        /// <summary>
        /// 在服务拥有者线程驱动一帧全局调度并推进服务关闭；禁止嵌套调用或在生命周期回调内重入。
        /// </summary>
        public void Tick()
        {
            CheckThread();
            if (ticking || CallbackDepth != 0) throw new InvalidOperationException("PoolService.Tick cannot be reentered.");
            if (State == PoolState.Closed) return;
            ticking = true;
            try { Scheduler.Tick(); AdvanceClose(); }
            finally { ticking = false; }
        }
        /// <summary>
        /// 执行最多一项已投递的主线程工作并释放队列名额；工作异常会记录诊断。
        /// </summary>
        internal bool ControlStep()
        {
            if (posted.TryDequeue(out var action))
            {
                Interlocked.Decrement(ref postCount);
                try { action(); } catch (Exception ex) { Report(default, "Posted work", ex); }
                return true;
            }
            return false;
        }
        /// <summary>
        /// 轮询一个作用域推进关闭，已关闭作用域从活动列表移除，避免一次遍历回收整棵树。
        /// </summary>
        internal bool ScopeStep()
        {
            if (scopes.Count == 0) return false;
            if (scopeCursor >= scopes.Count) scopeCursor = 0;
            var scope = scopes[scopeCursor]; var changed = scope.StepClose();
            if (scope.State == ScopeState.Closed) scopes.RemoveAt(scopeCursor); else scopeCursor++;
            return changed;
        }
        /// <summary>
        /// 提交估算驻留内存目标，后续调度逐步淘汰空闲实例，不同步销毁活动对象。
        /// </summary>
        /// <param name="targetResidentBytes">希望逐步收敛到的实例驻留估值，单位为字节，不能为负数。</param>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 只能回收可用空闲实例；活动借用、延迟销毁和释放故障可能使目标暂时无法达到。</remarks>
        public void RequestMemoryPressure(long targetResidentBytes)
        {
            CheckThread(); if (targetResidentBytes < 0) throw new ArgumentOutOfRangeException(nameof(targetResidentBytes));
            pressureTarget = targetResidentBytes; pressureRequested = true;
        }
        /// <summary>
        /// 优先从低保留优先级池退役一个空闲实例，并考虑已排队销毁的估值以避免过度淘汰。
        /// </summary>
        internal bool PressureStep()
        {
            if (!pressureRequested) return false;
            if (EstimatedResidentBytes <= pressureTarget) { pressureRequested = false; return false; }
            PoolBase candidate = null; long pendingBytes = 0;
            foreach (var pool in pools)
            {
                // Pending retirement remains charged, but should not cause excessive extra evictions.
                pendingBytes += pool.RetiringEstimatedBytes;
                if (pool.IdleCount > 0 && (candidate == null || pool.Config.RetentionPriority < candidate.Config.RetentionPriority)) candidate = pool;
            }
            if (EstimatedResidentBytes - pendingBytes <= pressureTarget) return false;
            if (candidate == null) { pressureRequested = false; return false; }
            return candidate.EvictOne();
        }
        /// <summary>
        /// 登记或更新作用域预热计划，取消同名旧任务，并按合并后的池级需求提交有限预热。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="planId">在当前作用域内稳定且非空的计划身份，同名提交表示更新。</param>
        /// <param name="demand">完整池键到该作用域预计并发需求的映射；调用时复制保存。</param>
        /// <param name="options">各池本次预热任务的期限设置。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>本计划各个池的预热任务数组，可使用 Task.WhenAll 等待。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 同一作用域计划取最大值，独立作用域需求相加；更新会取消同名旧任务。</remarks>
        public Task<WarmResult>[] SubmitWarmPlan(PoolScope scope, string planId, IReadOnlyDictionary<PoolKey, int> demand, WarmOptions options = default, CancellationToken cancellationToken = default)
        {
            CheckThread(); ValidateScope(scope);
            if (!scope.IsOpen || State != PoolState.Running) throw new InvalidOperationException("Scope/service is closing.");
            if (string.IsNullOrEmpty(planId) || demand == null) throw new ArgumentException("Plan id and demand are required.");
            var copy = new Dictionary<PoolKey, int>();
            foreach (var item in demand)
            {
                if (!lookup.ContainsKey(item.Key) || item.Value < 0) throw new ArgumentException("Unknown pool or negative warm demand.");
                copy.Add(item.Key, item.Value);
            }
            if (!plans.TryGetValue(scope, out var scopePlans)) { scopePlans = new Dictionary<string, WarmPlanRegistration>(); plans.Add(scope, scopePlans); }
            if (scopePlans.TryGetValue(planId, out var previous)) previous.Cancel();
            var registration = new WarmPlanRegistration { Demand = copy, Lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) };
            scopePlans[planId] = registration;
            var tasks = new Task<WarmResult>[copy.Count]; int i = 0;
            foreach (var item in copy) tasks[i++] = lookup[item.Key].PrewarmAsync(scope, GetWarmTarget(item.Key), options, registration.Lifetime.Token);
            return tasks;
        }
        /// <summary>
        /// 为配置了基础预热目标的池显式提交一次准备任务；结合已登记需求，不建立自动补货循环。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="options">本轮预热的期限设置。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>所有具有正基础目标的池所提交的预热任务；没有目标时返回空数组。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public Task<WarmResult>[] PrewarmDefaultsAsync(PoolScope scope, WarmOptions options = default, CancellationToken cancellationToken = default)
        {
            CheckThread(); ValidateScope(scope);
            var tasks = new List<Task<WarmResult>>();
            foreach (var pool in pools) if (pool.Config.BasePrewarmTargetReady > 0) tasks.Add(pool.PrewarmAsync(scope, GetWarmTarget(pool.Key), options, cancellationToken));
            return tasks.ToArray();
        }
        /// <summary>
        /// 计算指定池的准备目标：同作用域计划取最大值，开放独立作用域求和，再与基础目标取最大值。
        /// </summary>
        public int GetWarmTarget(PoolKey key)
        {
            CheckThread(); long total = 0;
            foreach (var scopePlans in plans)
            {
                if (!scopePlans.Key.IsOpen) continue;
                int scopeMax = 0;
                foreach (var plan in scopePlans.Value.Values) if (plan.Demand.TryGetValue(key, out int value)) scopeMax = Math.Max(scopeMax, value);
                total += scopeMax;
            }
            return (int)Math.Min(int.MaxValue, Math.Max(lookup[key].Config.BasePrewarmTargetReady, total));
        }
        /// <summary>
        /// 移除指定作用域的一项计划并取消其未完成任务；共享空闲实例按正常策略保留。
        /// </summary>
        public void RemoveWarmPlan(PoolScope scope, string planId)
        {
            CheckThread(); ValidateScope(scope);
            if (plans.TryGetValue(scope, out var registered))
            {
                if (registered.TryGetValue(planId, out var plan)) { plan.Cancel(); registered.Remove(planId); }
                if (registered.Count == 0) plans.Remove(scope);
            }
        }
        /// <summary>
        /// 移除作用域的全部预热需求并取消对应任务，供作用域关闭流程调用。
        /// </summary>
        internal void RemovePlans(PoolScope scope)
        {
            if (!plans.TryGetValue(scope, out var registered)) return;
            foreach (var plan in registered.Values) plan.Cancel();
            plans.Remove(scope);
        }
        /// <summary>
        /// 启动幂等的全局关闭，先使根作用域及子树失效，再由调度排空池；完成前必须继续驱动 Tick。
        /// </summary>
        /// <returns>全局关闭任务，全部作用域和池排空后成功；释放故障会导致任务失败。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 生命周期回调内禁止同步发起服务关闭，应通过 Post 投递。</remarks>
        public Task CloseAsync()
        {
            CheckThread();
            if (CallbackDepth != 0) throw new InvalidOperationException("Post service shutdown after lifecycle callbacks.");
            if (closed != null) return closed.Task;
            closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            State = PoolState.Closing; RootScope.CloseAsync(); return closed.Task;
        }
        /// <summary>
        /// 在上一次全局关闭失败后建立新的关闭任务，并对存在释放故障的池发起重试。
        /// </summary>
        /// <returns>本次重试的关闭任务。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 仅在先前关闭失败且故障已处理后使用，驱动仍需保持运行。</remarks>
        public Task RetryCloseAsync()
        {
            CheckThread();
            if (closed == null || !closed.Task.IsFaulted) throw new InvalidOperationException("Only a failed close can be retried.");
            closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var pool in pools) if (pool.ReleaseFault != null) pool.RetryCloseAsync();
            return closed.Task;
        }
        /// <summary>
        /// 协调全局关闭：先报告释放故障，等待根作用域结束，再关闭全部池，最后提交服务关闭完成。
        /// </summary>
        private void AdvanceClose()
        {
            if (State != PoolState.Closing || closed.Task.IsCompleted) return;
            foreach (var pool in pools) if (pool.ReleaseFault != null) { pool.CloseAsync(); closed.TrySetException(pool.ReleaseFault); return; }
            if (RootScope.State != ScopeState.Closed) return;
            if (!poolsClosing) { poolsClosing = true; foreach (var pool in pools) pool.CloseAsync(); }
            foreach (var pool in pools) if (pool.State != PoolState.Closed) return;
            State = PoolState.Closed; closed.TrySetResult(true);
        }
        internal List<PoolBase> PoolList => pools;
    }

    public sealed class PoolScheduler
    {
        private readonly PoolService service;
        private readonly PoolFrameBudget budget;
        private readonly Stopwatch watch = new Stopwatch();
        private int poolCursor;
        private int phase;
        public long Frame { get; private set; }
        public int CreatesThisFrame { get; private set; }
        public int DestroyRequestsThisFrame { get; private set; }
        public int MaintenanceStepsThisFrame { get; private set; }
        public double WorkMilliseconds { get; private set; }
        internal bool HasCreateBudget => CreatesThisFrame < budget.MaxCreatesPerFrame;
        /// <summary>
        /// 保存所属服务并复制、校验全局帧预算，避免外部后续修改原预算对象。
        /// </summary>
        internal PoolScheduler(PoolService service, PoolFrameBudget budget) { this.service = service; this.budget = budget.CopyValidated(); }
        /// <summary>
        /// 尝试占用本帧的一个创建名额，达到全局数量上限时返回失败。
        /// </summary>
        internal bool TryCreate() { if (CreatesThisFrame >= budget.MaxCreatesPerFrame) return false; CreatesThisFrame++; return true; }
        /// <summary>
        /// 尝试占用本帧的一个销毁请求名额，供全部池共享，而不是每池独立分配。
        /// </summary>
        internal bool TryDestroy() { if (DestroyRequestsThisFrame >= budget.MaxDestroyRequestsPerFrame) return false; DestroyRequestsThisFrame++; return true; }
        /// <summary>
        /// 交错执行控制、作用域、维护、借用、预热和压力回收，受数量及时间预算约束，并跨帧保留轮询进度。
        /// </summary>
        internal void Tick()
        {
            Frame++; CreatesThisFrame = 0; DestroyRequestsThisFrame = 0; MaintenanceStepsThisFrame = 0; watch.Restart();
            // One bounded control/maintenance unit always runs. Phases persist across frames, including tiny time budgets.
            do
            {
                switch (phase)
                {
                    case 0: service.ControlStep(); break;
                    case 1: service.ScopeStep(); break;
                    case 2: if (service.PoolList.Count != 0) CurrentPool().MaintenanceStep(); break;
                    case 3: if (service.PoolList.Count != 0) CurrentPool().RequestStep(false); break;
                    case 4: if (service.PoolList.Count != 0) CurrentPool().RequestStep(true); break;
                    case 5: service.PressureStep(); poolCursor++; break;
                }
                phase = (phase + 1) % 6; MaintenanceStepsThisFrame++;
            } while (MaintenanceStepsThisFrame < budget.MaxMaintenanceStepsPerFrame && watch.Elapsed.TotalMilliseconds < budget.MaxWorkMilliseconds);
            watch.Stop(); WorkMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
        /// <summary>
        /// 按当前轮询游标取得池，游标越过列表末尾时回绕；调用前要求至少存在一个池。
        /// </summary>
        private PoolBase CurrentPool() { if (poolCursor >= service.PoolList.Count) poolCursor = 0; return service.PoolList[poolCursor]; }
    }
}

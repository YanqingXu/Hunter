using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigWorld.Pooling
{
    /// <summary>Common identity/accounting engine; public typed entry points live on DataPool and PrefabPool.</summary>
    public abstract class ManagedPool<T, TArgs> : PoolBase, ILeaseOwner<T> where T : class
    {
        private sealed class Slot
        {
            internal T Value;
            internal ulong Version;
            internal SlotState State;
            internal PoolScope Scope;
            internal PoolScope RetirementOwner;
            internal int Previous = -1, Next = -1, IdleIndex = -1;
            internal double IdleSince;
            internal long DestroyFrame;
            internal bool DestroyWasIssued;
            internal Exception Fault;
        }
        private sealed class Pending
        {
            internal PoolScope Scope;
            internal CancellationToken Cancellation;
            internal double Started, Deadline;
            internal int Priority;
            internal bool Warm;
            internal TArgs Args;
            internal int Target, StartReady, Created;
            internal PoolStatus Waiting = PoolStatus.ResourceNotReady;
            internal TaskCompletionSource<RentResult<T>> RentCompletion;
            internal TaskCompletionSource<WarmResult> WarmCompletion;
        }
        private readonly List<Slot> slots;
        private readonly Stack<int> free;
        private readonly List<int> idleStack;
        private readonly List<Pending> requests;
        private readonly int[] counts = new int[10];
        private readonly long[] results = new long[Enum.GetValues(typeof(PoolStatus)).Length];
        private readonly bool orderedIdle;
        private int idleHead = -1, idleTail = -1, maintenanceCursor;
        private bool callback;
        private TaskCompletionSource<bool> closeCompletion;
        private Exception resourceFault, closeFault;
        private long resourceDiagnostic;
        private int peakBorrowed;
        private long cacheHits, rents, syncCreates, asyncCreates, warmCreates;
        private double queueSeconds, createMs, prepareMs, returnMs, destroyMs, maintenanceMs;
        protected PoolAdapter<T> Adapter { get; }
        internal override int ResidentCount => counts[1] + counts[2] + counts[3] + counts[4] + counts[5] + counts[6] + counts[7] + counts[8];
        internal override int IdleCount => counts[(int)SlotState.Idle];
        internal override long RetiringEstimatedBytes => (long)(counts[6] + counts[7]) * Config.EstimatedBytesPerInstance;
        internal override Exception ReleaseFault
        {
            get
            {
                if (closeFault != null) return closeFault;
                if (counts[(int)SlotState.DestroyFaulted] == 0) return null;
                foreach (var slot in slots) if (slot.Fault != null) return slot.Fault;
                return null;
            }
        }
        /// <summary>
        /// 构造统一的身份与计数内核，预留槽位、空闲索引和请求容器；可选择栈式或按归还时间排序的空闲存储。
        /// </summary>
        protected ManagedPool(PoolService service, PoolKey key, PoolConfig config, PoolAdapter<T> adapter, bool orderedIdle = false) : base(service, key, config)
        {
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter)); this.orderedIdle = orderedIdle;
            slots = new List<Slot>(config.InitialStorageCapacity); free = new Stack<int>(config.InitialStorageCapacity);
            idleStack = new List<int>(config.InitialStorageCapacity); requests = new List<Pending>(Math.Min(config.MaxPendingRequests, 64));
        }
        /// <summary>
        /// 检查调用线程、池注册关系和生命周期重入；违反任一约束时抛出编程错误。
        /// </summary>
        private void CheckOperation()
        {
            Service.CheckThread(); Service.CheckRegistered(this);
            if (callback) throw new InvalidOperationException("Lifecycle callbacks cannot reenter the same pool. Use PoolService.Post.");
        }
        /// <summary>
        /// 进入适配器或业务回调阶段，标记同池重入禁区并增加服务的回调深度。
        /// </summary>
        private void BeginCallback() { callback = true; Service.CallbackDepth++; }
        /// <summary>
        /// 退出回调阶段，恢复重入标记和服务回调深度；必须与进入操作成对调用。
        /// </summary>
        private void EndCallback() { Service.CallbackDepth--; callback = false; }
        /// <summary>
        /// 将 Stopwatch 起始时间戳到当前时刻的差值转换为毫秒，用于操作耗时统计。
        /// </summary>
        private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        /// <summary>
        /// 切换槽位状态并同步扣减旧状态、增加新状态的计数；已释放槽位不计入驻留。
        /// </summary>
        private void Transition(Slot slot, SlotState state)
        {
            if (slot.State != SlotState.Released) counts[(int)slot.State]--;
            slot.State = state;
            if (state != SlotState.Released) counts[(int)state]++;
        }
        /// <summary>
        /// 由具体池写入本次借用所需的类型化参数；此时槽位处于准备中，尚未交付凭证。
        /// </summary>
        protected abstract void PrepareForUse(T value, TArgs args);
        /// <summary>
        /// 在准备和准入检查通过后执行最终启用步骤；普通对象无需处理，Unity 池在此激活实例。
        /// </summary>
        protected virtual void Activate(T value) { }
        /// <summary>
        /// 检查操作合法性后执行仅缓存借用，并记录结果；不会因缓存不足而同步创建。
        /// </summary>
        protected RentResult<T> RentCached(PoolScope scope, TArgs args)
        {
            CheckOperation(); Service.ValidateScope(scope);
            return CountResult(Rent(scope, args, false, false, default, double.PositiveInfinity));
        }
        /// <summary>
        /// 执行允许同步创建的借用流程并记录结果；此路径不消耗逐帧创建预算，但仍遵守驻留配额。
        /// </summary>
        protected RentResult<T> RentSynchronously(PoolScope scope, TArgs args)
        {
            CheckOperation(); Service.ValidateScope(scope);
            return CountResult(Rent(scope, args, true, false, default, double.PositiveInfinity));
        }
        /// <summary>
        /// 校验准入和队列长度，登记带优先级及截止时间的借用请求，同时增加作用域的待处理计数。
        /// </summary>
        protected Task<RentResult<T>> EnqueueRent(PoolScope scope, TArgs args, RentOptions options, CancellationToken token)
        {
            CheckOperation(); Service.ValidateScope(scope);
            var status = Gate(scope, token, double.PositiveInfinity);
            if (status != PoolStatus.Success) return Task.FromResult(CountResult(new RentResult<T>(status)));
            if (requests.Count >= Config.MaxPendingRequests) return Task.FromResult(CountResult(new RentResult<T>(PoolStatus.QueueFull)));
            var request = new Pending { Scope = scope, Args = args, Cancellation = token, Started = Service.Clock.Now,
                Deadline = Service.Clock.Now + (options.TimeoutSeconds > 0 ? options.TimeoutSeconds : Config.RequestTimeoutSeconds), Priority = options.Priority,
                RentCompletion = new TaskCompletionSource<RentResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously) };
            scope.PendingRequests++; requests.Add(request); return request.RentCompletion.Task;
        }
        /// <summary>
        /// 提交一次有限的目标预热任务；目标统计已借出与空闲实例，结束后不会持续补货。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="targetReady">期望达到的 Borrowed + Idle 总量，不是额外创建数量。</param>
        /// <param name="options">本次预热的超时设置；零表示使用池默认期限。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>包含目标、起止可用量、新建成功数量和结束原因的预热结果。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 预热不提供独占配额，也不会在完成后自动补齐被借走的对象。</remarks>
        public override Task<WarmResult> PrewarmAsync(PoolScope scope, int targetReady, WarmOptions options = default, CancellationToken cancellationToken = default)
        {
            CheckOperation(); Service.ValidateScope(scope);
            if (targetReady < 0) throw new ArgumentOutOfRangeException(nameof(targetReady));
            var request = new Pending { Warm = true, Scope = scope, Target = targetReady, StartReady = Ready, Cancellation = cancellationToken,
                Started = Service.Clock.Now, Deadline = Service.Clock.Now + (options.TimeoutSeconds > 0 ? options.TimeoutSeconds : Config.RequestTimeoutSeconds),
                WarmCompletion = new TaskCompletionSource<WarmResult>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var status = Gate(scope, cancellationToken, double.PositiveInfinity);
            if (status != PoolStatus.Success) { request.WarmCompletion.SetResult(WarmOutcome(request, ToWarmReason(status))); return request.WarmCompletion.Task; }
            if (Ready >= targetReady) { request.WarmCompletion.SetResult(WarmOutcome(request, WarmEndReason.ReachedTarget)); return request.WarmCompletion.Task; }
            if (requests.Count >= Config.MaxPendingRequests) { request.WarmCompletion.SetResult(WarmOutcome(request, WarmEndReason.QueueFull)); return request.WarmCompletion.Task; }
            scope.PendingRequests++; requests.Add(request); return request.WarmCompletion.Task;
        }
        private int Ready => counts[(int)SlotState.Borrowed] + IdleCount;
        /// <summary>
        /// 依次检查作用域、池与服务状态、取消信号和截止时间，返回当前请求能否继续执行。
        /// </summary>
        private PoolStatus Gate(PoolScope scope, CancellationToken token, double deadline)
        {
            if (!scope.IsOpen) return PoolStatus.ScopeClosed;
            if (State != PoolState.Running || Service.State != PoolState.Running) return PoolStatus.PoolClosed;
            if (token.IsCancellationRequested) return PoolStatus.Cancelled;
            if (Service.Clock.Now >= deadline) return PoolStatus.TimedOut;
            return PoolStatus.Success;
        }
        /// <summary>
        /// 按状态累加一次最终借用结果，并原样返回该结果；等待中的重复尝试不在此计数。
        /// </summary>
        private RentResult<T> CountResult(RentResult<T> result) { results[(int)result.Status]++; return result; }
        /// <summary>
        /// 必要时启动资源加载，将加载中和加载失败转换为请求状态，并保存原始异常的诊断编号。
        /// </summary>
        private PoolStatus ResourceReady()
        {
            if (resourceFault != null) return PoolStatus.LoadFailed;
            try
            {
                BeginCallback();
                if (Adapter.ResourceState == ResourceState.NotLoaded) Adapter.BeginLoad();
                if (Adapter.ResourceState == ResourceState.Failed)
                {
                    resourceFault = Adapter.ResourceError ?? new InvalidOperationException("Resource loading failed.");
                    resourceDiagnostic = Service.Report(Key, "Load", resourceFault); return PoolStatus.LoadFailed;
                }
                return Adapter.ResourceState == ResourceState.Ready ? PoolStatus.Success : PoolStatus.ResourceNotReady;
            }
            catch (Exception ex) { resourceFault = ex; resourceDiagnostic = Service.Report(Key, "Load", ex); return PoolStatus.LoadFailed; }
            finally { EndCallback(); }
        }
        /// <summary>
        /// 执行缓存获取或创建、类型化准备、激活和最终校验；成功后递增版本并登记借用，失败则清理退役。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="args">本次借用的类型化业务参数，不应依赖激活回调补写。</param>
        /// <param name="allowCreate">缓存不足时是否允许创建新实例。</param>
        /// <param name="scheduled">是否由调度器驱动，决定创建是否占用本帧预算。</param>
        /// <param name="token">本次请求的取消令牌。</param>
        /// <param name="deadline">基于服务单调时钟的绝对截止时刻，单位为秒。</param>
        /// <returns>成功凭证或明确失败状态；准备失败的实例由当前流程清理并退役。</returns>
        private RentResult<T> Rent(PoolScope scope, TArgs args, bool allowCreate, bool scheduled, CancellationToken token, double deadline)
        {
            var gate = Gate(scope, token, deadline);
            if (gate != PoolStatus.Success) return new RentResult<T>(gate);
            if (counts[(int)SlotState.Borrowed] + counts[(int)SlotState.Preparing] + counts[(int)SlotState.CreateReserved] >= Config.MaxBorrowed)
                return new RentResult<T>(PoolStatus.CapacityExceeded);
            int index = TakeCached(); bool cached = index >= 0;
            if (index < 0)
            {
                if (!allowCreate) return new RentResult<T>(PoolStatus.CacheMiss);
                var resource = ResourceReady();
                if (resource != PoolStatus.Success) return new RentResult<T>(resource, diagnosticId: resourceDiagnostic);
                var created = Create(scope, scheduled, false, out index, out var diagnostic);
                if (created != PoolStatus.Success) return new RentResult<T>(created, diagnosticId: diagnostic);
            }
            var slot = slots[index];
            long started = Stopwatch.GetTimestamp();
            try
            {
                BeginCallback(); PrepareForUse(slot.Value, args);
                gate = Gate(scope, token, deadline);
                if (gate == PoolStatus.Success && Adapter.IsValid(slot.Value)) Activate(slot.Value);
                else if (gate == PoolStatus.Success) throw new InvalidOperationException("Instance became invalid during preparation.");
            }
            catch (Exception ex)
            {
                long diagnostic = Service.Report(Key, "Prepare/activate", ex);
                EndCallback(); prepareMs += Elapsed(started); CleanupAndRetire(index, scope);
                return new RentResult<T>(PoolStatus.InitializationFailed, diagnosticId: diagnostic);
            }
            EndCallback(); prepareMs += Elapsed(started);
            gate = Gate(scope, token, deadline);
            if (gate != PoolStatus.Success || !Adapter.IsValid(slot.Value))
            {
                CleanupAndRetire(index, scope);
                return new RentResult<T>(gate != PoolStatus.Success ? gate : PoolStatus.InitializationFailed);
            }
            // Saturated versions are retired by TakeCached; newly allocated slots start at zero.
            slot.Version++; slot.Scope = scope; Transition(slot, SlotState.Borrowed); scope.Add(this, index, slot.Version);
            peakBorrowed = Math.Max(peakBorrowed, counts[(int)SlotState.Borrowed]); rents++; if (cached) cacheHits++;
            return new RentResult<T>(PoolStatus.Success, new PoolLease<T>(this, index, slot.Version));
        }
        /// <summary>
        /// 先预留全局驻留配额，再创建并登记实例，最后执行一次性初始化；任何失败都保留正确的释放责任。
        /// </summary>
        /// <param name="scope">创建失败或取消时承担实例退役责任的作用域。</param>
        /// <param name="scheduled">是否检查并消耗全局本帧创建预算。</param>
        /// <param name="warm">是否为预热创建，决定创建统计的类别。</param>
        /// <param name="index">输出已分配的槽位编号；应仅在返回成功时使用其准备中的实例。</param>
        /// <param name="diagnostic">创建或初始化失败的诊断编号，无异常时为零。</param>
        /// <returns>创建与基础初始化的状态；失败可能仍留下等待销毁的已登记实例。</returns>
        private PoolStatus Create(PoolScope scope, bool scheduled, bool warm, out int index, out long diagnostic)
        {
            index = -1; diagnostic = 0;
            if (ResidentCount >= Config.MaxResident) return PoolStatus.CapacityExceeded;
            if (scheduled && !Service.Scheduler.HasCreateBudget) return PoolStatus.ResourceNotReady; // WaitingBudget, set by caller.
            if (!Service.Reserve(Config.EstimatedBytesPerInstance)) return PoolStatus.GlobalBudgetExceeded;
            if (scheduled) Service.Scheduler.TryCreate();
            index = free.Count != 0 ? free.Pop() : slots.Count;
            if (index == slots.Count) slots.Add(new Slot());
            var slot = slots[index]; Transition(slot, SlotState.CreateReserved);
            long started = Stopwatch.GetTimestamp();
            try { BeginCallback(); slot.Value = Adapter.Create(); if (ReferenceEquals(slot.Value, null)) throw new InvalidOperationException("Factory returned null."); }
            catch (Exception ex)
            {
                diagnostic = Service.Report(Key, "Create", ex); EndCallback(); createMs += Elapsed(started);
                ReleaseSlot(index); return PoolStatus.CreationFailed;
            }
            EndCallback(); createMs += Elapsed(started);
            Transition(slot, SlotState.Preparing);
            if (warm) warmCreates++; else if (scheduled) asyncCreates++; else syncCreates++;
            started = Stopwatch.GetTimestamp();
            try
            {
                BeginCallback(); Adapter.InitializeOnce(slot.Value);
                if (!Adapter.IsValid(slot.Value)) throw new InvalidOperationException("Instance invalid after initialization.");
            }
            catch (Exception ex)
            {
                diagnostic = Service.Report(Key, "InitializeOnce", ex); EndCallback(); prepareMs += Elapsed(started);
                CleanupAndRetire(index, scope); return PoolStatus.InitializationFailed;
            }
            EndCallback(); prepareMs += Elapsed(started); return PoolStatus.Success;
        }
        /// <summary>
        /// 在最大探测次数内取出最近归还的有效槽位；失效实例或版本耗尽的槽位进入退役流程。
        /// </summary>
        private int TakeCached()
        {
            for (int probe = 0; probe < Config.MaxCacheProbes && IdleCount > 0; probe++)
            {
                int index = orderedIdle ? idleTail : idleStack[idleStack.Count - 1];
                var slot = slots[index]; RemoveIdle(index);
                if (slot.Version == ulong.MaxValue || !Adapter.IsValid(slot.Value)) { Retire(index, null); continue; }
                Transition(slot, SlotState.Preparing); return index;
            }
            return -1;
        }
        /// <summary>
        /// 将槽位提交为空闲状态并记录单调时钟时间，按配置加入空闲索引栈或链表尾部。
        /// </summary>
        private void AddIdle(int index)
        {
            var slot = slots[index]; slot.IdleSince = Service.Clock.Now; Transition(slot, SlotState.Idle);
            if (!orderedIdle) { slot.IdleIndex = idleStack.Count; idleStack.Add(index); return; }
            slot.Previous = idleTail; slot.Next = -1;
            if (idleTail != -1) slots[idleTail].Next = index; else idleHead = index;
            idleTail = index;
        }
        /// <summary>
        /// 仅从空闲存储中摘除指定槽位；状态和计数由后续准备或退役操作统一切换。
        /// </summary>
        /// <param name="index">当前确实处于空闲存储中的槽位编号。</param>
        /// <remarks>本方法不切换槽位状态，调用方必须随后提交 Preparing 或 PendingDestroy，保持计数与存储一致。</remarks>
        private void RemoveIdle(int index)
        {
            var slot = slots[index];
            if (!orderedIdle)
            {
                int last = idleStack[idleStack.Count - 1]; idleStack[slot.IdleIndex] = last; slots[last].IdleIndex = slot.IdleIndex;
                idleStack.RemoveAt(idleStack.Count - 1); slot.IdleIndex = -1;
            }
            else
            {
                if (slot.Previous != -1) slots[slot.Previous].Next = slot.Next; else idleHead = slot.Next;
                if (slot.Next != -1) slots[slot.Next].Previous = slot.Previous; else idleTail = slot.Previous;
                slot.Previous = slot.Next = -1;
            }
        }
        /// <summary>
        /// 验证槽位、借用版本、借出状态、作用域和实例有效性；全部匹配后才返回对象。
        /// </summary>
        /// <param name="index">池内稳定槽位编号。</param>
        /// <param name="version">凭证保存的借用版本。</param>
        /// <param name="value">成功时返回有效对象；校验不通过时为 null。</param>
        /// <returns>该槽位是否仍对应这一次有效借用。</returns>
        bool ILeaseOwner<T>.TryGet(int index, ulong version, out T value)
        {
            Service.CheckThread(); value = null;
            if (index < 0 || index >= slots.Count) return false;
            var slot = slots[index];
            if (slot.Version != version || slot.State != SlotState.Borrowed || !slot.Scope.IsOpen || !Adapter.IsValid(slot.Value)) return false;
            value = slot.Value; return true;
        }
        /// <summary>
        /// 将凭证归还请求转交统一归还流程；旧版本或重复归还不会再次触发清理。
        /// </summary>
        /// <param name="index">需要结束借用的槽位编号。</param>
        /// <param name="version">凭证记录的原借用版本。</param>
        /// <returns>是否成功开始归还；失败不会执行重置或改变新借用的状态。</returns>
        bool ILeaseOwner<T>.TryReturn(int index, ulong version) => ReturnScoped(index, version);
        /// <summary>
        /// 先使匹配借用失效并解除作用域登记，再重置对象；可保留则入缓存，否则保持释放责任直到销毁确认。
        /// </summary>
        /// <param name="index">需要结束借用的槽位编号。</param>
        /// <param name="version">凭证记录的原借用版本。</param>
        /// <returns>是否成功开始归还；失败不会执行重置或改变新借用的状态。</returns>
        internal override bool ReturnScoped(int index, ulong version)
        {
            Service.CheckThread();
            if (index < 0 || index >= slots.Count) return false;
            var slot = slots[index];
            if (slot.Version != version || slot.State != SlotState.Borrowed) return false;
            CheckOperation();
            Transition(slot, SlotState.Returning);
            var scope = slot.Scope; slot.Scope = null; scope.Remove(this, index, version);
            bool clean = Reset(index);
            bool retain = false;
            if (clean && Adapter.IsValid(slot.Value) && State == PoolState.Running && Service.State == PoolState.Running && IdleCount < Config.MaxIdle && ResidentCount <= Config.MaxResident)
            {
                try { BeginCallback(); retain = Adapter.CanRetain(slot.Value); }
                catch (Exception ex) { Service.Report(Key, "Retention", ex); }
                finally { EndCallback(); }
            }
            if (retain) AddIdle(index); else Retire(index, scope);
            return true;
        }
        /// <summary>
        /// 尝试重置仍有效的实例并统计耗时；实例失效或回调抛异常时返回失败，禁止重新缓存。
        /// </summary>
        private bool Reset(int index)
        {
            var slot = slots[index]; if (!Adapter.IsValid(slot.Value)) return false;
            long started = Stopwatch.GetTimestamp();
            try { BeginCallback(); Adapter.Reset(slot.Value); return true; }
            catch (Exception ex) { Service.Report(Key, "Reset", ex); return false; }
            finally { EndCallback(); returnMs += Elapsed(started); }
        }
        /// <summary>
        /// 为准备失败或取消的实例尽力执行清理，然后登记退役及原作用域的销毁等待责任。
        /// </summary>
        private void CleanupAndRetire(int index, PoolScope scope) { Reset(index); Retire(index, scope); }
        /// <summary>
        /// 将实例转入等待销毁状态，必要时增加所属作用域的待释放计数；此时仍占用驻留配额。
        /// </summary>
        /// <param name="index">需要退役且已从空闲存储或借用登记中解除的槽位编号。</param>
        /// <param name="owner">需要等待本次销毁的原作用域；纯缓存淘汰可传 null。</param>
        private void Retire(int index, PoolScope owner)
        {
            var slot = slots[index];
            Transition(slot, SlotState.PendingDestroy);
            slot.RetirementOwner = owner; if (owner != null) owner.RetiringInstances++;
        }
        /// <summary>
        /// 在确认释放后清除对象与故障引用、结算作用域责任和全局配额；保留版本，版本耗尽的槽位不再复用。
        /// </summary>
        /// <param name="index">已确认释放，或创建尚未产生实例便失败的槽位编号。</param>
        /// <remarks>只有此阶段才返还全局驻留配额；不得在刚调用 Destroy 时提前执行。</remarks>
        private void ReleaseSlot(int index)
        {
            var slot = slots[index];
            slot.Value = null; slot.Fault = null; slot.DestroyWasIssued = false;
            if (slot.RetirementOwner != null) { slot.RetirementOwner.RetiringInstances--; slot.RetirementOwner = null; }
            Transition(slot, SlotState.Released); Service.Release(Config.EstimatedBytesPerInstance);
            if (slot.Version != ulong.MaxValue) free.Push(index);
        }
        /// <summary>
        /// 退役一个空闲实例；有序缓存选择最旧实例，栈式缓存选择栈顶，绝不抢占活动借用。
        /// </summary>
        internal override bool EvictOne()
        {
            if (IdleCount == 0) return false;
            int index = orderedIdle ? idleHead : idleStack[idleStack.Count - 1]; RemoveIdle(index); Retire(index, null); return true;
        }
        /// <summary>
        /// 处理指定类别中的一个请求：先结束取消或过期请求，再按优先级和等待时长选择候选。
        /// </summary>
        internal override bool RequestStep(bool warm)
        {
            CheckOperation();
            int best = -1; 
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i]; if (r.Warm != warm) continue;
                var gate = Gate(r.Scope, r.Cancellation, r.Deadline);
                if (gate != PoolStatus.Success) { Complete(i, gate); return true; }
                double score = r.Priority + (Service.Clock.Now - r.Started) * 10;
                if (score > bestScore) { best = i; bestScore = score; }
            }
            if (best == -1) return false;
            var request = requests[best];
            if (warm) return WarmStep(best, request);
            var result = Rent(request.Scope, request.Args, true, true, request.Cancellation, request.Deadline);
            request.Waiting = result.Status;
            bool waiting = result.Status == PoolStatus.ResourceNotReady || result.Status == PoolStatus.CapacityExceeded || result.Status == PoolStatus.GlobalBudgetExceeded;
            if (waiting && !(Config.OverflowPolicy == OverflowPolicy.Fail && result.Status != PoolStatus.ResourceNotReady)) return false;
            Complete(best, result.Status, result); return true;
        }
        /// <summary>
        /// 推进一次预热，检查目标缺口和保留能力后最多创建一个实例；达到目标或不可满足时结算任务。
        /// </summary>
        private bool WarmStep(int index, Pending request)
        {
            if (Ready >= request.Target) { CompleteWarm(index, WarmEndReason.ReachedTarget); return true; }
            if (IdleCount >= Config.MaxIdle) { CompleteWarm(index, WarmEndReason.CapacityLimited); return true; }
            if (ResidentCount >= Config.MaxResident)
            {
                // Deferred destruction may free quota shortly; otherwise the target cannot be retained.
                if (counts[6] + counts[7] != 0) return false;
                CompleteWarm(index, WarmEndReason.CapacityLimited); return true;
            }
            var resource = ResourceReady(); request.Waiting = resource;
            if (resource == PoolStatus.ResourceNotReady) return false;
            if (resource != PoolStatus.Success) { CompleteWarm(index, WarmEndReason.Failed, resourceDiagnostic); return true; }
            var status = Create(request.Scope, true, true, out int slotIndex, out long diagnostic); request.Waiting = status;
            if (status == PoolStatus.ResourceNotReady) return false;
            if (status != PoolStatus.Success)
            {
                CompleteWarm(index, status == PoolStatus.GlobalBudgetExceeded ? WarmEndReason.GlobalBudgetLimited : status == PoolStatus.CapacityExceeded ? WarmEndReason.CapacityLimited : WarmEndReason.Failed, diagnostic); return true;
            }
            var gate = Gate(request.Scope, request.Cancellation, request.Deadline);
            if (gate != PoolStatus.Success) { CleanupAndRetire(slotIndex, request.Scope); Complete(index, gate); return true; }
            AddIdle(slotIndex); request.Created++;
            if (Ready >= request.Target) CompleteWarm(index, WarmEndReason.ReachedTarget);
            return true;
        }
        /// <summary>
        /// 根据预热请求记录与当前可用量，构造包含目标、起始量、创建量、结束量和原因的结果。
        /// </summary>
        private WarmResult WarmOutcome(Pending r, WarmEndReason reason, long diagnostic = 0) => new WarmResult(r.Target, r.StartReady, r.Created, Ready, reason, diagnostic);
        /// <summary>
        /// 将取消、超时、作用域关闭等请求状态映射为预热结束原因，其余失败统一归为 Failed。
        /// </summary>
        private static WarmEndReason ToWarmReason(PoolStatus status)
        {
            switch (status)
            {
                case PoolStatus.Cancelled: return WarmEndReason.Cancelled;
                case PoolStatus.TimedOut: return WarmEndReason.TimedOut;
                case PoolStatus.ScopeClosed: return WarmEndReason.ScopeClosed;
                case PoolStatus.PoolClosed: return WarmEndReason.PoolClosed;
                default: return WarmEndReason.Failed;
            }
        }
        /// <summary>
        /// 移除已结束请求、结算作用域及排队时间，并完成对应任务；预热请求转交专用结算方法。
        /// </summary>
        private void Complete(int index, PoolStatus status, RentResult<T> result = default)
        {
            var r = requests[index];
            if (r.Warm) { CompleteWarm(index, ToWarmReason(status)); return; }
            requests.RemoveAt(index); r.Scope.PendingRequests--; queueSeconds += Service.Clock.Now - r.Started;
            r.RentCompletion.TrySetResult(CountResult(result.Status == status ? result : new RentResult<T>(status)));
        }
        /// <summary>
        /// 结束并移除预热请求，解除作用域待处理登记，返回当时的预热进度与结束原因。
        /// </summary>
        private void CompleteWarm(int index, WarmEndReason reason, long diagnostic = 0)
        {
            var r = requests[index]; requests.RemoveAt(index); r.Scope.PendingRequests--; queueSeconds += Service.Clock.Now - r.Started;
            r.WarmCompletion.TrySetResult(WarmOutcome(r, reason, diagnostic));
        }
        /// <summary>
        /// 推进一次资源轮询和有界维护，处理缓存淘汰、失效借用、销毁请求或确认，并尝试完成池关闭。
        /// </summary>
        internal override bool MaintenanceStep()
        {
            CheckOperation(); if (State == PoolState.Closed) return false;
            long started = Stopwatch.GetTimestamp();
            try
            {
                try { BeginCallback(); Adapter.PollResource(); }
                catch (Exception ex) { resourceFault = ex; resourceDiagnostic = Service.Report(Key, "Resource polling", ex); }
                finally { EndCallback(); }
                if (IdleCount > 0 && (State != PoolState.Running || IdleCount > Config.MaxIdle || ResidentCount > Config.MaxResident)) return EvictOne();
                if (orderedIdle && IdleCount > Config.MinIdle && Config.IdleTimeoutSeconds > 0 && Service.Clock.Now - slots[idleHead].IdleSince >= Config.IdleTimeoutSeconds) return EvictOne();
                if (slots.Count != 0)
                {
                    if (maintenanceCursor >= slots.Count) maintenanceCursor = 0;
                    int index = maintenanceCursor++; var slot = slots[index];
                    if (slot.State == SlotState.Borrowed && (!slot.Scope.IsOpen || !Adapter.IsValid(slot.Value))) return ReturnScoped(index, slot.Version);
                    if (slot.State == SlotState.Idle && (!Adapter.IsValid(slot.Value) || (!orderedIdle && IdleCount > Config.MinIdle && Config.IdleTimeoutSeconds > 0 && Service.Clock.Now - slot.IdleSince >= Config.IdleTimeoutSeconds)))
                    { RemoveIdle(index); Retire(index, null); return true; }
                    if (slot.State == SlotState.PendingDestroy && Service.Scheduler.TryDestroy())
                    {
                        long destroyStarted = Stopwatch.GetTimestamp();
                        try
                        {
                            BeginCallback(); Adapter.RequestDestroy(slot.Value); slot.DestroyWasIssued = true; slot.DestroyFrame = Service.Scheduler.Frame;
                            Transition(slot, SlotState.DestroyIssued);
                        }
                        catch (Exception ex) { Fault(slot, ex); }
                        finally { EndCallback(); destroyMs += Elapsed(destroyStarted); }
                        return true;
                    }
                    if (slot.State == SlotState.DestroyIssued)
                    {
                        try { BeginCallback(); if (Adapter.IsDestroyComplete(slot.Value, slot.DestroyFrame, Service.Scheduler.Frame)) ReleaseSlot(index); }
                        catch (Exception ex) { Fault(slot, ex); }
                        finally { EndCallback(); }
                        return true;
                    }
                }
                AdvanceClose(); return false;
            }
            finally { maintenanceMs += Elapsed(started); }
        }
        /// <summary>
        /// 记录销毁或确认异常，将槽位置为释放故障并继续保留实例及配额；若正在关闭则使关闭任务失败。
        /// </summary>
        private void Fault(Slot slot, Exception error)
        {
            slot.Fault = error; Transition(slot, SlotState.DestroyFaulted); Service.Report(Key, "Destroy/confirmation", error);
            if (closeCompletion != null) closeCompletion.TrySetException(error);
        }
        /// <summary>
        /// 关闭准入并建立幂等的关闭任务；等待开放作用域归还借用、销毁确认和资源释放，不强行结束活动借用。
        /// </summary>
        /// <returns>幂等的关闭任务；释放失败时任务失败而池继续保持 Closing。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 不会擅自回收开放作用域的活动借用，关闭期间需要持续驱动服务。</remarks>
        public override Task CloseAsync()
        {
            CheckOperation();
            if (closeCompletion != null) return closeCompletion.Task;
            State = PoolState.Closing; closeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ReleaseFault != null) closeCompletion.TrySetException(ReleaseFault);
            return closeCompletion.Task;
        }
        /// <summary>
        /// 仅在上一次关闭失败后建立新的关闭任务，并明确重试失败释放；原失败任务不会被改写。
        /// </summary>
        /// <returns>新的关闭尝试任务，原失败任务保持失败。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 仅允许在上一关闭任务已经失败后调用。</remarks>
        public override Task RetryCloseAsync()
        {
            CheckOperation();
            if (closeCompletion == null || !closeCompletion.Task.IsFaulted) throw new InvalidOperationException("Only a failed close can be retried.");
            closeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); RetryFailedReleases(); return closeCompletion.Task;
        }
        /// <summary>
        /// 清除待重试的释放故障，按失败阶段恢复到等待销毁或等待确认，避免重复发出已成功的销毁请求。
        /// </summary>
        public void RetryFailedReleases()
        {
            CheckOperation(); closeFault = null;
            foreach (var slot in slots) if (slot.State == SlotState.DestroyFaulted)
            { slot.Fault = null; Transition(slot, slot.DestroyWasIssued ? SlotState.DestroyIssued : SlotState.PendingDestroy); }
        }
        /// <summary>
        /// 在请求、实例和加载工作全部结算后释放资源并标记关闭；释放故障会保留 Closing 状态。
        /// </summary>
        private void AdvanceClose()
        {
            if (State != PoolState.Closing || closeCompletion.Task.IsCompleted) return;
            if (ReleaseFault != null) { closeCompletion.TrySetException(ReleaseFault); return; }
            if (ResidentCount != 0 || requests.Count != 0 || Adapter.ResourceState == ResourceState.Loading) return;
            try { BeginCallback(); Adapter.ReleaseResources(); State = PoolState.Closed; closeCompletion.TrySetResult(true); }
            catch (Exception ex) { closeFault = ex; Service.Report(Key, "Release resources", ex); closeCompletion.TrySetException(ex); }
            finally { EndCallback(); }
        }
        /// <summary>
        /// 主线程替换经过校验的只读配置并调整全局内存估值；缩容由后续准入与维护执行，不直接销毁活动借用。
        /// </summary>
        /// <param name="config">已经校验的只读运行时配置。</param>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 新上限可以暂时小于当前驻留量，活动对象继续使用，后续归还和维护逐步收敛。</remarks>
        public override void Reconfigure(PoolConfig config)
        {
            CheckOperation(); if (config == null) throw new ArgumentNullException(nameof(config));
            if (State != PoolState.Running) throw new InvalidOperationException("Cannot reconfigure a closing pool.");
            Service.ChangeEstimate(checked((config.EstimatedBytesPerInstance - Config.EstimatedBytesPerInstance) * ResidentCount)); Config = config;
        }
        /// <summary>
        /// 复制当前状态、请求等待原因、操作统计和故障信息，用于诊断；创建快照会分配托管对象。
        /// </summary>
        public override PoolSnapshot GetSnapshot()
        {
            Service.CheckThread(); int resource = 0, capacity = 0, budget = 0;
            foreach (var r in requests)
            {
                if (r.Waiting == PoolStatus.CapacityExceeded || r.Waiting == PoolStatus.GlobalBudgetExceeded) capacity++;
                else if (Adapter.ResourceState != ResourceState.Ready) resource++; else budget++;
            }
            return new PoolSnapshot { Key = Key, State = State, CreateReserved = counts[1], Preparing = counts[2], Borrowed = counts[3], Returning = counts[4], Idle = counts[5],
                PendingDestroy = counts[6], DestroyIssued = counts[7], DestroyFaulted = counts[8], MetadataSlots = slots.Count, PendingRequests = requests.Count,
                WaitingResource = resource, WaitingCapacity = capacity, WaitingBudget = budget, PeakBorrowed = peakBorrowed, CacheHits = cacheHits, SuccessfulRents = rents,
                SyncCreates = syncCreates, AsyncCreates = asyncCreates, WarmCreates = warmCreates, Results = (long[])results.Clone(), TotalQueueSeconds = queueSeconds,
                CreateMilliseconds = createMs, PrepareMilliseconds = prepareMs, ReturnMilliseconds = returnMs, DestroyMilliseconds = destroyMs, MaintenanceMilliseconds = maintenanceMs,
                EstimatedResidentBytes = Config.EstimatedBytesPerInstance * ResidentCount, SharedResourceEstimatedBytes = Adapter.SharedResourceEstimatedBytes,
                MemoryCostIsEstimate = Config.MemoryCostIsEstimate, CloseFault = ReleaseFault?.Message };
        }
    }
}

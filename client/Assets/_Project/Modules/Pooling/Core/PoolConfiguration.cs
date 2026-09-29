using System;

namespace BigWorld.Pooling
{
    [Serializable]
    public sealed class PoolSettings
    {
        public int InitialStorageCapacity = 16;
        public int BasePrewarmTargetReady;
        public int MinIdle;
        public int MaxIdle = 16;
        public int MaxBorrowed = 64;
        public int MaxResident = 80;
        public double IdleTimeoutSeconds = 30;
        public int RetentionPriority = 50;
        public int MaxPendingRequests = 64;
        public double RequestTimeoutSeconds = 10;
        public OverflowPolicy OverflowPolicy = OverflowPolicy.WaitOrFail;
        public long EstimatedBytesPerInstance = 4096;
        public bool MemoryCostIsEstimate = true;
        public int MaxCacheProbes = 4;
        /// <summary>
        /// 校验可编辑配置并生成独立的只读运行时配置；之后修改原设置不会影响已生成的配置。
        /// </summary>
        public PoolConfig Freeze() => new PoolConfig(this);
    }

    public sealed class PoolConfig : IEquatable<PoolConfig>
    {
        public int InitialStorageCapacity { get; }
        public int BasePrewarmTargetReady { get; }
        public int MinIdle { get; }
        public int MaxIdle { get; }
        public int MaxBorrowed { get; }
        public int MaxResident { get; }
        public double IdleTimeoutSeconds { get; }
        public int RetentionPriority { get; }
        public int MaxPendingRequests { get; }
        public double RequestTimeoutSeconds { get; }
        public OverflowPolicy OverflowPolicy { get; }
        public long EstimatedBytesPerInstance { get; }
        public bool MemoryCostIsEstimate { get; }
        public int MaxCacheProbes { get; }
        /// <summary>
        /// 验证容量关系、时间、队列及内存估值边界，然后复制配置字段；无效组合会抛出参数异常。
        /// </summary>
        internal PoolConfig(PoolSettings s)
        {
            if (s.InitialStorageCapacity < 0 || s.InitialStorageCapacity > 1000000 || s.MinIdle < 0 || s.MinIdle > s.MaxIdle || s.MaxIdle > s.MaxResident ||
                s.MaxBorrowed <= 0 || s.MaxBorrowed > s.MaxResident || s.MaxResident > 1000000 || s.BasePrewarmTargetReady < 0 || s.BasePrewarmTargetReady > s.MaxIdle ||
                s.MaxPendingRequests <= 0 || s.MaxPendingRequests > 4096 || s.MaxCacheProbes <= 0 || s.MaxCacheProbes > 128 || s.EstimatedBytesPerInstance <= 0 || s.EstimatedBytesPerInstance > long.MaxValue / Math.Max(1, s.MaxResident) ||
                s.IdleTimeoutSeconds < 0 || double.IsNaN(s.IdleTimeoutSeconds) || double.IsInfinity(s.IdleTimeoutSeconds) ||
                s.RequestTimeoutSeconds <= 0 || double.IsNaN(s.RequestTimeoutSeconds) || double.IsInfinity(s.RequestTimeoutSeconds) ||
                !Enum.IsDefined(typeof(OverflowPolicy), s.OverflowPolicy)) throw new ArgumentException("Invalid pool capacity, timeout or policy.");
            InitialStorageCapacity = s.InitialStorageCapacity; BasePrewarmTargetReady = s.BasePrewarmTargetReady;
            MinIdle = s.MinIdle; MaxIdle = s.MaxIdle; MaxBorrowed = s.MaxBorrowed; MaxResident = s.MaxResident;
            IdleTimeoutSeconds = s.IdleTimeoutSeconds; RetentionPriority = s.RetentionPriority; MaxPendingRequests = s.MaxPendingRequests;
            RequestTimeoutSeconds = s.RequestTimeoutSeconds; OverflowPolicy = s.OverflowPolicy; EstimatedBytesPerInstance = s.EstimatedBytesPerInstance;
            MemoryCostIsEstimate = s.MemoryCostIsEstimate; MaxCacheProbes = s.MaxCacheProbes;
        }
        /// <summary>
        /// 逐项比较两个运行时配置的全部行为字段，判断它们是否具有相同配置。
        /// </summary>
        public bool Equals(PoolConfig c) => c != null && InitialStorageCapacity == c.InitialStorageCapacity && BasePrewarmTargetReady == c.BasePrewarmTargetReady &&
            MinIdle == c.MinIdle && MaxIdle == c.MaxIdle && MaxBorrowed == c.MaxBorrowed && MaxResident == c.MaxResident && IdleTimeoutSeconds == c.IdleTimeoutSeconds &&
            RetentionPriority == c.RetentionPriority && MaxPendingRequests == c.MaxPendingRequests && RequestTimeoutSeconds == c.RequestTimeoutSeconds &&
            OverflowPolicy == c.OverflowPolicy && EstimatedBytesPerInstance == c.EstimatedBytesPerInstance && MemoryCostIsEstimate == c.MemoryCostIsEstimate && MaxCacheProbes == c.MaxCacheProbes;
        /// <summary>
        /// 先检查对象是否为运行时池配置，再执行类型化的逐字段比较。
        /// </summary>
        public override bool Equals(object obj) => obj is PoolConfig c && Equals(c);
        /// <summary>
        /// 根据主要容量字段生成哈希；哈希仅用于集合定位，最终相等性仍由完整字段比较决定。
        /// </summary>
        public override int GetHashCode() => MaxResident ^ MaxIdle ^ MaxBorrowed;
    }

    [Serializable]
    public sealed class PoolFrameBudget
    {
        public int MaxCreatesPerFrame = 2;
        public int MaxDestroyRequestsPerFrame = 4;
        public int MaxMaintenanceStepsPerFrame = 64;
        public double MaxWorkMilliseconds = 2;
        /// <summary>
        /// 要求每项帧预算为有效正值，并返回独立副本，防止外部修改影响已创建的调度器。
        /// </summary>
        internal PoolFrameBudget CopyValidated()
        {
            if (MaxCreatesPerFrame < 1 || MaxDestroyRequestsPerFrame < 1 || MaxMaintenanceStepsPerFrame < 1 ||
                MaxWorkMilliseconds <= 0 || double.IsNaN(MaxWorkMilliseconds) || double.IsInfinity(MaxWorkMilliseconds)) throw new ArgumentException("Frame budgets must be positive and finite.");
            return (PoolFrameBudget)MemberwiseClone();
        }
    }

    public sealed class PoolDiagnostic
    {
        public long Id { get; internal set; }
        public PoolKey Key { get; internal set; }
        public string Operation { get; internal set; }
        public Exception Exception { get; internal set; }
    }

    public sealed class PoolSnapshot
    {
        public PoolKey Key { get; internal set; }
        public PoolState State { get; internal set; }
        public int CreateReserved { get; internal set; }
        public int Preparing { get; internal set; }
        public int Borrowed { get; internal set; }
        public int Returning { get; internal set; }
        public int Idle { get; internal set; }
        public int PendingDestroy { get; internal set; }
        public int DestroyIssued { get; internal set; }
        public int DestroyFaulted { get; internal set; }
        public int ResidentSlots => CreateReserved + Preparing + Borrowed + Returning + Idle + PendingDestroy + DestroyIssued + DestroyFaulted;
        public int PhysicalInstances => ResidentSlots - CreateReserved;
        public int MetadataSlots { get; internal set; }
        public int PendingRequests { get; internal set; }
        public int WaitingResource { get; internal set; }
        public int WaitingCapacity { get; internal set; }
        public int WaitingBudget { get; internal set; }
        public int PeakBorrowed { get; internal set; }
        public long CacheHits { get; internal set; }
        public long SuccessfulRents { get; internal set; }
        public long SyncCreates { get; internal set; }
        public long AsyncCreates { get; internal set; }
        public long WarmCreates { get; internal set; }
        public long[] Results { get; internal set; }
        public double CacheHitRate => SuccessfulRents == 0 ? 0 : (double)CacheHits / SuccessfulRents;
        public double TotalQueueSeconds { get; internal set; }
        public double CreateMilliseconds { get; internal set; }
        public double PrepareMilliseconds { get; internal set; }
        public double ReturnMilliseconds { get; internal set; }
        public double DestroyMilliseconds { get; internal set; }
        public double MaintenanceMilliseconds { get; internal set; }
        public long EstimatedResidentBytes { get; internal set; }
        public long SharedResourceEstimatedBytes { get; internal set; }
        public bool MemoryCostIsEstimate { get; internal set; }
        public string CloseFault { get; internal set; }
    }
}

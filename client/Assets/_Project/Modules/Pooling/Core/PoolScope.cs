using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BigWorld.Pooling
{
    internal readonly struct LeaseIdentity : IEquatable<LeaseIdentity>
    {
        internal readonly PoolBase Pool;
        internal readonly int Slot;
        internal readonly ulong Version;
        /// <summary>
        /// 组合运行时池实例、槽位和版本，用于作用域登记单次借用，避免将凭证装箱。
        /// </summary>
        internal LeaseIdentity(PoolBase pool, int slot, ulong version) { Pool = pool; Slot = slot; Version = version; }
        /// <summary>
        /// 比较池对象引用、槽位和版本，确保只有同一次借用才相等。
        /// </summary>
        public bool Equals(LeaseIdentity other) => ReferenceEquals(Pool, other.Pool) && Slot == other.Slot && Version == other.Version;
        /// <summary>
        /// 检查对象是否为借用身份记录，再执行完整身份比较。
        /// </summary>
        public override bool Equals(object obj) => obj is LeaseIdentity other && Equals(other);
        /// <summary>
        /// 根据运行时池、槽位和借用版本生成作用域集合的查找哈希。
        /// </summary>
        public override int GetHashCode() { unchecked { return (Pool.GetHashCode() * 397 ^ Slot) * 397 ^ Version.GetHashCode(); } }
    }

    public sealed class PoolScope
    {
        private readonly HashSet<LeaseIdentity> leases = new HashSet<LeaseIdentity>();
        private readonly TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly PoolService Service;
        public PoolService OwnerService => Service;
        internal int PendingRequests;
        internal int RetiringInstances;
        internal int OpenChildren;
        public long Id { get; }
        public string Name { get; }
        public PoolScope Parent { get; }
        public ScopeState State { get; private set; }
        public bool IsOpen => State == ScopeState.Open && (Parent == null || Parent.IsOpen);
        public int BorrowedCount { get { Service.CheckThread(); return leases.Count; } }
        public int PendingCount { get { Service.CheckThread(); return PendingRequests; } }
        public int RetiringCount { get { Service.CheckThread(); return RetiringInstances; } }
        /// <summary>
        /// 建立新的作用域身份并记录父级；存在父级时增加其尚未关闭的子作用域计数。
        /// </summary>
        internal PoolScope(PoolService service, long id, string name, PoolScope parent)
        { Service = service; Id = id; Name = name; Parent = parent; if (parent != null) parent.OpenChildren++; }
        /// <summary>
        /// 在当前开放作用域下创建子作用域，后续父级关闭会立即使该子树逻辑失效。
        /// </summary>
        /// <param name="name">子作用域的诊断名称。</param>
        /// <returns>归属于当前作用域的新子作用域。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public PoolScope CreateChild(string name) => Service.CreateScope(name, this);
        /// <summary>
        /// 立即标记作用域关闭并使其子树的访问失效，实际请求结算和借用归还由后续预算调度完成。
        /// </summary>
        /// <returns>同一作用域的幂等关闭任务；请求、借用、待确认释放和子作用域均结算后完成。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。 逻辑失效立即发生；场景卸载必须等此任务完成。</remarks>
        public Task CloseAsync()
        {
            Service.CheckThread();
            if (State == ScopeState.Open) State = ScopeState.Closing;
            return closed.Task;
        }
        /// <summary>
        /// 将完整借用身份登记到作用域，供关闭时找到该次借用。
        /// </summary>
        internal void Add(PoolBase pool, int slot, ulong version) => leases.Add(new LeaseIdentity(pool, slot, version));
        /// <summary>
        /// 移除已经开始归还的借用身份，避免关闭流程再次清理同一次借用。
        /// </summary>
        internal void Remove(PoolBase pool, int slot, ulong version) => leases.Remove(new LeaseIdentity(pool, slot, version));
        /// <summary>
        /// 推进一个关闭步骤：移除预热计划、最多归还一个借用，或在子作用域及全部责任结算后完成关闭。
        /// </summary>
        internal bool StepClose()
        {
            if (IsOpen || State == ScopeState.Closed) return false;
            State = ScopeState.Closing;
            Service.RemovePlans(this);
            if (leases.Count != 0)
            {
                var e = leases.GetEnumerator();
                e.MoveNext(); var lease = e.Current; e.Dispose();
                lease.Pool.ReturnScoped(lease.Slot, lease.Version);
                return true;
            }
            if (PendingRequests != 0 || RetiringInstances != 0 || OpenChildren != 0) return false;
            State = ScopeState.Closed;
            if (Parent != null) Parent.OpenChildren--;
            closed.TrySetResult(true);
            return true;
        }
        /// <summary>
        /// 生成当前作用域持有的池、槽位和版本文本，供调试定位未归还借用；此查询会分配数组与字符串。
        /// </summary>
        public string[] DescribeBorrowers()
        {
            Service.CheckThread();
            var result = new string[leases.Count]; int i = 0;
            foreach (var lease in leases) result[i++] = lease.Pool.Key + " slot=" + lease.Slot + " version=" + lease.Version;
            return result;
        }
    }
}

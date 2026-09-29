// Adapted from Assets/YouYouFramework/Managers/Pool/ClassObjectPool.cs.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace YouYou.Framework
{
    public interface IRecyclable { void Clear(); }

    /// <summary>Main-thread pool. Objects are keyed by Type and compared by reference.</summary>
    public sealed class ClassObjectPool : IDisposable
    {
        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return RuntimeHelpers.GetHashCode(obj); }
        }

        private readonly Dictionary<Type, Queue<object>> pools = new Dictionary<Type, Queue<object>>();
        private readonly Dictionary<Type, int> capacities = new Dictionary<Type, int>();
        private readonly HashSet<object> idle = new HashSet<object>(new IdentityComparer());
        private readonly int defaultCapacity;

        public ClassObjectPool(int defaultCapacity = 64)
        {
            if (defaultCapacity < 0) throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
            this.defaultCapacity = defaultCapacity;
        }

        public void SetResideCount<T>(int count) where T : class
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            capacities[typeof(T)] = count;
            Release();
        }

        public T Dequeue<T>() where T : class, new()
        {
            if (!pools.TryGetValue(typeof(T), out var queue) || queue.Count == 0) return new T();
            var value = (T)queue.Dequeue();
            idle.Remove(value);
            return value;
        }

        public void Enqueue(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (idle.Contains(value)) throw new InvalidOperationException("Object was already returned to the pool.");
            (value as IRecyclable)?.Clear();
            var type = value.GetType();
            if (!pools.TryGetValue(type, out var queue)) pools.Add(type, queue = new Queue<object>());
            var capacity = capacities.TryGetValue(type, out var configured) ? configured : defaultCapacity;
            if (queue.Count >= capacity) return;
            queue.Enqueue(value);
            idle.Add(value);
        }

        public void Release()
        {
            foreach (var pair in pools)
            {
                var capacity = capacities.TryGetValue(pair.Key, out var configured) ? configured : defaultCapacity;
                while (pair.Value.Count > capacity) idle.Remove(pair.Value.Dequeue());
            }
        }

        public void Dispose() { pools.Clear(); capacities.Clear(); idle.Clear(); }
    }
}

using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    /// <summary>Business models and generated tables are registered by the application.</summary>
    public sealed class DataRegistry
    {
        private readonly Dictionary<Type, object> values = new Dictionary<Type, object>();
        public void Set<T>(T value) where T : class
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            values[typeof(T)] = value;
        }
        public T Get<T>() where T : class { return values.TryGetValue(typeof(T), out var value) ? (T)value : null; }
        public void Clear() { values.Clear(); }
    }
}

// Adapted from Assets/YouYouFramework/Managers/Event/SocketEvent.cs; no XLua dependency.
using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    public sealed class SocketEvent : IDisposable
    {
        public delegate void OnActionHandler(byte[] buffer);
        private readonly Dictionary<ushort, List<OnActionHandler>> listeners = new Dictionary<ushort, List<OnActionHandler>>();

        public void AddEventListener(ushort key, OnActionHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!listeners.TryGetValue(key, out var list)) listeners.Add(key, list = new List<OnActionHandler>());
            if (!list.Contains(handler)) list.Add(handler);
        }

        public void RemoveEventListener(ushort key, OnActionHandler handler)
        {
            if (!listeners.TryGetValue(key, out var list)) return;
            list.Remove(handler);
            if (list.Count == 0) listeners.Remove(key);
        }

        public void Dispatch(ushort key, byte[] buffer = null)
        {
            if (!listeners.TryGetValue(key, out var list)) return;
            foreach (var handler in list.ToArray()) handler(buffer);
        }

        public void Dispose() { listeners.Clear(); }
    }
}

// Adapted from Assets/YouYouFramework/Managers/Event/CommonEvent.cs.
using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    /// <summary>Main-thread event bus. Listener changes take effect on the next dispatch.</summary>
    public sealed class CommonEvent : IDisposable
    {
        public delegate void OnActionHandler(object userData);
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

        public void Dispatch(ushort key, object userData = null)
        {
            if (!listeners.TryGetValue(key, out var list)) return;
            foreach (var handler in list.ToArray()) handler(userData);
        }

        public void Dispose() { listeners.Clear(); }
    }
}

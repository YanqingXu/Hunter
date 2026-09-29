// Adapted from the original TimeManager: explicit delta time replaces GameEntry/Unity dependencies.
using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    public sealed class TimeManager : IDisposable
    {
        private readonly List<TimeAction> actions = new List<TimeAction>();
        private bool ticking;
        public TimeAction CreateTimeAction() { return new TimeAction(this); }
        internal void Register(TimeAction action) { if (!actions.Contains(action)) actions.Add(action); }
        internal void Remove(TimeAction action) { actions.Remove(action); }

        public void RemoveTimeActionByName(string name)
        {
            foreach (var action in actions.ToArray())
                if (string.Equals(action.TimeName, name, StringComparison.OrdinalIgnoreCase)) action.Stop();
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (ticking) throw new InvalidOperationException("Recursive timer updates are not supported.");
            ticking = true;
            try { foreach (var action in actions.ToArray()) action.Tick(deltaTime); }
            finally { ticking = false; }
        }

        public void Dispose()
        {
            foreach (var action in actions.ToArray()) action.Stop();
            actions.Clear();
        }
    }
}

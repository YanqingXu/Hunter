using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    public sealed class FsmManager : IDisposable
    {
        private readonly Dictionary<int, FsmBase> fsms = new Dictionary<int, FsmBase>();
        private int nextId;
        private bool disposing;

        public Fsm<T> Create<T>(T owner, FsmState<T>[] states) where T : class
        {
            while (fsms.ContainsKey(nextId)) nextId++;
            return Create(nextId++, owner, states);
        }

        public Fsm<T> Create<T>(int id, T owner, FsmState<T>[] states) where T : class
        {
            if (disposing) throw new InvalidOperationException("FSM manager is shutting down.");
            if (fsms.ContainsKey(id)) throw new ArgumentException("Duplicate FSM id.", nameof(id));
            var fsm = new Fsm<T>(id, owner, states);
            fsms.Add(id, fsm);
            return fsm;
        }

        public void Tick()
        {
            foreach (var fsm in new List<FsmBase>(fsms.Values)) fsm.OnUpdate();
        }

        public void DestroyFsm(int id)
        {
            if (!fsms.TryGetValue(id, out var fsm)) return;
            fsms.Remove(id);
            fsm.ShutDown();
        }

        public void Dispose()
        {
            if (disposing) return;
            disposing = true;
            var snapshot = new List<FsmBase>(fsms.Values);
            fsms.Clear();
            var errors = new List<Exception>();
            try
            {
                foreach (var fsm in snapshot)
                    try { fsm.ShutDown(); } catch (Exception e) { errors.Add(e); }
            }
            finally { disposing = false; }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}

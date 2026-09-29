using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    /// <summary>Independent lifetime for each client. No static GameEntry dependency.</summary>
    public sealed class ClientContext : IDisposable
    {
        private bool disposed;
        public EventManager Event { get; } = new EventManager();
        public TimeManager Time { get; } = new TimeManager();
        public FsmManager Fsm { get; } = new FsmManager();
        public ClassObjectPool Pool { get; } = new ClassObjectPool();
        public DataRegistry Data { get; } = new DataRegistry();
        public LocalizationManager Localization { get; } = new LocalizationManager();
        public ProcedureManager Procedure { get; }
        public ClientContext() { Procedure = new ProcedureManager(Fsm); }

        public void Tick(float deltaTime)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClientContext));
            Time.Tick(deltaTime);
            if (!disposed) Fsm.Tick();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            foreach (var service in new IDisposable[] { Procedure, Fsm, Time, Event, Pool })
                try { service.Dispose(); } catch (Exception e) { errors.Add(e); }
            Data.Clear(); Localization.Clear();
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}

// Adapted from Assets/YouYouFramework/Managers/Fsm/Fsm.cs. Original author: 边涯.
using System;
using System.Collections.Generic;

namespace YouYou.Framework
{
    public sealed class Fsm<T> : FsmBase where T : class
    {
        private readonly Dictionary<sbyte, FsmState<T>> states = new Dictionary<sbyte, FsmState<T>>();
        private readonly Dictionary<string, object> data = new Dictionary<string, object>();
        private FsmState<T> current;
        private bool disposed, leaving;
        public T Owner { get; private set; }

        public Fsm(int id, T owner, FsmState<T>[] states) : base(id)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (states == null || states.Length == 0 || states.Length > 128) throw new ArgumentException("Provide 1 to 128 states.", nameof(states));
            var unique = new HashSet<FsmState<T>>();
            foreach (var state in states)
                if (state != null && (state.CurrFsm != null || !unique.Add(state)))
                    throw new ArgumentException("Each state instance must belong to only one slot and FSM.", nameof(states));
            Owner = owner;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == null) continue;
                this.states.Add((sbyte)i, states[i]);
                states[i].CurrFsm = this;
            }
        }

        public FsmState<T> GetState(sbyte id) { return states.TryGetValue(id, out var state) ? state : null; }
        public override void OnUpdate() { if (!disposed) current?.OnUpdate(); }
        public void OnUpate() { OnUpdate(); } // Original spelling retained for migration.

        public void ChangeState(sbyte id)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Fsm<T>));
            if (leaving) throw new InvalidOperationException("Change state from OnEnter/OnUpdate, not OnLeave.");
            if (!states.TryGetValue(id, out var next)) throw new ArgumentOutOfRangeException(nameof(id));
            if (CurrStateType == id) return;
            leaving = true;
            try { current?.OnLeave(); }
            finally { leaving = false; }
            if (disposed) return;
            current = next;
            CurrStateType = id;
            current.OnEnter();
        }

        public void SetData<TData>(string key, TData value) { data[key] = value; }
        public TData GetData<TData>(string key) { return data.TryGetValue(key, out var value) ? (TData)value : default(TData); }

        public override void ShutDown()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            try { if (!leaving) current?.OnLeave(); } catch (Exception e) { errors.Add(e); }
            foreach (var state in states.Values)
            {
                try { state.OnDestroy(); } catch (Exception e) { errors.Add(e); }
                finally { state.CurrFsm = null; }
            }
            current = null; Owner = null; CurrStateType = -1;
            states.Clear(); data.Clear();
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}

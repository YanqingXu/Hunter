using System;

namespace YouYou.Framework
{
    public abstract class ProcedureBase : FsmState<ProcedureManager> { }

    /// <summary>Projects supply their own states; the framework has no login or role-selection flow.</summary>
    public sealed class ProcedureManager : IDisposable
    {
        private readonly FsmManager fsms;
        public Fsm<ProcedureManager> CurrFsm { get; private set; }
        public sbyte CurrProcedureState => CurrFsm == null ? (sbyte)-1 : CurrFsm.CurrStateType;
        public ProcedureManager(FsmManager fsms) { this.fsms = fsms ?? throw new ArgumentNullException(nameof(fsms)); }

        public void Start(ProcedureBase[] states, sbyte initialState = 0)
        {
            if (states == null || initialState < 0 || initialState >= states.Length || states[initialState] == null)
                throw new ArgumentException("The initial procedure must exist.");
            Dispose();
            CurrFsm = fsms.Create<ProcedureManager>(this, states);
            CurrFsm.ChangeState(initialState);
        }

        public void ChangeState(sbyte state)
        {
            if (CurrFsm == null) throw new InvalidOperationException("Start procedures first.");
            CurrFsm.ChangeState(state);
        }

        public void Dispose()
        {
            var previous = CurrFsm;
            CurrFsm = null;
            if (previous != null) fsms.DestroyFsm(previous.FsmId);
        }
    }
}

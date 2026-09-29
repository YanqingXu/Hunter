// Original FSM design: 边涯, http://www.u3dol.com. Decoupled for package reuse.
namespace YouYou.Framework
{
    public abstract class FsmBase
    {
        public int FsmId { get; }
        public sbyte CurrStateType { get; protected set; } = -1;
        protected FsmBase(int id) { FsmId = id; }
        public abstract void OnUpdate();
        public abstract void ShutDown();
    }
}

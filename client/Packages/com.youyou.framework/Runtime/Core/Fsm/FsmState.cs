// Original author: 边涯, http://www.u3dol.com.
namespace YouYou.Framework
{
    public abstract class FsmState<T> where T : class
    {
        public Fsm<T> CurrFsm { get; internal set; }
        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnLeave() { }
        public virtual void OnDestroy() { }
    }
}

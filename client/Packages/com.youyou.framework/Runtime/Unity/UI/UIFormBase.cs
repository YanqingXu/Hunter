using UnityEngine;

namespace YouYou.Framework
{
    public abstract class UIFormBase : MonoBehaviour
    {
        public string FormId { get; internal set; }
        public virtual void OnOpen(object userData) { }
        public virtual void OnClose() { }
    }
}

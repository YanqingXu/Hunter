using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Pooling.Tests
{
    public sealed class SecondReleaseProbe : MonoBehaviour, IPooledLifecycle
    {
        public static int Released;
        /// <summary>
        /// 提供无需额外初始化的第二个生命周期组件，用于验证组件清理的遍历行为。
        /// </summary>
        public void InitializeOnce() { }
        /// <summary>
        /// 记录第二个组件的清理次数，确认前一个组件抛异常后清理仍会继续。
        /// </summary>
        public void OnRelease() { Released++; }
    }
}

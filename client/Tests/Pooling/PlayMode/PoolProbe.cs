using System;
using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Pooling.Tests
{
    public sealed class PoolProbe : MonoBehaviour, IPooledLifecycle, IPooledInitializer<int>
    {
        public static int Initialized, Prepared, Enabled, Released, ArgsAtEnable;
        public static bool FailReset;
        public static Action Preparing;
        public int Args;
        /// <summary>
        /// 累计真实实例的一次性初始化次数，检查预热和复用是否重复初始化。
        /// </summary>
        public void InitializeOnce() { Initialized++; }
        /// <summary>
        /// 确认准备发生在未激活阶段，记录业务参数和准备次数，并触发可注入的取消回调。
        /// </summary>
        public void InitializeForUse(int args) { AssertInactive(); Args = args; Prepared++; Preparing?.Invoke(); }
        /// <summary>
        /// 断言实例当前没有在层级中激活，防止业务准备晚于 OnEnable。
        /// </summary>
        private void AssertInactive() { if (gameObject.activeInHierarchy) throw new InvalidOperationException("Preparation must happen while inactive."); }
        /// <summary>
        /// 记录激活次数和激活瞬间的业务参数，供测试核对生成顺序。
        /// </summary>
        private void OnEnable() { Enabled++; ArgsAtEnable = Args; }
        /// <summary>
        /// 累计清理次数并清空业务参数，可通过静态开关模拟组件清理异常。
        /// </summary>
        public void OnRelease() { Released++; Args = 0; if (FailReset) throw new Exception("Test reset failure"); }
        /// <summary>
        /// 重置全部静态计数、故障开关和回调，隔离不同生命周期测试。
        /// </summary>
        public static void Clear() { Initialized = Prepared = Enabled = Released = ArgsAtEnable = 0; FailReset = false; Preparing = null; }
    }
}

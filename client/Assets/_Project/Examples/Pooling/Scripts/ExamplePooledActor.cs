using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Pooling.Samples
{
    public readonly struct ActorSpawnArgs
    {
        public int EntityId { get; }
        /// <summary>
        /// 保存本次示例角色生成需要的实体编号，作为无需装箱的类型化参数。
        /// </summary>
        public ActorSpawnArgs(int entityId) { EntityId = entityId; }
    }
    public sealed class ExamplePooledActor : MonoBehaviour, IPooledLifecycle, IPooledInitializer<ActorSpawnArgs>
    {
        public int EntityId { get; private set; }
        /// <summary>
        /// 预留一次性组件缓存入口；示例不启动玩法，也不订阅本次借用事件。
        /// </summary>
        public void InitializeOnce() { /* Cache stable component references here, without gameplay subscriptions. */ }
        /// <summary>
        /// 在角色激活前写入本次实体编号，供后续玩法使用。
        /// </summary>
        public void InitializeForUse(ActorSpawnArgs args) { EntityId = args.EntityId; }
        /// <summary>
        /// 清空角色实体编号，示范归还时解除业务状态的位置；项目事件和任务也应在此清理。
        /// </summary>
        public void OnRelease() { EntityId = 0; /* Unsubscribe events and cancel this borrow's business tasks here. */ }
    }
}

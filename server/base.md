# 原生基础对象与 Lua 玩法

当前实现以 [SRV-010](intents/modules/objects.intent.md) 为契约。C++ 保存唯一权威状态，
Lua 负责玩法规则；高频输入、快照、确认和事件通过原生路径处理。

| C++ 对象 | 小写脚本 | 关系和数据 |
| --- | --- | --- |
| Entity | entity.lua | 身份、配置引用、位置、朝向、待移除标记 |
| Unit | unit.lua | 组合 Entity，增加运动和生命状态 |
| Player | player.lua | 组合 Unit 与 Weapon，增加输入、身份和备用弹药 |
| Monster | monster.lua | 组合 Unit，增加出生引用、AI 状态与计时 |
| Item | item.lua | 独立物品 ID、配置 ID 和正数量，不继承 Entity |
| Weapon | weapon.lua | 玩家枪械配置、弹药和冷却；Lua 执行射击与换弹规则 |
| World | world.lua | 会话、对象集合、稳定顺序、分配器和生命周期 |

C++ 头文件与实现并置于 src/game，Lua 文件位于 lua/game。Lua 包装表仅保存不可变身份和
句柄；不得把血量、位置、计时等跨 Tick 缓存在脚本中。当前片段可用局部标量完成计算，
例如 Unit.read_motion/write_motion 批量读取并提交运动结果，碰撞算法仍在 Lua。

固定 Luax 候选使用 ClassBuilder 的显式 getter/setter 方法，不依赖复杂表达式中的原生属性
读取。方法通过类表调用（例如 Unit.get_hp(player.health)）。方法检查线程、代次和写权限，
身份与配置引用没有 setter。导入和重开使旧组件句柄失效，Lua GC 不拥有原生对象。

高频 FrameInput 不进入 Lua on_event；C++ 在 Tick 边界锁存输入并生成确认。Lua 用
net.event 提交标量事件，C++ 暂存输出并在入口成功后提交。快照直接读取 C++ 状态生成
Protobuf，维持 60 Hz 模拟、20 Hz 快照和既有战斗顺序。

固定候选的自动 GC 依赖表写入检查点；原生状态迁移后，framework.state.gc_step 每 Tick
提供 128 次有界临时表写入以推进回收。该临时表不保存玩法状态，受正常指令和 GC 预算约束。
迁移绑定或升级 Luax 时必须复测此边界，不应直接删除检查点。

Item 最多 64 个实例，仅提供创建、查询、数量修改、销毁和导出导入。配置 ID 只验证格式
和值域，不加载物品配置；不实现地面掉落、拾取、背包、装备、堆叠或网络投影。
实体同样最多 64 个，死亡不自动删除，怪物移除在固定阶段执行，局内 ID 不复用。

网络协议 v3 和内容 v2 保持不变，Host 与内部状态为 v4。导入先验证独立候选再发布；
旧状态不迁移。实际构建、测试与性能结果见 [验证记录](VERIFICATION.md)。

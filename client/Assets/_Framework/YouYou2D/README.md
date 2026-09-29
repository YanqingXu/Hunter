# BigWorld 横版 2D 框架接入

本层将 `Packages/com.youyou.framework` 与当前项目的 Map2D、SkillEditorKit、BigWorld.Pooling 连接起来。代码位于 `BigWorld.YouYou2D` 命名空间，使用 XY 平面、Collider2D / Rigidbody2D 和正交相机。

## 运行示例

Unity 菜单 **Tools → BigWorld → 2D 框架 → 打开运行示例**，然后点 Play。

场景：`Assets/_Examples/Framework2D/Scenes/Framework2DDemo.unity`。

- A / D 或左右方向键：移动并改变技能朝向。
- Space：落地时跳跃。
- J：射击；K：近战；Esc：暂停 / 继续。
- 红色目标使用地图生成点的生命值。命中后 HUD 显示生命与伤害；离开范围再返回会保留血量，击败后 5 秒刷新。
- HUD 的计时来自 YouYou 定时器，命中数来自 YouYou 事件，界面预制体通过 YouYou UIManager 加载。

示例的键盘输入与移动组件是接入演示。正式角色控制、敌人 AI、完整存档界面和战斗数值系统可以在它们之外继续建设。

## 模块关系

| 组件 | 职责 |
| --- | --- |
| `GameServices2D` | 建立常驻 YouYou 入口，注入资源表，选择共享对象池，提供暂停和异步关闭 |
| `GameAssetCatalog` | 用稳定 Key 引用项目中的技能、地图、角色和 UI 资源 |
| `WorldSession2D` | 绑定地图与玩家，等待出生区域碰撞就绪后开启玩家物理，管理本世界 HUD |
| `SkillActor2D` | 提供技能施法者、受击与阵营接口，把伤害写入 MapStreamedEntity 并发送框架事件 |
| `CameraFollow2D` | 使用 Cinemachine 2.10.7 跟随玩家，提供朝向前视、分轴阻尼、垂直死区与地图视口限制 |
| `PlatformerDemoInput2D` | 示例输入、物理移动和跳跃；调用 SkillActor2D 释放技能 |
| `FrameworkDemoHud2D` | 示例界面、命中事件订阅和定时器；关闭时解除订阅 |

地图实体继续由 **BigWorld.Pooling** 管理，采用其租约、作用域和分帧回收流程。YouYou 自带的轻量 GameObjectPool 不参与这条地图实体链路。技能编辑器自身的投射物 / 特效实例生命周期沿用原实现。

## 接入自己的场景

1. 在场景根节点创建空物体，添加 `GameServices2D`，指定 `GameAssetCatalog`。示例资源表在 `Assets/_Examples/Framework2D/Data/Config/GameAssets.asset`。不必再放一个原包的 Framework Root；接入层负责在注入资源之前初始化它。
2. 用地图编辑器生成 `GridMapRenderer`，保留其分区加载设置。
3. 玩家添加 `Rigidbody2D`、`Collider2D`、`SkillActor2D`。施法时另外添加 `Animator`、`SkillAnimationPlayer`、`SkillPlayer` 和 `SkillFacing2D`，配置技能检测层。只接收伤害的目标无需动画 / 施法组件。
4. 创建 `WorldSession2D`，指定 Map 与 Player。玩家 Transform 应位于地图范围内、以脚底为锚点。给主相机添加 `CameraFollow2D`，将 Target 指向玩家、Map 指向 `GridMapRenderer`；必需的 `CinemachineBrain` 会一并添加。当前 HUD 配置面向一个活动世界。
5. NPC / 怪物预制体根节点添加 `MapStreamedEntity` 与 `SkillActor2D`，配置 Team 和 Collider2D，再交给地图的生成点。流式实体的生命与刷新时间取自地图；独立角色的生命取自 SkillActor2D.MaxHealth。

地图 / 技能均采用 XY 坐标，正方向为 +X。示例角色和目标均位于 layer 30，技能检测该层；地形位于 Default 层。正式项目可改为自己的碰撞层，按技能的敌我规则设置 Team；不需要修改全局 Layer 名称。

已有场景内的 PoolDriver 会先初始化，随后被 GameServices2D 和地图复用。启动顺序为 PoolDriver（-2500）、GameServices2D（-2000）、WorldSession2D（-1500）、原 FrameworkEntry（-1000）。不要在同一场景放多个活动 PoolDriver。

## 调用方式

资源保留在 `_Game` 中，通过资源表中的 Key 获取，不要求移动到 Resources：

```csharp
var game = BigWorld.YouYou2D.GameServices2D.Instance;
var entry = game.Framework;
var shoot = entry.Resource.Load<SkillEditorKit.SkillClip>("skill.shoot");
player.GetComponent<BigWorld.YouYou2D.SkillActor2D>().TryPlay(shoot);

// 原框架服务：
var app = entry.Context;
// 原项目对象池（与地图共享）：
var pools = game.Pools.Service;
```

资源表是直接引用方式，引用的资源在场景 / 应用生命周期内驻留，不提供大资源的磁盘分块读取。重复 Key 和空引用会在建立 Provider 时明确报错，类型不匹配或未知 Key 返回 null。

`GameEvents2D` 使用事件编号 2000–2003，载荷分别是 `WorldSession2D`、`SkillStarted2D`、`ActorDamage2D`、`bool`。订阅业务事件时应在组件禁用 / 回池时对称移除监听。

`SkillActor2D.TryPlay` 只接受 `SkillSpace.TwoD`，检查暂停、存活、当前施法与冷却。左右朝向通过 `SkillFacing2D.SetFacing(-1 / 1)` 控制。地图目标受击只调用 `MapStreamedEntity.TakeDamage`，死亡交给地图回收；不要直接 Destroy 池中实例。

跨场景沿用常驻服务，地图停用时自行关闭所属作用域。完全退出游戏会话时调用 `await GameServices2D.Instance.ShutdownAsync()`；等待期间保留驱动 Update，以完成异步回收。如果 PoolDriver 由已有场景或其他模块提供，接入层不会替它销毁共享驱动，外部所有者应在最后调用 `ShutdownAsync()`。

地图原有 `SaveRuntimeState()` / `LoadRuntimeState(json)` 接口仍可使用，本层没有新增磁盘存档格式或存档菜单。瞬移需要先暂停角色物理、更新跟随目标，等待落点区域 `IsCellLoaded` 后再恢复物理。

## 横版相机调节

示例已绑定新相机，打开场景并 Play 即可。选中 **Main Camera → Camera Follow 2D** 调节以下参数；Play 中的修改需要退出后重新填写才能保存。

| 参数 | 默认值 | 用途 |
| --- | --- | --- |
| Orthographic Size | 5 | 画面垂直半高，减小可拉近镜头 |
| Look Ahead Distance | 2 | 根据角色朝向，在左侧或右侧多展示的距离 |
| Vertical Offset | 2 | 从角色脚底向上移动构图中心 |
| Horizontal / Vertical Damping | 0.35 / 0.8 | 分别控制横向与纵向跟随，数值越大越缓慢 |
| Vertical Dead Zone | 0.25 | 屏幕高度的 25% 为垂直缓冲区；小幅起伏不带动镜头 |
| Camera Distance | 10 | 相机到目标 XY 平面的 Z 距离 |
| Teleport Distance | 8 | 单帧移动超过此距离时自动复位；设为 0 关闭自动检测 |

方向沿用技能的 `SkillFacing2D`，停止移动后保留前视方向，跳跃速度不会触发左右前视。较高的跳跃超出死区后仍会平滑跟随。暂停时相机冻结。复活或短距离传送后，可显式调用 `CameraFollow2D.Snap()` 立即重置阻尼和死区；示例掉落复位已经接入。

Map 边界取地图资产的**完整矩形**（包含空格子），通过 `CinemachineConfiner2D` 限制整个可视区域。它支持 XY 平面内地图的平移、缩放和旋转；屏幕比例、地图尺寸或镜头大小变化时刷新边界缓存。地图过小时会自动缩小 Orthographic Size，使画面仍落在地图内。未指定 Map 时仅跟随，不启用边界限制。

运行时会创建同场景的 **CM 2D Follow (runtime)**，其中包含 Virtual Camera、Framing Transposer 和 Confiner 2D；地图边界 Collider 仅作为几何数据，禁用物理碰撞。关闭跟随组件或卸载场景会回收这些对象。统一从 Main Camera 的包装组件调参数，因为它会同步配置虚拟相机。包装组件在 LateUpdate 读取插值后的角色位置，再手动调用 Brain 更新；不要再添加其他直接移动主相机的脚本。

新建示例时自动绑定；旧示例可以运行 **Tools → BigWorld → 2D 框架 → 更新示例相机绑定** 补上 Brain 和 Map 引用，该菜单不重建角色、地图和技能。

## 示例资源与验证

- 地图：`Assets/_Examples/Framework2D/Data/Maps/Framework2DExample.asset`。
- 技能副本：`Assets/_Examples/Framework2D/Data/Skills`，可用现有技能编辑器修改。
- 角色 / 目标 / HUD：`Assets/_Examples/Framework2D/Prefabs`。
- 示例复用已有地图 TileType 和技能示例的精灵、动画、子弹外观资源；移动资源时保留 `.meta`。
- 生成菜单只补建缺失资源，不覆盖已有场景、地图或预制体。打包演示时，将示例场景加入 Build Settings。

验证脚本位于工程根目录 `Tests/YouYou2D/Verify-Framework2D.ps1`。它在 Unity 2022.3.62f3c1 中使用临时验证场景执行实际 Play Mode 检查；运行前关闭当前工程的 Unity 实例。日志和报告位于 `TestResults/YouYouFramework/Adaptation2D`。

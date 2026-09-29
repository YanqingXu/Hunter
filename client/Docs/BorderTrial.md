# 边境试炼：可通关的横版关卡

入口：**Tools → BigWorld → 正式关卡 → 打开边境试炼**。正式场景位于 `Assets/_Project/Game/Scenes/BorderTrial.unity`，已设置为 Windows 打包入口。原框架整合示例保留在 `_Project/Examples`。

## 操作与目标

- Enter / 开始游戏按钮：开始。
- A / D 或左右方向键：移动；有地面加减速与空中控制。
- Space：跳跃；松开可短跳，支持离地后 0.12 秒容错与落地前 0.12 秒输入缓冲。
- J：射击；K：近战。攻击继续使用 SkillEditorKit 和 `SkillActor2D`。
- Esc：暂停 / 继续。R：死亡后从检查点复活，通关后再玩一次。
- 击败 3 名守卫，跨越两处断桥，到达右侧发光出口。守卫攻击会先变色蓄力，离开攻击范围、绕背或跳高可以躲开。
- 两面旗帜分别是断桥营地和边境前哨。死亡重试保留本轮守卫击杀和检查点进度；“重新开始”重置整关。

检查点进度保存在本次游戏会话内，退出程序后重新开始。当前没有磁盘存读档。

## 资源与参数

| 内容 | 位置 / 组件 | 常调参数 |
| --- | --- | --- |
| 玩家 | `_Project/Game/Prefabs/Player.prefab` / PlayerController2D | 移速 6、跳速 11、加速度、短跳系数、跳跃容错 / 缓冲时间 |
| 战斗 | 同预制体 / SkillActor2D | 最大血量 100、基础攻击 25、受伤无敌 0.65 秒 |
| 角色表现 | 同预制体 / PlayerVisual2D | 跑跳姿态、受伤闪色、无敌闪烁；使用原有精灵，未新增逐帧跑步素材 |
| 守卫 | `_Project/Game/Prefabs/PatrolGuard.prefab` / PatrolEnemy2D | 巡逻速度 2、追击 3、视距 7、攻击距离 1.3、前摇 0.4 秒、伤害 12 |
| 守卫生命与生成 | `_Project/Game/Data/Maps/BorderTrial.asset` 的 Spawns | 生命 50；本轮击败后不刷新 |
| 技能 | `_Project/Game/Data/Skills/Shoot2D.asset` / Melee2D.asset | 使用现有技能编辑器改时间轴、伤害倍率、命中范围 |
| 关卡流程 | 场景中的 Level Session / LevelSession2D | RequiredKills、出生点、终点、掉落死亡线 |
| 检查点 | 场景中的 Checkpoint / Checkpoint2D | Order、DisplayName、SpawnPoint；出生点需位于可站立地形 |
| 相机 | Main Camera / CameraFollow2D | 正交大小、前视、阻尼、垂直缓冲和地图边界 |
| 界面 | `_Project/Game/Prefabs/GameHud.prefab` / GameHud2D | 由框架 UIManager 加载，运行时构建 UGUI 并适配画面大小 |

`LevelSession2D` 管理 Loading → Ready → Playing → Paused / Dead / Won；复活进入 Respawning，先禁用物理，移动玩家并等待目标及脚下区块加载，之后恢复满血、给予 1.5 秒保护并开启控制。重复重试请求不会启动多个复活流程。

守卫沿用地图对象池的稳定实体 ID 与生命状态；停用和回池会清理目标、速度、攻击计时与战斗驻留标记。角色的受伤、死亡事件由 `SkillActor2D` 提供；原整合示例不设置无敌时间，保留其原有受击规则。

地面的 Solid Tilemap 使用静态 `CompositeCollider2D` 合并相邻格子，处理玩家和守卫开启连续碰撞检测时被内部格子接缝卡住的问题。区块或格子变化后同步更新碰撞几何；OneWay 单向平台仍保持原 `TilemapCollider2D + PlatformEffector2D` 规则。此修复不改变关卡布局、巡逻速度或攻击参数，细节见 [地图碰撞说明](../Assets/_Project/Modules/Map2D/README.md#碰撞与预览)。

**创建缺失资源并设为打包入口** 菜单只补建缺失资源，不覆盖已调整的关卡、预制体和技能。正常修改直接保存对应资产；不必重新生成。

## 验证

关闭当前工程的 Unity 编辑器后，在项目根目录运行：

```powershell
& ./Tests/Gameplay/Verify-Gameplay.ps1
& ./Tests/YouYou2D/Verify-Framework2D.ps1
& ./Tools/Build-Windows.ps1
```

新关卡检查报告在 `TestResults/VerticalSlice`，包含真实物理运动、技能投射物击杀、敌人前摇和躲避、无敌受击、检查点、死亡重试、通关及整关重置。脚本将临时验证场景移出 Assets 后才允许导出工程。

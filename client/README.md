# BigWorld

Unity 2022.3.62f3c1 的 2D 横版项目，已接入地图编辑 / 分区加载、技能编辑、对象池和 YouYou 客户端框架。

用 Unity Hub 打开此目录。游戏入口：**Tools → BigWorld → 正式关卡 → 打开边境试炼**，然后 Play。发布入口：**Tools → BigWorld → 打包 → Windows 64 位（发布）**。

边境试炼已连接角色移动 / 跳跃容错、守卫巡逻追击与攻击、受伤无敌和击退、检查点复活、暂停菜单与通关重试。击败 3 名守卫并到达右侧出口即可通关。

- [目录说明](Docs/ProjectStructure.md)
- [打包与源码导出](Docs/Build.md)
- [资源分类](Assets/README.md)
- [2D 接入说明](Assets/_Framework/YouYou2D/README.md)
- [正式关卡与调参](Docs/BorderTrial.md)

正式资源位于 `Assets/_Game`，框架位于 `Assets/_Framework`，整合示例位于 `Assets/_Examples`，构建产物位于 `Builds`。

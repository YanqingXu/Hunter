# BigWorld

Unity 2022.3.62f3c1 的 2D 横版项目，已迁入课程原版 YouYouFramework 并接通地图、技能、共享对象池和横版玩法。当前为纯 C# + HybridCLR：保留 18 个非 Lua 管理器，原脚本业务已迁为 C#，xLua 源码、绑定、脚本与原生库已移除。

**原框架入口：`Assets/YouYouFramework/GameEntry.cs`；底层管理器：`Assets/YouYouFramework/Managers`。** 当前运行的是 `YouYou.GameEntry`，原精简 `Packages/com.youyou.framework` 已从活动依赖移出。

用 Unity Hub 打开本目录。游戏入口：**Tools → BigWorld → 正式关卡 → 打开边境试炼**，然后 Play。修改资源目录、UI 配置或表格后，先运行 **Tools → BigWorld → 2D 框架 → 生成原框架运行内容**。发布入口：**Tools → BigWorld → 打包 → Windows 64 位（发布）**，会自动构建原格式资源包和完整 ZIP。

边境试炼包含角色移动 / 跳跃容错、守卫巡逻追击与攻击、受伤无敌和击退、检查点复活、暂停菜单与通关重试。击败 3 名守卫并到达右侧出口即可通关。Cinemachine 2D 跟随、地图边界限制和原有技能流程保留。

C# 热更新使用 **HybridCLR 8.15.0 / IL2CPP**。主包只包含 AOT 启动场景，原框架和 2D 游戏代码作为热更 DLL 加载；发布补丁使用 `Tools/Build-HotUpdatePatch.ps1`，对应 AOT 基线保存在 `Builds/HotUpdate/Bases`。

- [原框架迁移范围与验证](Docs/YouYouFramework-ImportStatus.md)
- [完整迁移与原接口使用](Docs/YouYouFramework-Migration.md)
- [目录说明](Docs/ProjectStructure.md)
- [打包与源码导出](Docs/Build.md)
- [纯 C# 迁移与接口变化](Docs/PureCSharp-Migration.md)
- [HybridCLR 热更新、补丁和基线交接](Docs/HybridCLR.md)
- [资源分类](Assets/README.md)
- [2D 接入说明](Assets/_Project/Modules/YouYou2D/README.md)
- [正式关卡与调参](Docs/BorderTrial.md)

正式业务位于 `Assets/_Project/Game`，复用模块位于 `Assets/_Project/Modules`，整合示例位于 `Assets/_Project/Examples`，构建产物位于 `Builds`。原框架、Download 及第三方依赖保留课程约定路径，避免破坏原加载器和生成工具。

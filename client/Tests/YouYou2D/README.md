# 原版 YouYouFramework 的 2D 接入验证

从工程根目录运行（先关闭此工程的其他 Unity 实例，两套依次运行）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\YouYou2D\Verify-Framework2D.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\Gameplay\Verify-Gameplay.ps1
```

脚本默认使用 Unity `2022.3.62f3c1`；其他安装位置可传 `-UnityPath '完整的 Unity.exe 路径'`。不使用 `-nographics`，以验证相机并保存真实画面。脚本只归档自己创建的临时验证场景和探针，不删除或移动正式场景。20 分钟进程上限包含首次导入和真实 AssetBundle 构建，Play Mode 中另有 300 秒总时限。

两套入口分别生成/检查示例或关卡，再调用 `NativeFramework2DBuild.PrepareContent(EditorUserBuildSettings.activeBuildTarget)`。该步骤构建原格式 AssetBundle、VersionFile、AssetInfo，将项目目录绑定原 ParamsSettings，并通过原生成的 FlatBuffers 类型保留课程 UI 行、追加 9001（关卡 HUD）和 9002（框架示例 HUD）。请保留两个场景对应的资源目录以生成这两行配置。

- `Tests/YouYou2D` 验证 18 个管理器、C# 表/UI、原 Reporter 资源、地图池、技能和 Cinemachine 相机，检查关闭时清除被外部保留的事件节点，同进程重启后创建新管理器，保留外部拥有的对象池。结果：`TestResults/YouYouFramework/Adaptation2D/play-checks.json`、日志与 `demo-camera.png`。
- `Tests/Gameplay` 保留移动、敌人 AI、攻击、检查点、死亡重试、暂停、胜利与原框架接入的 37 项检查，并调用 `Tests/PureCSharp/PureCSharpChecks.cs` 检查迁移窗体、按钮、表、共享数据与协议。结果：`TestResults/VerticalSlice/play-checks.json`、日志与截图。真实 HybridCLR Player 复验见 `Tests/HotUpdate/Verify-Release.ps1 -IncludeGameplay`。

启动阶段分别等待 `GameServices2D.IsReady` 和世界/关卡就绪，各允许 60 秒；`InitializationError` 或关卡 Error 会立即写失败报告。`IsReady` 必须由原资源索引、17 张 C# 表和 FMOD Banks 加载完成后设置。所有探针直接访问 `YouYou.GameEntry` 原静态管理器及原 `UIFormBase`，不依赖已移除的简化包、`Framework.Context` 或自定义替代资源提供器。

这两套检查验证项目接入。原框架底层管理器的独立功能、网络、场景与完整内容检查仍使用 `Tools/Verify-YouYouFull.ps1`；源码保存在 `Tests/YouYouFull/Runtime`、`Tests/YouYouFull/Editor`。runner 每次临时复制到 `Assets/YouYouFullValidation`，Unity 退出后将该次目录及 `.meta` 归档到 `TestResults/YouYouFull/ValidationAssets-*`，不会长期编入游戏。具体流程见 [原框架验证说明](../YouYouFull/README.md)。基础实例非空检查不能代替这套实际功能验证。

Solid 地形现由静态 `Rigidbody2D` 和 `CompositeCollider2D` 合并相邻格子的碰撞边界，处理连续碰撞角色沿格子接缝移动时卡住的问题；增删格子、区块加载/卸载和构建副本清理后都会提交合并几何。OneWay 仍用独立 Tilemap 与 `PlatformEffector2D`，此次没有改动其单向穿越规则。地图层面的回归入口见 `Tests/Map2D/Editor/GridMapPhysicsVerification.cs` 的 `RunSolidRegressionBatch`；玩法中的巡逻、追击和玩家运动仍走真实 2D 物理。检查数量描述的是用例范围，最终是否通过以本次新生成的报告为准。

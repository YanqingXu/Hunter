# 构建与源码导出

## Windows 程序

使用 Unity 2022.3.62f3c1，安装 Windows Build Support。

1. 当前 Build Settings 已选择正式关卡 `Assets/_Game/Scenes/BorderTrial.unity`。以后发布其他关卡时，在 **File → Build Settings** 中调整启用场景及顺序。
2. 选择 **Tools → BigWorld → 打包 → Windows 64 位（发布）**；调试时可选择“Windows 64 位（开发调试）”。工具使用 Build Settings 的启用场景，检查缺失或重复条目。
3. 输出到 `Builds/Windows64/Release-时间戳/` 或 `Development-时间戳/`。每次独立输出，成功后生成同名 ZIP。
4. 分发完整 ZIP。解压后运行 `BigWorld.exe`，保留 Data、DLL、MonoBleedingEdge 等同目录依赖，不能只发送 exe。

**使用正式关卡（边境试炼）** 菜单可恢复正式入口；**使用 2D 试玩场景** 菜单可切回框架整合示例。只切换构建场景，不覆盖场景内容。

`Builds/latest-windows.json` 记录最后一次成功构建的位置、场景、Unity 版本、耗时、错误和警告数量。每个产物目录有 `build-report.json`。

## 命令行

先关闭当前工程的 Unity 编辑器，在工程根目录运行：

```powershell
& ./Tools/Build-Windows.ps1
# 开发调试版：
& ./Tools/Build-Windows.ps1 -Development
# Unity 安装路径不同时，通过 -UnityPath 指定 Unity.exe。
```

脚本只使用构建菜单的相同流程，不替换 Build Settings 的场景列表。命令行日志位于 `Builds/Logs`。构建失败会返回错误，保留报告以便定位。

## 工程源码 ZIP

```powershell
& ./Tools/Export-Source.ps1
```

输出到 `Builds/Source`。归档按白名单收集 Assets、Packages、ProjectSettings、Docs、Tools、Tests、README.md 和 .gitignore，不包含 Library、Temp、obj、Builds、TestResults、.codex 等缓存或工作记录。

接收方解压后，用 Unity Hub 添加 ZIP 中的 BigWorld 目录，首次启动等待 Unity 重新导入资源和解析包。源码 ZIP 与可运行 Windows ZIP 用途不同。

## 资源范围

构建包含所选场景及其引用资产。正式关卡使用 `_Game` 内的角色、技能、子弹、动画、UI 和资源表；初始精灵由既有技能示例复制而来。原整合示例仍引用其原有示例资源。`_Examples` 只是分类目录，不是 Unity 的排除开关；普通资源、Resources / StreamingAssets 和脚本程序集应按 Unity 各自规则管理。

中文界面使用项目内的 Noto Sans CJK SC 字体。构建工具将字体授权文件 `NotoFont-OFL.txt` 一并放入可运行包。

本机当前配置 Windows 64 位；Android、iOS、WebGL 应安装对应 Unity 模块并配置平台专用设置后再建立构建流程。

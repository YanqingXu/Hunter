# 构建与源码导出

## Windows 程序

使用 Unity 2022.3.62f3c1，安装 Windows IL2CPP Build Support 和 C++ 构建工具。当前使用 HybridCLR 8.15.0 / IL2CPP，详见 [热更新说明](HybridCLR.md)。

1. `ProjectSettings/HotUpdateProject.json` 的 scenes 选择正式关卡 `Assets/_Project/Game/Scenes/BorderTrial.unity`。以后发布其他关卡时调整此列表及顺序。Player Build Settings 只保留 HybridBoot。
2. 选择 **Tools → BigWorld → 打包 → Windows 64 位（发布）**；调试时可选择“Windows 64 位（开发调试）”。工具生成 HybridCLR 桥接和 AOT 元数据，并将游戏场景打成热更资源包。若仍启用 `DISABLE_ASSETBUNDLE`，会先关闭该宏并等待重编译，再自动继续构建。
3. 输出到 `Builds/Windows64/Release-时间戳/` 或 `Development-时间戳/`。每次独立输出，成功后生成同名 ZIP。
4. 分发完整 ZIP。解压后运行 `BigWorld.exe`，保留 BigWorld_Data、GameAssembly.dll 等同目录依赖，不能只发送 exe。

**使用正式关卡（边境试炼）** 菜单可恢复正式入口；**使用 2D 试玩场景** 菜单可切回框架整合示例。只切换构建场景，不覆盖场景内容。

`Builds/latest-windows.json` 记录最后一次成功构建的位置、场景、Unity 版本、脚本后端、耗时、错误和警告数量。每个产物目录有 `build-report.json`；`runtimeFilesVerified` 表示产物中的原版原生库与内容清单已完成存在性检查，不等同于游戏运行测试。

## 原版框架内容与宏

正式构建通过 HybridCLR 流程调用原内容构建器。它在 `Assets/StreamingAssets` 根目录生成原版 `VersionFile.bytes`、`AssetInfo.bytes`，资源包保持 `download/{datatable,audio,ui,reporter}.assetbundle` 和 `youyou2d/project.assetbundle` 结构。补丁流程也生成同样格式的内容到独立补丁目录。游戏场景由 HotUpdateProject.json 决定。

`DISABLE_ASSETBUNDLE` 仅供原版框架在编辑器直接读取资源。发布流程会将它从 Standalone 宏中移除；其他宏保留。所有 Player 构建均有检查，防止携带此宏进入发行包。不要在一个 `-executeMethod` 调用中修改宏后立即构建；这会在旧 Editor domain 中继续运行。需要临时使用课程直接读取模式时，可以通过原宏配置重新启用，下次打包会再次关闭。

Player 随包携带完整的 `BigWorld_Data/StreamingAssets`，并使用原版资源解析、加载和缓存流程。不要只复制 bundle 而遗漏两份原版清单，也不要将 `Assets/Download` 当作 Player 可直接访问的目录。

FMOD 使用课程原版 Windows x64 本机库。发布版检查 `fmodstudio.dll`，开发版检查 `fmodstudiol.dll`。构建准备步骤修正 x64 CPU 标记，并排除原插件元数据误选入 Windows 的 Linux `.so`。原 FMOD Editor 银行输入保留在 `Assets/Plugins/FMOD/Editor/BankSource`，正式音频仍由原 `Assets/Download/Audio/*.bytes` 进入运行资源，不把 Editor 银行另行打包。

xLua 源码、生成绑定、原生库和 AOT 封送桥已移除。`Assets/_Project/Game/link.xml` 与 HybridCLR 生成的 link.xml 保留热更所需的 C# 和 AOT 类型；热更程序集不编入 AOT Player。该版本必须配套新的纯 C# 主包基线，不能混用旧主包补丁。

## 命令行

先关闭当前工程的 Unity 编辑器，在工程根目录运行：

```powershell
& ./Tools/Build-Windows.ps1
# 开发调试版：
& ./Tools/Build-Windows.ps1 -Development
# Unity 安装路径不同时，通过 -UnityPath 指定 Unity.exe。
```

脚本分两次启动 Unity：第一次执行 `PrepareWindowsBuild` 保存宏、本机插件和 HybridCLR 配置，第二次在重新编译后的 domain 中执行正式构建。两次都指定 `-buildTarget Win64`，Player 场景列表设置为 HybridBoot。日志分别位于 `Builds/Logs/windows-prepare-*.log` 与 `windows-*.log`。构建失败会返回错误，保留报告以便定位。

## 工程源码 ZIP

```powershell
& ./Tools/Export-Source.ps1
```

输出到 `Builds/Source`。归档按白名单收集 Assets、Packages、ProjectSettings、Docs、Tools、Tests、README.md 和 .gitignore，不包含 Library、Temp、obj、Builds、TestResults、.codex 等缓存或工作记录。

接收方解压后，用 Unity Hub 添加 ZIP 中的 BigWorld 目录，首次启动等待 Unity 重新导入资源和解析包。源码 ZIP 与可运行 Windows ZIP 用途不同。

已经发布主包后，还应执行 `Tools/Export-HotUpdateBaseline.ps1` 保存配套基线 ZIP。源码 ZIP 不包含 Builds 目录，单独的源码归档不能替代为旧主包生成补丁所需的冻结基线。

## 资源范围

构建包含所选场景及其引用资产，以及正式流程生成的原框架 StreamingAssets 内容。正式关卡使用 `_Project/Game` 内的角色、技能、子弹、动画、UI 和资源表；初始精灵由既有技能示例复制而来。完整课程框架保持在 `Assets/YouYouFramework`，课程逻辑与第三方依赖保持各自原目录。原整合示例仍引用其原有示例资源。`_Project/Examples` 只是分类目录，不是 Unity 的排除开关；普通资源、Resources / StreamingAssets 和脚本程序集应按 Unity 各自规则管理。

中文界面使用项目内的 Noto Sans CJK SC 字体。构建工具将字体授权文件 `NotoFont-OFL.txt` 一并放入可运行包。

本机当前配置 Windows 64 位；Android、iOS、WebGL 应安装对应 Unity 模块并配置平台专用设置后再建立构建流程。

## 发布包启动检查

构建完成后执行命令：

    & ./Tools/Verify-WindowsPlayer.ps1

脚本读取 latest-windows.json，启动该次真实程序并等待原框架和关卡就绪，随后向自有进程窗口发送正常关闭请求；不会用强制结束冒充退出成功。报告写入 TestResults/VerticalSlice/standalone-smoke.json。

此前 Windows x64 / Mono 的运行记录属于原框架迁移验证。当前 HybridCLR 验证记录保存在 `TestResults/HybridCLR`，以真实 IL2CPP 主包和补丁加载结果为准。

最新纯 C# 主包、补丁与玩法验证见 [详细记录](HybridCLR-Validation.md)。

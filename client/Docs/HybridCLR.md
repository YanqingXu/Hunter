# HybridCLR C# 热更新

项目使用官方 HybridCLR 8.15.0（提交 d17a727fac8a56186c35294ec285362902f4a04d）和 Unity 2022.3.62f3c1。包内嵌在 `Packages/com.code-philosophy.hybridclr`，授权文件保留。当前构建工具针对 Windows x64 / IL2CPP；其他平台尚未建立或验证发布流程。

本机已通过主包、补丁、回退与 2D 玩法验证，具体版本和记录见 [验证结果](HybridCLR-Validation.md)。

## 代码与资源位置

| 内容 | 位置 / 程序集 | 发布方式 |
| --- | --- | --- |
| 启动、下载、校验、失败回退 | `_Project/Modules/HotUpdate/Runtime` / `BigWorld.HotUpdate.Bootstrap` | AOT，修改后重打主包 |
| 课程原框架、18 个管理器、2D 玩法、地图和对象池 | 原目录 / `Assembly-CSharp` | C# 热更新 DLL |
| 技能运行时 | `SkillEditorKit.Runtime` | C# 热更新 DLL，先于游戏程序集加载 |
| 游戏入口 | `_Project/Game/Scripts/Flow/GameEntryPoint.cs` | DLL 加载后反射调用 Initialize |
| 启动场景 | `_Project/Modules/HotUpdate/Boot/HybridBoot.unity` | 唯一随主包直接构建的场景 |
| 游戏场景 | `ProjectSettings/HotUpdateProject.json` 的 scenes | `hotupdate/scenes.assetbundle` |
| 原资源表、UI、音频及游戏资源 | 原 Download 等目录 | 原格式 5 个 AssetBundle 和两份索引 |

按本次迁移要求移除 Lua 专用 API：LuaManager、LuaForm、共享数组桥、脚本生成工具及 xLua 原生封送层。其余原接口保持，九个脚本窗体与协议业务迁为 C#，具体映射见 [纯 C# 迁移](PureCSharp-Migration.md)。原资源管理器继续使用 VersionFile、AssetInfo、加载器和缓存读取资源。热更加载完成后进入现有 2D 关卡。

## 构建主包

本机 Unity 已安装 Windows IL2CPP 模块。新机器需要相同 Unity 版本、Windows IL2CPP Build Support、可在命令行使用的 Git，以及 Unity 支持的 Visual Studio C++ 构建工具和 Windows SDK。

在项目根目录执行（先关闭这个项目的 Unity 编辑器）：

```powershell
& ./Tools/Build-Windows.ps1
& ./Tools/Verify-WindowsPlayer.ps1 -ExpectedCodeVersion 1
```

编辑器入口是 **Tools → BigWorld → 打包 → Windows 64 位（发布）**。首次准备自动安装项目本地 HybridCLR 原生运行时，需要访问 GitHub；其目录是 `HybridCLRData`，不修改其他 Unity 工程的全局 IL2CPP。正式构建执行 GenerateAll、生成桥接代码和裁剪 AOT、打资源与场景包、冻结基线，再构建 IL2CPP Player。

每次完整主包生成独立 baseId。输出程序/ZIP 位于 `Builds/Windows64`，最新成功结果记录在 `Builds/latest-windows.json`。`GameAssembly.dll` 是主包原生代码；热更 DLL 以 `.dll.bytes` 放在 StreamingAssets/hotupdate/assemblies。

游戏场景列表保存在 `ProjectSettings/HotUpdateProject.json`，顺序第一项为入口。菜单“使用正式关卡”及“使用 2D 试玩场景”会更新此列表。不要把游戏场景直接加回 Player Build Settings；那里仅保留 HybridBoot。使用项目构建入口，避免跳过元数据与资源生成。

## 生成和分发补丁

修改热更程序集代码或资源后，保留同一主包基线，执行：

```powershell
& ./Tools/Build-HotUpdatePatch.ps1
```

编辑器入口：**Tools → BigWorld → 热更新 → 生成当前主包的补丁**。补丁输出目录记录在 `Builds/HotUpdate/latest-patch.json`。把该目录全部文件按原相对路径部署到自己的 HTTPS 静态资源服务，以其中 `manifest.json` 为更新地址。发布时先上传所有资源，最后切换 manifest，避免玩家读取到未上传完成的版本。

主包内置更新地址在打主包之前写入 `ProjectSettings/HotUpdateProject.json` 的 `updateUrl`。默认空字符串，游戏使用随包内容和已验证缓存；未配置任何生产服务器。也可在本机验证时给 Player 传入 `-bigworld-update-url http://127.0.0.1:端口/manifest.json`。远程地址要求 HTTPS，本机回环地址允许 HTTP。

```powershell
& ./Tools/Verify-WindowsPlayer.ps1 -UpdateUrl 'http://127.0.0.1:8765/manifest.json' -ExpectedCodeVersion 2
```

补丁在下次启动时应用。正在运行的进程不卸载或替换已加载的 C# 程序集。修改 GameEntryPoint.CodeVersion 可在 Player 日志观察实际加载的代码版本。

## 基线与回退

`Builds/HotUpdate/Bases/<baseId>` 保存原主包 manifest、完整内容、裁剪后的 AOT DLL 和编译时 AOT DLL。**给已发布主包做补丁必须保留这份基线。** 补丁工具检查 AOT DLL 是否变化，并用官方 MissingMetadataChecker 检查新代码是否引用被裁剪的 API；不满足时要求重新发布主包。补丁复用该主包冻结的 AOT 补充元数据，不重新生成或替换它。

本横版项目在 Game/link.xml 保留完整的 UnityEngine.Physics2DModule，允许后续 2D 碰撞和物理逻辑补丁使用其 API。其他新增的 AOT、原生插件或桥接签名仍需按主包能力检查，不能通过热更 DLL 补回已裁剪的原生实现。

客户端按 baseId 隔离缓存，对文件大小和 SHA-256 进行校验，完整下载到临时目录后再切换。网络失败、错误主包版本或文件校验失败时，使用已验证的缓存或内置内容。关卡完成 WorldReady 后才记录为可用版本；上次启动未完成的版本在下次启动时会被隔离并回退。首次基线本身启动失败需要修复并重打主包。程序启动期间被强制关闭，也会被视为未完成启动。

SHA-256 用于文件完整性校验，HTTPS 用于服务器身份和传输保护；当前未实现独立补丁数字签名。生产部署地址和发布权限由项目方管理。

## 交接与日常开发

源码导出 `Tools/Export-Source.ps1` 包含嵌入的 HybridCLR 包，不包含 `HybridCLRData` 和 `Library` 缓存。使用 `Tools/Export-HotUpdateBaseline.ps1` 另存当前主包基线 ZIP；恢复到工程根目录后可继续为同一主包制作补丁。源码 ZIP、主包 ZIP、对应基线 ZIP 应成组保存。

编辑器可直接打开 BorderTrial 进行日常 Play；编辑器运行自身编译的程序集，不证明实际 Player 的热更成功。真实验证必须运行 IL2CPP 程序并观察 `BIGWORLD_HYBRIDCLR_ASSEMBLY_LOADED`、`BIGWORLD_HYBRIDCLR_CODE_VERSION`、`BIGWORLD_HYBRIDCLR_READY` 及原框架、游戏就绪日志。

FMOD 编辑器缓存会检查银行输入根目录；工程复制或移动后自动重建缓存，避免沿用旧工程的绝对音频路径。

## 复验

`Tests/HotUpdate/ManifestChecks` 直接链接生产清单代码，检查路径、主包/平台匹配、程序集种类、大小、SHA-256 和下载完整性：

```powershell
dotnet run --project ./Tests/HotUpdate/ManifestChecks/ManifestChecks.csproj -- ./TestResults/HybridCLR/manifest-checks.json
```

完整 Player 复验要求源码和内置主包的 GameEntryPoint.CodeVersion 都为 1。脚本临时将版本改为 2，构建匹配原主包的补丁，用本机 Python 3 HTTP 服务测试，最后恢复源码和原缓存并关闭测试服务：

```powershell
& ./Tests/HotUpdate/Verify-Release.ps1 -PythonPath '你的 Python 3/python.exe' -IncludeGameplay
```

`-IncludeGameplay` 将既有 GameplayPlayChecks 暂时编入测试补丁，保留全部玩法断言，省略与热更无关的截图导出；正常补丁启动不会自动运行探针。测试包括原管理器与 C# 界面/协议、标题/暂停 UI、移动跳跃、真实地图碰撞、守卫 AI、技能子弹、检查点、死亡复活、胜利、重开和完整关闭。该测试补丁只用于验证，不应作为正式补丁部署。

结果在 `TestResults/HybridCLR/player-latest.json`，每项保留实际 Player 日志，玩法断言保存在对应运行目录的 gameplay-checks.json。回退测试通过留下启动未完成标记来模拟中断，未使用系统断电或真实崩溃作为测试方式。脚本同时校验 exe 和 GameAssembly.dll 在补丁验证前后保持相同 SHA-256。

官方参考：[热更新程序集设置](https://www.hybridclr.cn/docs/basic/hotupdateassemblysetting)、[构建流程](https://www.hybridclr.cn/docs/basic/buildpipeline)、[加载热更代码](https://www.hybridclr.cn/docs/basic/runhotupdatecodes)。

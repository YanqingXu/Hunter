# 原版 YouYouFramework 验证

验证源代码位于 `Tests/YouYouFull/Runtime` 和 `Editor`，不会作为游戏运行程序集进入 Player。不要把这些源码长期放到 `Assets`。

在关闭本工程的 Unity 编辑器后，从项目根目录运行：

```powershell
& ./Tools/Verify-YouYouFull.ps1 -Stage Core
& ./Tools/Verify-YouYouFull.ps1 -Stage Content
& ./Tools/Verify-YouYouFull.ps1 -Stage Bundles
& ./Tools/Verify-YouYouFull.ps1 -Stage Scenes
& ./Tools/Verify-YouYouFull.ps1 -Stage Network
```

可通过 `-UnityPath` 指定 Unity 2022.3 的安装位置。脚本临时复制运行时探针、`Tests/PureCSharp/PureCSharpChecks.cs` 与 Editor 入口到 `Assets/YouYouFullValidation`，场景与测试素材由入口动态创建，不从源码目录携带 fixture 或场景。

- `Content`、`Scenes` 使用原版 `DISABLE_ASSETBUNDLE` 编辑器直接读取分支。
- `Bundles` 必须关闭该宏，真实构建并加载原版格式的 bundle、版本和资产索引，包括持久化缓存与离线包的优先级/MD5 检查。
- `Core`、`Network` 保持执行前的宏状态；Network 使用独立的 loopback HTTP/TCP 服务。

脚本先以 `-quit` 执行 `PrepareDirectMode` 或 `PrepareBundleMode`，再启动新的 Unity 进程运行验证，避免改宏后仍在旧 domain 执行。结束时，待自己启动的 Unity 进程退出，再仅恢复 Standalone 的 `DISABLE_ASSETBUNDLE` 状态，保留当前其他宏，并恢复执行前的 Build Settings 场景列表。宏恢复只匹配 `scriptingDefineSymbols` 映射，不会误改 `buildNumber.Standalone`。此方式也适用于脚本编译失败时的清理。

结果写入 `TestResults/YouYouFull/<stage>-checks.json` 和相应日志。成功、失败或超时都会将本次创建的临时资产目录连同 `.meta` 移到 `TestResults/YouYouFull/ValidationAssets-*`。预先存在的验证目录会被拒绝覆盖；脚本只结束自己启动的 Unity 进程。

首次整理前的旧探针、fixtures 和验证场景已归档至 `TestResults/YouYouFull/ValidationAssets-SourceMigration-*`，不属于游戏源码或发布内容。

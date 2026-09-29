# HybridCLR 纯 C# 版本验证

2026-09-29，Unity 2022.3.62f3c1 / HybridCLR 8.15.0 / Windows x64 IL2CPP。

| 检查 | 当前结果 |
| --- | --- |
| 框架 API 审计 | 1414 项非脚本专用原声明保持，未预期缺失 0；165 项脚本专用声明删除/替换、7 个文件移除 |
| 编辑器内容 | 103 项通过，涵盖 C# 表、九个迁移 UI、协议、FMOD 和反复初始化/关闭 |
| 本机网络回归 | 73 项通过，覆盖 HTTP 签名/重试、TCP 加密压缩及断点下载，无警告 |
| 热更清单 | 23 项通过：平台、主包 ID、路径、哈希及下载完整性 |
| 真实 IL2CPP Player | 8 项通过，所有进程正常关闭，退出码 0，无运行异常 |
| 热更后的玩法与 C# 迁移 | 55 项通过，原 37 项玩法检查加 18 项迁移检查 |
| 主包构建 | 最终 BuildReport 为 0 错误、0 警告 |
| 发行产物 | 94 项通过，包含三份 ZIP 的 CRC、资源哈希、无 xLua 文件/热更类型、无失效资源引用 |

编辑器和 Player 检查有重复覆盖，不把数量相加作为独立测试总量。编辑器内容测试记录一条测试场景 FMOD Listener 警告；源码首次编译仍有原框架旧 API 等警告，最终增量主包报告的 0 警告不代表所有源码警告已修复。

主包：`Builds/Windows64/Release-20260929-152951-063.zip`。

baseId：`bw-aa0d0c18e3294cd2997a6753e6103f5e`。

GameAssembly.dll SHA-256：`529B612E2E3B93CB65BE1D2187D416F5CE7DFB7D4D70698EB135D124C56FAA40`。

测试先启动内置代码 1，随后只替换热更内容加载代码 2，确认无更新地址时使用缓存；损坏下载和错误主包补丁均回退到验证过的版本。留下启动未完成标记后恢复到内置代码 1，并拒绝再次使用被隔离的版本。整个过程 exe 与 GameAssembly.dll 哈希保持不变。

55 项检查覆盖 18 个管理器、17 张 C# 表、九个迁移窗体、64 位共享数据和角色协议、UI 按钮及缓存重开，随后完成真实关卡的标题/暂停、移动跳跃、地图碰撞、守卫和技能子弹、伤害死亡、检查点复活、出口与胜利重开、对象池及管理器释放。

证据：

- [Player 汇总](../TestResults/HybridCLR/player-latest.json)
- [本次 Player 日志与 55 项玩法报告](../TestResults/HybridCLR/player-20260929-153436-146)
- [103 项编辑器内容报告](../TestResults/YouYouFull/content-checks.json)
- [73 项网络报告](../TestResults/YouYouFull/network-checks.json)
- [接口审计](../TestResults/PureCSharp/api-preservation.json)
- [清单检查](../TestResults/PureCSharp/manifest-checks.json)
- [发行产物检查](../TestResults/PureCSharp/artifacts.json)

测试补丁位于 `patch-20260929-073533-229`，带有 VALIDATION-ONLY 标记，不能作为正式补丁部署。正式补丁 `patch-20260929-073904-812` 已去除测试探针，实际加载代码 1、正常退出码 0；见 [正式补丁验证](../TestResults/PureCSharp/production-patch-smoke.json)。最新补丁以 `Builds/HotUpdate/latest-patch.json` 为准。实际程序默认使用随包内容；项目尚未配置生产 HTTPS 更新地址。

复验命令和基线交接见 [HybridCLR.md](HybridCLR.md)，接口映射见 [PureCSharp-Migration.md](PureCSharp-Migration.md)。此前带 xLua 的主包和验证记录已标记为历史，不能替代本次结果。

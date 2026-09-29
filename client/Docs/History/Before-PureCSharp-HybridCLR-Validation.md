> 历史记录：移除 xLua 之前的迁移与验证；不代表当前版本。当前入口见 [纯 C# 迁移](../PureCSharp-Migration.md)。

# HybridCLR 验证记录 · 2026-09-29

实际工程：`E:\study\BigWorld`。Unity 2022.3.62f3c1，Windows x64 / IL2CPP，官方 HybridCLR 8.15.0。验证在真实 Windows Player 中完成。

| 检查 | 结果 |
| --- | --- |
| 原框架接口审计 | 177 个源文件、1579 个原 API 声明，无缺失 |
| 热更清单校验 | 23 项通过 |
| IL2CPP 主包构建 | 成功，0 错误、12 条旧 API / 未使用成员等编译警告 |
| 真实 Player 集成 | 8 项通过，均正常退出，无强制结束、无运行异常 |
| 热更代码上的 2D 玩法 | 37 项通过 |

8 项 Player 检查依次是：内置版本 1 启动、从本机 HTTP 服务加载版本 2、无更新地址时加载缓存版本 2、损坏下载回退到版本 2、错误 baseId 补丁被拒绝、版本 2 上完整玩法检查、启动未完成标记导致回退至内置版本 1、隔离过的失败版本不再启用。

37 项玩法检查复用了已有 GameplayPlayChecks 的全部断言，省略截图导出。覆盖原版 19 个管理器、Lua 和课程表格启动，原资源/UI 加载、标题与暂停、移动跳跃和地图碰撞、守卫与真实技能子弹、受伤死亡、检查点复活、出口判定、胜利重开，以及资源池和原管理器完整释放。

该轮测试的 exe SHA-256：`5B7D85F7D69A1BE92841E7A6B630827C079F3BF6C3CFD06E75149CB09E043D7A`。

GameAssembly.dll SHA-256：`8563321BBDA19425EFD581FFF12CEF136FB66F65AB3A1593955AB22EA5023715`。

两者在整个补丁验证前后保持一致。版本 1 → 2 来自新的 Assembly-CSharp 热更 DLL。主包基线为 `bw-6c748cce526d4c3497b61806e4fb97d4`。

原始报告：`TestResults/HybridCLR/player-latest.json`；每项 Player 日志和 37 项玩法报告位于 `TestResults/HybridCLR/player-20260929-145037-142`。清单检查及原接口审计也在 TestResults/HybridCLR。测试输出不包含在常规源码 ZIP 内。

测试结束后，源代码版本恢复为 1，临时探针移出 Assets，本机测试服务关闭，测试缓存归档。包含探针的验证补丁带有 VALIDATION-ONLY.txt，不用于正式发布。随后从恢复后的业务源码重新生成无探针补丁。

尚未配置生产更新服务器；默认 updateUrl 为空。本次仅验证 Windows x64，未声称 Android/iOS/WebGL 已验证。回退用启动标记模拟中断，未进行实际断电实验。

使用与复验命令见 [HybridCLR 热更新说明](../HybridCLR.md)。

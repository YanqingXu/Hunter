# 纯 C# + HybridCLR 迁移

2026-09-29，按项目要求将原脚本业务迁为 C#，并从活动工程移除 xLua。框架入口仍为 `Assets/YouYouFramework/GameEntry.cs`，保留 18 个非 Lua 管理器。地图、技能、共享对象池、Cinemachine 2D 相机和边境试炼保持原接入方式。

## 已清理的内容

移除 `Assets/XLua`、`Download/xLuaLogic`、框架 `Managers/Lua`、Dll2LuaLib 编辑器工具，以及 Tools 中的生成/注入程序。Windows、macOS、Linux、Android、iOS、WebGL、WSA 的 xLua 原生插件和对应 `.meta` 一并移出。HybridCLR 启动程序集的 `XLuaNative.cs` 封送桥已删除。

打包清单不再包含 xlualogic.assetbundle，StreamingAssets、热更 manifest 与 AOT 生成文件随新主包重新生成。当前内容包为 `download/{datatable,audio,ui,reporter}.assetbundle` 和 `youyou2d/project.assetbundle`；游戏场景另在 `hotupdate/scenes.assetbundle`。

删除前的完整源码归档及移出的原文件保存在本机 `.codex/backups/PureCSharp-20260929`，不进入源码导出或发行包。旧 Builds 与历史验证目录保留此前版本，不能与新主包混用。

## C# 替代实现

| 原功能 | 当前实现 |
| --- | --- |
| Main / GameInit 启动 | GameServices2D：原资源索引 → 17 张 C# 表 → UI 补充表 → FMOD → 2D 世界 |
| Job / JobLevel 脚本表 | 原 FlatBuffers `DTJobListExt`、`DTJobLevelListExt`；DataTableManager 加载两表 |
| 用户共享数组 | ShareUserData 的 AccountId / CurrRoleId / CurrJobId，保持 long / long / int 和 Dispose |
| 动态 PB 描述文件 | 已有 Google.Protobuf 生成类型及 SocketProtoListener；补齐返回角色列表分发 |
| 原始字节发包 | `GameEntry.Socket.SendRawMainMsg(ushort, byte, byte[])`，沿用原压缩、加密和发送逻辑 |
| 脚本 HTTP / JSON 适配 | UIAccountForm 直接调用 Http.SendData 与 LitJson，保留账号协议与原 HTTP 签名 |
| 派发整数事件 | BoundUIForm 使用原 VarInt 池、CommonEvent.Dispatch，并在 finally 归还 |
| 共享数据生成器 | ShareDataSettings 仅生成可直接使用的 C# 属性，不再生成共享数组或脚本文件 |

`Assets/YouYouScript/UI/UIForm` 中的九个控制器替换原脚本窗体：UILoadingForm、UILoginForm、UIRegisterForm、UILogonBackgroundForm、UICreateRoleForm、UISelectRoleForm、UITaskForm、UITaskDetailForm、UIMainCityForm。预制体改绑对应 `.cs` 的 GUID，组件对象引用保留在 BoundUIForm 的命名绑定中。任务详情沿用原静态展示内容；没有把原本未实现的业务计作新增功能。

登录和注册保留输入校验、账号请求、错误码弹窗、账号 ID 与流程跳转，并忽略关闭/重用窗口后的过期响应。角色列表由 C# 解码后选择创建或选角界面，保持 64 位角色 ID；职业列表和头像沿用原表和资源加载器。创建界面缓存重开时重新设置返回按钮状态。

移除 LuaManager、LuaForm、LuaArrAccess、相关特性、事件常量、脚本资源分类与脚本工具接口。其余原接口没有简化。`Tests/YouYouFull/ApiAudit` 使用 `--pure-csharp` 时只豁免明确列出的脚本专用接口；枚举还校验其余分类名称与数值保持。当前报告基于原 177 文件 / 1579 声明：7 个脚本专用文件移除，165 项声明为授权删除/替换，其余 1414 项原声明保留，未预期缺失 0 项。

## 构建与验证

本次移除了 AOT 封送代码，已重新建立主包基线。以后发补丁应使用这一新主包及其 `Builds/HotUpdate/Bases/<baseId>`；旧 xLua 主包的 AOT 基线不适用于本次发布。

```powershell
& ./Tools/Build-Windows.ps1
& ./Tools/Build-HotUpdatePatch.ps1
& ./Tools/Export-Source.ps1
& ./Tools/Export-HotUpdateBaseline.ps1
```

编辑器内容检查已通过 103 项，覆盖原表/UI/FMOD、九个迁移窗体、按钮事件、角色列表协议、64 位 ID、界面缓存重开以及两轮初始化/释放。测试宿主记录一条 FMOD Listener 警告；该场景不是正式关卡。

`Tests/PureCSharp/PureCSharpChecks.cs` 同时用于编辑器内容测试和真实 IL2CPP 热更补丁测试。真实主包与补丁验证结果见 [HybridCLR 验证](HybridCLR-Validation.md)。

网络回归 73 项通过、无警告；真实 Player 的 8 项补丁/回退检查与 55 项玩法/迁移检查通过，所有测试程序正常退出。当前主包为 `Release-20260929-152951-063`，新基线为 `bw-aa0d0c18e3294cd2997a6753e6103f5e`。

产物检查 94 项通过：源码、Windows 和基线 ZIP 均无 xLua 文件；全部 manifest 文件大小/SHA-256 匹配，热更程序集无脚本运行时类型和测试探针，活动预制体、场景及配置没有指向被删除资源的 GUID。见 [产物报告](../TestResults/PureCSharp/artifacts.json)。

课程线上账号与网关未部署，本地 UI / 协议验证不代表已完成真实服务器登录联调。

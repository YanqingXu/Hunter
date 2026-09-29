# YouYouFramework 当前接入

框架源码位于 `Assets/YouYouFramework`，业务与生成协议位于 `Assets/YouYouScript`。入口是原 `YouYou.GameEntry`；先完整迁入课程源码，再按项目要求完成 2D 与 HybridCLR 接入、迁走脚本业务并移除 xLua。原抽取版 `Packages/com.youyou.framework` 已停用。

当前 18 个管理器：Logger、Event、Time、Fsm、Procedure、DataTable、Socket、Http、Data、Localization、Pool、Scene、Resource、Download、UI、Audio、Input、Task。各自仍是原实现；LuaManager 及专用桥接接口的替代方式见 [纯 C# 迁移](PureCSharp-Migration.md)。

`GameServices2D` 创建并配置真实 GameEntry，默认关闭课程联网流程的自动启动。它从 HybridCLR 当前验证版本读取 VersionFile / AssetInfo，加载 17 张 C# FlatBuffers 表、2D UI 补充表与 FMOD Banks，完成后才设置 IsReady。原 JobLevel 表现在由 C# 批量入口加载。

正式 HUD 通过原资源索引和 UI 表 9001 加载，示例 HUD 使用 9002。课程 UI 表行继续保留；九个原脚本界面的预制体改绑 C# 控制器。原界面分组、层级、缓存与回收机制保持。地图和技能实体使用 BigWorld.Pooling，原 GameEntry.Pool 继续管理框架资源、窗体和类对象。

地图为 XY 平面、Collider2D / Rigidbody2D，Solid 地形使用 CompositeCollider2D 合并接缝，OneWay 平台保留 PlatformEffector2D。相机继续采用 Cinemachine 2D 跟随与地图边界限制。

异步关闭先停止世界和回调，等待地图实体及共享池完成清理，再等待 GameEntry.ShutdownAsync() 退出当前 Unity 帧并释放原池与日志。重复请求共享关闭任务，同进程可以重新初始化。事件销毁逐节点清除委托；没有脚本环境存活检查或等待脚本回收的分支。

菜单 **Tools → BigWorld → 2D 框架 → 生成原框架运行内容** 生成五个内容包与原格式压缩清单。正式 Windows 构建自动完成内容与场景打包。详见 [打包说明](Build.md)、[HybridCLR](HybridCLR.md)。

当前接口审计和回归结果见 [纯 C# 验证](PureCSharp-Migration.md)。原 177 文件 / 1579 声明的全量迁入记录保存在 [历史说明](History/Before-PureCSharp-YouYouFramework-Migration.md)，其中 Lua 和 Mono 的验证不能视作当前发行包结果。

课程账号、网关与完整 3D 世界未部署。C# 登录、注册和选角逻辑已迁移，本地验证不能代替对真实课程服务器的联调。当前发布目标为 Windows x64 / IL2CPP。

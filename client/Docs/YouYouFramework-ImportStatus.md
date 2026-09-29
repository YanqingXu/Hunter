# YouYouFramework 导入状态

当前使用完整课程框架的非 Lua 实现与 HybridCLR 热更；框架在 `Assets/YouYouFramework`，管理器在其 `Managers`，入口为 `YouYou.GameEntry`。

原全量迁入阶段包含 177 个框架 C# 文件和 19 个管理器。随后按要求移除 xLua，保留 18 个非 Lua 管理器及其接口，九个脚本 UI、共享用户数据和角色列表协议已迁为 C#。不再需要安装 xLua 或生成 Lua 绑定。

- [迁移细节与接口映射](PureCSharp-Migration.md)
- [当前 2D 接入](YouYouFramework-Migration.md)
- [构建和导出](Build.md)
- [HybridCLR 验证](HybridCLR-Validation.md)

原版 Lua / Mono 测试与早期迁移数量保存在 [历史记录](History/Before-PureCSharp-YouYouFramework-ImportStatus.md)。最新程序以 `Builds/latest-windows.json` 为准，源码和配套 AOT 基线分别导出。

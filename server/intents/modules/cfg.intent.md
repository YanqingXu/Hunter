---
id: SRV-013
status: active
target: ["hunter_server_core", "hunter_scripts", "hunter_content", "hunter_client"]
depends_on: ["SRV-010", "SRV-011", "SRV-012"]
verification: ["hunter_gameplay_content_contract", "hunter_gameplay_contract", "hunter_gameplay_edges_contract", "hunter_lua_cfg_integration", "hunter_package_contract"]
---

# Lua 配置与玩法规则

## 目标与来源

根目录 design/demo_sources.json 显式选择生产记录，各表导出独立 Lua table，保留数字主键和
英文字段。Python 只负责读取、类型、选取与确定性序列化；配置转换、关联及玩法语义由 Lua
执行。game.cfg 在构建和启动使用同一实现。客户端 JSON 和摘要是该实现的派生产物。
旧配置只作显式测试夹具，生产不回退旧表或将缺值解释为零。未选草稿不影响内容摘要。

## 不变量、线程与所有权

Lua 可以缓存只读配置；全部可变对局状态仍由逻辑线程上的 C++ World 独占。
Lua 决定初始化参数、配装、拾取叠堆、掉落拆分、工具扣次清槽和配置语义校验。
C++ 保留网络、存储、身份句柄、容量、整数边界和原子提交，不持有完整配置文档，
不按配置种类重复裁定玩法。原生只保存快照、实例和结构验证需要的只读投影。
禁止同步脚本重入，Tick 高频路径保持句柄和标量访问。

## 接口与失败行为

Host、上下文、低频脚本事件和内部状态升级 v7；网络 v5、内容 v4、SQLite V1 保持。
启动上下文不再携带 content，Lua 加载配置后一次性注册内容身份和原生结构边界。
Runtime 先只读调用 Lua 验证并规范化配装，成功后再分配持久局号；参数冲突与重试语义不变。
拾取、补给及消耗先算完整变更，原生核对句柄、实例、数量和容量后一次提交，业务拒绝不修改状态。
Lua 先验证候选快照的配置语义，C++ 再验证独立候选的结构，全部成功后才替换活动世界。
错误候选不修改原状态或句柄代际。旧上下文、旧内部快照明确拒绝。
构建失败不发布半份制品；配置随源码组装或签名 Bundle 加载，不开启任意文件访问和热更新。

## 验证与交付

覆盖确定性导表、中文转义、缺值引用单位错误、发布失败、草稿隔离及旧摘要对照。
修改合法表值后只重新生成脚本，验证同一服务端可执行文件使用新玩法数值。
两种 Runtime 验证配装、拾取原子性、工具耗尽、补给幂等、掉落、非法快照和旧版本拒绝。
执行连续十局、十次启停、旧档案和解包联调；确认 Bundle 服务端不链接编译器。
完成实际验证后再更新状态；Windows 证据不能代替 Unity 或 Android 验收。

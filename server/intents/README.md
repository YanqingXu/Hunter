# 服务端 Intent 索引

契约先于实现；`active` 表示当前实现契约，并不表示所有平台已验证。
Android 真机和 Unity 联调必须单独提供证据。结果见 [验证记录](../VERIFICATION.md)。

正式契约使用 `*.intent.md`，模板不纳入索引。元数据包含
`id/status/target/depends_on/verification`，列表使用单行 JSON 数组（合法 YAML）。
`verification` 是 CTest 名称，`target` 是 CMake 目标；draft/deferred 不产生可用性声明。
验证项可加 `dev:`（仅开发 Runtime）或 `tools:`（仅构建本机工具）前缀，前缀不属于
CTest 名称；无前缀项在两种模式都必须存在。检查器从 CMake 接收当前构建模式，拒绝未知前缀，
每个 active 契约在当前模式仍必须有至少一个真实验证入口。

| ID | 契约 | 本轮边界 |
| --- | --- | --- |
| SRV-001 | [宿主](architecture/host.intent.md) | Windows 控制与退出；Android P1 延期 |
| SRV-002 | [传输](modules/transport.intent.md) | 回环握手、分帧、背压与输入序号 |
| SRV-003 | [调度](modules/tick.intent.md) | 逻辑线程、固定 Tick、暂停和有界队列 |
| SRV-004 | [脚本桥接](modules/host.intent.md) | 同步输出事务、受限 Host 与异步探针 |
| SRV-005 | [模块](modules/modules.intent.md) | 构建组装、原生状态与句柄视图、制品身份 |
| SRV-006 | [热更新](modules/reload.intent.md) | deferred；安全点切换不在本轮 |
| SRV-007 | [存档](modules/storage.intent.md) | deferred；SQLite 事务不在本轮 |
| SRV-008 | [PvE 验收](usecases/pve.intent.md) | deferred；完整撤离与 Android 真机尚待验收 |
| SRV-009 | [基础战斗](usecases/combat.intent.md) | 基础对象组合、实体／配置 ID、枪械与伤害、普通怪和终态重开；Windows 验证 |
| SRV-010 | [原生对象](modules/objects.intent.md) | C++ 权威状态、七类句柄绑定、原生高频网络与基础 Item |

`tools/verify_intents.py` 对照构建生成的目标清单与 CTest 注册信息检查 active 契约，
并检查索引、唯一 ID、引用及依赖环。它核对注册名称，不检查测试源码内容或证明行为覆盖；
人工评审和实际运行仍需核对测试是否验证了相应契约。

## 开发顺序

先修改契约和适用规则，明确所有权与失败语义；新增能暴露实际风险的测试；
实现后运行关联测试，最后同步状态与实际命令。未运行的平台检查不得写成通过。

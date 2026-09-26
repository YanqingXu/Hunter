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
| SRV-007 | [存档](modules/storage.intent.md) | SQLite 异步事务；宿主与玩法接入由 SRV-011 扩展 |
| SRV-008 | [PvE 验收](usecases/pve.intent.md) | deferred；完整 Demo 的 Unity 与 Android 真机尚待验收 |
| SRV-009 | [基础战斗](usecases/combat.intent.md) | 基础对象组合、实体／配置 ID、枪械与伤害、普通怪和终态重开；Windows 验证 |
| SRV-010 | [原生对象](modules/objects.intent.md) | C++ 权威状态、七类句柄绑定、原生高频网络与基础 Item |
| SRV-011 | [单人撤离](usecases/demo.intent.md) | Windows 撤离、背包与真实存档闭环，进度见 V1_TASKS |
| SRV-012 | [验证玩法](usecases/gameplay.intent.md) | active；免费配装、姿态恢复、枪弹工具与场景交互，两种 Windows Runtime 已验证 |
| SRV-013 | [Lua 配置](modules/cfg.intent.md) | Lua 表加载、配置解析与玩法规则迁移；Windows 双 Runtime 已验证 |

`tools/verify_intents.py` 对照构建生成的目标清单与 CTest 注册信息检查 active 契约，
并检查索引、唯一 ID、引用及依赖环。它核对注册名称，不检查测试源码内容或证明行为覆盖；
人工评审和实际运行仍需核对测试是否验证了相应契约。

## 开发顺序

先修改契约和适用规则，明确所有权与失败语义；新增能暴露实际风险的测试；
实现后运行关联测试，最后同步状态与实际命令。未运行的平台检查不得写成通过。

## 公共结果类型约定

`src/common/Types.h` 统一提供 `Expect<T, E>` 与 `Unexpect<E>`，分别等价于
`std::expected<T, E>` 与 `std::unexpected<E>`。本次迁移覆盖 `src/` 的全部引用，
保留成功值、错误值、`void` 返回及 `Unexpect(error)` 的模板参数推导语义；
线程、所有权、失败路径、协议和 ABI 不变。验证使用 Windows 开发构建及既有
会话、脚本、网络、核心与存档契约，不新增业务行为。

2026-09-26 验证：使用构建缓存指定的 VS 2026 CMake 执行
`cmake --build --preset win-dev --parallel 8` 成功；
`ctest --preset win-dev -R "hunter_(session|script|async|net|core|storage|intent).*"`
通过 9/9。`src/` 共替换 154 处，原始拼写仅保留在别名定义中；Android 未验证。

后续容器与视图迁移：在 `Types.h` 定义 `Arr<T, N>`、`Opt<T>`、
`Span<T, N = std::dynamic_extent>` 与 `StrView`，替换 `src/` 对应引用。
数组容量、可选值状态、视图的只读限定和借用生命周期保持不变；保留数组与视图的
模板参数推导，并同时支持固定和动态长度视图。验证使用开发构建及相关既有契约。

2026-09-26 容器与视图验证：VS 2026 CMake 的 `win-dev` 构建成功，
相关 CTest 契约通过 17/17，测试输出见 `build/short-types-tests.log`。
`src/` 本轮替换 39 处，原始拼写仅保留在别名定义中，差异检查通过；Android 未验证。

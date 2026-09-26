---
id: SRV-004
status: active
target: ["hunter_script"]
depends_on: []
verification: ["hunter_script_contract", "dev:hunter_async_contract"]
---

# Luax Host 桥接

## 目标与非目标

真实 Luax Runtime 执行七个生命周期入口和只读配装校验入口；提供只读 `net/cfg/diagnostics` namespace、
受预算约束的调用和调用级输出事务。独立 Asio 异步适配验证 continuation 与 detached completion。
桥接不决定玩法，不实现存档、热更新或跨代次活动 continuation 迁移；玩法由 SRV-009、SRV-011、SRV-012 与 SRV-013 定义。

## 不变量、线程与所有权

Runtime、Isolate、ModuleHandle、FunctionHandle 和 ContinuationHandle 仅属于创建线程。
Script 不可复制；所有入口拒绝错误线程与同步重入。普通 table 不穿过 Host。
所有执行指定指令、原生工作量、截止时间和内存预算，JSON 最大长度来自 Cfg。
输出先缓冲，入口返回 true 且整批 schema 校验通过才移交 Runtime；Runtime 再预留发送容量。
任何入口失败清空输出并禁止继续使用本局状态。导出、导入、校验和退出禁止发包。

## 接口与值语义

`Script::open/event/input/tick` 返回持有原生 Protobuf 消息的 ScriptOut 数组；export_state 返回 JSON 字符串。
input 在 C++ 处理，Lua on_event 仅处理低频控制；stats 在入口边界提供内存和 GC 指标。
脚本 init/on_event/tick/import_state/validate_state/shutdown 返回 true，export_state 返回 string。
Host `net.emit(kind,payload)` 接受契约列明的 v5 登录、开局、确认、快照、事件和错误 JSON；
ID、seq/tick_id 为十进制字符串，
字段集合、数值范围以 `lua/contract.json` 为权威；构建时生成共享 schema 常量，Script 和
Protocol 使用同一生成的原生字段校验器，拒绝 Ack 零序号、非法实体、有符号范围外 Tick 和多余字段。
实体投影必须携带 cfg_id 十进制字符串，范围为 1～2147483647，转换为 Protobuf uint32。
`cfg.get()` 返回 v7 初始化上下文（仅版本与快照频率）的副本；`diagnostics.log()` 缓冲有限诊断。
`cfg.install(content_json,bounds_json)` 仅在 init 内成功一次，规范化内容只用于计算摘要后释放，
原生保存有限结构投影。`check_loadout` 在禁止写状态和输出的能力下调用 Lua，业务拒绝返回
错误码，成功返回规范配装；Runtime 在其成功之后才申请局号。
拥有线程在入口返回后通过 `take_logs()` 移交日志；宿主再通过有界队列交给输出线程。
事件类型 event_id 与 Tick ID 使用 Luax integer，超出 i64 范围时明确拒绝。
输入序号在高频路径使用原生 uint64；JSON 状态往返仍使用十进制字符串。
生产玩法只通过标量 `net.event` 和无参数 `net.snapshot` 请求高频输出；`net.emit` 为低频 JSON 适配器。
七类 ClassBuilder 对象通过显式方法调用；对象权威状态和生命周期由 SRV-010 定义。

异步操作以 instance/op/generation 标识，启动前预留完成槽。跨线程票据只持有 detached 数据与
有界收件槽，不持有 VM 句柄。唤醒合并；完成、取消、超时只有一个终态，过期完成明确拒绝。
需要再次执行脚本的宿主工作通过 Asio 延后调度，同步游戏入口不挂起。

## 失败、取消与退出

初始化失败不发布 Ready；入口错误返回有界诊断。`Script::shutdown` 返回 `expected<void, Str>`，
错误线程与重入明确拒绝；脚本返回 false、抛错或原生关闭失败时，仍依次尽力关闭 Isolate 和
Runtime，保留最多 4096 字节的错误。重复关闭返回相同终态，关闭日志仍可 `take_logs()`。
关闭 VM 前由拥有者停止异步适配器；取消所有 timer 与 continuation。
票据在关闭后拒绝新完成，不能复活旧代次。
生产构建仅加载独立 policy 指定公钥、epoch 和兼容性 identity 的签名 Bundle。
四项 Hunter 契约摘要还必须匹配编译时常量，旧制品和旧 policy 成对输入同样拒绝。

## 验证与证据

`hunter_script_contract` 验证真实双向调用、状态回读、错误线程、预算、只读 namespace、
参数边界与失败不提交。`hunter_async_contract` 验证定时器、真实线程 detached 回传、
完成容量、取消、超时及重复/过期完成。开发构建的两个 CTest 已通过；生产 Bundle 的真实加载
与拒绝用例由同一脚本契约目标验证，最终执行结果写入仓库验证记录。active 仅表示契约入口存在。

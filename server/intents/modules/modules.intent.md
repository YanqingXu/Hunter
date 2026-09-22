---
id: SRV-005
status: active
target: ["hunter_scripts"]
depends_on: ["SRV-004"]
verification: ["hunter_assemble_contract", "hunter_script_contract", "tools:hunter_bundle_contract", "hunter_schema_contract"]
---

# SRV-005：脚本模块和权威状态

## 目标与非目标

构建期将显式依赖的 Luax 工厂组装为一个 ModuleHandle，导出七个生命周期入口。
框架承载 SRV-009 的会话、运动、枪械和普通怪模块，不实现存档或热更新。

## 不变量

- `modules.json` schema 为 `{version:1,entry:"main",modules:[{name,file,deps}]}`。
  模块名为点分标识符；文件相对于清单目录；依赖按名字注入工厂的 `deps` 表。
- 拒绝重名、缺失依赖、环、绝对路径、父目录路径和符号链接逃逸。无依赖节点按模块名排序，
  入口必须依赖全部传递使用的模块，生成源和行号映射不包含工作区绝对路径。
- 功能文件返回接收 `deps` 的工厂；入口也返回工厂。模块表不跨 Host；只导出七个函数。
- C++ World 独占可变玩法状态；脚本模块只持有代际句柄、不可变身份索引及局部计算值。
- Lua 文件和清单路径必须全小写，并检查文件系统实际路径大小写一致。
- 序号为规范 uint64 十进制字符串；
  Tick 为非负有符号 64 位整数的精确字符串。导入先完整验证，再替换状态。

## 线程与所有权

逻辑线程独占 C++ World 和 Isolate，World 管理对象的创建和销毁，Lua GC 不拥有原生对象。
构建工具独立运行并只输出制品；身份视图不复制原生可变状态。

## 接口与值语义

入口为 `init(ctx_json)`、`on_event(event_id,payload_json)`、`tick(tick_id,dt_seconds)`、
`export_state()`、`import_state(snapshot_json)`、`validate_state()`、`shutdown(reason)`。
除导出返回 JSON 字符串外均返回 `true`；失败抛出脚本错误。

初始化上下文为 `{v:4,snapshot_every:3,content:共享配置}`；内容 v2 在会话中只读。
低频事件 2～4 处理登录、开局及暂停；动作直接由 C++ input 接口处理，字段以 SRV-009 为准。
每 `snapshot_every` Tick 输出权威快照，开局和终态立即补充快照。
内部导出状态还包括动作锁存、冷却、AI、请求去重与分配器；导入先完整验证再替换。
内部状态 v4 独立描述实体组件、ID 字典和遍历索引，不引用网络字段作为内部 schema。
校验实体 ID 唯一性、索引完整性、配置/出生引用、玩家引用、分配高水位及阶段关系。
导出状态只保存身份和数据；旧状态版本拒绝，无迁移。C++ 负责网络投影，snapshot.lua 只发出请求。
状态新增至多 64 个 Item 实例及独立 ID 高水位；不包含物品玩法或配置加载。
Hunter 契约、上下文与内部状态为 v4；网络输出及协议保持 v3，内容 v2，清单 v1。
签名 identity 从更新后的契约重新派生，不修改固定 Luax 的兼容版本常量。
生产加载将四项应用摘要与编译时契约核对，旧 Bundle 与旧 policy 也不能成对绕过。
宿主提供原生对象方法、`net.event`、`net.snapshot`，以及低频 `net.emit`、`cfg.get`、日志接口。

## 失败、取消与退出

脚本参数、状态或输出不合法时失败，桥接层丢弃调用级输出并终止会话；不在部分失败后续局。
`shutdown` 清空状态，不承担存档。

源码／映射及 policy／provenance 成套发布前，先在各目标目录准备完整、同步落盘的暂存文件，
保存旧字节并取得合作式排他锁。输入或准备失败不修改已有制品；发布捕获异常后恢复原有整套，
原本不存在的目标则撤销。Bundle 最终路径只排他链接完整、验证通过的暂存文件，已有文件
不会被覆盖，也不向最终路径分段写入。目标目录需要支持硬链接；不支持时明确失败。

上述保证以调用者独占消费阶段和文件系统仍允许回滚为前提，不宣称多路径替换对并发读者、
进程崩溃或掉电原子。回滚失败返回明确错误，保留备份、`recovery.json` 和锁；完成恢复前
不能消费或再次发布。提交已完整成功但临时文件清理失败时报告警告，成功制品仍可使用。
父目录可能在准备过程中创建，不作为制品回滚。
制品名不得使用不区分大小写的 `.hunter-publish.lock` 后缀；Windows 拒绝末尾点或空格的
目标名，避免与合作锁或其他制品成为文件系统别名。Bundle 外层编译暂存目录的清理同样
区分提交前失败与提交后警告，不能把已提交制品误报为构建失败。

生产制品由真实 `luaxc` 和 `luax-bundle` 编译、构建、签名及验证。九项 identity 源自固定
Luax 提交边界文件与 Hunter 版本化契约，生产只接收外部公钥和 policy，测试 RFC 密钥只写入
测试构建目录；不生成或默认使用生产密钥。生产 Runtime 不包含编译器和签名私钥。

## 验证入口

- `hunter_assemble_contract`：稳定拓扑、注入、行号映射、重名、缺依赖、环、路径和链接逃逸。
- `hunter_script_contract`：真实 Luax 双向调用、初始化、状态导出导入和非法参数。
- `hunter_bundle_contract`：真实编译签名、路径无关可复现、错误公钥、篡改和 identity 变化。
- `hunter_schema_contract`：正式 schema 派生字段与边界、不支持的漂移拒绝、生成失败保留旧头文件。
- Windows 运行证据由验证记录单独维护；Android ARM64 未运行时不标记 P0 完成。

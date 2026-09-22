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
- 所有可变玩法状态保存在入口持有的单一纯数据表，功能模块不保存第二份状态。
- 序号为规范 uint64 十进制字符串；
  Tick 为非负有符号 64 位整数的精确字符串。导入先完整验证，再替换状态。

## 线程与所有权

脚本数据只由逻辑线程中的 Isolate 创建、修改和释放。构建工具独立运行，拥有输入字节，
仅输出构建制品；C++ 搬运 JSON 字符串，不建立权威状态副本。

## 接口与值语义

入口为 `init(ctx_json)`、`on_event(event_id,payload_json)`、`tick(tick_id,dt_seconds)`、
`export_state()`、`import_state(snapshot_json)`、`validate_state()`、`shutdown(reason)`。
除导出返回 JSON 字符串外均返回 `true`；失败抛出脚本错误。

初始化上下文为 `{v:3,snapshot_every:3,content:共享配置}`；内容 v2 在会话中只读。
事件 1～4 分别处理动作、登录、开局和宿主暂停；字段以 SRV-009 和脚本契约为准。
每 `snapshot_every` Tick 输出权威快照，开局和终态立即补充快照。
内部导出状态还包括动作锁存、冷却、AI、请求去重与分配器；导入先完整验证再替换。
内部状态 v3 独立描述实体组件、ID 字典和遍历索引，不引用网络字段作为内部 schema。
校验实体 ID 唯一性、索引完整性、配置/出生引用、玩家引用、分配高水位及阶段关系。
实体引用不保存对象别名；旧 v2 快照明确拒绝，无状态迁移。网络投影由 snapshot 模块完成。
Hunter 契约、上下文、输入、输出和状态版本分别检查；本轮均升级为 3，清单仍为 1。
签名 identity 从更新后的契约重新派生，不修改固定 Luax 的兼容版本常量。
宿主提供只读 `net.emit(kind,payload_json)`、`cfg.get()`、`diagnostics.log(message)`。

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

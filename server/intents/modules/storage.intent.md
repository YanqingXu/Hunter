---
id: SRV-007
status: active
target: ["hunter_storage", "hunter_sqlite"]
depends_on: []
verification: ["hunter_storage_contract", "hunter_storage_backend_contract", "hunter_storage_crash_integration"]
---

# 持久化结算

## 目标与非目标

本模块提供永久玩家、物品、持久对局 ID、猎人资产及原子幂等结算，已由 Runtime 接入。
比赛使用 SQLite；本轮拆分异步门面与同步业务后端，不改变玩法、协议或存档内容。
MySQL 实现留到赛后，由独立服务端访问新库，不导入比赛存档；账号及远程部署独立安排。

## 不变量

Accepted 不等于 Committed；同 ID 同内容返回已保存结果，不同内容返回冲突。
未完成局不发奖、不隐式恢复；调用方提供已裁决的奖励，存储层不决定玩法。
结果、物品和玩家 revision 在同一事务提交。永久 item_uid 与运行时实体 ID 独立。
SQLite 当前 user_version=2，与 World 状态版本无关；空库初始化，合法 V1 事务升级 V2。
迁移保留物品 UID、局号高水位与历史结果原文，已确认结果不重新计算。

## 后端边界

Storage 只持有 Backend 接口，覆盖 open/close、玩家读取、局号分配、结算提交与查询、
猎人读取、操作提交与操作查询。Backend 同步返回现有拥有型值，以 Error 报告失败。
BackendFactory 在存档线程创建后端；打开、业务调用、关闭及销毁也只发生在该线程。
默认工厂仅支持 sqlite，未实现或未知后端返回 Unsupported，不自动回退或创建 SQLite 文件。
SqliteBackend 独占 Db/Schema/Store/HunterStore；初始化、迁移及全库未完成出战回退仅属于
单实例独占文件的 SQLite 打开策略，不是公共 open 契约。MySQL 需另定实例归属与失效恢复。
请求校验和确定性编码共享，公共门面不包含 SQLite 头文件，不构造 SQL。
异步受理、完成预算、Committed、幂等原文、错误分类及关闭屏障均保持现有语义。

## 线程与所有权

单一 std::jthread 独占连接、语句和事务，任务和完成包拥有数据；无 VM 依赖。
Storage 由一个逻辑线程创建并调用，Asio io_context 必须由同一线程运行并活过关闭回调。
完成总是延后交付；每个对象独立持有收件箱及单调操作 ID，不复用旧实例回调。
默认最多 64 个未完成操作，请求总量 1 MiB、完成总量 4 MiB、单个结果 1 MiB。
接受前同时预留任务及最坏情况完成容量；计费覆盖固定对象和拥有的字符串、数组数据。
关闭使用独立控制槽，不受普通任务饱和影响。回调不得抛异常。

## 接口与值语义

Storage 提供 open/load_player/alloc_match/commit_match/find_match/stop，以及
load_hunters/apply_hunter/find_hunter_op。open 接受 OpenCfg{backend,path}，backend 默认 sqlite；
原 open(path,done) 保留为 SQLite 兼容入口。StorageCfg 只描述容量，不承担连接配置。
宿主 --storage sqlite 选择后端，--save 保留文件路径语义；仅 SQLite 准备默认路径和目录。
Error.native_code 保存后端原始码，backend 标明来源；通用校验错误可无后端来源。
业务调用方只依赖 Code 与 commit_unknown，不按驱动错误码分支。
返回 Accepted 只表示拥有任务；Rsp 携带 instance/op 和结果或结构化错误。
成功的 MatchResult 明确表示 Committed；result_json 保存完整结果，不另设可写成功标志。
打开完成前拒绝业务请求；打开失败后须关闭该对象，新对象可重新尝试。
PlayerSave 包含 player_id/revision/last_match_id/items；首次玩家为 1，revision 为 1。
CommitMatch 包含 match_id/player_id/expected_revision/outcome/content_key/items。
物品增量只新增正数数量，允许空数组；数组顺序属于请求内容，不合并同 cfg_id 的不同条目。
请求 JSON 由完整 typed 值生成固定格式 v1，整数直接编码，不经浮点转换。
同 match_id 同请求先于 revision 检查返回保存的原始 result_json，replayed 表示重放。
相同 ID 不同请求返回 Conflict；新提交要求 ID 已分配且 revision 匹配。
ID 与 revision 正数且不超过 i64 上限；last_match_id 可为 0，溢出拒绝。
已分配对局 ID 允许空洞，不复用；物品 UID 由 SQLite AUTOINCREMENT 分配。
outcome/content_key 为非空 UTF-8 字符串，语义由调用方定义，不接受嵌入 NUL。
WAL、FULL、foreign_keys 和 busy_timeout=2000 必须设置并检查生效。

## 失败、取消与退出

损坏、未知版本、非空无版本库保留原文件并报告，不重建、不降级。
查询只返回已提交记录；不存在返回 NotFound，结果超限返回 TooLarge，不截断。
队列饱和、非法参数、错误线程、锁超时、写失败和 revision 冲突可区分。
序列化及结果大小检查在 COMMIT 前完成；不能确认 COMMIT 时标记提交状态未知，允许查询重试。
stop 立即拒绝新请求；已接受任务不取消，先交付完成，再由工作线程关闭数据库并通知 Closed。
正常调用方必须驱动 io 至 Closed；析构仅保底等待工作退出，不保存额外状态，已排队完成仍拥有数据。
回调延后且不捕获 Storage 裸指针，销毁旧对象不会访问新对象；调用方回调捕获也须满足此生命周期。

## 验证与依赖

测试不依赖 Luax，使用真实 SQLite、线程和临时文件。覆盖初始化、拒绝异常库、事务及异步容量。
独立进程在事务前、中及提交后通知前强杀；重启验证全部提交或全部不存在，重试不重复发奖。
测试专用编译目标提供故障检查点，正式 hunter_storage 不含注入入口。
Windows 开发和签名 Bundle 两套运行；Android 真机继续独立验收。
共用业务契约走 Storage，SQLite 文件、PRAGMA、锁和迁移检查保留专属验证。
无数据库测试后端验证工厂、调用、销毁的线程归属，以及完成延后和关闭排空。
MySQL 后续须通过相同业务契约，并补充并发 revision、ID、幂等竞争、断连及提交未知测试；
需独立 DDL、即时外键写入顺序、原文字节比较与安全恢复，不以解耦完成代替 MySQL 可用。

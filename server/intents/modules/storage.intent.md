---
id: SRV-007
status: active
target: ["hunter_storage", "hunter_sqlite"]
depends_on: []
verification: ["hunter_storage_contract", "hunter_storage_crash_integration"]
---

# 持久化结算

## 目标与非目标

本轮实现独立 SQLite 底座：永久玩家、物品、持久对局 ID、结果查询及原子幂等结算。
不接入 Runtime、World、Lua、协议或 Android 宿主，不改变现有战斗行为。

## 不变量

Accepted 不等于 Committed；同 ID 同内容返回已保存结果，不同内容返回冲突。
未完成局不发奖、不隐式恢复；调用方提供已裁决的奖励，存储层不决定玩法。
结果、物品和玩家 revision 在同一事务提交。永久 item_uid 与运行时实体 ID 独立。
数据库 user_version=1，与 World 状态版本无关；只允许空库从 0 初始化。

## 线程与所有权

单一 std::jthread 独占连接、语句和事务，任务和完成包拥有数据；无 VM 依赖。
Storage 由一个逻辑线程创建并调用，Asio io_context 必须由同一线程运行并活过关闭回调。
完成总是延后交付；每个对象独立持有收件箱及单调操作 ID，不复用旧实例回调。
默认最多 64 个未完成操作，请求总量 1 MiB、完成总量 4 MiB、单个结果 1 MiB。
接受前同时预留任务及最坏情况完成容量；计费覆盖固定对象和拥有的字符串、数组数据。
关闭使用独立控制槽，不受普通任务饱和影响。回调不得抛异常。

## 接口与值语义

Storage 提供 open/load_player/alloc_match/commit_match/find_match/stop，路径显式传入。
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
Windows 开发和签名 Bundle 两套运行；Android 真机及玩法接入继续延期。

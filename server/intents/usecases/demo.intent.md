---
id: SRV-011
status: active
target: ["hunter_server_core", "hunter_script", "hunter_storage", "hunter_client", "hunter_content"]
depends_on: ["SRV-002", "SRV-003", "SRV-004", "SRV-005", "SRV-007", "SRV-010"]
verification: ["hunter_session_contract", "hunter_raid_contract", "hunter_pve_contract", "hunter_demo_content_contract", "hunter_demo_integration", "hunter_demo_edges_integration", "hunter_runtime_crash_integration"]
---

# 单人撤离与永久结果闭环

## 目标与非目标

Windows 共用核心完成 Excel 地图 2、固定装备、Boss、背包、掉落、撤离和 SQLite 结算。
一个会话、一个活动世界、一名玩家；不做多人、永久装备带入、额外主动技能或热更新。
Unity 与 Android 保持独立验收，不由本契约声明完成。

## 不变量

C++ World 独占可变玩法状态，Luax 决定规则。身份来自服务端会话绑定。
Boss 死亡只解锁撤离；死亡事件仅生成一次掉落。拾取整份原子成功或拒绝。
背包默认 8 格，拾取半径 1500 毫米，同配置按 MaxStack 堆叠。
正伤害、离区、死亡清空撤离进度；战斗及死亡先于同 Tick 撤离完成。
玩家状态与世界阶段分离；暂停冻结游戏时间，不冻结存档完成交付。

## 线程与所有权

逻辑线程独占世界、VM、会话和存档门面；工作线程独占 SQLite。
所有异步数据拥有自身存储，回调校验实例及对局；旧完成不得操作新世界。
脚本成功返回后才提交外部副作用，错误则中止本局。

## 接口与值语义

SessionId/PlayerId/WorldId/MatchId 与实体、物品身份分离。
启动先 open/load_player，成功才 Ready；开局先 alloc_match，成功才创建实体。
结算冻结 match_id/player_id/expected_revision/outcome/content_key/items。
撤离奖励为背包；死亡、主动放弃提交空奖励。只有 Committed 表示成功。
原 SQLite V1 不迁移，仍是一局一名玩家。仓库查询按 revision 分页，每页最多 128 条。
协议 v4、内容 v3、Host/内部状态 v5；旧内部状态拒绝，持久存档保持兼容。

## 失败、取消与退出

未确认结算不允许重开；未知提交先查询，未找到时重试完全相同请求。
断连或强杀不恢复战斗，已提交结果从数据库读取；不额外产生未冻结的奖励。
正常关闭停止接收命令，排空 Storage 已接受任务和回调，再释放所有者。
损坏与未知版本存档保留原文件，不静默清档。

## 验证与依赖

配置、身份、Boss、背包、掉落、撤离、状态往返和真实进程故障均需行为测试。
开发源码与签名 Bundle 两种模式验收，连续 10 局和 10 次启停；实际证据另记。

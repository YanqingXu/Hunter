---
id: SRV-009
status: active
target: ["hunter_server_core", "hunter_scripts", "hunter_content", "hunter_client"]
depends_on: ["SRV-001", "SRV-002", "SRV-003", "SRV-004", "SRV-005"]
verification: ["hunter_game_contract", "hunter_entity_contract", "hunter_content_contract", "hunter_process_integration"]
---

# 本机基础战斗切片

## 目标与非目标

在 Windows 宿主验证鉴权、会话登录、开局、跑跳碰撞、射线枪械、普通怪、死亡与清怪重开。
共用核心继续供 Android 使用，本契约不宣称 Unity、Android、撤离或存档已经完成。

## 不变量

Luax 独占世界、玩家、局次及战斗状态；C++ 只维护传输、队列和协议缓存。
输入不能携带权威位置、伤害或命中目标。游戏固定 60 Hz，快照 20 Hz。
地图与数值唯一来源为 design，经 export 校验生成双方可使用的数据及内容摘要。
脚本输出整批校验成功才提交，普通业务拒绝不导致脚本故障。

## 线程与所有权

沿用单逻辑线程与有界队列。登录、开局和输入在调度边界调用同步脚本入口。
没有活动对局时仍调度命令；只有 Playing 推进局内计时。暂停停止定时调度。

## 接口与值语义

协议 v3 保留 Hello 令牌鉴权、LoginReq/Rsp、StartReq/Rsp、玩法输入、快照和事件。
阶段为 Unauthenticated、Lobby、Playing、Dead、Cleared。重复登录返回同一玩家。
开局携带 req_id 与 after_match_id；重复成功请求返回同一局，不重置状态。
输入序号连接内单调递增，重开不归零；旧局或重复输入不得再次执行。
JSON 的版本为 3，ID、序号和 Tick 用规范十进制字符串；整数毫米、脚底中心、X右Y上。
脚本事件编号：1 输入，2 登录，3 开局，4 暂停状态。输入带 applied_tick。
上下文为 v、snapshot_every、content；content 为导出的完整只读配置对象。

## 基础对象与配置契约

Entity/Unit/Player/Monster 是纯数据构造模块，Weapon/Damage 分别负责枪械与伤害。
实体公共字段为 id/kind/cfg_id/pose/pending_remove，单位增加 motion/health。
玩家独占 player_id/controls/weapon/reserve，怪物独占 spawn_id/ai；怪物不保存枪械字段。
combat 只保留几何计算，snapshot 负责内部组件到网络字段的投影。
构造模块不依赖 world/weapon/damage；world 持有实例，weapon/ai 依赖 damage。

world.entities 按 ID 保存唯一实例，entity_ids 保存稳定遍历顺序，player_entity_id 定位玩家。
last_entity_id 是局内分配高水位，ID 不复用；跨阶段引用为 match_id/entity_id。
spawn 在配置、容量和 ID 校验后才加入世界，最多 64 个实体；失败不消耗 ID。
remove 只标记怪物，重复或未知目标返回 false；find/resolve 不返回待移除实体。
flush 在怪物攻击后、终态裁定前清理标记，保留其他实体顺序。死亡不自动删除。
模拟顺序固定为玩家移动、怪物移动、枪械、存活怪物攻击、flush、终态判断。

内容 v2 使用 players/monsters/weapons 配置表，配置 ID 在各表内唯一。
玩家出生引用 cfg_id/weapon_cfg_id；怪物出生记录使用独立 spawn_id/cfg_id。
ID 均为规范正十进制字符串，配置和出生 ID 最大 2147483647；不与运行时实体 ID 关联。
地图保存公共 gravity，玩家配置保存 jump_speed。默认地图及数值不变，多配置由测试验收。
Entity.cfg_id 使用新增 Protobuf 字段 16（uint32），网络怪物 kind 继续为 enemy。
旧 v2 协议、内部状态及 v1 内容不迁移；输入、暂停、事件和重开语义保持不变。
本轮不实现物品、装备、技能、撤离、存档或开发热更新。

## 失败、取消与退出

暂停清除排队命令与持续/边沿动作，返回被丢弃序号高水位；暂停中新命令明确拒绝。
恢复不回放旧输入、不追赶暂停时间。断连不恢复局，沿用 Stop/EOF 收尾流程。
Dead/Cleared 冻结战斗并允许新局；地图、实体、弹药、冷却和动作全部重置。
坏帧、认证失败、容量超限和脚本契约故障中止会话。

## 验证与依赖

新增真实 Luax 玩法契约和共享配置测试，扩展网络、脚本事务及真实 TCP 进程测试。
开发构建通过后生成签名测试 Bundle，再顺序构建验证生产 Runtime。
验收覆盖移动碰撞、枪械遮挡/射速/换弹、AI、死亡、清怪、重开和暂停输入作废。

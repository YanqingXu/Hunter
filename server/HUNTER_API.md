# 永久猎人客户端契约

适用版本：协议 v6、内容 v5、Host／状态 v8、SQLite V2。消息唯一来源为
[`protobuf/hunter.proto`](../protobuf/hunter.proto)。本页说明已接入的单账号单人 PvE 路径，
不定义账号登录服务或多人资产交易。

## 身份与档案

完成 Hello 与 Login 后，以 `HunterReq.kind=PROFILE` 查询当前档案。`player_id` 为账号，
`hunter_id` 为招募实例，`cfg_id` 为配置模板，`entity_id` 为本局实体，不能互换。
客户端必须无损保存 uint64 身份和 revision；CLI 使用十进制字符串。`result_json` 内的
领域整数是 JSON 整数，采用 JavaScript 的客户端须使用无损整数解析，不能先转浮点数。

`HunterRsp` 字段：

| 字段 | 含义 |
| --- | --- |
| `req_id` | 本次网络请求关联 ID |
| `op_id` | 本次修改的永久幂等 ID；PROFILE 为空 |
| `revision` | 该响应所对应的账号版本 |
| `hunter_id` | 本次操作对应实例；招募成功返回新实例 ID |
| `match_id` | 若操作涉及对局，返回持久局号；普通交易为 0 |
| `replayed` | 是否直接返回先前已提交结果 |
| `result_json` | PROFILE 返回档案；修改返回 `{v:2,player_id,revision,hunter_id,match_id,kind,profile}` |

档案包含 `player_id/revision/account_xp/currency/hunters/stash/raids`。
`hunters` 只列 `ready` 或 `in_raid` 实例，含 `cfg_id/level/xp/points/state`；
每个技能保存 `cfg_id/paid_cost/source`，来源为 `recruit/purchased/loot`。
`equipment` 保存 `slot/item_uid/cfg_id/count/acquired_match_id`。
`stash` 仅列未装备资产，装备到其他猎人的物品不可再次选用。
`raids` 列尚未关闭的 `match_id/hunter_id/content_key`。

## 猎人操作

修改请求都必须带非空 `req_id/op_id` 和当前 `revision`；`op_id` 最多 96 字节，
不允许空字节。PROFILE 只需 `req_id`。价格、退款、经验、奖励和内部经济 JSON
均不属于客户端请求，服务端读取已验证的配置与真实档案计算。

| `HunterReq.kind` | 需要的业务字段 | 行为 |
| --- | --- | --- |
| PROFILE = 0 | 无 | 查询当前档案；暂停期间也可查询 |
| RECRUIT = 1 | `cfg_id`，`hunter_id=0` | 扣招募价格，创建等级 1、经验 0 的独立实例，初始技能点取模板 |
| EQUIP = 2 | `hunter_id/slot/item_uid` | 替换指定槽；`item_uid=0` 卸下并归仓；同实例内移动会解除原槽 |
| BUY_SKILL = 3 | `hunter_id/skill_id` | 按配置扣技能点，记录本次实际支付值；已持有技能拒绝重复购买 |
| REMOVE_SKILL = 4 | `hunter_id/skill_id` | 仅退实际支付点数的一半向下取整；支付 1 退 1，免费来源退 0 |
| RETIRE = 5 | `hunter_id` | 仅满级可退役；账号经验、装备归仓、人物退出名单同事务提交 |

装备槽固定为 1–2 枪械、3–6 工具、7–10 消耗品。类型匹配由配置验证。
同一 `item_uid` 只能装备给一名猎人。出战、准备出战或正在结算期间不允许修改永久配装、
买卖技能、招募或退役。服务端在一项请求未完成时可返回 `hunter_busy`。

账号版本在每次成功修改、登记出战、原子终局和异常回退时递增。失败不递增。
服务端按账号和 `op_id` 永久去重，原始意图指纹覆盖
`kind/revision/hunter_id/cfg_id/skill_id/slot/item_uid`，不包含 `req_id`。
同一 ID、同一指纹重发返回原始响应内容，即使人物已退役或版本已前进，也不会再次扣款。
同一 ID 改任何指纹字段返回 `hunter_operation_conflict`。真正的新操作使用新的 ID 和最新版本。
重放响应的版本可能旧于客户端当前档案，客户端不能用它覆盖较新的档案。

## 出战与工具余量

永久模式的 `StartReq` 使用非零 `hunter_id`、当前 `revision`、唯一 `req_id`，
`test_mode=false`，且不能携带 `loadout`。`after_match_id` 必须等于本会话最近对局 ID，
首次为 0。服务端先读档案、执行 Lua 校验，再原子占用资产和登记出战基线，最后创建世界。
空枪槽、负重或配置不合法均拒绝，不能补入免费默认装备。

`StartRsp` 返回 `hunter_id/test_mode/loadout` 和权威 `world_id/match_id/player_entity_id`。
第一次快照已经使用真实工具数量：局内可用量为 `min(资产 count, 配置 uses)`。
成功撤离后的持久余量为出战前数量减去本局实际使用数，超出本局可用上限的堆叠不会丢失。

同一活动对局重复 Start 且身份参数不变可返回原开局，不创建新局。
准备期间请求可返回忙碌；跨重启重发已关闭的永久 Start 返回
`hunter_start_already_closed`，客户端应查询档案，以新 `req_id/revision` 开始下一局。
持久局号只增不复用，包括已回退的局。

免费测试模式使用 `hunter_id=0/revision=0/test_mode=true`，可携带旧 `Loadout`。
线协议没有隐式测试模式；CLI 对未给 `hunter_id` 的旧 `start` 命令补 `test_mode=true`。
这一路径保留现有演示和 V1 战利品结算兼容性，免费初始枪械、工具不会凭出战转换为仓库资产。

## 原子终局与进程退出

撤离时技能、剩余装备、战利品、升级、账号奖励、revision 与对局结果在同一 SQLite 事务提交。
局内拾取技能只有成功撤离后才持久；已消费的一次性技能从持久技能中移除。
死亡与主动放弃会销毁人物携带装备与技能，移出可用猎人名单；仓库和其他人物保持原状。
等待手动复活的倒地状态还不是永久死亡，继续使用既有 ActionReq 的 `REVIVE/death_seq`。

`SaveRsp.state=Saving` 只是受理，只有 `Committed` 表示提交完成。
`Failed/Unknown` 应保留相同对局身份，使用既有 SaveReq 查询／重试，不能创建替代结算。
RESULT 可查询 V1 和 V2 历史结果；V2 除旧 `items` 字段外还包含猎人成长、技能和装备结算。

进程重启时先保留已提交的终局记录，再回退仍活动的出战。回退恢复人物出战前状态，
包括本局已消费的一次性技能，清除出战占用并增加 revision；不会恢复整个账号快照。
已提交但客户端尚未收到的撤离、死亡或放弃不会被回退。
断线不视作主动放弃，也不承诺恢复正在运行的世界；客户端以宿主退出和重新启动后的档案为准。
准备出战期间 Pause 若作废开局，服务端先解除已登记占用；异常中断时由重启恢复处理。

## 配置门禁与错误

生产配置仍以正式 19 表为输入，不把草稿推测值写入生产表。
当前未填写正式成长、奖励和退役曲线时，导出 `career=false`：永久出战和退役返回
`career_not_configured`，防止开局成功后无法结算。明确 `Cost='0'` 的普通模板可以免费招募；
缺失价额不按 0 处理。新账号货币初始为 0，当前接口不提供充值或测试赠送资产命令。
全链路验证使用隔离测试配置和隔离 SQLite 中的真实物品 UID。

| 常见错误码 | 客户端处理 |
| --- | --- |
| `not_logged_in` | 先完成 Login |
| `paused` / `hunter_busy` / `invalid_state` | 等待当前阶段结束或查询档案；不要假定既有在途操作已撤销 |
| `player_revision_conflict` | 重新 PROFILE，用户的新意图使用新 `op_id` 与新版本 |
| `hunter_operation_conflict` / `request_conflict` | 同一操作 ID 被用于不同参数，不能盲目重试 |
| `career_not_configured` | 所需生产成长／奖励配置未补齐 |
| `invalid_hunter_start` / `invalid_test_start` | 两种出战模式的参数混用或缺失 |
| `hunter_start_already_closed` | 原出战已经提交或回退，查询后发起新局 |
| `hunter_not_found` / `hunter_not_ready` / `hunter_busy` | 实例不存在、已失效或正被占用 |
| `item_not_owned` / `item_already_equipped` / `invalid_equipment_slot` | 重新选择实际持有且槽位类型匹配的资产 |
| `insufficient_currency` / `insufficient_skill_points` | 资产或未消费点数不足 |
| `duplicate_skill` / `skill_not_owned` | 持有状态与请求不匹配 |
| `hunter_not_max_level` | 未达到配置最高等级 |
| `hunter_result_limit` | 持久事务内已检测到响应预算不足，整项修改回滚，原档案仍可查询 |
| `hunter_profile_too_large` | 既有档案或历史重放超出当前网络预算；不能把无响应视为操作未提交，先查询同操作与保存状态 |

验证入口：`hunter_hunter_store_contract` 覆盖事务迁移、真实强杀、资产原子性和幂等；
`hunter_hunter_integration` 使用真实 TCP、离线 fixture 和 SQLite 覆盖交易、出战、撤离升级、
退役、异常回退、主动放弃与缺配置拒绝。以当前实际 CTest 结果判断验证状态。

---
id: SRV-013
status: active
target: ["hunter_server_core", "hunter_scripts", "hunter_content", "hunter_client"]
depends_on: ["SRV-010", "SRV-011", "SRV-012"]
verification: ["hunter_gameplay_content_contract", "hunter_gameplay_contract", "hunter_gameplay_edges_contract", "hunter_lua_cfg_integration", "hunter_package_contract"]
---

# Lua 配置与玩法规则

## 目标与来源

读取 design 根目录全部配置工作簿、全部服务端工作表和记录，各表导出独立 Lua table，保留
数字主键和英文字段。编辑器临时文件、子目录及无导出工作表不属于生产来源，不再读取
demo_sources.json。Python 只负责读取、类型与确定性序列化；配置转换、关联及玩法语义由 Lua
执行。game.cfg 在构建和启动使用同一实现。客户端 JSON 和摘要是该实现的派生产物。
旧配置只作显式测试夹具，生产不回退旧表或将缺值解释为零。禁用记录仍导出和验证，但不进入
运行候选；合法禁用参数不改变运行内容摘要。

Server 唯一一行 ServerIdx=1，以 MapIdx、BagIdx、RulesIdx、LoadoutIdx 选择正外键，
CareerIdx、EncounterIdx 的零值明确关闭对应机制。地图子记录按 MapIdx 过滤，不固定 Map2、
规则主键或场景种类，不要求恰好一个出口；身份唯一、几何、出生支撑和玩法引用仍严格检查。
Tool、Monster 的 Enabled 必填且仅接受 0/1，未启用机制不会通过读取全部根表自动发布。
PlayerSkill 只验证角色、技能外键及角色内槽位唯一性，默认技能由 Loadout.Skills 明确提供。

## 不变量、线程与所有权

Lua 可以缓存只读配置；全部可变对局状态仍由逻辑线程上的 C++ World 独占。
Lua 决定初始化参数、配装、拾取叠堆、掉落拆分、工具扣次清槽和配置语义校验。
C++ 保留网络、存储、身份句柄、容量、整数边界和原子提交，不持有完整配置文档，
不按配置种类重复裁定玩法。原生只保存快照、实例和结构验证需要的只读投影。
禁止同步脚本重入，Tick 高频路径保持句柄和标量访问。

## 接口与失败行为

当前规范内容升级 v6，Host／上下文／内部状态版本由最新协议契约统一规定，SQLite V2 保持。
启动上下文不再携带 content，Lua 加载配置后一次性注册内容身份和原生结构边界。
Runtime 先只读调用 Lua 验证并规范化配装，成功后再分配持久局号；参数冲突与重试语义不变。
拾取、补给及消耗先算完整变更，原生核对句柄、实例、数量和容量后一次提交，业务拒绝不修改状态。
Lua 先验证候选快照的配置语义，C++ 再验证独立候选的结构，全部成功后才替换活动世界。
错误候选不修改原状态或句柄代际。旧上下文、旧内部快照明确拒绝。
构建失败不发布半份制品；配置随源码组装或签名 Bundle 加载，不开启任意文件访问和热更新。

Skill.Enabled 与 Attribute 属性字典、SkillEffect.AttributeId 共同定义技能来源。
全部行先验证结构、外键、属性对象、单位、运算和参数，只有 Enabled=1 的技能进一步检查
已实现处理器并进入 stat/op/value 运行投影。技能 3、6、14、15 保持禁用；禁用不能掩盖非法
参数或外键，物品与默认配装不能引用禁用技能。奔跑、跳跃不扣体力，仅近战扣耗。

弹药补给数量来自 Ammunition.RefillCount，Prop 是可穿透 Scene ID 集合，空串或零表示无引用，
PropDamage 定义对场景道具的伤害，Weaken 定义每次穿透掩体后的子弹伤害扣减；源引用只允许
cover，实际穿透同时受场景 Penetrable 开关控制。
全局弹药只投影当前地图的场景引用。常规工具补量来自 Tool.RefillCount，消耗品补给权重来自
SupplyWeight；ammo_supply 是独立可交互场景类型。枪内加备弹总容量不能超过 100000。

ItemSpawn 按地图选择随机物品点，SpawnPoolEntry 定义正权重候选和数量。全部池引用和数量
先验证，技能必须启用且数量为 1，只允许消耗品或撤离战利品作为其他地面来源。随机点不得
位于实心物内部，每点至少一个候选，总权重不超过 int32。随机点与两波怪物的最坏情况地面
实例总量不超过原生固定容量 64；普通工具、枪械和货币不因存在 Item 行而自动开放随机拾取。

## 验证与交付

覆盖全部根表的确定性导出、中文转义、服务器外键选择、缺值引用单位错误、发布失败、禁用
记录校验、未实现处理器门禁、灵活场景布局、随机池与几何边界及显式旧夹具升级。
修改合法表值后只重新生成脚本，验证同一服务端可执行文件使用新玩法数值。
两种 Runtime 验证配装、拾取原子性、工具耗尽、补给幂等、掉落、非法快照和旧版本拒绝。
执行连续十局、十次启停、旧档案和解包联调；确认 Bundle 服务端不链接编译器。
完成实际验证后再更新状态；Windows 证据不能代替 Unity 或 Android 验收。

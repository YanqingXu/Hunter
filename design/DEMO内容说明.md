# 当前 DEMO 生产内容

`demo_sources.json` 逐项指定根工作簿、工作表和主键。`export/gameplay.py` 读取这些记录，
按表生成保留原英文字段和数字主键的 Lua 文件。`game.cfg` 在 Lua 中转换字段、解析关联并
校验玩法语义；固定 Luax 验证成功后发布配置、派生的客户端内容 v4 JSON 和摘要。
服务端由 Lua 加载这些表并执行玩法，C++ 保留权威状态和通用边界，不读取完整 JSON 配置。
未选草稿不参与运行数据或内容身份，生成物不得手工修改。正式地图来源只有 `地图表.xlsx`；
`关卡表.xlsx` 是历史阅读副本。

## 本版选择与身份

| 内容 | 正式来源与主键 |
| --- | --- |
| 角色 | 角色表 Player 1、2 |
| 武器和普通弹药 | 武器表 Weapon 1～3、Ammunition 1～3 |
| 物品 | 道具表 Item 10001～10003、20001～20003、30001/30002/30004/30006、2800001/2800002 |
| 枪械关联与工具 | Equip 10001～10003；Tool 30001/30002/30004/30006 |
| 怪物与招式 | 怪物表 Monster 1002/1003、独立 Attack 1002/1003 |
| 掉落 | 掉落表 Drop 1/2、DropEntry 1/2/3 |
| 地图闭包 | Map 2、MapSolid 201～203、PlayerSpawn 2、MonsterSpawn 12/13/14、ExtractPoint 1 |
| 全局规则 | 地图表 Bag 1、Rules 1、Loadout 1、Scene 1～3 |

所有 ID 分属各配置表的独立命名空间，不按名称或数字位猜测关联。Ammunition.ItemIdx
显式关联弹药物品，Equip.ItemIdx 和 Tool.ItemIdx 直接关联物品总表。Item.Kind 中
2 为枪械、3 为弹药、4 为工具、5 为本版撤离战利品。永久战利品 ID 2800001/2800002 保持不变。

怪物 1002/1003 保留旧首版普通怪与 Boss 数值；根表草稿 1～7 保持原样。地图出生
12/13/14 分别引用 1002/1002/1003。Boss 使用 Rank=3，出口继续绑定出生 14。
Attack 单独维护攻击距离、伤害、冷却和前后摇。Skill、PlayerSkill、MonsterSkill
中的天赋与旧草稿绑定不进入本版生产闭包。

## 已确认的演示默认值

- 两角色均为 150 HP，站立 600×1600 mm，走/跑/跳分别为 100/150/240 mm/Tick，负重 3。
  匍匐 1000×600 mm、速度 50 mm/Tick。血段顺序为 `50|50|25|25`，仅允许 25/50 的组合且总和等于最大生命。
- 体力 100，恢复延迟 120 Tick，每秒恢复 20。奔跑和跳跃的体耗固定 0；近战耗 20，间隔 30 Tick。
  生命无伤 300 Tick 后每秒恢复 5，分段恢复规则由玩法执行器保证。
- 三枪保留源表弹匣、备弹、间隔、近战和换弹类型。射程为 18000/6000/12000 mm，弹丸 1/5/1，
  总扩散角 0/10/0 度，穿怪数 1/0/1，每次穿透扣除固定伤害点 50/10/30。只启用各枪默认弹药，不附加状态。
- 匕首 100 伤害、600 mm 范围，Quantity=1 表示可用工具，使用不减少次数。
  急救包 3 次、恢复 50、延迟 120 Tick；生命针剂 1 次、恢复 150、延迟 90 Tick；
  炸药束 1 次、125 伤害、1200 mm 半径、延迟 180 Tick，投掷 1600 mm、速度 100 mm/Tick。
- 武器槽 2、常规工具槽 4、消耗品槽 4；使用读条期间保留 70% 移速。场景实体上限 32，投掷物上限 16。
  默认角色 1，武器 1/3，弹药 1/3，常规工具 30001/30002，消耗品 30004/30006。
- Scene 1 梯子位于 (6100,0)，600×1500 mm，上端接旧平台；Scene 2 补给箱位于 (3400,0)，
  600×800 mm；Scene 3 可穿掩体位于 (15600,0)，300×1000 mm。掩体只在 Scene 中声明，
  不重复写入 MapSolid；导出验证其不与旧实体出生及怪物巡逻相交。

## 生成和回归

```powershell
python export/gameplay.py --source design/demo_sources.json --check
python export/gameplay.py --source design/demo_sources.json `
    --output server/build/generated/content.json --header server/build/generated/ContentId.h `
    --cfg server/build/generated/cfg
python server/tests/scripts/test_gameplay_content.py -v
python server/tests/scripts/test_demo.py -v
```

先按 [构建说明](../server/README.md) 构建固定 Luax 工具；也可通过 `--luax` 指定该构建的
`luax.exe`。`--check` 不写正式输出；正式生成使用 `--output`、`--header` 和 `--cfg`。
每表输出独立文件，例如 `Player.lua`、`Weapon.lua`，另有显式模块清单和聚合入口。源码模式
将表与玩法一起组装，发布模式编入同一签名 Bundle。策划双击入口见 [导表说明](../export/README.md)，
草稿输出单独位于 `server/build/draft-cfg`。

派生 JSON 中所有配置 ID 为规范十进制字符串，坐标为整数毫米，速度为毫米/Tick，60 Tick 为一秒。
`ContentId.h` 只保存客户端需要的内容身份，不包含完整配置。内容键为
`gameplay-v4:` 加规范 JSON 字节的 SHA-256。未选源表或记录改变不影响生产内容。

`design/demo/首版.xlsx` 保持冻结，仅供旧回归；`design/combat_demo.json` 的显式测试升级
使用 `--fixture` 生成隔离的 Lua 配置夹具，由 Lua 保留旧地图、生命、伤害和弹量并补齐 v4 字段。
生产读取不支持旧格式回退。

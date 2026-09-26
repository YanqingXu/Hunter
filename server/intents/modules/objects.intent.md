---
id: SRV-010
status: active
target: ["hunter_script"]
depends_on: ["SRV-004", "SRV-005", "SRV-009"]
verification: ["hunter_object_contract", "hunter_game_contract", "hunter_entity_contract", "hunter_endurance_contract"]
---

# 原生对象与脚本行为


当前边界由 [SRV-013](cfg.intent.md) 增补：C++ 不保存完整配置文档，Lua 计算初始化参数、
物品和配装规则；原生保留权威状态、有限结构投影及原子提交。Host 和内部状态为 v7，
具体接口与新增 body／撤离展示字段以 `lua/contract.json` 为准。

## 目标与非目标

Entity、Unit、Player、Monster、Item、Weapon、World 各有同名 C++ 文件及小写 Lua 模块。
C++ 独占可变状态与生命周期，Lua 保留运动、AI、枪械、伤害和终态规则。
Item 仅提供实例 ID、配置 ID、正数量及有限容量的创建、查询、修改、销毁；不接入物品配置、
背包、拾取、掉落和网络投影。Weapon 为 Player 组件，Item 不继承 Entity。

## 不变量、线程与所有权

Script 在逻辑线程拥有唯一 World。Unit 公开继承 Entity，Player/Monster 公开继承 Unit。
实体继承链只在 Entity 保存一份 Access*、身份和位置；Weapon 继续作为 Player 的组合组件。
Actor 保留 variant<Player, Monster> 值存储，unit() 只借用基类引用；不通过基类指针销毁对象。
原生字段可见性与受控脚本方法保持，继承不引入第二份状态、自引用兼容成员或虚拟玩法钩子。
Luax ClassBuilder 注册七类代际句柄，脚本包装表只保存只读身份与句柄，不复制可变字段。
七类 TypeTag、工厂及显式方法清单保持；C++ 继承不允许 Lua 句柄跨类型隐式转换。
固定候选以类表上的显式 getter/setter 方法访问对象；运动使用多返回值读取和批量提交。
每 Tick 通过 128 次有界临时表写入推进固定候选的自动 GC；该表不承载玩法状态。
身份、配置引用和分配器不可由普通脚本写入；移除、重开和导入使旧实例及组件句柄失效。
World 会话句柄在会话内稳定；关闭后所有对象失效。禁止错误线程访问和同步重入。

## 接口与值语义

输入在 C++ Tick 边界校验并锁存，保持序号消费、边沿合并和确认语义。快照由 C++ 原生状态
生成，战斗事件通过 typed Host 提交；高频路径不编解码 JSON。网络协议 v3、内容 v2 不变。
Host 与内部状态为 v4；旧状态拒绝导入。JSON 仅用于低频控制、上下文和完整状态往返。

Monster 的出生配置解析与状态导入共用原生校验，配置边界不依赖 Lua 调用方自律。
状态属性仅接受 spawn/patrol/chase/attack/dead，禁止重新出生及死亡后恢复活态；
attack_ticks 上限取所属怪物配置，spawn/dead 只能写零。配置派生上限不进入快照，
创建及导入时重新取得。Unit 拒绝零生命恢复正值和死单位非零速度，生死标志须与生命值一致。
状态选择、巡逻、追击、攻击时机与伤害仍由 Lua 决定，原生不执行 AI 调度。
不存在的规范出生 ID 返回 invalid_spawn；非法身份格式或配置错误抛出边界错误。
所有出生校验在分配前完成，失败不改变实体集合、代次和 ID 高水位。

## 失败、取消与退出

对象容量或分配失败不消费 ID。导入在独立候选完成结构、引用、配置和状态关系校验后发布。
发布前为候选对象更新代次，并将继承链及 Weapon/Item 的门禁指针绑定到目标 World::access。
批量运动仍在全部校验成功后写入；身份精度、状态 v4 schema 和死亡处理顺序不变。
输出拥有数据并按调用暂存，脚本及整批输出验证成功后提交；失败丢弃整批并中止会话。
快照可合并，可靠事件不静默丢弃；沿用有界队列、执行预算及停机收尾。

## 验证

真实绑定验证可见性、只读字段、错误类型、陈旧句柄、线程、容量和完整 uint64 分配边界。
状态往返覆盖 Item 和非法候选；既有游戏、实体、网络、进程契约验证行为不变。
继承回归验证基类与具体类视图共享状态和门禁、导入后的写入与只读拒绝、批量写入失败不变，
以及 Lua 不同类型句柄互传仍拒绝、重开和导入后各实体视图失效。
Windows 开发及生产签名 Bundle 均须验证；性能比较默认和 64 实体的固定 Tick 与网络输出。
Windows 两种模式已运行，结果见 VERIFICATION.md；Android 证据保持原有边界。

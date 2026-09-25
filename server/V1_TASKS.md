# 服务端 V1 实施记录

范围：Windows 单人 PvE 撤离持久化闭环。Unity、Android 为独立联调发布门槛。
以 SRV-011 为增量契约；热更新不再作为本版前置。配置采用地图 2 的首版 Excel。
2026-09-25：T01—T13 已实现并完成 Windows 验证；Unity／Android 不在此完成声明内。

| 任务 | 产出 | 状态 |
| --- | --- | --- |
| V1-T01 | 基线、契约、依赖与验证 | 已验证；原开发基线 17/17，含 Bundle 制品契约 |
| V1-T02 | Session/CmdCtx、按身份查找、观察者与定向发送 | 已验证；session/raid 契约、旧世界 TCP 拒绝 |
| V1-T03 | Excel 运行配置、引用、堆叠与最坏掉落容量校验 | 已验证；demo_content 契约 |
| V1-T04 | 存档先加载再 Ready、显式路径、停止排空 | 已验证；demo_edges/runtime_crash |
| V1-T05 | 持久 MatchId、Preparing、独立玩家状态、放弃 | 已验证；十次同会话放弃及跨启动不复用 |
| V1-T06 | 显式操作者、目标入口、Boss 前摇／恢复／冷却 | 已验证；game/pve 契约及真实战斗 |
| V1-T07 | 配置 ID／局内 ID／永久 UID、归属、原子堆叠 | 已验证；raid/entity 契约 |
| V1-T08 | 死亡仅掉落一次、服务端拾取与重复拒绝 | 已验证；raid/pve/demo |
| V1-T09 | Boss 实例资格、累计／剩余 Tick、伤害／离区取消 | 已验证；pve 契约和十局真实撤离 |
| V1-T10 | 冻结结算、Unknown 查询、原请求重试、重启恢复 | 已验证；真实 Runtime 提交边界强杀 |
| V1-T11 | v4 协议、C++／C#、客户端、仓库分页 | 已验证；生成客户端完成闭环，300+ 物品分页 |
| V1-T12 | Runtime＋TCP＋Storage 的强杀／写失败／暂停／关闭 | 已验证；两种模式故障集通过 |
| V1-T13 | 两种模式、同一 Runtime 十局、十次启停、联调包 | 已验证；开发 24/24、Bundle 23/23，最终增量回归通过 |

执行顺序：T01 → T02/T03 → T04/T05 → T06/T07/T08/T09 → T10 → T12/T13。
T11 随各功能接入。所有状态仅依据实际修改与测试更新。

## 实施决定

- 当前根目录策划 Excel 已改为天赋／弹药等新结构，与评估引用的撤离样例不兼容。
  `design/demo/首版.xlsx` 保留历史地图 2 的依赖闭包并补充背包、堆叠和 Boss 前后摇；
  原工作簿未修改，详见 [内容说明](../design/demo/README.md)。正式构建只读取这一本。
- `design/combat_demo.json` 仅生成测试夹具，独立 fixture 宿主／客户端不属于交付白名单。
- SQLite V1 保持兼容，一局一名玩家、固定本地存档身份；Session/World 边界不代表多人能力。
- SRV-008 的完整 Demo／Android 契约保持 deferred，SRV-006 热更新保持 deferred。
- 改动前重新跑的是开发全套及其中 Bundle 制品契约；独立 Bundle Runtime 的本轮证据是
  新版本完整 23 项回归，不把以前记录或改动前已有二进制视作本轮通过。

## 交付

[启动、协议和保存说明](README.md)、[验证记录](VERIFICATION.md)、
[首版契约](intents/usecases/demo.intent.md)。

本机联调包：`build/delivery/hunter-v1-source.zip`、`build/delivery/hunter-v1-bundle.zip`。
每包自带 START.txt、manifest.json、内容 JSON、C# 和 Proto；不包含存档、实例令牌或私钥。
Bundle 使用公开测试向量签名，仅供本地验收；正式发行必须按既有签名流程使用发行密钥。
两个解压包均已从独立目录完成真实撤离、保存和重启查询。

# 验证 DEMO 玩法实施记录

2026-09-26：SRV-012 六个切片均完成 Windows 实现与实际验证。
此前 SRV-011 完成记录保留；本表只记录本轮增量。

| 任务 | 内容 | 状态 |
| --- | --- | --- |
| G01 | 契约、根表迁移、显式来源、内容v4及严格校验 | 已验证；gameplay_content、导表及历史夹具回归 |
| G02 | 原生状态、Host／状态v6、协议v5与动作防重放 | 已验证；demo/action/schema、真实 TCP 动作重放与暂停边界 |
| G03 | 配装、体型姿态、体力与分段恢复 | 已验证；两种 Runtime gameplay 契约 |
| G04 | 三枪三弹、多槽装填、近战、散射与穿透 | 已验证；两种 Runtime game/gameplay/edges 契约 |
| G05 | 四工具、梯子、补给箱、掩体与炸药 | 已验证；两种 Runtime gameplay/edges、补给重放 TCP |
| G06 | 两种Runtime、存档回归、十局／启停与联调包 | 已验证；完整开发30/30、Bundle29/29，最终增量8/8及10/10，最终两模式各十局／十次启停与解包运行通过 |

精确规则见 [SRV-012](intents/usecases/gameplay.intent.md)。
Unity、Android及正式发行签名不属于本表完成声明。

本轮修复包含梯顶退出遮挡误判、梯上动作和拾取门禁、端点移动离梯、暂停匍匐朝向、
装备误入奖励背包、异步局号分配跨暂停恢复的
旧开局执行问题。历史灰盒测试已显式升级到内容v4，按逐发装填与结算提交门禁验证，
不通过生产旧版本分支维持旧预期。原生状态导入仍先验证完整候选，再一次替换活动世界。

实际命令、时间、内容和包摘要见 [验证记录](VERIFICATION.md)。

联调包：`build/delivery/hunter-v1-source.zip` 与 `build/delivery/hunter-v1-bundle.zip`。
包内包含客户端、C#、Proto、共享内容及启动说明；Bundle 使用公开测试签名。

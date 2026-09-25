# 首版服务端内容

`首版.xlsx` 是地图 2 单人撤离 Demo 的正式构建输入。修改后构建会重新生成
`server/build/<preset>/generated/content.json`、`ContentSpec.h` 和内容 SHA-256 身份。
复用 `export/export.py` 读表，业务映射和校验位于 `export/demo.py`。

当前根目录策划表已改为新的物品、天赋和弹药结构，与评估引用的撤离样例不兼容。
本工作簿取历史提交 `568e58b` 的地图 2 依赖闭包并显式补充 Bag 表、物品堆叠、
Boss 前后摇；原策划表保持不变。合并后续策划结构需同时修改适配器与内容契约。
`design/combat_demo.json` 只供灰盒回归测试，不参与正式服务内容生成。

首版：8 格背包、拾取半径 1500 mm；地图 2 的 Boss 出生实例 14 死亡后，
玩家回到出生地附近出口并停留 180 Tick。每局固定步枪和弹药，永久仓库只读。

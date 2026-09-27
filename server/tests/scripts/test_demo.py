# 验证当前 DEMO 显式生产闭包；历史首版工作簿已由仓库维护删除。
import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "export"))
import gameplay


class DemoTest(unittest.TestCase):
    # 每个风险用例使用真实工作簿解析结果的独立副本。
    def setUp(self):
        self.tables = gameplay.read_tables(ROOT / "design/demo_sources.json")

    # 同一输入产生逐字节一致内容，策划字段修改进入正式配置和摘要。
    def test_excel_source(self):
        data, header = gameplay.artifacts(self.tables)
        self.assertEqual((data, header), gameplay.artifacts(self.tables))
        self.assertEqual(json.loads(data)["bag"]["slots"], 8)
        self.tables["Bag"].rows[1]["Slots"] = 9
        changed, identity = gameplay.artifacts(self.tables)
        self.assertEqual(json.loads(changed)["bag"]["slots"], 9)
        self.assertNotEqual(header, identity)

    # 错误引用、概率、堆叠、出生碰撞和最坏掉落容量均在构建期拒绝。
    def test_rejections(self):
        cases = [("ExtractPoint", "NeedBossSpawnIdx", 9999),
                 ("DropEntry", "ChanceBp", 10001), ("Item", "MaxStack", 0),
                 ("DropEntry", "MaxCount", 2147483647),
                 ("Bag", "Slots", 33), ("PlayerSpawn", "X", -1),
                 ("Attack", "WindupTicks", 3601)]
        for table, field, value in cases:
            with self.subTest(table=table, field=field):
                tables = copy.deepcopy(self.tables)
                key = 2800001 if table == "Item" else next(iter(tables[table].rows))
                tables[table].rows[key][field] = value
                with self.assertRaises((ValueError, KeyError)):
                    gameplay.artifacts(tables)


if __name__ == "__main__":
    unittest.main()

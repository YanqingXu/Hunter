# 验证生产清单、根表内容、几何边界和显式旧夹具升级，错误输入不得产生正式配置。
import copy
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "export"))
import gameplay


class GameplayContentTest(unittest.TestCase):
    # 所有用例从真实生产清单取独立数据，避免测试值反向污染源表。
    def setUp(self):
        self.tables = gameplay.read_tables(ROOT / "design/demo_sources.json")

    # 两角色、三枪三弹和四工具采用已确认字段，旧战利品身份和出生几何保持稳定。
    def test_production_values(self):
        data, header = gameplay.artifacts(self.tables)
        self.assertEqual((data, header), gameplay.artifacts(self.tables))
        doc = json.loads(data)
        self.assertEqual(doc["v"], 4)
        self.assertEqual(set(doc["players"]), {"1", "2"})
        self.assertEqual(doc["players"]["1"]["hp_segments"], [50, 50, 25, 25])
        self.assertEqual(doc["players"]["2"]["run_cost"], 0)
        self.assertEqual(doc["default_loadout"]["weapons"], ["1", "3"])
        self.assertEqual(doc["default_loadout"]["tools"], ["30001", "30002"])
        self.assertEqual(doc["default_loadout"]["consumables"], ["30004", "30006"])
        self.assertEqual([doc["ammo"][k]["range"] for k in ("1", "2", "3")], [18000, 6000, 12000])
        self.assertEqual(doc["ammo"]["2"]["pellets"], 5)
        self.assertEqual(doc["ammo"]["1"]["loss"], 50)
        self.assertEqual(doc["tools"]["30002"]["uses"], 3)
        self.assertEqual(doc["tools"]["30006"]["use_ticks"], 180)
        self.assertEqual(set(doc["monsters"]), {"1002", "1003"})
        self.assertEqual(doc["monsters"]["1003"]["rank"], 3)
        self.assertEqual(doc["extracts"][0]["boss_spawn_id"], "14")
        self.assertEqual(doc["items"]["2800001"]["max_stack"], 99)
        self.assertEqual({s["kind"] for s in doc["scenes"]}, {"ladder", "supply", "cover"})

    # 被选字段改变必须改变运行内容和摘要，未选草稿不会被隐式加入清单。
    def test_identity_and_explicit_sources(self):
        original = gameplay.artifacts(self.tables)
        self.tables["Player"].rows[1]["Run"] = 151
        self.assertNotEqual(original, gameplay.artifacts(self.tables))
        with tempfile.TemporaryDirectory(prefix="hunter-production-") as folder:
            target = Path(folder)
            source = ROOT / "design/demo_sources.json"
            manifest = json.loads(source.read_text(encoding="utf-8"))
            shutil.copyfile(source, target / source.name)
            for name in {s["file"] for s in manifest["sources"]}:
                shutil.copyfile(ROOT / "design" / name, target / name)
            (target / "技能表.xlsx").write_bytes(b"not even an xlsx")
            (target / "关卡表.xlsx").write_bytes(b"duplicate map drafts must not be read")
            self.assertEqual(original, gameplay.artifacts(gameplay.read_tables(target / source.name)))
            manifest["sources"][0]["ids"].append(999)
            (target / source.name).write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "999"):
                gameplay.read_tables(target / source.name)

    # 引用、血段、动作数值、容量和新掩体对旧巡逻的影响都必须在构建时拒绝。
    def test_rejected_source_changes(self):
        cases = [
            ("Weapon", 1, "DefaultBullet", 99),
            ("Weapon", 1, "BulletType", 2),
            ("Player", 1, "HPSetting", "50|25"),
            ("Player", 1, "ProneWidth", 999),
            ("Player", 1, "RunCost", 1),
            ("Player", 1, "Run", 1001),
            ("Player", 1, "ProneSpeed", 1001),
            ("Player", 1, "EquipmentLimit", 2),
            ("Tool", 30002, "SlowPercent", 31),
            ("Tool", 30006, "Speed", 0),
            ("Ammunition", 2, "Bullet", 17),
            ("Ammunition", 1, "AdditionalStatus", 1),
            ("DropEntry", 1, "ChanceBp", 10001),
            ("DropEntry", 1, "MaxCount", 2147483647),
            ("Monster", 1003, "Rank", 2),
            ("Attack", 1003, "WindupTicks", 90),
            ("Scene", 3, "X", 18000),
            ("Scene", 1, "H", 1400),
            ("Scene", 3, "Penetrable", 2),
            ("Rules", 1, "MaxScenes", 33),
            ("Loadout", 1, "Tools", "30002|30002"),
            ("ExtractPoint", 1, "NeedBossSpawnIdx", 12),
        ]
        for table, key, field, value in cases:
            with self.subTest(table=table, field=field):
                tables = copy.deepcopy(self.tables)
                tables[table].rows[key][field] = value
                with self.assertRaises((ValueError, KeyError)):
                    gameplay.artifacts(tables)

    # 显式旧夹具升级保留旧核心数值，同时提供运行时必需的完整 v4 默认状态。
    def test_explicit_fixture_upgrade(self):
        old = json.loads((ROOT / "design/combat_demo.json").read_text(encoding="utf-8"))
        doc = gameplay.upgrade_fixture(old)
        self.assertEqual(doc["v"], 4)
        self.assertEqual(doc["map"], old["map"])
        self.assertEqual(doc["players"]["1"]["hp"], 100)
        self.assertEqual(doc["players"]["1"]["hp_segments"], [25, 25, 25, 25])
        self.assertEqual(doc["weapons"]["1"]["damage"], 20)
        self.assertEqual(doc["weapons"]["1"]["magazine"], 6)
        self.assertEqual(doc["weapons"]["1"]["reserve"], 30)
        self.assertEqual(doc["weapons"]["1"]["reload_ticks"], 90)
        self.assertEqual(doc["default_loadout"]["weapons"], ["1"])
        self.assertEqual(doc["scenes"], [])
        self.assertEqual(doc["tools"], {})
        gameplay.content_artifacts(doc)

    # 检查模式无需输出位置，也不会在工作目录创建文件。
    def test_check_mode_is_read_only(self):
        with tempfile.TemporaryDirectory(prefix="hunter-content-check-") as folder:
            result = subprocess.run([sys.executable, "-B", str(ROOT / "export/gameplay.py"),
                "--source", str(ROOT / "design/demo_sources.json"), "--check"],
                cwd=folder, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(list(Path(folder).iterdir()), [])

    # 真实导出保留原英文字段和数值主键，服务端身份头不能隐藏第二份配置模型。
    def test_lua_artifacts_and_shared_identity(self):
        with tempfile.TemporaryDirectory(prefix="hunter-lua-content-") as folder:
            root = Path(folder)
            gameplay.export_content(source=ROOT / "design/demo_sources.json",
                output=root / "content.json", header=root / "ContentId.h", cfg=root / "cfg")
            raw = (root / "cfg/Player.lua").read_text(encoding="utf-8")
            self.assertIn('[1] = {', raw)
            self.assertIn('["HPSetting"] = "50|50|25|25"', raw)
            self.assertNotIn('hp_segments', raw)
            header = (root / "ContentId.h").read_text(encoding="utf-8")
            self.assertNotIn('json_text', header)
            self.assertIn('65104619225ac997076578c36c5d4cca91ebf28af6fe7d66df19f13d1b056908',
                          header)
            manifest = json.loads((root / "cfg/Manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(sum(m["kind"] == "data" for m in manifest["modules"]), 19)
            self.assertEqual(gameplay.content_bytes(gameplay.evaluate({
                p.name: p.read_bytes() for p in (root / "cfg").iterdir()}))[0],
                (root / "content.json").read_bytes())

    # Lua 语义失败或多个文件发布途中失败均保留完整上一份有效配置。
    def test_validation_and_publication_failure_preserve_artifacts(self):
        with tempfile.TemporaryDirectory(prefix="hunter-lua-rollback-") as folder:
            root = Path(folder)
            args = {"source": ROOT / "design/demo_sources.json", "output": root / "content.json",
                    "header": root / "ContentId.h", "cfg": root / "cfg"}
            gameplay.export_content(**args)
            before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()}
            invalid = copy.deepcopy(self.tables)
            invalid["Weapon"].rows[1]["DefaultBullet"] = 99
            with mock.patch.object(gameplay, "read_tables", return_value=invalid):
                with self.assertRaisesRegex(ValueError, "Lua configuration validation failed"):
                    gameplay.export_content(**args)
            self.assertEqual(before,
                {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()})
            replace = gameplay.sheets.PUBLISH.os.replace
            writes = 0

            # 第二个制品发布失败，恢复流程继续使用真实替换操作。
            def fail_second(source, target):
                nonlocal writes
                writes += 1
                if writes == 2:
                    raise OSError("injected configuration publication failure")
                return replace(source, target)

            with mock.patch.object(gameplay.sheets.PUBLISH.os, "replace", side_effect=fail_second):
                with self.assertRaisesRegex(OSError, "injected"):
                    gameplay.export_content(**args)
            self.assertEqual(before,
                {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()})

    # 运行时相同代码检查规范夹具，不能通过改为测试来源绕过非法字段或版本。
    def test_fixture_semantics_cannot_bypass_validation(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        for field, value in (("v", 3), ("tick_hz", 30), ("unsupported", 1)):
            changed = copy.deepcopy(doc)
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                gameplay.content_artifacts(changed)


if __name__ == "__main__":
    unittest.main()

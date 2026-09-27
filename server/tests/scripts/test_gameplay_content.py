# 验证生产清单、根表内容、几何边界和显式旧夹具升级，错误输入不得产生正式配置。
import copy
import hashlib
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
        self.assertEqual(doc["v"], 5)
        self.assertEqual(doc["players"]["1"]["recruit_cost"], {"currency_id": "1", "amount": 100})
        self.assertEqual(doc["players"]["2"]["recruit_cost"], {"currency_id": "0", "amount": 0})
        self.assertEqual(doc["players"]["1"]["skill_points"], 10)
        self.assertEqual(doc["skills"], {})
        self.assertFalse(doc["legacy_ai"])
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
            ("Player", 1, "Cost", ""),
            ("Player", 1, "Cost", "1:0"),
            ("Player", 1, "Cost", "1:-1"),
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

    # 显式旧夹具升级保留旧核心数值，同时提供 v5 字段并标记旧 AI 回归。
    def test_explicit_fixture_upgrade(self):
        old = json.loads((ROOT / "design/combat_demo.json").read_text(encoding="utf-8"))
        doc = gameplay.upgrade_fixture(old)
        self.assertEqual(doc["v"], 5)
        self.assertTrue(doc["legacy_ai"])
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
            self.assertIn('gameplay-v5:' + hashlib.sha256(
                (root / "content.json").read_bytes()).hexdigest(), header)
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

    # 内容边界与原生读条和投射上限一致，运行时不能才发现离线配置越界。
    def test_tool_native_bounds(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        for key, field, value in (("30002", "use_ticks", 0),
                                  ("30002", "use_ticks", 36001),
                                  ("30006", "throw_range", 100001),
                                  ("30006", "speed", 1001)):
            changed = copy.deepcopy(doc)
            changed["tools"][key][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                gameplay.content_artifacts(changed)
        doc["tools"]["30002"]["use_ticks"] = 36000
        doc["tools"]["30006"].update(throw_range=100000, speed=1000)
        gameplay.content_artifacts(doc)

    # 新能力、召唤、双波和场景参数使用明确测试值，未填参数不得隐式回退。
    def test_v5_mechanics_and_required_parameters(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        summon = copy.deepcopy(doc["monsters"]["1002"])
        summon.update(rank=4, drops=[])
        doc["monsters"]["2001"] = summon
        doc["abilities"] = {
            "1": {"kind": "melee", "range": 1000, "damage": 10, "windup": 5,
                  "recover": 5, "cooldown": 60, "summon_cfg_id": "0", "despawn_ticks": 0},
            "2": {"kind": "summon", "range": 1500, "damage": 0, "windup": 5,
                  "recover": 5, "cooldown": 120, "summon_cfg_id": "2001", "despawn_ticks": 90}}
        ai = {"alert_speed": 40, "disengage_ticks": 60, "damage_reduction_bp": 5000,
              "basic": False, "abilities": [{"cfg_id": "2", "phase": "alert", "priority": 1}],
              "rage_thresholds": [], "rage_ticks": 0, "rage_ability_id": "0"}
        doc["monsters"]["1002"]["ai"] = ai
        boss_ai = copy.deepcopy(ai)
        boss_ai.update(basic=True, abilities=[{"cfg_id": "1", "phase": "rage", "priority": 1}],
                       rage_thresholds=[180, 100], rage_ticks=120, rage_ability_id="1")
        doc["monsters"]["1003"]["ai"] = boss_ai
        doc["encounter"] = {"bounty_cfg_id": "2800001", "alert_scale_bp": 15000,
                            "second_wave": ["12", "13"]}
        next(s for s in doc["scenes"] if s["kind"] == "supply")["interaction"] = {
            "mode": "channel", "hold_ticks": 60}
        doc["scenes"].append({"id": "4", "kind": "barrel", "x": 5000, "y": 0,
                              "w": 600, "h": 800, "penetrable": False, "interaction": False,
                              "barrel": {"hp": 30, "fuse_ticks": 60, "radius": 2000,
                                         "damage": 50}})
        gameplay.content_artifacts(doc)
        cases = [("missing cooldown", lambda d: d["abilities"]["1"].pop("cooldown")),
                 ("recursive summon", lambda d: d["monsters"]["2001"].update(ai=copy.deepcopy(ai))),
                 ("Boss in second wave", lambda d: d["encounter"].update(second_wave=["14"])),
                 ("unordered threshold", lambda d: d["monsters"]["1003"]["ai"].update(
                     rage_thresholds=[100, 180])),
                 ("missing barrel parameter", lambda d: d["scenes"][-1]["barrel"].pop("radius"))]
        for label, change in cases:
            candidate = copy.deepcopy(doc)
            change(candidate)
            with self.subTest(label=label), self.assertRaises(ValueError):
                gameplay.content_artifacts(candidate)

    # 天赋结构合法但未填写效果仍拒绝，未实现技能也不能通过清单直接启用。
    def test_v5_skills_are_explicit(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        doc["skills"]["4"] = {"name": "军需官", "target": "player", "category": "numeric",
                               "kind": "passive", "cost": 1, "effects": []}
        with self.assertRaisesRegex(ValueError, "effects are required"):
            gameplay.content_artifacts(doc)
        doc["skills"]["4"]["effects"] = [{"stat": "weight", "op": "add", "value": 1}]
        gameplay.content_artifacts(doc)
        doc["skills"]["3"] = doc["skills"].pop("4")
        with self.assertRaisesRegex(ValueError, "unsupported player skill"):
            gameplay.content_artifacts(doc)

    # 单独改变显式技能表的效果即可改变内容身份，未选择的草稿仍不进入导出。
    def test_selected_skill_source_changes_identity(self):
        sheets = {s.name: s for s in gameplay.sheets.read_workbook(ROOT / "design/技能表.xlsx")}
        self.tables["Skill"] = gameplay.select(sheets["技能|Skill"], [4])
        effect_sheet = sheets["技能效果|SkillEffect"]
        fields = gameplay.sheets.read_fields(effect_sheet)
        self.tables["SkillEffect"] = gameplay.sheets.Table("SkillEffect", effect_sheet,
            fields, {1: {"EffectIdx": 1, "SkillIdx": 4, "Stat": "weight", "Op": "add", "Value": 1}})
        first, identity = gameplay.artifacts(self.tables)
        self.assertEqual(json.loads(first)["skills"]["4"]["effects"][0]["value"], 1)
        self.tables["SkillEffect"].rows[1]["Value"] = 2
        second, changed = gameplay.artifacts(self.tables)
        self.assertNotEqual(first, second)
        self.assertNotEqual(identity, changed)
        self.tables["SkillEffect"].rows.clear()
        with self.assertRaisesRegex(ValueError, "effects are required"):
            gameplay.artifacts(self.tables)

    # 成长阈值和每种非召唤怪奖励必须完整、单调且非负。
    def test_career_fixture_requires_complete_rewards(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        doc["career"] = {"levels": [0, 100, 300], "kill_xp": {"1002": 10, "1003": 50},
                         "extract_xp": 30, "bounty_xp": 20, "bounty_currency": 100,
                         "retire_xp": 200}
        gameplay.content_artifacts(doc)
        for field, value in (("levels", [0, 100, 100]), ("kill_xp", {"1002": 10}),
                             ("retire_xp", -1)):
            changed = copy.deepcopy(doc)
            changed["career"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                gameplay.content_artifacts(changed)


if __name__ == "__main__":
    unittest.main()

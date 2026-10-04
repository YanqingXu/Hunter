# 验证全部根表、服务器外键、启用门禁和几何边界，错误输入不得产生正式配置。
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


# 旧版本数据只作为隔离夹具保存在测试内，生产根目录不恢复已移除的示例 JSON。
def legacy_fixture():
    return {"v": 3, "tick_hz": 60,
        "map": {"width": 20000, "height": 10000, "gravity": 8, "solids": [],
                "spawn": {"x": 1000, "y": 0, "cfg_id": "1", "weapon_cfg_id": "1"},
                "enemies": [{"spawn_id": "1", "cfg_id": "1", "x": 4000, "y": 0,
                             "patrol_min": 3500, "patrol_max": 5000}]},
        "players": {"1": {"width": 1000, "height": 1800, "hp": 100,
                          "speed": 100, "jump_speed": 160}},
        "weapons": {"1": {"range": 10000, "damage": 20, "magazine": 6,
                          "reserve": 30, "fire_ticks": 20, "reload_ticks": 90}},
        "monsters": {"1": {"width": 600, "height": 800, "hp": 50, "speed": 20,
                           "detect_range": 5000, "attack_range": 600,
                           "damage": 10, "attack_ticks": 60}}}


class GameplayContentTest(unittest.TestCase):
    # 所有用例从真实根工作簿取独立数据，避免测试值反向污染源表。
    def setUp(self):
        self.tables = gameplay.read_tables(ROOT / "design")

    # 两角色、三枪三弹和四工具采用已确认字段，旧战利品身份和出生几何保持稳定。
    def test_production_values(self):
        data, header = gameplay.artifacts(self.tables)
        self.assertEqual((data, header), gameplay.artifacts(self.tables))
        doc = json.loads(data)
        self.assertEqual(doc["v"], 6)
        self.assertEqual(set(doc["skills"]), {str(key) for key, value
            in self.tables["Skill"].rows.items() if value["Enabled"] == 1})
        self.assertTrue({"3", "6", "14", "15"}.isdisjoint(doc["skills"]))
        self.assertFalse(doc["legacy_ai"])
        self.assertEqual(set(doc["players"]), {"1", "2"})
        self.assertEqual(doc["players"]["1"]["hp_segments"], [50, 50, 25, 25])
        self.assertEqual(doc["players"]["2"]["run_cost"], 0)
        self.assertEqual(set(doc["monsters"]), {str(key) for key, value
            in self.tables["Monster"].rows.items() if value["Enabled"] == 1})
        self.assertEqual(set(doc["tools"]), {str(key) for key, value
            in self.tables["Tool"].rows.items() if value["Enabled"] == 1})
        for key, cfg in doc["ammo"].items():
            source = self.tables["Ammunition"].rows[int(key)]
            self.assertEqual(cfg["refill_count"], source["RefillCount"])
            self.assertEqual(cfg["prop_damage"], source["PropDamage"])
        for key, cfg in doc["tools"].items():
            source = self.tables["Tool"].rows[int(key)]
            self.assertEqual(cfg["refill_count"], source["RefillCount"])
            self.assertEqual(cfg["supply_weight"], source["SupplyWeight"])
        self.assertTrue(doc["extracts"])
        self.assertIn("ammo_supply", {s["kind"] for s in doc["scenes"]})
        self.assertTrue(doc["item_spawns"])

    # 全部根表字段进入来源；子目录和编辑器临时文件不属于根配置。
    def test_identity_and_root_sources(self):
        original = gameplay.artifacts(self.tables)
        self.tables["Player"].rows[1]["Run"] = 151
        self.assertNotEqual(original, gameplay.artifacts(self.tables))
        with tempfile.TemporaryDirectory(prefix="hunter-production-") as folder:
            target = Path(folder)
            for source in (ROOT / "design").glob("*.xlsx"):
                if not source.name.startswith("~$"):
                    shutil.copyfile(source, target / source.name)
            (target / "~$编辑中.xlsx").write_bytes(b"not an xlsx")
            nested = target / "archive"
            nested.mkdir()
            (nested / "历史配置.xlsx").write_bytes(b"not an xlsx")
            self.assertEqual(original, gameplay.artifacts(gameplay.read_tables(target)))
            (target / "新增根表.xlsx").write_bytes(b"not an xlsx")
            with self.assertRaises(ValueError):
                gameplay.read_tables(target)

    # 引用、血段、动作数值、容量和新掩体对旧巡逻的影响都必须在构建时拒绝。
    def test_rejected_source_changes(self):
        selected = self.tables["Server"].rows[1]
        weapon = next(iter(self.tables["Weapon"].rows))
        ammo = next(iter(self.tables["Ammunition"].rows))
        boss = next(key for key, value in self.tables["Monster"].rows.items()
                    if value["Enabled"] == 1 and value["Rank"] == 3)
        attack = self.tables["Monster"].rows[boss]["AttackIdx"]
        scene = next(key for key, value in self.tables["Scene"].rows.items()
                     if value["MapIdx"] == selected["MapIdx"])
        entry = next(iter(self.tables["DropEntry"].rows))
        extract = next(key for key, value in self.tables["ExtractPoint"].rows.items()
                       if value["MapIdx"] == selected["MapIdx"])
        cases = [
            ("Server", 1, "MapIdx", 2147483647),
            ("Weapon", weapon, "DefaultBullet", 2147483647),
            ("Weapon", weapon, "BulletType", self.tables["Weapon"].rows[weapon]["DefaultBullet"] + 1),
            ("Player", 1, "HPSetting", "50|25"),
            ("Player", 1, "ProneWidth", 999),
            ("Player", 1, "RunCost", 1),
            ("Player", 1, "Run", 1001),
            ("Player", 1, "ProneSpeed", 1001),
            ("Player", 1, "EquipmentLimit", 0),
            ("Player", 1, "Cost", ""),
            ("Player", 1, "Cost", "1:0"),
            ("Player", 1, "Cost", "1:-1"),
            ("Tool", 30002, "SlowPercent", 31),
            ("Tool", 30006, "Speed", 0),
            ("Ammunition", ammo, "Bullet", 17),
            ("Ammunition", ammo, "AdditionalStatus", 1),
            ("Ammunition", ammo, "RefillCount", -1),
            ("Ammunition", ammo, "Prop", "1||2"),
            ("DropEntry", entry, "ChanceBp", 10001),
            ("DropEntry", entry, "MaxCount", 2147483647),
            ("Monster", boss, "Rank", 2),
            ("Monster", boss, "Enabled", 2),
            ("Attack", attack, "CooldownTicks", 0),
            ("Scene", scene, "X", 100000),
            ("Scene", scene, "H", 0),
            ("Scene", scene, "Penetrable", 2),
            ("Rules", selected["RulesIdx"], "MaxScenes", 33),
            ("Loadout", selected["LoadoutIdx"], "Tools", "30002|30002"),
            ("Loadout", selected["LoadoutIdx"], "Skills", "3"),
            ("ExtractPoint", extract, "NeedBossSpawnIdx", 2147483647),
        ]
        for table, key, field, value in cases:
            with self.subTest(table=table, field=field):
                tables = copy.deepcopy(self.tables)
                tables[table].rows[key][field] = value
                with self.assertRaises((ValueError, KeyError)):
                    gameplay.artifacts(tables)

    # 服务器外键可选择其他主键，场景类别与出口数量由内容决定而非旧 Demo 门禁。
    def test_server_references_and_flexible_layout(self):
        expected = gameplay.artifacts(self.tables)
        selected = self.tables["Server"].rows[1]
        old_map = selected["MapIdx"]
        new_key = 2147483646
        for name in ("Map", "Bag", "Rules", "Loadout", "Career", "Encounter"):
            old_key = selected[name + "Idx"]
            if old_key == 0:
                continue
            table = self.tables[name]
            value = copy.deepcopy(table.rows[old_key])
            value[table.fields[0].name] = new_key
            table.rows[new_key] = value
            selected[name + "Idx"] = new_key
        for table in self.tables.values():
            for row in table.rows.values():
                if row.get("MapIdx") == old_map:
                    row["MapIdx"] = new_key
        self.assertEqual(expected, gameplay.artifacts(self.tables))
        doc = json.loads(expected[0])
        doc["scenes"] = []
        doc["exploration"] = False
        for ammo in doc["ammo"].values():
            ammo["prop_ids"] = []
        another = copy.deepcopy(doc["extracts"][0])
        another["id"] = max(point["id"] for point in doc["extracts"]) + 1
        doc["extracts"].append(another)
        gameplay.content_artifacts(doc)

    # 随机池拒绝禁用技能、普通工具、无效权重、数量越界和实心物内部出生。
    def test_item_spawn_pool_boundaries(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        spawn = doc["item_spawns"][0]
        spawn["choices"] = [{"cfg_id": "30006", "count": 1, "weight": 1}]
        gameplay.content_artifacts(doc)
        cases = [
            ("unknown item", lambda d: d["item_spawns"][0]["choices"][0].update(cfg_id="999999")),
            ("ordinary tool", lambda d: d["item_spawns"][0]["choices"][0].update(cfg_id="30002")),
            ("zero weight", lambda d: d["item_spawns"][0]["choices"][0].update(weight=0)),
            ("consumable capacity", lambda d: d["item_spawns"][0]["choices"][0].update(count=101)),
            ("outside map", lambda d: d["item_spawns"][0].update(x=d["map"]["width"] + 1)),
            ("invalid chance", lambda d: d["item_spawns"][0].update(chance=10001)),
            ("empty pool", lambda d: d["item_spawns"][0].update(choices=[])),
        ]
        for label, change in cases:
            candidate = copy.deepcopy(doc)
            change(candidate)
            with self.subTest(label=label), self.assertRaises(ValueError):
                gameplay.content_artifacts(candidate)
        capacity = copy.deepcopy(doc)
        capacity["encounter"] = False
        for monster in capacity["monsters"].values():
            monster["drops"] = []
        capacity["item_spawns"] = [dict(copy.deepcopy(spawn), id=str(i + 1))
                                   for i in range(64)]
        gameplay.content_artifacts(capacity)
        capacity["item_spawns"].append(dict(copy.deepcopy(spawn), id="65"))
        with self.assertRaises(ValueError):
            gameplay.content_artifacts(capacity)
        if doc["map"]["solids"]:
            solid = doc["map"]["solids"][0]
            spawn.update(x=solid["x"] + 1, y=solid["y"] + 1)
            with self.assertRaisesRegex(ValueError, "inside solid"):
                gameplay.content_artifacts(doc)

    # 补给与穿透参数均受内容门禁约束，开关关闭可以保留有效掩体引用。
    def test_refill_penetration_and_consumable_slots(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        ammo_key = next(iter(doc["ammo"]))
        weapon_key = next(iter(doc["weapons"]))
        cover = next(scene for scene in doc["scenes"] if scene["kind"] == "cover")
        doc["ammo"][ammo_key]["prop_ids"] = [cover["id"]]
        cover["penetrable"] = False
        doc["default_loadout"]["consumables"] = ["30006", "30006"]
        gameplay.content_artifacts(doc)
        cases = [
            ("ammo refill", lambda d: d["ammo"][ammo_key].update(refill_count=-1)),
            ("duplicate prop", lambda d: d["ammo"][ammo_key].update(prop_ids=[cover["id"], cover["id"]])),
            ("unknown prop", lambda d: d["ammo"][ammo_key].update(prop_ids=["2147483647"])),
            ("tool refill", lambda d: d["tools"]["30002"].update(refill_count=101)),
            ("supply weight", lambda d: d["tools"]["30006"].update(supply_weight=-1)),
            ("ammo capacity", lambda d: d["weapons"][weapon_key].update(reserve=100000)),
        ]
        for label, change in cases:
            candidate = copy.deepcopy(doc)
            change(candidate)
            with self.subTest(label=label), self.assertRaises(ValueError):
                gameplay.content_artifacts(candidate)

    # 显式旧夹具升级保留旧核心数值，同时提供 v5 字段并标记旧 AI 回归。
    def test_explicit_fixture_upgrade(self):
        old = legacy_fixture()
        doc = gameplay.upgrade_fixture(old)
        self.assertEqual(doc["v"], 6)
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
                "--source", str(ROOT / "design"), "--check"],
                cwd=folder, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(list(Path(folder).iterdir()), [])

    # 真实导出保留原英文字段和数值主键，服务端身份头不能隐藏第二份配置模型。
    def test_lua_artifacts_and_shared_identity(self):
        with tempfile.TemporaryDirectory(prefix="hunter-lua-content-") as folder:
            root = Path(folder)
            gameplay.export_content(source=ROOT / "design",
                output=root / "content.json", header=root / "ContentId.h", cfg=root / "cfg")
            raw = (root / "cfg/Player.lua").read_text(encoding="utf-8")
            self.assertIn('[1] = {', raw)
            self.assertIn('["HPSetting"] = "50|50|25|25"', raw)
            self.assertNotIn('hp_segments', raw)
            header = (root / "ContentId.h").read_text(encoding="utf-8")
            self.assertNotIn('json_text', header)
            self.assertIn('gameplay-v6:' + hashlib.sha256(
                (root / "content.json").read_bytes()).hexdigest(), header)
            manifest = json.loads((root / "cfg/Manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(sum(m["kind"] == "data" for m in manifest["modules"]), len(self.tables))
            self.assertEqual(gameplay.content_bytes(gameplay.evaluate({
                p.name: p.read_bytes() for p in (root / "cfg").iterdir()}))[0],
                (root / "content.json").read_bytes())

    # Lua 语义失败或多个文件发布途中失败均保留完整上一份有效配置。
    def test_validation_and_publication_failure_preserve_artifacts(self):
        with tempfile.TemporaryDirectory(prefix="hunter-lua-rollback-") as folder:
            root = Path(folder)
            args = {"source": ROOT / "design", "output": root / "content.json",
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
    def test_v6_mechanics_and_required_parameters(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        normal = next(key for key, cfg in doc["monsters"].items() if cfg["rank"] <= 2)
        boss = next(key for key, cfg in doc["monsters"].items() if cfg["rank"] == 3)
        for cfg in doc["monsters"].values():
            cfg["ai"] = False
        summon = copy.deepcopy(doc["monsters"][normal])
        summon.update(rank=4, drops=[], ai=False)
        doc["monsters"]["2001"] = summon
        doc["abilities"] = {
            "1": {"kind": "melee", "range": 1000, "damage": 10, "windup": 5,
                  "recover": 5, "cooldown": 60, "summon_cfg_id": "0", "despawn_ticks": 0},
            "2": {"kind": "summon", "range": 1500, "damage": 0, "windup": 5,
                  "recover": 5, "cooldown": 120, "summon_cfg_id": "2001", "despawn_ticks": 90}}
        ai = {"alert_speed": 40, "disengage_ticks": 60, "damage_reduction_bp": 5000,
              "basic": False, "abilities": [{"cfg_id": "2", "phase": "alert", "priority": 1}],
              "rage_thresholds": [], "rage_ticks": 0, "rage_ability_id": "0"}
        doc["monsters"][normal]["ai"] = ai
        boss_ai = copy.deepcopy(ai)
        boss_ai.update(basic=True, abilities=[{"cfg_id": "1", "phase": "rage", "priority": 1}],
                       rage_thresholds=[doc["monsters"][boss]["hp"] - 1, 1],
                       rage_ticks=120, rage_ability_id="1")
        doc["monsters"][boss]["ai"] = boss_ai
        bounty = doc["encounter"]["bounty_cfg_id"] if doc["encounter"] else "2800001"
        wave = [spawn["spawn_id"] for spawn in doc["map"]["enemies"]
                if doc["monsters"][spawn["cfg_id"]]["rank"] <= 2]
        boss_spawn = next(spawn["spawn_id"] for spawn in doc["map"]["enemies"]
                          if doc["monsters"][spawn["cfg_id"]]["rank"] == 3)
        doc["encounter"] = {"bounty_cfg_id": bounty, "alert_scale_bp": 15000,
                            "second_wave": wave}
        next(s for s in doc["scenes"] if s["kind"] == "supply")["interaction"] = {
            "mode": "channel", "hold_ticks": 60}
        barrel_id = str(max(int(scene["id"]) for scene in doc["scenes"]) + 1)
        doc["scenes"].append({"id": barrel_id, "kind": "barrel", "x": 5000, "y": 0,
                              "w": 600, "h": 800, "penetrable": False, "interaction": False,
                              "barrel": {"hp": 30, "fuse_ticks": 60, "radius": 2000,
                                         "damage": 50}})
        gameplay.content_artifacts(doc)
        cases = [("missing cooldown", lambda d: d["abilities"]["1"].pop("cooldown")),
                 ("recursive summon", lambda d: d["monsters"]["2001"].update(ai=copy.deepcopy(ai))),
                 ("Boss in second wave", lambda d: d["encounter"].update(second_wave=[boss_spawn])),
                 ("unordered threshold", lambda d: d["monsters"][boss]["ai"].update(
                     rage_thresholds=[1, doc["monsters"][boss]["hp"] - 1])),
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

    # 启用效果改变内容身份，禁用效果不进入运行模型但仍必须完整合法。
    def test_skill_source_changes_identity_and_disabled_validation(self):
        effect = next(key for key, row in self.tables["SkillEffect"].rows.items()
                      if row["SkillIdx"] == 4)
        first, identity = gameplay.artifacts(self.tables)
        self.assertEqual(json.loads(first)["skills"]["4"]["effects"][0]["value"], 1)
        self.tables["SkillEffect"].rows[effect]["Value"] = 2
        second, changed = gameplay.artifacts(self.tables)
        self.assertNotEqual(first, second)
        self.assertNotEqual(identity, changed)
        disabled = next(key for key, row in self.tables["SkillEffect"].rows.items()
                        if row["SkillIdx"] == 3)
        self.tables["SkillEffect"].rows[disabled]["Value"] = 8000
        self.assertEqual((second, changed), gameplay.artifacts(self.tables))
        self.tables["SkillEffect"].rows[disabled]["AttributeId"] = 2147483647
        with self.assertRaisesRegex(ValueError, "unknown skill attribute"):
            gameplay.artifacts(self.tables)
        self.tables["SkillEffect"].rows[disabled]["AttributeId"] = 1
        self.tables["Skill"].rows[3]["Enabled"] = 1
        with self.assertRaisesRegex(ValueError, "unsupported player skill"):
            gameplay.artifacts(self.tables)
        self.tables["Skill"].rows[3]["Enabled"] = 0
        self.tables["SkillEffect"].rows.pop(effect)
        with self.assertRaisesRegex(ValueError, "effects are required"):
            gameplay.artifacts(self.tables)

    # 成长阈值和每种非召唤怪奖励必须完整、单调且非负。
    def test_career_fixture_requires_complete_rewards(self):
        doc = json.loads(gameplay.artifacts(self.tables)[0])
        rewards = {key: 10 for key, cfg in doc["monsters"].items() if cfg["rank"] != 4}
        doc["career"] = {"levels": [0, 100, 300], "kill_xp": rewards,
                         "extract_xp": 30, "bounty_xp": 20, "bounty_currency": 100,
                         "retire_xp": 200}
        gameplay.content_artifacts(doc)
        for field, value in (("levels", [0, 100, 100]), ("kill_xp", {}),
                             ("retire_xp", -1)):
            changed = copy.deepcopy(doc)
            changed["career"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                gameplay.content_artifacts(changed)


if __name__ == "__main__":
    unittest.main()

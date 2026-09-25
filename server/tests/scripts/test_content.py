# 验证灰盒内容的严格来源、可复现身份、几何边界及失败时的制品保留。
import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("hunter_content", ROOT / "export/combat.py")
CONTENT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CONTENT)
SOURCE = ROOT / "design/combat_demo.json"


class ContentTest(unittest.TestCase):
    # 每个用例读取独立的正式配置副本，防止相互污染。
    def setUp(self):
        self.doc = json.loads(SOURCE.read_text(encoding="utf-8"))

    # 规范内容与生成头必须使用完全相同的字节与哈希，键顺序不改变身份。
    def test_canonical_identity(self):
        data, header = CONTENT.artifacts(self.doc)
        expected = b"combat-v3:" + hashlib.sha256(data).hexdigest().encode("ascii")
        self.assertIn(expected, header)
        self.assertIn(b'R"CONTENT(' + data + b')CONTENT"', header)
        self.assertNotIn(b"\n", data)
        self.assertEqual(json.loads(data), self.doc)
        reordered = dict(reversed(list(self.doc.items())))
        self.assertEqual(CONTENT.artifacts(reordered), (data, header))
        reordered["weapons"]["1"] = dict(reordered["weapons"]["1"], damage=21)
        self.assertNotEqual(CONTENT.artifacts(reordered), (data, header))

    # 未知字段、缺失字段、布尔、浮点、越界和非固定步长均拒绝。
    def test_exact_fields_and_numbers(self):
        cases = [("v", True), ("v", 1), ("v", 2.0), ("tick_hz", 30), ("extra", 1)]
        for key, value in cases:
            doc = copy.deepcopy(self.doc)
            doc[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        for group, key, value in (("player", "width", 601), ("player", "hp", 0),
                                  ("enemy", "speed", 0),
                                  ("weapon", "magazine", 1001), ("weapon", "reserve", -1),
                                  ("weapon", "fire_ticks", 0), ("weapon", "damage", 1.0),
                                  ("enemy", "attack_range", 6001)):
            doc = copy.deepcopy(self.doc)
            doc[{"player": "players", "enemy": "monsters", "weapon": "weapons"}[group]]["1"][key] = value
            with self.subTest(group=group, key=key), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        del self.doc["players"]["1"]["hp"]
        with self.assertRaises(ValueError):
            CONTENT.validate(self.doc)

    # 地图矩形不能越界或互相穿透，零尺寸也不能作为隐式平台。
    def test_solid_bounds_and_overlap(self):
        for key, value in (("x", -1), ("x", 23999), ("w", 0), ("y", 9999)):
            doc = copy.deepcopy(self.doc)
            doc["map"]["solids"][0][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        self.doc["map"]["solids"][1].update(x=5001, y=1201)
        with self.assertRaises(ValueError):
            CONTENT.validate(self.doc)

    # 出生标识只在出生记录内唯一，拒绝数值别名和越界。
    def test_duplicate_and_invalid_ids(self):
        for value in ("2", "02", "0", 4, "2147483648"):
            doc = copy.deepcopy(self.doc)
            doc["map"]["enemies"][1]["spawn_id"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        self.doc["map"]["solids"][1]["id"] = self.doc["map"]["solids"][0]["id"]
        with self.assertRaises(ValueError):
            CONTENT.validate(self.doc)

    # 出生检查使用完整角色矩形，边缘接触合法，悬空和穿透均失败。
    def test_spawn_geometry(self):
        for pos in ({"x": 0, "y": 0}, {"x": 2000, "y": 1},
                    {"x": 5500, "y": 1400}, {"x": 2000, "y": 10000},
                    {"x": 12000, "y": 0}):
            doc = copy.deepcopy(self.doc)
            doc["map"]["spawn"].update(pos)
            with self.subTest(pos=pos), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        self.doc["map"]["spawn"].update(x=5500, y=1500)
        CONTENT.validate(self.doc)

    # 巡逻区必须包含出生点、有宽度且不能越界、穿墙或经过没有支撑的空隙。
    def test_patrol_geometry(self):
        for minimum, maximum in ((13000, 14000), (12000, 12000), (0, 14000),
                                 (11000, 16000), (11000, 25000)):
            doc = copy.deepcopy(self.doc)
            doc["map"]["enemies"][0].update(patrol_min=minimum, patrol_max=maximum)
            with self.subTest(minimum=minimum, maximum=maximum), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        self.doc["map"]["enemies"][0].update(x=6000, y=1500,
                                               patrol_min=5500, patrol_max=6800)
        CONTENT.validate(self.doc)
        self.doc["map"]["enemies"][0]["patrol_max"] = 7200
        with self.assertRaises(ValueError):
            CONTENT.validate(self.doc)

    # 集合数量直接受内容预算限制，空怪物列表不构成基础战斗切片。
    def test_collection_limits(self):
        for key, values in (("solids", [None] * 129), ("enemies", []),
                            ("enemies", [None] * 33), ("solids", {})):
            doc = copy.deepcopy(self.doc)
            doc["map"][key] = values
            with self.subTest(key=key, count=len(values)), self.assertRaises(ValueError):
                CONTENT.validate(doc)

    # 配置引用按各自命名空间解析，出生几何必须使用实际引用的角色体型。
    def test_cfg_references_and_shapes(self):
        doc = copy.deepcopy(self.doc)
        doc["monsters"]["2"] = dict(doc["monsters"]["1"], width=2400, hp=120)
        doc["map"]["enemies"][1]["cfg_id"] = "2"
        CONTENT.validate(doc)
        doc["map"]["enemies"][1]["patrol_max"] = 23000
        with self.assertRaises(ValueError):
            CONTENT.validate(doc)
        for path in (("map", "spawn", "cfg_id"), ("map", "spawn", "weapon_cfg_id"),
                     ("map", "enemies", 0, "cfg_id")):
            doc = copy.deepcopy(self.doc)
            node = doc
            for key in path[:-1]:
                node = node[key]
            node[path[-1]] = "99"
            with self.subTest(path=path), self.assertRaises(ValueError):
                CONTENT.validate(doc)
        for group in ("players", "monsters", "weapons"):
            for key in ("01", "0", "2147483648"):
                doc = copy.deepcopy(self.doc)
                doc[group][key] = doc[group].pop("1")
                with self.subTest(group=group, key=key), self.assertRaises(ValueError):
                    CONTENT.validate(doc)
        doc = copy.deepcopy(self.doc)
        doc["map"]["enemies"][0]["spawn_id"] = "101"
        CONTENT.validate(doc)
        doc["map"]["gravity"] = True
        with self.assertRaises(ValueError):
            CONTENT.validate(doc)

    # 成功导出使用正式来源，且输出 JSON 可以再次严格校验。
    def test_export(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            output, header = folder / "content.json", folder / "ContentSpec.h"
            CONTENT.export(SOURCE, output, header)
            self.assertEqual((output.read_bytes(), header.read_bytes()),
                             CONTENT.artifacts(self.doc))
            CONTENT.validate(json.loads(output.read_bytes()))
            self.assertEqual(set(folder.iterdir()), {output, header})

    # 格式歧义与非法内容在发布前失败，已存在的两个制品逐字节保持不变。
    def test_invalid_source_preserves_outputs(self):
        for text in ('{"v":1,"v":1}', "{}", "[", " " * (CONTENT.MAX_BYTES + 1)):
            with self.subTest(text=text[:20]), tempfile.TemporaryDirectory() as temp:
                folder = Path(temp)
                source = folder / "source.json"
                output, header = folder / "content.json", folder / "ContentSpec.h"
                source.write_text(text, encoding="utf-8")
                output.write_bytes(b"old-json")
                header.write_bytes(b"old-header")
                with self.assertRaises(ValueError):
                    CONTENT.export(source, output, header)
                self.assertEqual(output.read_bytes(), b"old-json")
                self.assertEqual(header.read_bytes(), b"old-header")

    # 第二项发布失败必须回滚已替换的第一项，不能留下身份与内容不同步的制品。
    def test_publish_failure_rolls_back_pair(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            output, header = folder / "content.json", folder / "ContentSpec.h"
            output.write_bytes(b"old-json")
            header.write_bytes(b"old-header")
            replace = CONTENT.PUBLISH.os.replace

            # 只拒绝新头文件提交，保留回滚所需的原子替换能力。
            def fail_header(source, target):
                if Path(source).name == "prepared" and Path(target) == header:
                    raise OSError("injected header publication failure")
                replace(source, target)

            with mock.patch.object(CONTENT.PUBLISH.os, "replace", side_effect=fail_header):
                with self.assertRaises(OSError):
                    CONTENT.export(SOURCE, output, header)
            self.assertEqual(output.read_bytes(), b"old-json")
            self.assertEqual(header.read_bytes(), b"old-header")
            self.assertEqual(set(folder.iterdir()), {output, header})

    # 暂存写失败发生在任何目标替换之前，旧制品仍然可用。
    def test_stage_failure_preserves_outputs(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            output, header = folder / "content.json", folder / "ContentSpec.h"
            output.write_bytes(b"old-json")
            header.write_bytes(b"old-header")
            with mock.patch.object(CONTENT.PUBLISH.os, "fsync", side_effect=OSError("injected")):
                with self.assertRaises(OSError):
                    CONTENT.export(SOURCE, output, header)
            self.assertEqual(output.read_bytes(), b"old-json")
            self.assertEqual(header.read_bytes(), b"old-header")

    # 输入和两个输出路径不能互相覆盖，即便路径书写不同也按解析后的路径比较。
    def test_path_aliases(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "source.json"
            source.write_bytes(SOURCE.read_bytes())
            before = source.read_bytes()
            with self.assertRaises(ValueError):
                CONTENT.export(source, source, Path(temp) / "ContentSpec.h")
            self.assertEqual(source.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()

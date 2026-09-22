# 验证唯一契约控制桥接字段、精度和容器边界，并在生成失败时保留旧产物。
import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("gen_schema", ROOT / "tools" / "gen_schema.py")
SCHEMA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SCHEMA)


class SchemaTest(unittest.TestCase):
    # 每个用例读取独立契约副本，防止变异泄漏到其他验证。
    def setUp(self):
        self.doc = json.loads((ROOT / "lua" / "contract.json").read_text(encoding="utf-8"))

    # 输出消息及实体的全部字段和 ID 极值来自唯一正式描述。
    def test_current_contract(self):
        header = SCHEMA.generate(self.doc)
        self.assertIn("version = 3", header)
        self.assertIn('"max":"18446744073709551615"', header)
        self.assertIn('"max":"9223372036854775807"', header)
        self.assertIn('"max":64,"type":"array"', header)
        self.assertIn('"reload_ticks"', header)
        self.assertIn('"cfg_id":{"max":"2147483647","min":"1","type":"id"}', header)
        for kind in SCHEMA.KINDS:
            self.assertIn('"' + kind + '":', header)

    # 合法收紧边界反映到生成值，不得静默使用旧固定上限。
    def test_changed_limits(self):
        schemas = self.doc["effect"]["schemas"]
        schemas["snapshot"]["fields"]["entities"]["max"] = 16
        schemas["error"]["fields"]["detail"]["max"] = 32
        header = SCHEMA.generate(self.doc)
        self.assertIn('"max":16,"type":"array"', header)
        self.assertIn('"detail":{"max":32,"min":0,"type":"string"}', header)

    # 未知类型、非法范围和无法映射到冻结消息的字段必须在构建阶段失败。
    def test_unsupported_schema(self):
        mutations = [
            (("effect", "schemas", "ack", "fields", "seq", "max"), "18446744073709551616"),
            (("effect", "schemas", "ack", "fields", "seq", "min"), "01"),
            (("effect", "schemas", "ack", "fields", "applied_tick", "max"),
             "9223372036854775808"),
            (("effect", "schemas", "ack", "fields", "v", "max"), 4),
            (("effect", "schemas", "ack", "fields", "v", "min"), True),
            (("effect", "schemas", "ack", "fields", "seq"), {"type": "bool"}),
            (("effect", "schemas", "snapshot", "fields", "entities", "max"), 65),
            (("effect", "schemas", "snapshot", "fields", "entities", "max"), True),
            (("effect", "schemas", "error", "fields", "detail", "min"), -1),
            (("effect", "schemas", "event", "fields", "x", "max"), 2147483648),
            (("effect", "schemas", "event", "fields", "x", "min"), 2147483648),
            (("effect", "schemas", "login", "fields", "req_id"), {"type": "bool"}),
            (("effect", "schemas", "start", "fields", "phase", "values"), ["Playing", "Playing"]),
            (("effect", "schemas", "snapshot", "fields", "entities", "item", "fields",
              "cfg_id", "max"), "2147483648"),
            (("state", "v"), 2),
            (("state", "v"), 3.0),
            (("host_api", "ctx", "v"), 2),
            (("effect", "input", "v"), 2),
            (("effect", "kinds"), ["ack"]),
            (("effect", "v"), True),
            (("version",), 1),
        ]
        for path, value in mutations:
            doc = copy.deepcopy(self.doc)
            node = doc
            for key in path[:-1]:
                node = node[key]
            node[path[-1]] = value
            with self.subTest(path=path), self.assertRaises(ValueError):
                SCHEMA.generate(doc)

    # 遗漏、额外字段及没有上限的实体容器不可进入编译产物。
    def test_invalid_shapes(self):
        for field in ("seq", "match_id", "applied_tick"):
            doc = copy.deepcopy(self.doc)
            del doc["effect"]["schemas"]["ack"]["fields"][field]
            with self.subTest(field=field), self.assertRaises(ValueError):
                SCHEMA.generate(doc)
        self.doc["effect"]["schemas"]["ack"]["fields"]["extra"] = {"type": "bool"}
        with self.assertRaises(ValueError):
            SCHEMA.generate(self.doc)
        for key in ("version", "state", "effect"):
            doc = json.loads((ROOT / "lua" / "contract.json").read_text(encoding="utf-8"))
            del doc[key]
            with self.subTest(key=key), self.assertRaises(ValueError):
                SCHEMA.generate(doc)

    # 准备或替换失败保留原头文件，并移除本次创建的暂存文件。
    def test_write_failure_preserves_header(self):
        for operation in ("fsync", "replace"):
            with self.subTest(operation=operation), tempfile.TemporaryDirectory() as temp:
                folder = Path(temp)
                output = folder / "SchemaSpec.h"
                output.write_bytes(b"previous-header")
                with mock.patch.object(SCHEMA.os, operation, side_effect=OSError("injected")):
                    with self.assertRaises(OSError):
                        SCHEMA.write_header(output, SCHEMA.generate(self.doc))
                self.assertEqual(output.read_bytes(), b"previous-header")
                self.assertEqual(list(folder.iterdir()), [output])

    # 非法契约在命令行入口返回失败，不能覆盖已经生成的合法头文件。
    def test_invalid_contract_preserves_header(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            contract = folder / "contract.json"
            output = folder / "SchemaSpec.h"
            output.write_bytes(b"previous-header")
            self.doc["version"] = 1
            contract.write_text(json.dumps(self.doc), encoding="utf-8")
            result = subprocess.run([sys.executable, str(ROOT / "tools" / "gen_schema.py"),
                                     "--contract", str(contract), "--output", str(output)],
                                    capture_output=True, check=False, timeout=10)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output.read_bytes(), b"previous-header")
            self.assertEqual(set(folder.iterdir()), {contract, output})


if __name__ == "__main__":
    unittest.main()

# 验证正式契约驱动原生输出校验，拒绝无法表达或互相矛盾的 schema。
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

    # 原生常量与字段集合应由正式描述确定，而不是生成器中的默认值。
    def test_current_contract(self):
        header = SCHEMA.generate(self.doc)
        self.assertIn('min_count = -1000000000LL', header)
        self.assertIn('max_count = 1000000000LL', header)
        self.assertIn('max_tick = 9223372036854775807ULL', header)
        self.assertIn('max_seq = 18446744073709551615ULL', header)
        self.assertIn('min_ack_seq = 1ULL', header)
        self.assertIn('ack_fields{"count", "seq", "v"}', header)
        self.assertIn('snapshot_fields{"count", "seq", "tick_id", "v"}', header)

    # 合法边界变更必须改变生成值，不能继续使用旧的固定常量。
    def test_changed_limits(self):
        self.doc["state"]["count"] = "integer [-7,19]"
        self.doc["state"]["tick_id"] = "canonical decimal string [0,123]"
        header = SCHEMA.generate(self.doc)
        self.assertIn('min_count = -7LL', header)
        self.assertIn('max_count = 19LL', header)
        self.assertIn('max_tick = 123ULL', header)
        self.assertNotIn('1000000000', header)

    # 字段或值域不能编码到当前桥接时，在构建前明确失败。
    def test_unsupported_schema(self):
        cases = [
            ("state", "count", "integer [9,1]"),
            ("state", "count", "integer [-9223372036854775809,1]"),
            ("state", "tick_id", "canonical decimal string [0,9223372036854775808]"),
            ("state", "seq", "canonical decimal string [0,18446744073709551616]"),
            ("state", "extra_fields", True),
            ("effect", "snapshot", "arbitrary table"),
            ("effect", "kinds", ["ack", "snapshot", "unknown"]),
            ("effect", "v", 2),
        ]
        for group, key, value in cases:
            doc = copy.deepcopy(self.doc)
            doc[group][key] = value
            with self.subTest(group=group, key=key), self.assertRaises(ValueError):
                SCHEMA.generate(doc)
        self.doc["effect"]["ack"]["extra"] = "integer"
        with self.assertRaises(ValueError):
            SCHEMA.generate(self.doc)

    # JSON 的 bool 不能代替版本整数，描述缺失也不能回退到宽松默认值。
    def test_invalid_types(self):
        for key in ("version", "state", "effect"):
            doc = copy.deepcopy(self.doc)
            doc.pop(key)
            with self.subTest(key=key), self.assertRaises(ValueError):
                SCHEMA.generate(doc)
        self.doc["effect"]["ack"]["v"] = True
        with self.assertRaises(ValueError):
            SCHEMA.generate(self.doc)

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
            self.doc["version"] = 2
            contract.write_text(json.dumps(self.doc), encoding="utf-8")
            result = subprocess.run([sys.executable, str(ROOT / "tools" / "gen_schema.py"),
                                     "--contract", str(contract), "--output", str(output)],
                                    capture_output=True, check=False, timeout=10)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output.read_bytes(), b"previous-header")
            self.assertEqual(set(folder.iterdir()), {contract, output})


if __name__ == "__main__":
    unittest.main()

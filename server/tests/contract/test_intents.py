# 验证契约检查器会拒绝虚假目标、缺失依赖与索引错误。
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "tools" / "verify_intents.py"
SPEC = importlib.util.spec_from_file_location("verify_intents", SCRIPT)
CHECK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECK)


class IntentTest(unittest.TestCase):
    # 在临时目录建立独立契约样例，不修改正式契约。
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.folder = self.root / "intents"
        self.folder.mkdir()
        self.write_intent("SRV-001", "one", [])
        (self.folder / "README.md").write_text("[一](one.intent.md)", encoding="utf-8")

    # 写入具备必需章节和注册入口的受控样例。
    def write_intent(self, name, stem, deps, verification=None):
        import json
        verification = ["test"] if verification is None else verification
        text = (f"---\nid: {name}\nstatus: active\ntarget: [\"core\"]\n"
                f"depends_on: {json.dumps(deps)}\nverification: {json.dumps(verification)}\n---\n")
        text += "\n".join("## " + h for h in
                          ["目标", "不变量", "线程", "接口", "失败", "验证"])
        (self.folder / (stem + ".intent.md")).write_text(text, encoding="utf-8")

    # 真实目标和测试存在时允许 active 契约。
    def test_valid(self):
        self.assertEqual(CHECK.verify(self.root, ["core"], ["test"]), 1)

    # 没有注册的目标和测试不能被声明为 active 能力。
    def test_missing_entry(self):
        for targets, tests in [([], ["test"]), (["core"], [])]:
            with self.assertRaises(ValueError):
                CHECK.verify(self.root, targets, tests)

    # 缺失依赖与依赖环必须失败。
    def test_deps(self):
        for deps in [["SRV-002"], ["SRV-001"]]:
            self.write_intent("SRV-001", "one", deps)
            with self.assertRaises(ValueError):
                CHECK.verify(self.root, ["core"], ["test"])

    # 各模式都核对实际适用的入口，不把开发专属测试误要求到生产构建。
    def test_conditional_verification(self):
        self.write_intent("SRV-001", "one", [], ["test", "dev:async", "tools:bundle"])
        self.assertEqual(CHECK.verify(self.root, ["core"], ["test", "async", "bundle"],
                                     mode="development", build_tools=True), 1)
        self.assertEqual(CHECK.verify(self.root, ["core"], ["test"],
                                     mode="production", build_tools=False), 1)
        for mode, build_tools, tests in (("development", False, ["test"]),
                                         ("production", True, ["test"])):
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                CHECK.verify(self.root, ["core"], tests, mode=mode, build_tools=build_tools)

    # 未知前缀和在当前模式没有任何验证入口的 active 契约都必须失败。
    def test_invalid_verification_mode(self):
        for entries in (["test", "future:missing"], ["dev:async"], ["tools:"]):
            self.write_intent("SRV-001", "one", [], entries)
            with self.subTest(entries=entries), self.assertRaises(ValueError):
                CHECK.verify(self.root, ["core"], ["test"], mode="production", build_tools=False)

    # 遗漏索引和重复 ID 必须失败。
    def test_index_and_duplicate(self):
        (self.folder / "README.md").write_text("", encoding="utf-8")
        with self.assertRaises(ValueError):
            CHECK.verify(self.root, ["core"], ["test"])
        self.write_intent("SRV-001", "two", [])
        (self.folder / "README.md").write_text(
            "[一](one.intent.md) [二](two.intent.md)", encoding="utf-8")
        with self.assertRaises(ValueError):
            CHECK.verify(self.root, ["core"], ["test"])


if __name__ == "__main__":
    unittest.main()

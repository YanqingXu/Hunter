# 验证正式来源仅为根 Excel、所有服务端记录必须校验，JSON 不再选择生产数据。
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

from test_export import cell, cells, write_book


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "export"))
import gameplay


class GameplaySourceTest(unittest.TestCase):
    # 用临时 OOXML 工作簿验证来源边界，不改写当前生产数值。
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="hunter-root-source-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "design"

    # 新表无需 Python 白名单；空服务端表与全部数据行均进入生成模块。
    def test_all_root_server_tables_and_rows(self):
        write_book(self.source / "技能表.xlsx", [
            ("技能|Skill", cells([[2, "二", "", 2], [1, "一", "", 1]])),
            ("属性|Attribute", cells()), ("说明", {"A1": cell("无需四行表头")})])
        write_book(self.source / "来源.XLSX", [
            ("服务端|Server", cells()), ("道具刷新|ItemSpawn", cells([])),
            ("玩家技能|PlayerSkill", cells())])
        client = cells()
        for column in "ABCD":
            client[column + "4"] = cell("client")
        write_book(self.source / "表现.xlsx", [("表现|Client", client)])
        (self.source / "~$打开.xlsx").write_bytes(b"Office temporary file")
        (self.source / "demo_sources.json").write_text("obsolete and invalid", encoding="utf-8")
        nested = self.source / "archive"
        nested.mkdir()
        (nested / "旧表.xlsx").write_bytes(b"nested draft must not be read")
        tables = gameplay.read_tables(self.source)
        self.assertEqual(set(tables), {"Skill", "Attribute", "Server", "ItemSpawn", "PlayerSkill"})
        self.assertEqual(set(tables["Skill"].rows), {1, 2})
        self.assertEqual(tables["ItemSpawn"].rows, {})
        files = gameplay.config_files(tables)
        manifest = json.loads(files["Manifest.json"])
        self.assertEqual(sum(row["kind"] == "data" for row in manifest["modules"]), 5)
        self.assertIn(b"[1]", files["Skill.lua"])
        self.assertIn(b"[2]", files["Skill.lua"])
        self.assertEqual(files["ItemSpawn.lua"], b"return {}\n")

    # 以井号开头的主键说明行跳过，未填写主键的实际数据和后续非法记录不能被隐藏。
    def test_comments_and_every_data_row(self):
        values = cells([[1, "一", "", 1], ["#说明", "分组说明", "", "不是配置"],
                        [2, "二", "", 2]])
        write_book(self.source / "道具.xlsx", [("道具|Item", values)])
        self.assertEqual(set(gameplay.read_tables(self.source)["Item"].rows), {1, 2})
        values["D7"] = cell(1.5, "n")
        write_book(self.source / "道具.xlsx", [("道具|Item", values)])
        with self.assertRaisesRegex(gameplay.sheets.ConfigError, "D7"):
            gameplay.read_tables(self.source)
        values["D7"] = cell(2)
        values["A7"] = cell("")
        write_book(self.source / "道具.xlsx", [("道具|Item", values)])
        with self.assertRaisesRegex(gameplay.sheets.ConfigError, "A7"):
            gameplay.read_tables(self.source)

    # 根工作簿损坏及大小写表名冲突必须在调用玩法校验和发布之前拒绝。
    def test_invalid_root_and_duplicate_tables(self):
        write_book(self.source / "甲.xlsx", [("道具|Item", cells())])
        write_book(self.source / "乙.xlsx", [("重复|item", cells())])
        with self.assertRaisesRegex(gameplay.sheets.ConfigError, "输出文件名冲突"):
            gameplay.read_tables(self.source)
        (self.source / "乙.xlsx").write_bytes(b"broken root workbook")
        with self.assertRaisesRegex(gameplay.sheets.ConfigError, "无法读取 Excel"):
            gameplay.read_tables(self.source)

    # source 只接受目录，旧生产 JSON 文件不能悄悄继续决定记录范围。
    def test_json_source_is_rejected(self):
        self.source.mkdir()
        legacy = self.source / "demo_sources.json"
        legacy.write_text('{"v":1,"sources":[]}', encoding="utf-8")
        with self.assertRaisesRegex(gameplay.sheets.ConfigError, "不是目录"):
            gameplay.read_tables(legacy)

    # 内部聚合与夹具名字不可被生产工作表覆盖，避免错误地切换来源入口。
    def test_internal_table_names_are_reserved(self):
        for name in ("Tables", "Fixture"):
            with self.subTest(name=name):
                write_book(self.source / "内部.xlsx", [("冲突|" + name, cells())])
                with self.assertRaisesRegex(gameplay.sheets.ConfigError, "内部聚合或测试夹具"):
                    gameplay.read_tables(self.source)

    # 夹具入口不扫描生产目录且仍通过同一份 Lua 语义校验。
    def test_explicit_fixture_remains_isolated(self):
        fixture = self.root / "fixture.json"
        fixture.write_text('{"v":6}', encoding="utf-8")
        with mock.patch.object(gameplay, "read_tables") as read, mock.patch.object(
                gameplay, "evaluate", return_value={"v": 6}) as validate:
            gameplay.export_content(fixture=fixture, check=True)
        read.assert_not_called()
        validate.assert_called_once()
        self.assertIn("Fixture.lua", validate.call_args.args[0])
        self.assertEqual(list(self.root.iterdir()), [fixture])

    # 目录内容先全部通过语义门禁，失败后保持上一份制品且不会新增输出。
    def test_validation_failure_preserves_artifacts(self):
        write_book(self.source / "来源.xlsx", [("服务端|Server", cells())])
        output = self.root / "generated"
        output.mkdir()
        (output / "content.json").write_bytes(b"previous content")
        with mock.patch.object(gameplay, "evaluate", side_effect=ValueError("Lua rejected source")):
            with self.assertRaisesRegex(ValueError, "Lua rejected"):
                gameplay.export_content(source=self.source, output=output / "content.json",
                    header=output / "ContentId.h", cfg=output / "cfg")
        self.assertEqual((output / "content.json").read_bytes(), b"previous content")
        self.assertEqual({path.name for path in output.iterdir()}, {"content.json"})


if __name__ == "__main__":
    unittest.main()

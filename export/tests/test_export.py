# 验证四行表头读取、服务端字段筛选、Lua 字节语义及发布失败时保留原输出。
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
from zipfile import ZipFile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("hunter_excel_export", ROOT / "export/export.py")
EXPORT = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = EXPORT
SPEC.loader.exec_module(EXPORT)
NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PKG = "http://schemas.openxmlformats.org/package/2006/relationships"


# 测试夹具直接写最小 OOXML 包，不依赖 Excel 或第三方 Python 库。
def cell(value, kind=None, formula=None):
    return {"value": value, "kind": kind or ("n" if type(value) is int else "inlineStr"),
            "formula": formula}


def cells(rows=None):
    headers = [
        ["编号", "名称", "客户端资源", "数量"],
        ["int32", "string", "string", "int32"],
        ["Id", "Name", "Icon", "Count"],
        ["server/client/key", "client/server", "client/index", "server"],
    ]
    result = {}
    for number, values in enumerate(headers + (rows if rows is not None else [[1, "物品", "icon", 0]]), 1):
        for column, value in enumerate(values, 1):
            if value is not None:
                result[f"{EXPORT.column_name(column)}{number}"] = cell(value)
    return result


# 文件序号刻意不等于 sheetId，且共享字符串使用父级相对关系，覆盖真实关系解析。
def write_book(path, sheets=None, shared=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    workbook = ET.Element(f"{{{NS}}}workbook")
    entries = ET.SubElement(workbook, f"{{{NS}}}sheets")
    rels = ET.Element(f"{{{PKG}}}Relationships")
    with ZipFile(path, "w") as archive:
        for index, (name, values) in enumerate(sheets or [("测试|Test", cells())], 1):
            identifier = f"link{index}"
            ET.SubElement(entries, f"{{{NS}}}sheet", name=name, sheetId=str(index + 40),
                          attrib={f"{{{REL}}}id": identifier})
            target = f"worksheets/data{index + 7}.xml"
            ET.SubElement(rels, f"{{{PKG}}}Relationship", Id=identifier, Type=REL + "/worksheet", Target=target)
            root = ET.Element(f"{{{NS}}}worksheet")
            body = ET.SubElement(root, f"{{{NS}}}sheetData")
            rows = {}
            for address, item in values.items():
                number = EXPORT.ADDRESS.fullmatch(address)[2]
                if number not in rows:
                    rows[number] = ET.SubElement(body, f"{{{NS}}}row", r=number)
                entry = ET.SubElement(rows[number], f"{{{NS}}}c", r=address, t=item["kind"])
                if item["formula"] is not None:
                    ET.SubElement(entry, f"{{{NS}}}f").text = item["formula"]
                value = str(item["value"])
                if item["kind"] == "inlineStr":
                    ET.SubElement(ET.SubElement(entry, f"{{{NS}}}is"), f"{{{NS}}}t").text = value
                else:
                    ET.SubElement(entry, f"{{{NS}}}v").text = value
            archive.writestr("xl/" + target, ET.tostring(root, encoding="utf-8"))
        if shared is not None:
            ET.SubElement(rels, f"{{{PKG}}}Relationship", Id="strings", Type=REL + "/sharedStrings",
                          Target="../strings.xml")
            archive.writestr("strings.xml", f'<sst xmlns="{NS}">{shared}</sst>')
        archive.writestr("xl/workbook.xml", ET.tostring(workbook, encoding="utf-8"))
        archive.writestr("xl/_rels/workbook.xml.rels", ET.tostring(rels, encoding="utf-8"))


class ExportTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="hunter-export-test-")
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.source = self.base / "source"
        self.book = self.source / "book.xlsx"
        self.output = self.base / "out"

    def load(self, values=None, name="测试|Test"):
        write_book(self.book, [(name, values if values is not None else cells())])
        return EXPORT.collect_tables(self.source)

    # 共享、内联、缓存字符串以及科学计数数值都按其真实类型读取。
    def test_cell_storage_and_relationships(self):
        values = cells()
        values["A5"] = cell("0", "s")
        values["B5"] = cell("1", "s")
        values["D5"] = cell("1.00E+2", "n")
        shared = '<si><t>3</t></si><si><r><t>红</t></r><r><t>钻</t></r><rPh sb="0" eb="1"><t>hong</t></rPh></si>'
        write_book(self.book, [("测试|Test", values)], shared)
        table = EXPORT.collect_tables(self.source)[0]
        self.assertEqual(table.rows, {3: {"Id": 3, "Name": "红钻", "Count": 100}})
        values["A5"] = cell("4", "str")
        values["B5"] = cell("空 格", "str")
        self.assertEqual(self.load(values)[0].rows[4]["Name"], "空 格")

    # 解码 OOXML 转义只做一次，并支持转义后的代理对与控制字符。
    def test_ooxml_escapes(self):
        values = cells()
        values["B5"] = cell("_x0000_9_x000A__x005F_x0041__xD83D__xDE00_")
        table = self.load(values)[0]
        self.assertEqual(table.rows[1]["Name"], "\0" + "9\n_x0041_😀")
        values["B5"] = cell("_xD800_")
        with self.assertRaisesRegex(EXPORT.ConfigError, "B5.*Unicode"):
            self.load(values)

    # 仅服务器字段参与转换；模板右侧说明和 Office 锁文件不参与导出。
    def test_filtering_recursive_scan_and_notes(self):
        values = cells()
        values["C2"] = cell("ClientAsset")
        values["C5"] = cell("#REF!", "e", "BAD()")
        values["F1"] = cell("待接入")
        values["F5"] = cell("说明")
        values["F500"] = cell("另一段说明")
        client = cells()
        for col in "ABCD":
            client[col + "4"] = cell("client")
        write_book(self.source / "sub" / "upper.XLSX", [
            ("测试|Test", values), ("说明", {"A1": cell("正文")}), ("客户端|Client", client)])
        (self.source / "~$broken.xlsx").write_bytes(b"not a workbook")
        tables = EXPORT.export_config(self.source, self.output)
        self.assertEqual([table.name for table in tables], ["Test"])
        self.assertEqual(tables[0].rows[1], {"Id": 1, "Name": "物品", "Count": 0})
        self.assertNotIn(b"Icon", (self.output / "Test.lua").read_bytes())

    def test_empty_rows_empty_strings_and_empty_table(self):
        values = cells([[2, None, None, 0], [], [1, "", None, -2147483648]])
        table = self.load(values)[0]
        self.assertEqual(set(table.rows), {1, 2})
        self.assertEqual(table.rows[2]["Name"], "")
        data = EXPORT.render_lua(table)
        self.assertLess(data.index(b"[1]"), data.index(b"[2]"))
        self.assertEqual(EXPORT.render_lua(self.load(cells([]))[0]), b"return {}\n")
        values["C6"] = cell("only client data")
        with self.assertRaisesRegex(EXPORT.ConfigError, "A6"):
            self.load(values)

    def test_invalid_ints_formulas_and_excel_errors(self):
        bad = [cell(""), cell("1.2", "n"), cell(2147483648), cell(-2147483649),
               cell("1", "b"), cell("01"), cell(" 1"), cell("1.0"), cell("1e2"),
               cell("NaN", "n"), cell("Infinity", "n"), cell("1_000", "n"),
               cell("2026-01-01", "d"), cell("#DIV/0!", "e"), cell(2, formula="1+1")]
        for value in bad:
            with self.subTest(value=value):
                values = cells()
                values["D5"] = value
                with self.assertRaisesRegex(EXPORT.ConfigError, r"book.xlsx \[测试\|Test\]!D5:"):
                    self.load(values)
        for value in (cell(4), cell("0", "b"), cell("#N/A", "e"), cell("cached", "str", '"cached"')):
            values = cells()
            values["B5"] = value
            with self.subTest(value=value), self.assertRaisesRegex(EXPORT.ConfigError, "B5"):
                self.load(values)

    def test_primary_keys(self):
        for value in (0, -1, None):
            with self.subTest(value=value), self.assertRaisesRegex(EXPORT.ConfigError, "A5"):
                self.load(cells([[value, "name", None, 0]]))
        with self.assertRaisesRegex(EXPORT.ConfigError, "A6.*第 5 行"):
            self.load(cells([[3, "a", None, 0], ["3", "b", None, 0]]))
        table = self.load(cells([[2147483647, "边界", None, 2147483647]]))[0]
        self.assertEqual(table.rows[2147483647]["Count"], 2147483647)

    def test_headers_and_key_definition(self):
        cases = [("A1", ""), ("A2", "float"), ("B3", "Id"), ("B3", "bad-name"),
                 ("A4", "server"), ("A4", "client/key"), ("B4", "server/key"),
                 ("B4", "serve"), ("B4", "server/server"), ("B4", "server/"),
                 ("A2", "string"), ("A4", "key")]
        for address, value in cases:
            with self.subTest(address=address, value=value):
                values = cells()
                values[address] = cell(value)
                with self.assertRaises(EXPORT.ConfigError):
                    self.load(values)
        values = cells()
        del values["B3"]
        with self.assertRaisesRegex(EXPORT.ConfigError, "B3"):
            self.load(values)
        values = cells()
        del values["D3"]
        with self.assertRaisesRegex(EXPORT.ConfigError, "D3"):
            self.load(values)
        values = cells()
        values["A3"] = cell("Id", "str", '"Id"')
        with self.assertRaisesRegex(EXPORT.ConfigError, "A3.*公式"):
            self.load(values)

    def test_invalid_and_colliding_names(self):
        for name in ("测试|../Escape", "测试|CON", "测试|Lpt9", "|Test", "测试|X|Y", "测试|bad-name"):
            with self.subTest(name=name), self.assertRaisesRegex(EXPORT.ConfigError, "A1"):
                self.load(name=name)
        write_book(self.book, [("甲|Test", cells())])
        write_book(self.source / "other.xlsx", [("乙|test", cells())])
        with self.assertRaisesRegex(EXPORT.ConfigError, "冲突"):
            EXPORT.export_config(self.source, self.output)
        self.assertFalse(self.output.exists())

    def test_invalid_packages_and_no_tables(self):
        self.source.mkdir()
        with self.assertRaisesRegex(EXPORT.ConfigError, "没有可导出"):
            EXPORT.collect_tables(self.source)
        self.book.write_bytes(b"not a zip")
        with self.assertRaisesRegex(EXPORT.ConfigError, "book.xlsx.*A1.*Excel"):
            EXPORT.collect_tables(self.source)
        write_book(self.book, [("说明", {"A1": cell("正文")})])
        with self.assertRaisesRegex(EXPORT.ConfigError, "没有可导出"):
            EXPORT.collect_tables(self.source)
        with self.assertRaisesRegex(EXPORT.ConfigError, "输入目录"):
            EXPORT.collect_tables(self.book)

    def test_shared_index_and_unsafe_relationship(self):
        values = cells()
        values["A5"] = cell("99", "s")
        write_book(self.book, [("测试|Test", values)], '<si><t>1</t></si>')
        with self.assertRaisesRegex(EXPORT.ConfigError, "A5.*共享"):
            EXPORT.collect_tables(self.source)
        for target, mode in (("../../outside.xml", "Internal"), ("https://example.com", "External")):
            rel = ET.Element("Relationship", Target=target, TargetMode=mode)
            with self.assertRaises(EXPORT.ConfigError):
                EXPORT.part_target("xl/workbook.xml", rel)

    def test_deterministic_output_and_check_has_no_writes(self):
        original = cells([[9, "后", None, 0], [1, "前", None, 1]])
        write_book(self.book, [("测试|Test", original)])
        EXPORT.export_config(self.source, self.output, check=True)
        self.assertFalse(self.output.exists())
        EXPORT.export_config(self.source, self.output)
        first = (self.output / "Test.lua").read_bytes()
        write_book(self.book, [("测试|Test", cells([[1, "前", None, 1], [9, "后", None, 0]]))])
        EXPORT.export_config(self.source, self.output)
        self.assertEqual((self.output / "Test.lua").read_bytes(), first)
        self.assertNotIn(b"\r", first)
        self.assertFalse(first.startswith(b"\xef\xbb\xbf"))
        self.assertNotIn(str(self.base).encode(), first)

    def test_validation_failure_preserves_all_files(self):
        self.output.mkdir()
        before = {"Test.lua": b"old test", "ZBad.lua": b"old bad", "manual.lua": b"manual"}
        for name, data in before.items():
            (self.output / name).write_bytes(data)
        bad = cells()
        bad["D5"] = cell(1, formula="1")
        write_book(self.book, [("正常|Test", cells()), ("错误|ZBad", bad)])
        with self.assertRaises(EXPORT.ConfigError):
            EXPORT.export_config(self.source, self.output)
        self.assertEqual({p.name: p.read_bytes() for p in self.output.iterdir()}, before)

    def test_publication_failure_rolls_back_and_retains_unrelated_files(self):
        write_book(self.book, [("甲|A", cells()), ("乙|B", cells())])
        self.output.mkdir()
        (self.output / "A.lua").write_bytes(b"old A")
        (self.output / "manual.lua").write_bytes(b"manual")
        real_replace = os.replace

        def fail_second(source, target):
            if Path(source).name == "prepared" and Path(target).name == "B.lua":
                raise PermissionError("simulated file lock")
            return real_replace(source, target)

        with mock.patch.object(EXPORT.PUBLISH.os, "replace", side_effect=fail_second):
            with self.assertRaisesRegex(PermissionError, "simulated"):
                EXPORT.export_config(self.source, self.output)
        self.assertEqual({p.name: p.read_bytes() for p in self.output.iterdir()},
                         {"A.lua": b"old A", "manual.lua": b"manual"})
        EXPORT.export_config(self.source, self.output)
        self.assertEqual((self.output / "manual.lua").read_bytes(), b"manual")

    def test_cli_paths_and_exit_status(self):
        self.assertEqual(EXPORT.repo_path("design"), ROOT / "design")
        self.assertEqual(EXPORT.repo_path(str(self.source)), self.source)
        write_book(self.book)
        run = subprocess.run([sys.executable, str(ROOT / "export/export.py"), "--check",
                              "--source", str(self.source), "--output", str(self.output)],
                             cwd=self.base, capture_output=True)
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertFalse(self.output.exists())
        errors = io.StringIO()
        with contextlib.redirect_stderr(errors):
            code = EXPORT.main(["--source", str(self.base / "missing")])
        self.assertEqual(code, 1)
        self.assertIn("导表失败", errors.getvalue())

    # 通用命令默认仅写草稿目录，不覆盖正式导表的同名文件。
    def test_default_cli_output_is_separate_draft_directory(self):
        write_book(self.book)
        production = self.base / "server/build/generated/cfg/Test.lua"
        production.parent.mkdir(parents=True)
        production.write_bytes(b"production config")
        with mock.patch.object(EXPORT, "ROOT", self.base), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(EXPORT.main(["--source", str(self.source)]), 0)
        self.assertTrue((self.base / "server/build/draft-cfg/Test.lua").is_file())
        self.assertEqual(production.read_bytes(), b"production config")


# 真实配置回归用例与引擎检查分开，使未安装 Lua 的机器也能运行通用测试。
class RepositoryTest(unittest.TestCase):
    # 通用导出用冻结工作簿回归；根表的显式生产选择由 gameplay 内容测试覆盖。
    def test_frozen_demo_tables(self):
        tables = EXPORT.collect_tables(ROOT / "design/demo")
        counts = {"Bag": 1, "Item": 3, "Equip": 1, "Map": 1, "MapSolid": 3,
                  "PlayerSpawn": 1, "MonsterSpawn": 3, "ExtractPoint": 1, "Player": 1,
                  "Monster": 2, "MonsterSkill": 2, "Weapon": 1, "Skill": 3, "Drop": 2, "DropEntry": 3}
        self.assertEqual({t.name: len(t.rows) for t in tables}, counts)
        self.assertEqual(len({t.sheet.path for t in tables}), 1)
        self.assertEqual(sum(len(t.rows) for t in tables), 28)
        by_name = {t.name: t for t in tables}
        self.assertEqual(by_name["Item"].rows[2800001]["Type"], 0)
        self.assertNotIn("IconId", by_name["Item"].rows[2800001])
        self.assertNotIn("Name", by_name["Equip"].rows[100000001])
        self.assertEqual(by_name["Skill"].rows[1]["Damage"], 20)
        self.assertEqual(by_name["Weapon"].rows[1]["Magazine"], 6)

    @unittest.skipUnless(shutil.which("lua"), "需要 Lua 5.1 进行加载及字符串字节校验")
    def test_lua_loads_all_records_and_escaped_strings(self):
        with tempfile.TemporaryDirectory(prefix="hunter-lua-test-") as folder:
            folder = Path(folder)
            tables = EXPORT.export_config(ROOT / "design/demo", folder)
            text = '中文😀"\\\n\r\t' + "".join(chr(i) for i in range(32)) + '\x7f9_x0041_"}; error("injected"); --'
            source = folder / "source"
            # 控制字符先按 OOXML 转义，保证测试输入是合法 XML。
            encoded = "".join(f"_x{ord(c):04X}_" if ord(c) < 32 else c for c in text.replace('_x0041_', '_x005F_x0041_'))
            values = cells([[1, encoded, None, 0]])
            values["B3"] = cell("end")
            write_book(source / "escape.xlsx", [("转义|Escape", values)])
            tables += EXPORT.export_config(source, folder)
            script = []
            for table in tables:
                script += [f'local t = assert(loadfile("{table.name}.lua"))()', "local count = 0",
                           'for k, v in pairs(t) do assert(type(k) == "number"); count = count + 1 end',
                           f"assert(count == {len(table.rows)})"]
                for identity, row in table.rows.items():
                    script += [f"assert(t[{identity}] ~= nil)", "local fields = 0",
                               f"for _ in pairs(t[{identity}]) do fields = fields + 1 end",
                               f"assert(fields == {len(row)})"]
                    for name, value in row.items():
                        expected = ("string.char(" + ",".join(str(b) for b in value.encode('utf-8')) + ")"
                                    if isinstance(value, str) else str(value))
                        script.append(f'assert(t[{identity}]["{name}"] == {expected})')
            script.append('assert(Escape == nil)')
            (folder / "verify.lua").write_text("\n".join(script), encoding="utf-8")
            run = subprocess.run([shutil.which("lua"), "verify.lua"], cwd=folder, capture_output=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            self.assertEqual(tables[-1].rows[1]["end"], text)

    def test_luax_strict_compiles_generated_files(self):
        compiler = os.environ.get("LUAXC") or shutil.which("luaxc")
        local = ROOT / "server/build/win-dev/_deps/luax-build/Release/luaxc.exe"
        if not compiler and local.is_file():
            compiler = str(local)
        if not compiler:
            self.skipTest("需要 luaxc；可通过 LUAXC 指定，未运行 strict 编译检查")
        with tempfile.TemporaryDirectory(prefix="hunter-luax-test-") as folder:
            folder = Path(folder)
            tables = EXPORT.export_config(ROOT / "design/demo", folder)
            for table in tables:
                with self.subTest(table=table.name):
                    run = subprocess.run([compiler, "--profile", "strict", "-o", str(folder / (table.name + ".lux")),
                                          str(folder / (table.name + ".lua"))], capture_output=True)
                    self.assertEqual(run.returncode, 0, run.stderr)


if __name__ == "__main__":
    unittest.main()

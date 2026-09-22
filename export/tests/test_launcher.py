# 验证策划清单选择、中文反馈和不依赖当前工作目录的双击入口。
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from test_export import cells, write_book


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "export"))
try:
    import launcher as LAUNCHER
finally:
    sys.path.pop(0)


class LauncherTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="hunter-planner-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "中文 项目"
        self.source = self.root / "design"
        self.output = self.root / "server/build/generated/cfg"
        self.target = self.root / "export/导出目标.txt"
        self.target.parent.mkdir(parents=True)

    def targets(self, text, encoding="utf-8"):
        self.target.write_bytes(text.encode(encoding))

    def test_only_selected_workbooks_are_exported(self):
        write_book(self.source / "道具表.xlsx", [("道具|Item", cells()), ("装备|Equip", cells())])
        (self.source / "错误表.xlsx").write_bytes(b"broken but not selected")
        self.targets("# 只导出一张文件\n\n道具表.xlsx\n")
        report = LAUNCHER.run_export(self.root)
        self.assertTrue(report.ok, report.detail)
        self.assertEqual({p.name for p in self.output.iterdir()}, {"Item.lua", "Equip.lua"})
        self.assertIn("1 个 Excel 文件", report.detail)
        self.assertIn("2 张表", report.detail)
        self.assertIn("导出完成", (self.target.parent / LAUNCHER.LOG_NAME).read_text(encoding="utf-8"))

    def test_notepad_encodings_extension_and_relative_path(self):
        expected = self.source / "子目录/道具表.xlsx"
        write_book(expected)
        for encoding in ("utf-8", "utf-8-sig", "utf-16", "gb18030"):
            with self.subTest(encoding=encoding):
                self.targets("# 说明\n道具表\n", encoding)
                self.assertEqual(LAUNCHER.select_workbooks(self.source, self.target), [expected])
        self.targets("子目录\\道具表.XLSX")
        self.assertEqual(LAUNCHER.select_workbooks(self.source, self.target), [expected])

    def test_bad_target_never_changes_output(self):
        write_book(self.source / "道具表.xlsx")
        self.output.mkdir(parents=True)
        old = self.output / "Test.lua"
        old.write_bytes(b"previous")
        self.targets("道具表.xlsx\n不存在.xlsx\n")
        report = LAUNCHER.run_export(self.root)
        self.assertFalse(report.ok)
        self.assertIn("第 2 行", report.detail)
        self.assertEqual(old.read_bytes(), b"previous")
        self.assertEqual(list(self.output.iterdir()), [old])

    def test_empty_missing_duplicate_and_unsafe_targets(self):
        write_book(self.source / "道具表.xlsx")
        report = LAUNCHER.run_export(self.root)
        self.assertFalse(report.ok)
        self.assertIn("无法读取", report.detail)
        for text, reason in (("\n# 说明\n", "清单为空"), ("道具表\n道具表.xlsx", "重复"),
                             ("../道具表.xlsx", "相对路径"), ("C:/道具表.xlsx", "相对路径"),
                             ("道具表.csv", "只支持"), ("~$道具表.xlsx", "临时")):
            with self.subTest(text=text):
                self.targets(text)
                report = LAUNCHER.run_export(self.root)
                self.assertFalse(report.ok)
                self.assertIn(reason, report.detail)
                self.assertFalse(self.output.exists())

    def test_ambiguous_names_require_relative_path(self):
        write_book(self.source / "甲/道具表.xlsx")
        write_book(self.source / "乙/道具表.xlsx")
        self.targets("道具表")
        report = LAUNCHER.run_export(self.root)
        self.assertFalse(report.ok)
        self.assertIn("多个同名", report.detail)
        self.targets("乙/道具表")
        self.assertTrue(LAUNCHER.run_export(self.root).ok)

    def test_only_check_and_return_code(self):
        write_book(self.source / "道具表.xlsx")
        self.targets("道具表")
        report = LAUNCHER.run_export(self.root, check=True)
        self.assertTrue(report.ok)
        self.assertFalse(self.output.exists())
        with mock.patch.object(LAUNCHER.cfg, "ROOT", self.root), mock.patch.object(LAUNCHER, "show_report") as ui:
            self.assertEqual(LAUNCHER.main(["--check"]), 0)
            ui.assert_called_once()
        self.targets("不存在")
        with mock.patch.object(LAUNCHER.cfg, "ROOT", self.root), mock.patch.object(LAUNCHER, "show_report"):
            self.assertEqual(LAUNCHER.main([]), 1)

    @unittest.skipUnless(sys.platform == "win32", "Windows 策划窗口")
    def test_result_window_builds_for_success_and_failure(self):
        for success in (True, False):
            window = LAUNCHER.tk.Tk()
            window.withdraw()
            try:
                report = LAUNCHER.Report(success, "导出完成" if success else "导出失败", "道具|Item!E5\n中文结果", self.root)
                with mock.patch.object(LAUNCHER.tk, "Tk", return_value=window), mock.patch.object(window, "mainloop"):
                    LAUNCHER.show_report(report)
                window.update_idletasks()
                self.assertEqual(window.title(), "Hunter 配置导出")
            finally:
                window.destroy()

    @unittest.skipUnless((ROOT / "export/导出配置.exe").is_file(), "尚未打包策划 EXE")
    def test_packaged_exe_without_python_on_path(self):
        executable = self.target.parent / "导出配置.exe"
        shutil.copy2(ROOT / "export/导出配置.exe", executable)
        write_book(self.source / "道具表.xlsx", [("道具|Item", cells()), ("装备|Equip", cells())])
        (self.source / "未选中的坏表.xlsx").write_bytes(b"broken")
        self.targets("道具表")
        environment = dict(os.environ)
        environment["PATH"] = str(Path(os.environ["SystemRoot"]) / "System32")
        result = subprocess.run([str(executable), "--no-ui", "--check"], cwd=self.temp.name,
                                env=environment, capture_output=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(self.output.exists())
        result = subprocess.run([str(executable), "--no-ui"], cwd=self.temp.name,
                                env=environment, capture_output=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual({p.name for p in self.output.iterdir()}, {"Item.lua", "Equip.lua"})
        before = {p.name: p.read_bytes() for p in self.output.iterdir()}
        self.targets("不存在.xlsx")
        result = subprocess.run([str(executable), "--no-ui"], cwd=self.temp.name,
                                env=environment, capture_output=True, timeout=60)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("第 1 行", (self.target.parent / LAUNCHER.LOG_NAME).read_text(encoding="utf-8"))
        self.assertEqual({p.name: p.read_bytes() for p in self.output.iterdir()}, before)


if __name__ == "__main__":
    unittest.main()

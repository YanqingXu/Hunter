# 验证模块图、路径边界和可复现的单模块组装结果。
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

TOOL = Path(__file__).resolve().parents[2] / "tools" / "assemble.py"
sys.path.insert(0, str(TOOL.parent))
import publish

SPEC = importlib.util.spec_from_file_location("assemble", TOOL)
assemble = importlib.util.module_from_spec(SPEC)


class AssembleContract(unittest.TestCase):
    # 每个用例使用独立模块目录。
    def setUp(self):
        SPEC.loader.exec_module(assemble)
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    # 创建含两个工厂的最小清单。
    def manifest(self, modules=None):
        if modules is None:
            modules = [
                {"name": "main", "file": "main.lua", "deps": ["state"]},
                {"name": "state", "file": "state.lua", "deps": []},
            ]
        main = "-- 测试入口工厂，原样返回注入的模块表。\nreturn function(deps)\n"
        state = "-- 测试状态工厂，创建独立模块表。\nreturn function(deps)\n"
        (self.root / "main.lua").write_text(main + "    return deps\nend\n", encoding="utf-8")
        (self.root / "state.lua").write_text(state + "    return {}\nend\n", encoding="utf-8")
        path = self.root / "modules.json"
        path.write_text(json.dumps({"version": 1, "entry": "main", "modules": modules}))
        return path

    # 清单排列和工作区位置不影响脚本及映射。
    def test_stable_order_and_map(self):
        path = self.manifest()
        source, mapping = assemble.assemble(path)
        doc = json.loads(path.read_text())
        doc["modules"].reverse()
        path.write_text(json.dumps(doc))
        self.assertEqual((source, mapping), assemble.assemble(path))
        self.assertEqual(mapping["order"], ["state", "main"])
        for entry in mapping["sources"]:
            original = (self.root / entry["file"]).read_text(encoding="utf-8").splitlines()
            for offset, line in enumerate(original):
                generated = source.splitlines()[entry["generated_start"] - 1 + offset]
                self.assertEqual(generated, "        " + line)
            self.assertEqual(entry["generated_end"] - entry["generated_start"] + 1,
                             len(original))
        self.assertNotIn(str(self.root), source)
        self.assertIn('function on_event(event_id, payload_json)', source)
        self.assertTrue(source.endswith("\nreturn true\n"))

    # 重名和缺失依赖必须在输出前被拒绝。
    def test_duplicate_and_missing(self):
        for modules in (
            [{"name": "main", "file": "main.lua", "deps": []}] * 2,
            [{"name": "main", "file": "main.lua", "deps": ["missing"]}],
        ):
            with self.subTest(modules=modules), self.assertRaises(ValueError):
                assemble.assemble(self.manifest(modules))

    # 依赖环和入口被其他模块依赖均不能生成可执行制品。
    def test_cycles(self):
        path = self.manifest([
            {"name": "main", "file": "main.lua", "deps": ["state"]},
            {"name": "state", "file": "state.lua", "deps": ["main"]},
        ])
        with self.assertRaisesRegex(ValueError, "cycle"):
            assemble.assemble(path)

    # 脚本文件必须小写，大小写不敏感文件系统也不能掩盖引用错误。
    def test_lowercase_files(self):
        with self.assertRaisesRegex(ValueError, "lowercase"):
            assemble.assemble(self.manifest([
                {"name": "main", "file": "Main.lua", "deps": []}]))
        (self.root / "Upper.lua").write_text("return function() return {} end", encoding="utf-8")
        with self.assertRaises((ValueError, FileNotFoundError)):
            assemble.assemble(self.manifest([
                {"name": "main", "file": "upper.lua", "deps": []}]))

    # 未加载的玩法占位文件也须小写，不允许通过遗漏清单绕开检查。
    def test_unlisted_uppercase_file(self):
        path = self.manifest()
        (self.root / "Item.Lua").write_text("return {}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "lowercase"):
            assemble.assemble(path)

    # 模块身份和源码依赖引用使用严格一致的小写名字。
    def test_dependency_case(self):
        with self.assertRaisesRegex(ValueError, "module name"):
            assemble.assemble(self.manifest([
                {"name": "Main", "file": "main.lua", "deps": []}]))
        path = self.manifest()
        (self.root / "main.lua").write_text(
            'return function(deps) return deps["State"] end', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "dependency reference"):
            assemble.assemble(path)

    # 非规范路径及符号链接逃逸均不可读取清单目录之外的文件。
    def test_path_escape(self):
        for filename in ("../outside.lua", "/outside.lua", "C:/outside.lua", "a/../main.lua"):
            with self.subTest(filename=filename), self.assertRaises(ValueError):
                assemble.assemble(self.manifest([
                    {"name": "main", "file": filename, "deps": []},
                ]))

    # Windows 无符号链接权限时使用目录联接验证相同的真实路径边界。
    def test_link_escape(self):
        path = self.manifest()
        outside = tempfile.TemporaryDirectory()
        self.addCleanup(outside.cleanup)
        target = Path(outside.name)
        (target / "escape.lua").write_text(
            "-- 验证链接越界的合法模块。\nreturn function()\n    return {}\nend\n",
            encoding="utf-8")
        link = self.root / "escape"
        try:
            link.symlink_to(target, target_is_directory=True)
        except OSError as error:
            if os.name != "nt":
                self.skipTest(f"symlink unavailable: {error}")
            result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)],
                                    capture_output=True, check=False)
            if result.returncode:
                self.skipTest(f"directory junction unavailable: {result.returncode}")
        doc = json.loads(path.read_text())
        doc["modules"][0]["file"] = "escape/escape.lua"
        path.write_text(json.dumps(doc))
        with self.assertRaisesRegex(ValueError, "escape"):
            assemble.assemble(path)

    # 错误类型、重复依赖和不存在的入口都必须给出明确失败。
    def test_invalid_manifest(self):
        path = self.manifest()
        baseline = json.loads(path.read_text())
        for key, value in (("version", True), ("entry", "none"),
                           ("entry", []), ("modules", [])):
            doc = dict(baseline)
            doc[key] = value
            path.write_text(json.dumps(doc))
            with self.subTest(key=key), self.assertRaises(ValueError):
                assemble.assemble(path)
        baseline["modules"][0]["deps"] = ["state", "state"]
        path.write_text(json.dumps(baseline))
        with self.assertRaises(ValueError):
            assemble.assemble(path)

    # 未被入口声明的模块不能隐式获得执行机会。
    def test_unreachable_module(self):
        path = self.manifest([
            {"name": "main", "file": "main.lua", "deps": []},
            {"name": "state", "file": "state.lua", "deps": []},
        ])
        with self.assertRaisesRegex(ValueError, "every module"):
            assemble.assemble(path)

    # 映射目标非法时不创建新源码，也不覆盖已存在源码。
    def test_invalid_map_destination_preserves_source(self):
        manifest = self.manifest()
        output = self.root / "game.lua"
        mapping = self.root / "map-dir"
        mapping.mkdir()
        args = ["assemble.py", "--manifest", str(manifest), "--output", str(output),
                "--map", str(mapping)]
        for old in (None, b"old source"):
            if old is not None:
                output.write_bytes(old)
            with self.subTest(old=old), patch.object(sys, "argv", args):
                with self.assertRaises(SystemExit) as raised:
                    assemble.main()
                self.assertEqual(raised.exception.code, 1)
                if old is None:
                    self.assertFalse(output.exists())
                else:
                    self.assertEqual(output.read_bytes(), old)

    # 注入第二个临时文件的部分写入失败，两种初始状态均须保持原样。
    def test_staging_write_failure_rolls_back(self):
        manifest = self.manifest()
        output, mapping = self.root / "game.lua", self.root / "game.map.json"
        args = ["assemble.py", "--manifest", str(manifest), "--output", str(output),
                "--map", str(mapping)]
        write = publish._write_file
        for previous in (False, True):
            if previous:
                output.write_bytes(b"old source")
                mapping.write_bytes(b"old map")
            baseline = {path.name for path in self.root.iterdir()}
            calls = 0

            # 在映射暂存文件落下一部分字节后模拟磁盘写入错误。
            def fail_write(path, data):
                nonlocal calls
                calls += 1
                if calls == 2:
                    path.write_bytes(data[:7])
                    raise OSError("injected partial write")
                write(path, data)

            with self.subTest(previous=previous), patch.object(sys, "argv", args):
                with patch.object(publish, "_write_file", side_effect=fail_write):
                    with self.assertRaises(SystemExit):
                        assemble.main()
                self.assertEqual({path.name for path in self.root.iterdir()}, baseline)
                if previous:
                    self.assertEqual(output.read_bytes(), b"old source")
                    self.assertEqual(mapping.read_bytes(), b"old map")

    # 第一项已切换而第二项失败时，返回前恢复旧的一整套或删除新的一整套。
    def test_second_publish_failure_restores_set(self):
        manifest = self.manifest()
        output, mapping = self.root / "game.lua", self.root / "game.map.json"
        args = ["assemble.py", "--manifest", str(manifest), "--output", str(output),
                "--map", str(mapping)]
        replace = publish.os.replace
        for previous in (False, True):
            if previous:
                output.write_bytes(b"old source")
                mapping.write_bytes(b"old map")
            baseline = {path.name for path in self.root.iterdir()}

            # 确认源码确已发布，再拒绝映射的首次替换；回滚替换正常执行。
            def fail_mapping(source, target):
                if Path(source).name == "prepared" and Path(target) == mapping:
                    self.assertIn(b"return true", output.read_bytes())
                    raise OSError("injected second publish failure")
                replace(source, target)

            with self.subTest(previous=previous), patch.object(sys, "argv", args):
                with patch.object(publish.os, "replace", side_effect=fail_mapping):
                    with self.assertRaises(SystemExit):
                        assemble.main()
                self.assertEqual({path.name for path in self.root.iterdir()}, baseline)
                if previous:
                    self.assertEqual(output.read_bytes(), b"old source")
                    self.assertEqual(mapping.read_bytes(), b"old map")

    # 目标别名不能让源码和映射互相覆盖。
    def test_duplicate_publish_target(self):
        output = self.root / "game.lua"
        output.write_bytes(b"original")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            publish.publish_files([(output, b"source"), (output, b"map")])
        self.assertEqual(output.read_bytes(), b"original")

    # 合法制品不能占用合作锁的保留名称，否则清理锁会误删最终文件。
    def test_reserved_lock_targets_rejected(self):
        output = self.root / "game.lua"
        mapping = output.with_name(output.name + ".hunter-publish.lock")
        with self.assertRaisesRegex(ValueError, "reserved.*lock"):
            publish.publish_files([(output, b"source"), (mapping, b"map")])
        self.assertFalse(list(self.root.iterdir()))
        for name in ("standalone.hunter-publish.lock", "UPPER.HUNTER-PUBLISH.LOCK"):
            target = self.root / name
            target.write_bytes(b"existing artifact")
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "reserved.*lock"):
                publish.publish_files([(target, b"replacement")])
            self.assertEqual(target.read_bytes(), b"existing artifact")

    # Windows 会折叠末尾点和空格，不能允许两个不同文本目标落到同一个文件。
    @unittest.skipUnless(os.name == "nt", "Windows filename aliases")
    def test_windows_filename_aliases_rejected(self):
        output = self.root / "game.lua"
        output.write_bytes(b"original")
        for suffix in (".", " "):
            alias = output.with_name(output.name + suffix)
            with self.subTest(suffix=suffix), self.assertRaisesRegex(ValueError, "alias"):
                publish.publish_files([(output, b"source"), (alias, b"map")])
            self.assertEqual(output.read_bytes(), b"original")

    # 已有合作锁拒绝另一发布者，且不能删除不属于本次调用的锁。
    def test_existing_lock_preserves_owner(self):
        output = self.root / "game.lua"
        output.write_bytes(b"original")
        lock = output.with_name(output.name + ".hunter-publish.lock")
        lock.write_bytes(b"another publisher")
        with self.assertRaises(FileExistsError):
            publish.publish_files([(output, b"replacement")])
        self.assertEqual(output.read_bytes(), b"original")
        self.assertEqual(lock.read_bytes(), b"another publisher")

    # 排他创建遇到后来出现的目标时，回滚不能删除其他写入者的文件。
    def test_create_race_keeps_unrelated_file(self):
        output = self.root / "game.luxb"
        link = publish.os.link

        # 绕过合作锁制造竞争，实际硬链接操作仍由文件系统排他检查。
        def race_link(source, target):
            Path(target).write_bytes(b"external artifact")
            link(source, target)

        with patch.object(publish.os, "link", side_effect=race_link):
            with self.assertRaises(FileExistsError):
                publish.publish_files([(output, b"our artifact")], replace=False)
        self.assertEqual(output.read_bytes(), b"external artifact")
        self.assertEqual({path.name for path in self.root.iterdir()}, {output.name})

    # 文件系统连回滚也拒绝时，错误必须保留可恢复旧字节的备份和清单。
    def test_rollback_failure_retains_recovery(self):
        output, mapping = self.root / "game.lua", self.root / "game.map.json"
        output.write_bytes(b"old source")
        mapping.write_bytes(b"old map")
        replace = publish.os.replace

        # 拒绝第二次发布及第一次制品的恢复，模拟持续不可用的目标路径。
        def fail_restore(source, target):
            if Path(source).name == "prepared" and Path(target) == mapping:
                raise OSError("second publish failed")
            if Path(source).name == "previous" and Path(target) == output:
                raise OSError("restore denied")
            replace(source, target)

        with patch.object(publish.os, "replace", side_effect=fail_restore):
            with self.assertRaisesRegex(OSError, "rollback failed.*recover from"):
                publish.publish_files([(output, b"new source"), (mapping, b"new map")])
        journals = list(self.root.glob(".hunter-publish-*/recovery.json"))
        self.assertEqual(len(journals), 1)
        recovery = json.loads(journals[0].read_text(encoding="utf-8"))
        source_record = next(item for item in recovery if Path(item["target"]) == output)
        self.assertEqual(Path(source_record["backup"]).read_bytes(), b"old source")
        self.assertEqual(len(list(self.root.glob("*.hunter-publish.lock"))), 2)

    # 成套提交完成后清理失败只报告警告，不把已成功切换误报成回滚成功。
    def test_committed_cleanup_failure_warns(self):
        output, mapping = self.root / "game.lua", self.root / "game.map.json"
        cleanup = publish.shutil.rmtree
        calls = 0

        # 首个暂存目录无法删除，其余清理仍须继续。
        def fail_cleanup(path):
            nonlocal calls
            calls += 1
            if calls == 1:
                raise OSError("cleanup denied")
            cleanup(path)

        with patch.object(publish.shutil, "rmtree", side_effect=fail_cleanup):
            with self.assertWarnsRegex(RuntimeWarning, "artifact set committed"):
                publish.publish_files([(output, b"source"), (mapping, b"map")])
        self.assertEqual(output.read_bytes(), b"source")
        self.assertEqual(mapping.read_bytes(), b"map")
        self.assertFalse(list(self.root.glob("*.hunter-publish.lock")))


if __name__ == "__main__":
    unittest.main()

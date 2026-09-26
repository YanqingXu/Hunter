# 验证联调包白名单和配置身份闭环，拒绝旧内容、旧聚合源码及错误 Bundle。
import argparse
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import package_demo as package


class PackageContract(unittest.TestCase):
    # 真实配置只生成一次，各用例使用独立生成目录检查不一致资源。
    @classmethod
    def setUpClass(cls):
        tables = package.gameplay.read_tables(ROOT.parent / "design/demo_sources.json")
        cls.configs = package.gameplay.config_files(tables)
        cls.content, cls.header = package.gameplay.artifacts(tables)

    # 创建不含用户数据的构建夹具，保留真实源码和配置摘要。
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="hunter-package-test-")
        self.addCleanup(self.temp.cleanup)
        self.build = Path(self.temp.name)
        generated = self.build / "generated"
        (generated / "cfg").mkdir(parents=True)
        (generated / "csharp").mkdir()
        (self.build / "Release").mkdir()
        for name, value in self.configs.items():
            (generated / "cfg" / name).write_bytes(value)
        (generated / "content.json").write_bytes(self.content)
        (generated / "ContentId.h").write_bytes(self.header)
        (generated / "csharp/Hunter.cs").write_text("// 协议产物夹具。\n", encoding="utf-8")
        source, mapping = package.assemble.assemble(ROOT / "lua/modules.json", generated / "cfg")
        (generated / "game.lua").write_text(source, encoding="utf-8", newline="\n")
        (generated / "game.map.json").write_text(json.dumps(mapping), encoding="utf-8")
        for name in ("hunter_server_desktop.exe", "hunter_client.exe", "luaxc.exe"):
            (self.build / "Release" / name).write_bytes(b"native artifact test fixture")
        (generated / "test-only.seed").write_bytes(b"must not ship")
        (generated / "cfg/Unused.lua").write_text("return {}\n", encoding="utf-8")
        self.args = argparse.Namespace(build=self.build, bundle=None, policy=None,
            test_signature=False, luax=None, bundle_tool=None)

    # 源包带显式 Lua 配置和身份元数据，但不收集未选旧文件、编译器或私钥。
    def test_source_package_contains_selected_configuration(self):
        files = package.files(self.args)
        manifest = json.loads(files["manifest.json"])
        self.assertEqual(manifest["host_state"], 7)
        self.assertEqual(manifest["content_key"],
                         "gameplay-v4:" + hashlib.sha256(self.content).hexdigest())
        self.assertEqual(manifest["script_source_sha256"],
                         hashlib.sha256(files["game.lua"]).hexdigest())
        self.assertIn("cfg/Manifest.json", files)
        self.assertIn("cfg/Player.lua", files)
        self.assertEqual(sum(name.startswith("cfg/") and name.endswith(".lua")
                             for name in files), 20)
        self.assertNotIn("cfg/Unused.lua", files)
        self.assertFalse(any(name.endswith(".seed") or "luaxc" in name for name in files))
        self.assertIn("ContentId.h", files)

    # 数值字节与身份头之间不可只改一个，即使 JSON 仍能解析也必须拒绝。
    def test_content_identity_mismatch(self):
        header = self.header.replace(b"651046", b"ffffff")
        with self.assertRaisesRegex(ValueError, "identity mismatch"):
            package.content_identity(self.content, header)
        with self.assertRaisesRegex(ValueError, "canonical"):
            package.content_identity(self.content + b"\n", self.header)

    # 验证失败必须保留已交付 ZIP，不能用部分新资源覆盖旧包。
    def test_failure_preserves_previous_package(self):
        output = self.build / "delivery.zip"
        output.write_bytes(b"previous package")
        (self.build / "generated/ContentId.h").write_text("invalid identity", encoding="utf-8")
        args = ["package_demo.py", "--build", str(self.build), "--output", str(output)]
        with mock.patch.object(sys, "argv", args):
            with self.assertRaises(SystemExit) as raised:
                package.main()
        self.assertEqual(raised.exception.code, 1)
        self.assertEqual(output.read_bytes(), b"previous package")

    # 原始 Lua 表变更而共享内容未导出时，打包不能携带不同数值。
    def test_stale_shared_content(self):
        path = self.build / "generated/cfg/Player.lua"
        value = path.read_text(encoding="utf-8").replace('["Run"] = 150', '["Run"] = 151')
        self.assertIn('["Run"] = 151', value)
        path.write_text(value, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Lua configuration differs"):
            package.files(self.args)

    # 生成源码和源映射分别核对，不能用新数据配旧聚合脚本。
    def test_stale_assembled_resources(self):
        source = self.build / "generated/game.lua"
        original = source.read_bytes()
        source.write_bytes(original + b"\n")
        with self.assertRaisesRegex(ValueError, "game.lua differs"):
            package.files(self.args)
        source.write_bytes(original)
        (self.build / "generated/game.map.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "game.map.json differs"):
            package.files(self.args)

    # Bundle 必须绑定相同源码，并使用当前 Host 契约；签名验证由真实离线工具负责。
    def test_bundle_source_and_contract_identity(self):
        self.args.bundle = self.build / "game.luxb"
        self.args.policy = self.build / "policy.json"
        self.args.bundle_tool = Path(sys.executable)
        self.args.bundle.write_bytes(b"signed artifact fixture")
        self.args.policy.write_text("{}", encoding="utf-8")
        with mock.patch.object(package.bundle, "verify", return_value={"source_sha256": "bad"}):
            with self.assertRaisesRegex(ValueError, "source identity differs"):
                package.verify_bundle(self.args, self.build, "expected")
        contract = json.loads((ROOT / "lua/contract.json").read_text(encoding="utf-8"))
        identities = {key: package.bundle.digest({"identity": key, "source": {
            "contract_version": contract["version"], "schema": contract[key]}})
            for key in package.bundle.KEYS[5:]}
        fields = {"source_sha256": "expected"}
        with mock.patch.object(package.bundle, "verify", return_value=fields), \
             mock.patch.object(package.bundle, "load_policy", return_value={"identities": identities}):
            package.verify_bundle(self.args, self.build, "expected")
            identities["host_api"] = "outdated"
            with self.assertRaisesRegex(ValueError, "current contract"):
                package.verify_bundle(self.args, self.build, "expected")

    # Bundle 包只携带签名数据，原表和编译工具都不进入交付面。
    def test_bundle_package_omits_loose_configuration(self):
        self.args.bundle = self.build / "game.luxb"
        self.args.policy = self.build / "policy.json"
        self.args.test_signature = True
        self.args.bundle.write_bytes(b"signed artifact fixture")
        self.args.policy.write_text("{}", encoding="utf-8")
        with mock.patch.object(package, "verify_bundle") as verify:
            files = package.files(self.args)
        verify.assert_called_once()
        self.assertIn("game.luxb", files)
        self.assertFalse(any(name.startswith("cfg/") for name in files))
        self.assertNotIn("game.lua", files)
        self.assertTrue(json.loads(files["manifest.json"])["test_signature"])


if __name__ == "__main__":
    unittest.main()

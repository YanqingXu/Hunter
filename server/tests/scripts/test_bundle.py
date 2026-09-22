# 使用真实 Luax 工具验证可复现签名制品；RFC 密钥仅用于构建目录内的测试。
import argparse
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

TOOL = Path(__file__).resolve().parents[2] / "tools" / "bundle.py"
sys.path.insert(0, str(TOOL.parent))
import publish

SPEC = importlib.util.spec_from_file_location("hunter_bundle", TOOL)
bundle = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bundle)
OPTIONS = None
SEED = bytes.fromhex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60")
PUBLIC = bytes.fromhex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")
WRONG_PUBLIC = "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"


class BundleContract(unittest.TestCase):
    # 建立真实编译、签名与验证所需的隔离测试制品。
    @classmethod
    def setUpClass(cls):
        if OPTIONS is None:
            raise unittest.SkipTest("run through hunter_bundle_contract with real Luax tool paths")
        OPTIONS.output_dir.mkdir(parents=True, exist_ok=True)
        cls.temp = tempfile.TemporaryDirectory(prefix="contract-", dir=OPTIONS.output_dir)
        cls.addClassCleanup(cls.temp.cleanup)
        cls.root = Path(cls.temp.name)
        cls.seed = cls.root / "test-only.seed"
        cls.pub = cls.root / "public.key"
        cls.policy = cls.root / "policy.json"
        cls.artifact = cls.root / "game.luxb"
        cls.seed.write_bytes(SEED)
        cls.pub.write_bytes(PUBLIC)
        cls.doc = bundle.write_policy(OPTIONS.luax_root, cls.pub, cls.policy)
        bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, OPTIONS.source,
                     cls.policy, cls.seed, cls.artifact)

    # 保留可供后续 Runtime 测试消费的已签名样本，删除临时中间目录。
    @classmethod
    def tearDownClass(cls):
        paths = (cls.artifact, cls.policy, cls.pub, cls.policy.with_suffix(".provenance.json"))
        files = [(OPTIONS.output_dir / path.name, path.read_bytes()) for path in paths]
        publish.publish_files(files)

    # 不同路径下的相同源码和身份必须产生相同签名字节。
    def test_reproducible_paths(self):
        source = self.root / "renamed.lua"
        artifact = self.root / "replayed.luxb"
        source.write_bytes(OPTIONS.source.read_bytes())
        bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, source,
                     self.policy, self.seed, artifact)
        self.assertEqual(self.artifact.read_bytes(), artifact.read_bytes())
        fields = bundle.verify(OPTIONS.bundle_tool, artifact, self.policy)
        self.assertEqual(fields["signature_state"], "signed-unverified")

    # 公钥、九项兼容身份和代次都必须被独立核对。
    def test_policy_rejection(self):
        for key in ("public_key", *bundle.KEYS, "build_epoch"):
            doc = json.loads(self.policy.read_text(encoding="utf-8"))
            if key == "public_key":
                doc[key] = WRONG_PUBLIC
            elif key == "build_epoch":
                doc[key] += 1
            else:
                doc["identities"][key] = "ab" * 32
            path = self.root / f"wrong-{key}.json"
            path.write_text(json.dumps(doc), encoding="utf-8")
            with self.subTest(key=key), self.assertRaises(ValueError):
                bundle.verify(OPTIONS.bundle_tool, self.artifact, path)

    # 签名、清单和字节码任一修改均不得通过公钥验证。
    def test_tampering(self):
        original = self.artifact.read_bytes()
        for offset in (20, 424, len(original) - 1):
            changed = bytearray(original)
            changed[offset] ^= 1
            path = self.root / f"tampered-{offset}.luxb"
            path.write_bytes(changed)
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                bundle.verify(OPTIONS.bundle_tool, path, self.policy)

    # 即使重新签名合法，旧 Runtime 版本仍不满足当前宿主兼容策略。
    def test_signed_old_runtime_version(self):
        changed = bytearray(self.artifact.read_bytes())
        changed[120:124] = (1).to_bytes(4, "little")
        changed[424:488] = bytes(64)
        unsigned = self.root / "runtime-v1.unsigned.luxb"
        signed = self.root / "runtime-v1.signed.luxb"
        unsigned.write_bytes(changed)
        bundle.run([OPTIONS.bundle_tool, "sign", "--secret-key", self.seed,
                    "-o", signed, unsigned])
        bundle.run([OPTIONS.bundle_tool, "verify", "--public-key", self.pub, signed])
        with self.assertRaisesRegex(ValueError, "version mismatch"):
            bundle.verify(OPTIONS.bundle_tool, signed, self.policy)

    # 已有制品不可被覆盖，失败不能改写其字节。
    def test_no_overwrite(self):
        expected = self.artifact.read_bytes()
        with self.assertRaisesRegex(ValueError, "already exists"):
            bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, OPTIONS.source,
                         self.policy, self.seed, self.artifact)
        self.assertEqual(expected, self.artifact.read_bytes())

    # 版本化契约必须形成不同的兼容身份，并保留真实来源而非占位散列。
    def test_identity_provenance(self):
        hashes = self.doc["identities"]
        self.assertEqual(len(set(hashes.values())), 9)
        self.assertEqual(self.doc["luax_commit"], bundle.PIN)
        provenance = self.policy.with_suffix(".provenance.json")
        evidence = json.loads(provenance.read_text(encoding="utf-8"))
        self.assertIn("include/luax/Bundle.hpp", evidence["bundle"]["files"])
        self.assertGreater(len(evidence["compiler"]["files"]), 10)
        changed = dict(evidence["state"])
        changed["contract_version"] += 1
        changed_hash = bundle.digest({"identity": "state", "source": changed})
        self.assertNotEqual(hashes["state"], changed_hash)
        self.assertNotIn(SEED.hex(), self.policy.read_text(encoding="utf-8"))

    # 错误源码编译失败后不出现最终 Bundle。
    def test_compile_failure(self):
        source = self.root / "invalid.lua"
        output = self.root / "invalid.luxb"
        source.write_text("function broken(\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, source,
                         self.policy, self.seed, output)
        self.assertFalse(output.exists())

    # 完整签名后暂存写入仍可能失败，最终路径及所有临时文件都不能遗留。
    def test_final_staging_write_failure(self):
        output = self.root / "write-failed.luxb"
        baseline = {path.name for path in self.root.iterdir()}
        write = publish._write_file

        # 写入部分签名字节后抛错，覆盖曾经在最终路径留下半文件的问题。
        def fail_signed_write(path, data):
            if data.startswith(b"LUXB"):
                path.write_bytes(data[:16])
                raise OSError("injected signed artifact write failure")
            write(path, data)

        with patch.object(publish, "_write_file", side_effect=fail_signed_write):
            with self.assertRaisesRegex(OSError, "signed artifact write failure"):
                bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, OPTIONS.source,
                             self.policy, self.seed, output)
        self.assertFalse(output.exists())
        self.assertEqual({path.name for path in self.root.iterdir()}, baseline)

    # 最终排他链接已建立后发生异常时，也必须撤销这次创建的最终文件。
    def test_final_publish_failure_removes_new_file(self):
        output = self.root / "publish-failed.luxb"
        baseline = {path.name for path in self.root.iterdir()}
        link = publish.os.link

        # 在实际创建硬链接后注入异常，验证回滚不依赖调用正常返回。
        def fail_after_link(source, target):
            link(source, target)
            raise OSError("injected final publication failure")

        with patch.object(publish.os, "link", side_effect=fail_after_link):
            with self.assertRaisesRegex(OSError, "final publication failure"):
                bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, OPTIONS.source,
                             self.policy, self.seed, output)
        self.assertFalse(output.exists())
        self.assertEqual({path.name for path in self.root.iterdir()}, baseline)

    # 外层编译暂存目录清理失败发生在提交之后，须保留成功 Bundle 并仅报告警告。
    def test_committed_stage_cleanup_warns(self):
        output = self.root / "committed-with-cleanup-warning.luxb"
        cleanup = publish.shutil.rmtree

        # 只阻止 Bundle 编译暂存目录删除，不影响制品发布和其他临时目录。
        def fail_stage_cleanup(path, *args, **kwargs):
            if Path(path).name.startswith("bundle-stage-"):
                raise OSError("injected bundle staging cleanup failure")
            return cleanup(path, *args, **kwargs)

        with patch.object(publish.shutil, "rmtree", side_effect=fail_stage_cleanup):
            with self.assertWarnsRegex(RuntimeWarning, "Bundle committed.*staging cleanup"):
                result = bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, OPTIONS.source,
                                      self.policy, self.seed, output)
        self.assertEqual(result, output)
        self.assertEqual(output.read_bytes(), self.artifact.read_bytes())
        bundle.verify(OPTIONS.bundle_tool, output, self.policy)
        self.assertEqual(len(list(self.root.glob("bundle-stage-*"))), 1)

    # 构建与清理同时失败时，报告原始构建原因和残留目录，不发布最终制品。
    def test_failed_build_and_stage_cleanup_report_both(self):
        source = self.root / "invalid-cleanup.lua"
        source.write_text("function broken(\n", encoding="utf-8")
        output = self.root / "failed-build-and-cleanup.luxb"
        cleanup = publish.shutil.rmtree

        # 清理故障不能把原始编译失败悄悄替换成无关联的目录错误。
        def fail_stage_cleanup(path, *args, **kwargs):
            if Path(path).name.startswith("bundle-stage-"):
                raise OSError("injected cleanup failure")
            return cleanup(path, *args, **kwargs)

        with patch.object(publish.shutil, "rmtree", side_effect=fail_stage_cleanup):
            with self.assertRaisesRegex(OSError, "Bundle build failed.*staging cleanup failed"):
                bundle.build(OPTIONS.luaxc, OPTIONS.bundle_tool, source,
                             self.policy, self.seed, output)
        self.assertFalse(output.exists())


class PolicyPublishContract(unittest.TestCase):
    # 隔离策略发布故障，不调用工具链或改写共享 Bundle 样本。
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.pub = self.root / "public.key"
        self.pub.write_bytes(PUBLIC)
        self.hashes = {name: str(index + 1) * 64 for index, name in enumerate(bundle.KEYS)}

    # policy 目标为目录时必须保留原 provenance 字节。
    def test_invalid_policy_preserves_provenance(self):
        policy = self.root / "policy.json"
        provenance = policy.with_suffix(".provenance.json")
        policy.mkdir()
        provenance.write_bytes(b"previous provenance")
        with patch.object(bundle, "identities", return_value=(self.hashes, {"version": 1})):
            with self.assertRaises((ValueError, OSError)):
                bundle.write_policy(self.root, self.pub, policy)
        self.assertEqual(provenance.read_bytes(), b"previous provenance")

    # policy 发布失败时，已发布的 provenance 必须恢复到与旧 policy 对应的字节。
    def test_second_policy_publish_restores_pair(self):
        policy = self.root / "policy.json"
        provenance = policy.with_suffix(".provenance.json")
        policy.write_bytes(b"previous policy")
        provenance.write_bytes(b"previous provenance")
        baseline = {path.name for path in self.root.iterdir()}
        replace = publish.os.replace

        # provenance 已改变，第二项 policy 替换失败；备份恢复允许成功。
        def fail_policy(source, target):
            if Path(source).name == "prepared" and Path(target) == policy:
                self.assertNotEqual(provenance.read_bytes(), b"previous provenance")
                raise OSError("injected policy publish failure")
            replace(source, target)

        with patch.object(bundle, "identities", return_value=(self.hashes, {"version": 1})):
            with patch.object(publish.os, "replace", side_effect=fail_policy):
                with self.assertRaisesRegex(OSError, "policy publish failure"):
                    bundle.write_policy(self.root, self.pub, policy)
        self.assertEqual(policy.read_bytes(), b"previous policy")
        self.assertEqual(provenance.read_bytes(), b"previous provenance")
        self.assertEqual({path.name for path in self.root.iterdir()}, baseline)


# 以 CTest 提供的当前构建工具运行契约，不依赖 PATH 中的旧版工具。
def main():
    global OPTIONS
    parser = argparse.ArgumentParser(description="验证 Hunter 签名 Bundle")
    parser.add_argument("--luax-root", type=Path, required=True)
    parser.add_argument("--luaxc", type=Path, required=True)
    parser.add_argument("--bundle-tool", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    OPTIONS = parser.parse_args()
    unittest.main(argv=[__file__], verbosity=2)


if __name__ == "__main__":
    main()

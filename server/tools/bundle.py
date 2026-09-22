# 使用固定 Luax 工具生成 identity、离线编译、签名和验证；不生成生产密钥。
import argparse
import hashlib
import json
import subprocess
import tempfile
import warnings
from pathlib import Path

from publish import publish_files

PIN = "d8a8160420074f8e590d94910d1af5a5f5a93245"
KEYS = ("bundle", "bytecode", "compiler", "runtime", "language",
        "host_api", "capability", "state", "effect")
# 固定候选 Bundle.hpp 和 Language.hpp 的版本，升级提交时必须同时审查。
VERSIONS = dict(zip(KEYS, (1, 3, 1, 2, 0x00020001, 1, 1, 1, 1)))
CONTRACT = Path(__file__).resolve().parents[1] / "lua" / "contract.json"
GROUPS = {
    "bundle": ("include/luax/Bundle.hpp", "src/bundle/"),
    "bytecode": ("include/luax/ByteIo.hpp", "src/runtime/binary_chunk",
                 "src/runtime/bytecode_verifier", "src/bytecode/"),
    "compiler": ("src/compiler/", "src/tools/luaxc_main.cpp"),
    "runtime": ("include/luax/", "src/runtime/", "src/vm/", "src/luax/", "src/gc/",
                "src/core/", "src/stdlib/", "docs/compatibility/runtime-v2.json",
                "docs/compatibility/cost-model-v2.json",
                "docs/compatibility/standard-library-v2.json"),
    "language": ("include/luax/Language.hpp", "src/runtime/language_profile.hpp"),
}


# 执行真实离线工具；不在错误输出中回显签名私钥内容。
def run(args):
    result = subprocess.run([str(arg) for arg in args], capture_output=True, check=False)
    if result.returncode:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"{Path(args[0]).name} failed ({result.returncode}): {detail}")
    return result.stdout


# 对规范 JSON 求摘要，排序并排除平台换行差异。
def digest(value):
    data = json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(data.encode("utf-8")).hexdigest()


# 从已锁定提交的真实文件产生九项兼容身份，记录每项的来源摘要。
def identities(luax_root, contract=CONTRACT):
    root = Path(luax_root).resolve(strict=True)
    head = run(["git", "-C", root, "rev-parse", "HEAD"]).decode().strip()
    if head != PIN:
        raise ValueError(f"Luax pin mismatch: expected {PIN}, got {head}")
    if run(["git", "-C", root, "status", "--porcelain", "--untracked-files=no"]).strip():
        raise ValueError("Luax source has tracked modifications")
    paths = run(["git", "-C", root, "ls-tree", "-r", "--name-only", PIN])
    paths = paths.decode("utf-8").splitlines()
    evidence = {}
    for name, prefixes in GROUPS.items():
        selected = sorted(path for path in paths if any(path.startswith(p) for p in prefixes))
        if not selected:
            raise ValueError(f"identity has no source files: {name}")
        files = {}
        for path in selected:
            data = run(["git", "-C", root, "show", f"{PIN}:{path}"])
            files[path] = hashlib.sha256(data).hexdigest()
        evidence[name] = {"luax_commit": PIN, "files": files}
    hunter = json.loads(Path(contract).read_text(encoding="utf-8"))
    for name in KEYS[5:]:
        evidence[name] = {"contract_version": hunter["version"], "schema": hunter[name]}
    return {key: digest({"identity": key, "source": evidence[key]}) for key in KEYS}, evidence


# 检查公钥和全部 identity，生产路径缺少任一字段均拒绝。
def load_policy(path):
    doc = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(doc, dict) or type(doc.get("v")) is not int or doc["v"] != 1:
        raise ValueError("policy version must be 1")
    epoch = doc.get("build_epoch")
    if type(epoch) is not int or epoch < 1 or epoch > 18446744073709551615:
        raise ValueError("policy build_epoch must be positive uint64")
    values = doc.get("identities")
    if not isinstance(values, dict) or set(values) != set(KEYS):
        raise ValueError("policy requires all nine identities")
    for key, value in {"public_key": doc.get("public_key"), **values}.items():
        if not isinstance(value, str) or len(value) != 64:
            raise ValueError(f"invalid 32-byte hex field: {key}")
        try:
            raw = bytes.fromhex(value)
        except ValueError as error:
            raise ValueError(f"invalid hex field: {key}") from error
        if len(raw) != 32 or raw == bytes(32):
            raise ValueError(f"empty 32-byte field: {key}")
    return doc


# 生成仅含公钥的运行时策略，私钥不进入此制品。
def write_policy(luax_root, public_key, output, epoch=1, contract=CONTRACT):
    if type(epoch) is not int or epoch < 1 or epoch > 18446744073709551615:
        raise ValueError("build_epoch must be positive uint64")
    raw_key = Path(public_key).read_bytes()
    if len(raw_key) != 32 or raw_key == bytes(32):
        raise ValueError("public key must contain exactly 32 nonzero bytes")
    hashes, evidence = identities(luax_root, contract)
    doc = {"v": 1, "public_key": raw_key.hex(), "build_epoch": epoch,
           "luax_commit": PIN, "identities": hashes}
    output = Path(output)
    provenance = output.with_suffix(".provenance.json")
    provenance_text = json.dumps(evidence, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
    policy_text = json.dumps(doc, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
    publish_files([(provenance, provenance_text.encode("utf-8")),
                   (output, policy_text.encode("utf-8"))])
    return doc


# 用指定策略编译签名，并在完整验证后创建最终制品；不覆盖已有输出。
def build(luaxc, bundle_tool, source, policy, secret_key, output):
    doc = load_policy(policy)
    output = Path(output).absolute()
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists() or output.is_symlink():
        raise ValueError(f"output already exists: {output}")
    source = Path(source).resolve(strict=True)
    secret_key = Path(secret_key).resolve(strict=True)
    if secret_key.stat().st_size != 32:
        raise ValueError("secret seed must contain exactly 32 bytes")
    temp = tempfile.TemporaryDirectory(prefix="bundle-stage-", dir=output.parent)
    try:
        stage = Path(temp.name)
        pub = stage / "public.key"
        bytecode = stage / "game.lux"
        unsigned = stage / "unsigned.luxb"
        signed = stage / "signed.luxb"
        pub.write_bytes(bytes.fromhex(doc["public_key"]))
        run([luaxc, "--profile", "strict", "--chunk-name", "=(hunter-game)",
             "-o", bytecode, source])
        args = [bundle_tool, "build", "--source", source, "--bytecode", bytecode,
                "--public-key", pub, "--epoch", doc["build_epoch"], "--profile", "strict"]
        for key in KEYS:
            args += ["--" + key.replace("_", "-") + "-hash", doc["identities"][key]]
        run(args + ["-o", unsigned])
        run([bundle_tool, "sign", "--secret-key", secret_key, "-o", signed, unsigned])
        verify(bundle_tool, signed, policy)
        publish_files([(output, signed.read_bytes())], replace=False)
    except BaseException as error:
        try:
            temp.cleanup()
        except OSError as cleanup_error:
            raise OSError(f"Bundle build failed ({error}); staging cleanup failed: "
                          f"{temp.name}: {cleanup_error}") from error
        raise
    try:
        temp.cleanup()
    except OSError as error:
        warnings.warn(f"Bundle committed; staging cleanup incomplete: {temp.name}: {error}",
                      RuntimeWarning)
    return output


# 验证签名后核对完整兼容身份和发行代次，不能只相信 inspect 标记。
def verify(bundle_tool, source, policy):
    doc = load_policy(policy)
    source = Path(source).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="bundle-verify-", dir=source.parent) as temp:
        pub = Path(temp) / "public.key"
        pub.write_bytes(bytes.fromhex(doc["public_key"]))
        run([bundle_tool, "verify", "--public-key", pub, source])
    fields = {}
    for line in run([bundle_tool, "inspect", source]).decode("utf-8").splitlines():
        if "=" in line:
            name, value = line.split("=", 1)
            fields[name] = value
    if fields.get("epoch") != str(doc["build_epoch"]):
        raise ValueError("Bundle build_epoch mismatch")
    names = {"capability": "capabilities", "state": "state_schema", "effect": "effect_schema"}
    for key in KEYS:
        prefix = names.get(key, key)
        if fields.get(prefix + "_hash") != doc["identities"][key]:
            raise ValueError(f"Bundle identity mismatch: {key}")
        if fields.get(prefix + "_version") != str(VERSIONS[key]):
            raise ValueError(f"Bundle compatibility version mismatch: {key}")
    return fields


# 提供分离的身份生成、签名构建和只读验证命令。
def main():
    parser = argparse.ArgumentParser(description="Hunter 离线 Bundle 工具")
    commands = parser.add_subparsers(dest="command", required=True)
    identity = commands.add_parser("identity")
    identity.add_argument("--luax-root", type=Path, required=True)
    identity.add_argument("--public-key", type=Path, required=True)
    identity.add_argument("--output", type=Path, required=True)
    identity.add_argument("--epoch", type=int, default=1)
    for name in ("build", "verify"):
        command = commands.add_parser(name)
        command.add_argument("--bundle-tool", type=Path, required=True)
        command.add_argument("--source", type=Path, required=True)
        command.add_argument("--policy", type=Path, required=True)
        if name == "build":
            command.add_argument("--luaxc", type=Path, required=True)
            command.add_argument("--secret-key", type=Path, required=True)
            command.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "identity":
            write_policy(args.luax_root, args.public_key, args.output, args.epoch)
        elif args.command == "build":
            build(args.luaxc, args.bundle_tool, args.source, args.policy,
                  args.secret_key, args.output)
        else:
            verify(args.bundle_tool, args.source, args.policy)
    except (ValueError, OSError) as error:
        parser.exit(1, f"bundle: {error}\n")


if __name__ == "__main__":
    main()

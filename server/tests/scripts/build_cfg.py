# 将契约测试的配置候选离线导出、组装及签名，生产 Runtime 始终只加载最终制品。
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import bundle


# 运行固定本机工具，失败保留完整诊断并让调用的契约失败。
def run(args):
    result = subprocess.run([str(arg) for arg in args], capture_output=True)
    if result.returncode:
        raise ValueError(result.stdout.decode("utf-8", errors="replace")
                         + result.stderr.decode("utf-8", errors="replace"))


# 内容、Lua 规则和桥接版本共同决定缓存，旧规则制品不能掩盖回归。
def cache_key(content, mode, policy, tools):
    digest = hashlib.sha256(content)
    digest.update(mode.encode())
    digest.update(policy.read_bytes() if policy else b"")
    digest.update(json.dumps(tools, sort_keys=True).encode())
    paths = [*sorted((ROOT / "lua").rglob("*.lua")), ROOT / "lua/modules.json",
             ROOT / "lua/contract.json", ROOT / "tools/assemble.py", ROOT / "tools/bundle.py",
             ROOT.parent / "export/gameplay.py", ROOT.parent / "export/export.py", Path(__file__)]
    for path in paths:
        digest.update(path.relative_to(ROOT.parent).as_posix().encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()


# 所有中间文件位于唯一临时目录，完成后才发布完整目录；失败不复用半份缓存。
def build(args):
    tools = json.loads(args.tools.read_text(encoding="utf-8-sig"))
    content = args.input.read_bytes()
    cache = Path(tools["cache"]).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    key = cache_key(content, args.mode, args.policy, tools)
    output = cache / key
    name = "game.luxb" if args.mode == "bundle" else "game.lua"
    if not (output / name).is_file():
        with tempfile.TemporaryDirectory(prefix=".cfg-", dir=cache) as temporary:
            stage = Path(temporary)
            (stage / "input.json").write_bytes(content)
            run([sys.executable, "-B", ROOT.parent / "export/gameplay.py", "--fixture",
                 stage / "input.json", "--output", stage / "content.json", "--header",
                 stage / "ContentId.h", "--cfg", stage / "cfg", "--luax", tools["luax"]])
            run([sys.executable, "-B", ROOT / "tools/assemble.py", "--manifest",
                 ROOT / "lua/modules.json", "--cfg", stage / "cfg", "--output",
                 stage / "game.lua", "--map", stage / "game.map.json"])
            if args.mode == "bundle":
                seed = stage / "test-only.seed"
                seed.write_bytes(bytes.fromhex(
                    "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"))
                bundle.build(tools["luaxc"], tools["bundle_tool"], stage / "game.lua",
                             args.policy, seed, stage / name)
                seed.unlink()
            try:
                os.replace(stage, output)
            except OSError:
                if not (output / name).is_file():
                    raise
    args.result.write_text(json.dumps({"path": str(output / name)}), encoding="utf-8")


# 接收测试端文件路径，禁止把配置正文插入任何 shell 命令。
def main():
    parser = argparse.ArgumentParser()
    for name in ("tools", "input", "result"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--mode", choices=("source", "bundle"), required=True)
    parser.add_argument("--policy", type=Path)
    args = parser.parse_args()
    if args.mode == "bundle" and args.policy is None:
        parser.error("bundle fixtures require the current test policy")
    try:
        build(args)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Lua fixture: {error}\n")


if __name__ == "__main__":
    main()

# 从正式模块清单组装隔离的实体测试入口，测试能力不进入游戏制品。
import argparse
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
import assemble
from publish import publish_files


# 复用正式路径校验与组装器，只在临时目录增加测试入口。
def build(manifest, fixture, output, mapping):
    entry, modules = assemble.load_manifest(manifest)
    with tempfile.TemporaryDirectory(prefix="hunter-entities-") as temp:
        root = Path(temp)
        items = []
        for name, module in modules.items():
            target = root / module["file"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(module["path"].read_bytes())
            items.append({"name": name, "file": module["file"], "deps": module["deps"]})
        (root / "entities.lua").write_bytes(fixture.read_bytes())
        items.append({"name": "tests.entities", "file": "entities.lua", "deps": [
            entry, "game.world", "game.weapon", "game.ai", "game.damage", "game.snapshot",
            "game.movement"]})
        path = root / "modules.json"
        path.write_text(json.dumps({"version": 1, "entry": "tests.entities", "modules": items}),
                        encoding="utf-8")
        source, evidence = assemble.assemble(path)
    publish_files([(output, source.encode("utf-8")),
                   (mapping, (json.dumps(evidence, indent=2) + "\n").encode("utf-8"))])


# 接收构建系统提供的正式清单和测试夹具位置。
def main():
    parser = argparse.ArgumentParser()
    for name in ("manifest", "fixture", "output", "map"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    build(args.manifest, args.fixture, args.output, args.map)


if __name__ == "__main__":
    main()

# 按显式依赖组装 Luax 工厂，输出单模块源码及原文件行号映射。
import argparse
import hashlib
import heapq
import json
import re
from pathlib import Path, PurePosixPath, PureWindowsPath

from publish import publish_files

NAME = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\Z")
ENTRIES = {
    "init": "ctx_json",
    "on_event": "event_id, payload_json",
    "tick": "tick_id, dt_seconds",
    "export_state": "",
    "import_state": "snapshot_json",
    "validate_state": "",
    "shutdown": "reason",
}


# 读取严格清单，在访问源码前确认依赖图与目录边界。
def load_manifest(path):
    path = Path(path).resolve(strict=True)
    root = path.parent
    doc = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(doc, dict) or type(doc.get("version")) is not int or doc["version"] != 1:
        raise ValueError("manifest version must be 1")
    if not isinstance(doc.get("modules"), list) or not doc["modules"]:
        raise ValueError("modules must be a nonempty list")
    modules = {}
    for item in doc["modules"]:
        if not isinstance(item, dict):
            raise ValueError("module must be an object")
        name, filename, deps = item.get("name"), item.get("file"), item.get("deps")
        if not isinstance(name, str) or not NAME.fullmatch(name) or name in modules:
            raise ValueError(f"invalid or duplicate module name: {name}")
        if not isinstance(filename, str) or not filename or "\\" in filename:
            raise ValueError(f"invalid module path: {filename}")
        rel = PurePosixPath(filename)
        win = PureWindowsPath(filename)
        if rel.is_absolute() or win.drive or ".." in rel.parts or rel.suffix != ".lua":
            raise ValueError(f"module path escape: {filename}")
        full = (root / filename).resolve(strict=True)
        if not full.is_relative_to(root) or not full.is_file():
            raise ValueError(f"module path escape: {filename}")
        if not isinstance(deps, list) or any(not isinstance(dep, str) for dep in deps):
            raise ValueError(f"deps must be module names: {name}")
        if len(set(deps)) != len(deps):
            raise ValueError(f"duplicate dependency: {name}")
        modules[name] = {"file": rel.as_posix(), "path": full, "deps": sorted(deps)}
    if not isinstance(doc.get("entry"), str) or doc["entry"] not in modules:
        raise ValueError("entry module is missing")
    for name, item in modules.items():
        if any(dep not in modules for dep in item["deps"]):
            raise ValueError(f"missing dependency: {name}")
    return doc["entry"], modules


# 以名字排序同时就绪的节点，确保输入清单排列不会改变输出。
def ordered(modules):
    pending = {name: len(item["deps"]) for name, item in modules.items()}
    users = {name: [] for name in modules}
    for name, item in modules.items():
        for dep in item["deps"]:
            users[dep].append(name)
    ready = [name for name, count in pending.items() if count == 0]
    heapq.heapify(ready)
    result = []
    while ready:
        name = heapq.heappop(ready)
        result.append(name)
        for user in sorted(users[name]):
            pending[user] -= 1
            if pending[user] == 0:
                heapq.heappush(ready, user)
    if len(result) != len(modules):
        raise ValueError("module dependency cycle")
    return result


# 将工厂按拓扑顺序封装，所有跨模块引用均来自显式注入。
def assemble(path):
    entry, modules = load_manifest(path)
    order = ordered(modules)
    required = set()
    pending = [entry]
    while pending:
        name = pending.pop()
        if name not in required:
            required.add(name)
            pending.extend(modules[name]["deps"])
    if required != set(modules):
        raise ValueError("entry must depend on every module")
    lines = ["-- 构建期生成的单模块；请修改清单中的原始文件。", "local modules = {}"]
    mapping = {"version": 1, "entry": entry, "order": order, "sources": []}
    for name in order:
        item = modules[name]
        text = item["path"].read_text(encoding="utf-8-sig").replace("\r\n", "\n")
        source = text.splitlines()
        lines += ["do", "    local factory = (function()"]
        start = len(lines) + 1
        lines += ["        " + line for line in source]
        lines += ["    end)()", "    local deps = {}"]
        for dep in item["deps"]:
            key = json.dumps(dep, ensure_ascii=True)
            lines.append(f"    deps[{key}] = modules[{key}]")
        lines += [f"    modules[{json.dumps(name)}] = factory(deps)", "end", ""]
        mapping["sources"].append({
            "name": name, "file": item["file"], "generated_start": start,
            "generated_end": start + len(source) - 1, "source_start": 1,
            "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
        })
    lines.append(f"local entry = modules[{json.dumps(entry)}]")
    for name, args in ENTRIES.items():
        lines += ["", f"-- 转交 {name} 生命周期入口，模块表保留在脚本内部。",
                  f"function {name}({args})", f"    return entry.{name}({args})", "end"]
    lines += ["", "return true"]
    source = "\n".join(lines) + "\n"
    mapping["source_sha256"] = hashlib.sha256(source.encode("utf-8")).hexdigest()
    return source, mapping


# 接收构建目录输出路径，失败时返回非零且不生成半份源码。
def main():
    parser = argparse.ArgumentParser(description="组装 Hunter Luax 单模块")
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--map", type=Path, required=True)
    args = parser.parse_args()
    try:
        source, mapping = assemble(args.manifest)
        map_text = json.dumps(mapping, ensure_ascii=False, indent=2) + "\n"
        publish_files([(args.output, source.encode("utf-8")),
                       (args.map, map_text.encode("utf-8"))])
    except (ValueError, OSError) as error:
        parser.exit(1, f"assemble: {error}\n")


if __name__ == "__main__":
    main()

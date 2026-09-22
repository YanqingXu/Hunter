# 校验契约索引、元数据、依赖和真实 CMake／CTest 注册入口。
import argparse
import json
import re
import subprocess
from pathlib import Path


# 读取五项受限 YAML 元数据；列表统一使用 JSON 数组避免隐式类型。
def read_meta(path):
    text = path.read_text(encoding="utf-8")
    parts = text.split("---", 2)
    if len(parts) != 3 or parts[0].strip():
        raise ValueError(f"缺少契约元数据：{path}")
    meta = {}
    for line in parts[1].strip().splitlines():
        key, sep, value = line.partition(":")
        key, value = key.strip(), value.strip()
        if not sep or key in meta:
            raise ValueError(f"重复或非法元数据：{path}: {line}")
        meta[key] = json.loads(value) if value.startswith("[") else value
    if set(meta) != {"id", "status", "target", "depends_on", "verification"}:
        raise ValueError(f"元数据键不完整：{path}")
    if not re.fullmatch(r"SRV-\d{3}", meta["id"]):
        raise ValueError(f"非法契约 ID：{path}")
    if meta["status"] not in {"draft", "active", "deferred"}:
        raise ValueError(f"非法契约状态：{path}")
    for key in ("target", "depends_on", "verification"):
        if not isinstance(meta[key], list) or any(not isinstance(v, str) for v in meta[key]):
            raise ValueError(f"元数据 {key} 必须是字符串数组：{path}")
        if len(set(meta[key])) != len(meta[key]):
            raise ValueError(f"元数据 {key} 含重复项：{path}")
    for heading in ("目标", "不变量", "线程", "接口", "失败", "验证"):
        if not re.search(r"^## .*" + heading, parts[2], re.MULTILINE):
            raise ValueError(f"缺少 {heading} 章节：{path}")
    return meta


# 对照构建注册信息校验索引和契约依赖，错误通过异常交给调用者。
def verify(root, targets, tests, mode="development", build_tools=True):
    if mode not in {"development", "production"} or type(build_tools) is not bool:
        raise ValueError("非法构建模式")
    intent_dir = root / "intents"
    index = (intent_dir / "README.md").read_text(encoding="utf-8")
    catalog = {}
    paths = sorted(intent_dir.rglob("*.intent.md"))
    for path in paths:
        meta = read_meta(path)
        if meta["id"] in catalog:
            raise ValueError(f"重复契约 ID：{meta['id']}")
        rel = path.relative_to(intent_dir).as_posix()
        if index.count(f"]({rel})") != 1:
            raise ValueError(f"契约必须在索引出现一次：{rel}")
        catalog[meta["id"]] = meta
        verification = select_tests(meta["verification"], mode, build_tools)
        if meta["status"] == "active":
            if not meta["target"] or not verification:
                raise ValueError(f"active 契约缺少构建或验证入口：{meta['id']}")
            if set(meta["target"]) - set(targets):
                raise ValueError(f"不存在的构建目标：{meta['id']}")
            if set(verification) - set(tests):
                raise ValueError(f"不存在的 CTest 入口：{meta['id']}")
    for link in re.findall(r"\]\(([^)]+\.intent\.md)\)", index):
        if not (intent_dir / link).is_file():
            raise ValueError(f"索引链接不存在：{link}")
    visited, visiting = set(), set()

    # 遍历契约依赖，发现缺失项或环时立即拒绝。
    def visit(name):
        if name in visiting:
            raise ValueError(f"契约依赖环：{name}")
        if name not in catalog:
            raise ValueError(f"缺失契约依赖：{name}")
        if name in visited:
            return
        visiting.add(name)
        for dep in catalog[name]["depends_on"]:
            visit(dep)
        visiting.remove(name)
        visited.add(name)

    for name in catalog:
        visit(name)
    return len(catalog)


# 按实际 Runtime 与工具构建模式选择验证入口，未知模式和空名称不能被跳过。
def select_tests(entries, mode, build_tools):
    selected = []
    for entry in entries:
        match = re.fullmatch(r"(?:(dev|tools):)?([A-Za-z_][A-Za-z0-9_]*)", entry)
        if not match:
            raise ValueError(f"非法验证入口或模式：{entry}")
        scope, name = match.groups()
        if scope == "dev" and mode != "development":
            continue
        if scope == "tools" and not build_tools:
            continue
        selected.append(name)
    return selected


# 查询当前构建的真实 CTest 注册信息并执行校验。
def main():
    parser = argparse.ArgumentParser(description="检查服务端 intent 与构建的一致性")
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--ctest", default="ctest")
    parser.add_argument("--config", default="Release")
    parser.add_argument("--mode", choices=("development", "production"), required=True)
    parser.add_argument("--build-tools", choices=("0", "1"), required=True)
    args = parser.parse_args()
    targets = json.loads((args.build / "hunter-targets.json").read_text(encoding="utf-8"))
    result = subprocess.run([args.ctest, "--test-dir", str(args.build), "-C", args.config,
                             "--show-only=json-v1"], capture_output=True, text=True,
                            encoding="utf-8", check=True, timeout=30)
    tests = [test["name"] for test in json.loads(result.stdout)["tests"]]
    count = verify(args.root, targets, tests, args.mode, args.build_tools == "1")
    print(f"已校验 {count} 份契约（{args.mode}，本机工具={args.build_tools}）")


if __name__ == "__main__":
    main()

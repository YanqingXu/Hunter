# 读取 CMake 实际链接片段，阻止生产可执行文件带入 Luax 编译器或开发运行时。
import argparse
import json
from pathlib import Path

FORBIDDEN = ("luax_compiler", "luax_ast", "luax_development_runtime")


# 检查链接器实际参数，而不是依赖手写的安全目标名单。
def verify(build, target, config):
    reply = Path(build) / ".cmake" / "api" / "v1" / "reply"
    index = max(reply.glob("index-*.json"), key=lambda path: path.stat().st_mtime_ns)
    doc = json.loads(index.read_text(encoding="utf-8"))
    model = json.loads((reply / doc["reply"]["codemodel-v2"]["jsonFile"]).read_text())
    for cfg in model["configurations"]:
        if cfg["name"] != config:
            continue
        for entry in cfg["targets"]:
            if entry["name"] != target:
                continue
            detail = json.loads((reply / entry["jsonFile"]).read_text())
            fragments = " ".join(item["fragment"] for item in detail["link"]["commandFragments"])
            normalized = fragments.lower().replace("\\", "/")
            if any(name in normalized for name in FORBIDDEN):
                raise ValueError("生产链接图含有开发 Runtime、Compiler 或 AST")
            if "luax_runtime" not in normalized:
                raise ValueError("生产链接图缺少 compiler-free Luax Runtime")
            return
    raise ValueError(f"找不到 {config}/{target} 的实际链接信息")


# 为 CTest 提供只读的生产链接校验入口。
def main():
    parser = argparse.ArgumentParser(description="检查生产 Runtime 链接边界")
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--target", default="hunter_server_desktop")
    parser.add_argument("--config", default="Release")
    args = parser.parse_args()
    verify(args.build, args.target, args.config)
    print("生产链接图不含开发 Runtime、Compiler 或 AST")


if __name__ == "__main__":
    main()

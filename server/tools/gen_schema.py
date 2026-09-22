# 从正式契约生成桥接校验常量；不支持的 schema 在构建阶段拒绝，禁止静默沿用旧值。
import argparse
import json
import os
import re
import tempfile
from pathlib import Path


# 解析契约明确给出的闭区间，并确认原生类型能够无损表达。
def interval(text, prefix, lower, upper):
    if not isinstance(text, str):
        raise ValueError("schema interval must be a string")
    match = re.fullmatch(re.escape(prefix) + r" \[(-?\d+),(-?\d+)\]", text)
    if not match:
        raise ValueError(f"unsupported schema interval: {text}")
    start, end = map(int, match.groups())
    if not lower <= start <= end <= upper:
        raise ValueError(f"schema interval exceeds native type: {text}")
    return start, end


# 输出精确的有符号 C++ 常量，避免最小 i64 被先解析为无符号字面量。
def signed_literal(value):
    return "(-9223372036854775807LL - 1)" if value == -(1 << 63) else f"{value}LL"


# 校验当前支持的消息形状并生成共享字段和边界，字段顺序不影响结果。
def generate(doc):
    if not isinstance(doc, dict) or type(doc.get("version")) is not int or doc["version"] != 1:
        raise ValueError("unsupported contract version")
    state, effect = doc.get("state"), doc.get("effect")
    if not isinstance(state, dict) or not isinstance(effect, dict):
        raise ValueError("contract requires state and effect schemas")
    if set(state) != {"v", "seq", "tick_id", "count", "extra_fields"}:
        raise ValueError("unsupported state fields")
    ack = effect.get("ack")
    if not isinstance(ack, dict) or set(ack) != {"v", "seq", "count"}:
        raise ValueError("unsupported ack fields")
    if state["extra_fields"] is not False or effect.get("snapshot") != "state schema":
        raise ValueError("snapshot must use the exact state schema")
    if effect.get("kinds") != ["ack", "snapshot"]:
        raise ValueError("unsupported output kinds")
    versions = (state.get("v"), effect.get("v"), ack.get("v"))
    if any(type(value) is not int or value != 1 for value in versions):
        raise ValueError("all output schemas must use integer version 1")
    if ack["seq"] != "positive uint64 decimal string" or ack["count"] != "bounded integer":
        raise ValueError("ack must use positive sequence and bounded state count")
    min_count, max_count = interval(state["count"], "integer", -(1 << 63), (1 << 63) - 1)
    min_tick, max_tick = interval(state["tick_id"], "canonical decimal string", 0, (1 << 63) - 1)
    min_seq, max_seq = interval(state["seq"], "canonical decimal string", 0, (1 << 64) - 1)
    if min_tick != 0 or min_seq != 0 or max_seq != (1 << 64) - 1:
        raise ValueError("state requires nonnegative Tick and complete uint64 sequence")
    lines = ["// 从 lua/contract.json 生成的桥接边界；请修改契约后重新构建。", "#pragma once", "",
             '#include "common/Types.h"', "", "#include <array>", "#include <string_view>", "",
             "namespace hunter::schema", "{",
             f"inline constexpr i32 version = {versions[0]};",
             "inline constexpr u64 min_ack_seq = 1ULL;",
             f"inline constexpr u64 max_seq = {max_seq}ULL;",
             f"inline constexpr u64 max_tick = {max_tick}ULL;",
             f"inline constexpr i64 min_count = {signed_literal(min_count)};",
             f"inline constexpr i64 max_count = {signed_literal(max_count)};", ""]
    for name, fields in (("ack", ack), ("snapshot", set(state) - {"extra_fields"})):
        keys = ", ".join(json.dumps(key) for key in sorted(fields))
        lines.append(f"inline constexpr std::array<std::string_view, {len(fields)}> {name}_fields"
                     + "{" + keys + "};")
    return "\n".join(lines + ["}", ""])


# 完整准备头文件后单次替换，失败保留旧输出并移除本次临时文件。
def write_header(output, header):
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    staged = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="\n",
                                         dir=output.parent, delete=False) as stream:
            staged = Path(stream.name)
            stream.write(header)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(staged, output)
    finally:
        if staged is not None:
            staged.unlink(missing_ok=True)


# 向构建目录输出校验头，契约错误或写入失败以非零退出报告。
def main():
    parser = argparse.ArgumentParser(description="从 Hunter 契约生成输出 schema 校验常量")
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        doc = json.loads(args.contract.read_text(encoding="utf-8"))
        write_header(args.output, generate(doc))
    except (OSError, ValueError) as error:
        parser.exit(1, f"schema: {error}\n")


if __name__ == "__main__":
    main()

# 从唯一契约生成闭集输出描述；构建阶段拒绝未知字段、无界容器和无法表示的范围。
import argparse
import json
import os
import re
import tempfile
from pathlib import Path

KINDS = {"ack", "snapshot", "login", "start", "event", "error"}
FIELDS = {
    "ack": {"v", "seq", "match_id", "applied_tick"},
    "snapshot": {"v", "tick_id", "seq", "match_id", "phase", "entities"},
    "login": {"v", "req_id", "player_id", "match_id", "phase"},
    "start": {"v", "req_id", "match_id", "phase"},
    "event": {"v", "match_id", "event_id", "tick_id", "kind", "actor_id",
              "target_id", "x", "y", "amount"},
    "error": {"v", "code", "detail", "req_id", "seq", "match_id"},
}
ENTITY_FIELDS = {"id", "cfg_id", "kind", "x", "y", "vx", "vy", "hp", "max_hp", "ammo", "reserve",
                 "reload_ticks", "grounded", "alive", "facing", "ai"}


# 检查有上下限的整数描述，拒绝把 JSON 布尔值当作整数。
def bounds(node, lower, upper):
    lo, hi = node.get("min"), node.get("max")
    if type(lo) is not int or type(hi) is not int or not lower <= lo <= hi <= upper:
        raise ValueError("invalid integer schema bounds")


# 验证当前六类输出使用的有限描述集合，不提供引用、递归类型或任意扩展。
def validate_node(node, depth=0):
    if not isinstance(node, dict) or depth > 4:
        raise ValueError("invalid or excessively nested schema")
    kind = node.get("type")
    keys = set(node)
    if kind == "object":
        if keys != {"type", "fields"} or not isinstance(node["fields"], dict):
            raise ValueError("object requires exact fields")
        if not 1 <= len(node["fields"]) <= 32:
            raise ValueError("object field count")
        for name, child in node["fields"].items():
            if not re.fullmatch(r"[a-z][a-z0-9_]*", name):
                raise ValueError("invalid schema field name")
            validate_node(child, depth + 1)
    elif kind == "array":
        if keys != {"type", "max", "item"} or type(node["max"]) is not int:
            raise ValueError("array requires a finite maximum")
        if not 1 <= node["max"] <= 64:
            raise ValueError("array maximum exceeds gameplay bound")
        validate_node(node["item"], depth + 1)
    elif kind == "id":
        if keys != {"type", "min", "max"}:
            raise ValueError("id requires exact bounds")
        for key in ("min", "max"):
            value = node[key]
            if not isinstance(value, str) or not re.fullmatch(r"0|[1-9][0-9]*", value):
                raise ValueError("id bounds must be canonical decimal strings")
        if not 0 <= int(node["min"]) <= int(node["max"]) <= (1 << 64) - 1:
            raise ValueError("id range exceeds uint64")
    elif kind in ("int", "string"):
        if keys not in ({"type", "min", "max"}, {"type", "min", "max", "values"}):
            raise ValueError("unexpected scalar schema fields")
        bounds(node, -(1 << 31) if kind == "int" else 0,
               (1 << 31) - 1 if kind == "int" else 4096)
        if "values" in node:
            values = node["values"]
            if not isinstance(values, list) or not 1 <= len(values) <= 16:
                raise ValueError("invalid closed value set")
            if any(type(value) is not (int if kind == "int" else str) for value in values):
                raise ValueError("closed values have wrong type")
            if len(set(values)) != len(values):
                raise ValueError("duplicate closed values")
            if any(not node["min"] <= (value if kind == "int" else len(value.encode()))
                   <= node["max"] for value in values):
                raise ValueError("closed value exceeds schema bounds")
    elif kind == "bool":
        if keys != {"type"}:
            raise ValueError("bool cannot carry extra schema fields")
    else:
        raise ValueError("unsupported schema node type")


# 校验冻结消息形状并生成唯一原生描述，合法边界改动会直接反映到产物。
def generate(doc):
    if not isinstance(doc, dict) or type(doc.get("version")) is not int or doc["version"] != 3:
        raise ValueError("unsupported contract version")
    state = doc.get("state")
    if not isinstance(state, dict) or type(state.get("v")) is not int or state["v"] != 3:
        raise ValueError("state schema must declare version 3")
    effect = doc.get("effect")
    if not isinstance(effect, dict) or type(effect.get("v")) is not int or effect["v"] != 3:
        raise ValueError("effect schema must declare version 3")
    host = doc.get("host_api")
    ctx = host.get("ctx") if isinstance(host, dict) else None
    if not isinstance(ctx, dict) or type(ctx.get("v")) is not int or ctx["v"] != 3:
        raise ValueError("context schema must declare version 3")
    input_spec = effect.get("input", {})
    if not isinstance(input_spec, dict) or type(input_spec.get("v")) is not int \
            or input_spec["v"] != 3:
        raise ValueError("input schema must declare version 3")
    schemas = effect.get("schemas")
    kinds = effect.get("kinds")
    if not isinstance(kinds, list) or len(kinds) != len(KINDS) or set(kinds) != KINDS:
        raise ValueError("unsupported output kinds")
    if not isinstance(schemas, dict) or set(schemas) != KINDS:
        raise ValueError("missing or unknown output schema")
    for name, node in schemas.items():
        validate_node(node)
        if node["type"] != "object" or set(node["fields"]) != FIELDS[name]:
            raise ValueError("unsupported message fields")
        if node["fields"]["v"] != {"type": "int", "min": 3, "max": 3}:
            raise ValueError("all messages require integer version 3")
        for field, spec in node["fields"].items():
            expected = ("id" if (field.endswith("_id") and field != "req_id")
                        or field in {"seq", "applied_tick"} else
                        "int" if field in {"v", "x", "y", "amount"} else
                        "array" if field == "entities" else "string")
            if spec["type"] != expected:
                raise ValueError("message field cannot change its wire type")
    entity = schemas["snapshot"]["fields"]["entities"]
    if entity["type"] != "array" or entity["item"]["type"] != "object":
        raise ValueError("snapshot requires entity objects")
    if set(entity["item"]["fields"]) != ENTITY_FIELDS:
        raise ValueError("unsupported entity fields")
    for field, spec in entity["item"]["fields"].items():
        expected = ("id" if field in {"id", "cfg_id"} else "string" if field in {"kind", "ai"}
                    else "bool" if field in {"grounded", "alive"} else "int")
        if spec["type"] != expected:
            raise ValueError("entity field cannot change its wire type")
    cfg_id = entity["item"]["fields"]["cfg_id"]
    if int(cfg_id["min"]) < 1 or int(cfg_id["max"]) > 2147483647:
        raise ValueError("configuration ID exceeds uint32 content range")
    for name, node in schemas.items():
        for field, spec in node["fields"].items():
            if field in {"tick_id", "applied_tick"}:
                if spec["type"] != "id" or int(spec["max"]) > (1 << 63) - 1:
                    raise ValueError("Tick exceeds signed runtime range")
            elif (field.endswith("_id") and field != "req_id") or field == "seq":
                if spec["type"] != "id":
                    raise ValueError("domain IDs require exact decimal representation")
    payload = json.dumps(schemas, sort_keys=True, ensure_ascii=True, separators=(",", ":"))
    return ("// 从 lua/contract.json 生成的闭集输出描述；请修改契约后重新构建。\n"
            "#pragma once\n\n"
            '#include "common/Types.h"\n\n'
            "namespace hunter::schema\n{\n"
            "inline constexpr i32 version = 3;\n"
            'inline constexpr const char* outputs = R"SCHEMA(' + payload + ')SCHEMA";\n'
            "}\n")


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
    parser = argparse.ArgumentParser(description="从 Hunter 契约生成输出 schema 校验描述")
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

# 读取显式根表清单并导出 Lua；玩法投影和语义校验只由同一份 Luax 脚本执行。
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

import export as sheets

ROOT = sheets.ROOT
sys.path.insert(0, str(ROOT / "server/tools"))
import assemble

TABLES = set("Player Weapon Ammunition Item Equip Tool Monster Attack Drop DropEntry "
             "Map MapSolid PlayerSpawn MonsterSpawn ExtractPoint Bag Rules Loadout Scene".split())


MAX_BYTES = 60 * 1024


# 拒绝重复 JSON 字段，清单与测试夹具不得依赖解析器覆盖顺序。
def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


# 只检查来源清单结构，玩法字段和含义由 Lua 负责。
def fields(value, names, name):
    if type(value) is not dict or set(value) != set(names.split()):
        raise ValueError(f"{name}: expected exactly {names}")


# 清单记录主键必须是实际整数，禁止布尔或浮点别名。
def integer_value(value, low, high, name):
    if type(value) is not int or not low <= value <= high:
        raise ValueError(f"{name}: expected integer [{low},{high}]")


# 只解析清单选中的记录，未选中的不完整草稿和以井号开头的说明行不进入生产数据。
def select(sheet, ids):
    fields = sheets.read_fields(sheet)
    keys = [field for field in fields if "key" in field.flags]
    if len(keys) != 1 or keys[0].kind != "int32" or "server" not in keys[0].flags:
        sheet.fail(4, 1, "生产表必须有唯一的 server/key int32 主键")
    key = keys[0]
    server = [field for field in fields if "server" in field.flags]
    rows = {}
    for number in sorted({r for r, c in sheet.cells if r >= 5}):
        cell = sheet.cell(number, key.column)
        if not cell.text or cell.text.startswith("#"):
            continue
        identity = sheets.convert(sheet, number, key)
        if identity not in ids:
            continue
        if identity in rows:
            sheet.fail(number, key.column, f"生产主键重复: {identity}")
        rows[identity] = {field.name: sheets.convert(sheet, number, field) for field in server}
    if set(rows) != set(ids):
        sheet.fail(1, 1, f"清单记录不存在: {sorted(set(ids) - set(rows))}")
    return sheets.Table(sheet.name.split("|")[-1], sheet, server, rows)


# 显式清单是唯一生产入口，拒绝目录逃逸、重复表和隐式补入依赖。
def read_tables(source):
    source = Path(source).resolve()
    spec = json.loads(source.read_text(encoding="utf-8"), object_pairs_hook=unique_pairs)
    fields(spec, "v sources", "manifest")
    if spec["v"] != 1 or not isinstance(spec["sources"], list):
        raise ValueError("manifest: unsupported version or sources")
    books, tables = {}, {}
    for entry in spec["sources"]:
        fields(entry, "file sheet ids", "source")
        if not isinstance(entry["file"], str) or not isinstance(entry["sheet"], str):
            raise ValueError("source: file and sheet must be strings")
        path = (source.parent / entry["file"]).resolve()
        if path.parent != source.parent or path.suffix.lower() != ".xlsx":
            raise ValueError("source: only root design workbooks are allowed")
        ids = entry["ids"]
        if not isinstance(ids, list) or not ids or len(set(ids)) != len(ids):
            raise ValueError("source.ids: expected nonempty unique IDs")
        for key in ids:
            integer_value(key, 1, 2147483647, "source.ids")
        if path not in books:
            books[path] = {sheet.name: sheet for sheet in sheets.read_workbook(path)}
        if entry["sheet"] not in books[path]:
            raise ValueError(f"{path}: missing worksheet {entry['sheet']}")
        table = select(books[path][entry["sheet"]], ids)
        if table.name in tables:
            raise ValueError(f"duplicate production table: {table.name}")
        tables[table.name] = table
    if set(tables) != TABLES:
        raise ValueError(f"manifest: expected tables {sorted(TABLES)}")
    return tables


# 从固定依赖的本地工具选择解释器，不使用系统 PATH 中的其他 Lua 实现。
def luax_cli(value=None):
    selected = value or os.environ.get("HUNTER_LUAX") or os.environ.get("LUAX")
    if selected:
        path = Path(selected).resolve(strict=True)
    else:
        paths = [ROOT / f"server/build/{build}/_deps/luax-build/Release/luax.exe"
                 for build in ("win-dev", "win-bundle")]
        path = next((path for path in paths if path.is_file()), None)
    if path is None or not path.is_file():
        raise ValueError("fixed Luax CLI is required; build luax_cli or pass --luax")
    return path


# 把显式测试 JSON 转为保持对象和数组类型的 Lua 常量，不解释任何玩法字段。
def literal(value):
    if value is None:
        return "json.null"
    if type(value) is bool:
        return "true" if value else "false"
    if type(value) is int:
        return str(value)
    if type(value) is str:
        return sheets.lua_string(value)
    if type(value) is list:
        return "json.array({" + ",".join(literal(item) for item in value) + "})"
    if type(value) is dict and all(type(key) is str for key in value):
        return "json.object({" + ",".join("[" + sheets.lua_string(key) + "]=" + literal(value[key])
                                          for key in sorted(value)) + "})"
    raise ValueError("fixture supports only exact integer JSON values")


# 生产各表保留原始英文字段与数值主键，聚合模块只声明显式依赖。
def config_files(tables=None, fixture=None):
    files, modules, names = {}, [], []
    if fixture is not None:
        files["Fixture.lua"] = ("-- 显式测试夹具，不属于生产源表。\nreturn "
                                + literal(fixture) + "\n").encode("utf-8")
        names = ["Fixture"]
    else:
        if set(tables) != TABLES:
            raise ValueError("production requires the selected source table set")
        for name, table in sorted(tables.items()):
            files[name + ".lua"] = sheets.render_lua(table)
            names.append(name)
    for name in names:
        modules.append({"name": "cfg." + name.lower(), "file": name + ".lua",
                        "kind": "data", "deps": []})
    lines = ["-- 显式聚合本次生成的数据表，不读取目录中的其他文件。",
             "return function(deps)", "    return {"]
    for name in names:
        lines.append(f'        [{sheets.lua_string(name)}] = deps["cfg.{name.lower()}"],')
    lines += ["    }", "end", ""]
    files["Tables.lua"] = "\n".join(lines).encode("utf-8")
    modules.append({"name": "cfg.tables", "file": "Tables.lua", "kind": "factory",
                    "deps": ["cfg." + name.lower() for name in names]})
    manifest = {"version": 1, "entry": "cfg.tables", "modules": modules}
    files["Manifest.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    return files


# 只组装配置入口的依赖闭包，避免构建校验意外触碰世界或其他 Host 接口。
def validation_source(cfg):
    _, modules = assemble.load_manifest(ROOT / "server/lua/modules.json")
    _, configs = assemble.load_manifest(Path(cfg) / "Manifest.json", data=True)
    modules.update(configs)
    required, pending = {}, ["game.cfg"]
    while pending:
        name = pending.pop()
        if name not in required:
            if name not in modules:
                raise ValueError(f"missing validation module: {name}")
            required[name] = modules[name]
            pending.extend(modules[name]["deps"])
    return assemble.assemble_modules("game.cfg", required,
        footer="return json.encode(entry.load())")[0]


# 在临时目录用固定 Luax strict 执行正式加载器，失败时正式输出完全不动。
def evaluate(files, luax=None):
    cli = luax_cli(luax)
    with tempfile.TemporaryDirectory(prefix="hunter-lua-cfg-") as folder:
        root = Path(folder)
        for name, data in files.items():
            (root / name).write_bytes(data)
        script = root / "validate.lua"
        script.write_text(validation_source(root), encoding="utf-8", newline="\n")
        run = subprocess.run([str(cli), "--profile", "strict", str(script)],
                             capture_output=True, timeout=60)
        if run.returncode != 0:
            detail = run.stderr.decode("utf-8", errors="replace").strip()
            raise ValueError("Lua configuration validation failed: " + detail)
        try:
            value = json.loads(run.stdout.decode("utf-8"), object_pairs_hook=unique_pairs)
        except (UnicodeError, ValueError) as error:
            raise ValueError("Lua configuration loader did not return one JSON object") from error
    return value


# 只对 Lua 返回的数据进行通用规范编码和摘要，不保存第二套 Python 玩法模型。
def content_bytes(value):
    data = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    if len(data) > MAX_BYTES:
        raise ValueError("gameplay content exceeds 60 KiB")
    version = "gameplay-v4:" + hashlib.sha256(data).hexdigest()
    header = ('// 从 Lua 配置派生的内容身份，禁止手工修改。\n#pragma once\n'
              '#include <string_view>\nnamespace hunter::content\n{\n'
              f'inline constexpr std::string_view version = "{version}";\n}}\n')
    return data, header.encode("utf-8")


# 从真实源表经 Lua 得到客户端共享 JSON 和不含配置数值的身份头文件。
def artifacts(tables, luax=None):
    return content_bytes(evaluate(config_files(tables), luax))


# 显式测试内容仍由 Lua 升级与校验，不能作为生产导出的回退入口。
def upgrade_fixture(old, luax=None):
    return evaluate(config_files(fixture=old), luax)


# 测试需要的已规范内容同样走 Lua 语义验证，不提供绕过路径。
def content_artifacts(doc, luax=None):
    return content_bytes(upgrade_fixture(doc, luax))


# 先完成完整校验，再一起发布 Lua 数据、共享 JSON 与身份文件。
def export_content(source=None, fixture=None, output=None, header=None, cfg=None,
                   luax=None, check=False):
    if (source is None) == (fixture is None):
        raise ValueError("choose exactly one source manifest or explicit fixture")
    tables = None
    if fixture is not None:
        value = json.loads(Path(fixture).read_text(encoding="utf-8"),
                           object_pairs_hook=unique_pairs)
        files = config_files(fixture=value)
    else:
        tables = read_tables(source)
        files = config_files(tables)
    data, identity = content_bytes(evaluate(files, luax))
    if not check:
        if output is None or header is None or cfg is None:
            raise ValueError("output, header and cfg paths are required")
        targets = [(Path(cfg) / name, value) for name, value in files.items()]
        targets += [(Path(output), data), (Path(header), identity)]
        sheets.PUBLISH.publish_files(targets)
    return tables, data


# 命令行、CMake 与策划窗口共用同一个显式清单和失败不发布入口。
def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--source", type=Path)
    source.add_argument("--fixture", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--header", type=Path)
    parser.add_argument("--cfg", type=Path)
    parser.add_argument("--luax", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.check and (args.output is None or args.header is None or args.cfg is None):
        parser.error("--output, --header and --cfg are required unless --check is used")
    try:
        export_content(args.source, args.fixture, args.output, args.header, args.cfg,
                       args.luax, args.check)
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired) as error:
        parser.exit(1, f"gameplay export failed: {error}\n")


if __name__ == "__main__":
    main()

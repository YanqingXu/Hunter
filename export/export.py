# 从四行表头的 Excel 配置生成服务端 Lua；只使用 Python 标准库，不接入运行时。
import argparse
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
import importlib.util
from pathlib import Path
import posixpath
import re
import sys
from urllib.parse import unquote
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile


if getattr(sys, "frozen", False):
    # 单文件 EXE 内置发布模块，项目目录仍以 EXE 所在的 export 目录定位。
    ROOT = Path(sys.executable).resolve().parent.parent
    import publish as PUBLISH
else:
    ROOT = Path(__file__).resolve().parents[1]
    SPEC = importlib.util.spec_from_file_location("hunter_publish", ROOT / "server/tools/publish.py")
    PUBLISH = importlib.util.module_from_spec(SPEC)
    SPEC.loader.exec_module(PUBLISH)
IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
INTEGER_TEXT = re.compile(r"-?(?:0|[1-9][0-9]*)\Z")
EXCEL_NUMBER = re.compile(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[Ee][+-]?[0-9]+)?\Z")
ADDRESS = re.compile(r"([A-Z]+)([1-9][0-9]*)\Z")
ESCAPE = re.compile(r"_x([0-9A-Fa-f]{4})_")
FLAGS = {"server", "client", "key", "index"}
RESERVED = {"con", "prn", "aux", "nul", "clock$"} | {
    f"{prefix}{number}" for prefix in ("com", "lpt") for number in range(1, 10)
}


class ConfigError(ValueError):
    """可直接展示给填表人员的定位错误。"""


@dataclass(frozen=True)
class Cell:
    kind: str = "n"
    text: str = ""
    formula: bool = False


@dataclass(frozen=True)
class Field:
    column: int
    name: str
    kind: str
    flags: frozenset


@dataclass
class Sheet:
    path: Path
    name: str
    cells: dict

    # 所有填表错误统一携带源文件、工作表与 A1 地址。
    def fail(self, row, column, message):
        raise ConfigError(f"{self.path} [{self.name}]!{column_name(column)}{row}: {message}")

    def cell(self, row, column):
        return self.cells.get((row, column), Cell())


@dataclass
class Table:
    name: str
    sheet: Sheet
    fields: list
    rows: dict


# Excel 列序号只用于定位和顺序，不依赖表名或业务字段。
def column_name(number):
    result = ""
    while number:
        number, digit = divmod(number - 1, 26)
        result = chr(65 + digit) + result
    return result


# 同时识别常规和 strict OOXML 命名空间。
def namespace(root):
    return root.tag.split("}")[0] + "}" if root.tag.startswith("{") else ""


# 按包内关系解析目标，拒绝外部关系或逃出压缩包的路径。
def part_target(base, relationship):
    target = unquote(relationship.get("Target", ""))
    if relationship.get("TargetMode") == "External" or not target or "\\" in target or ":" in target:
        raise ConfigError(f"不支持的 Excel 内部关系: {target!r}")
    result = posixpath.normpath(posixpath.join(posixpath.dirname(base), target))
    if target.startswith("/"):
        result = posixpath.normpath(target.lstrip("/"))
    if result in (".", "..") or result.startswith("../"):
        raise ConfigError(f"Excel 内部关系越界: {target!r}")
    return result


# 只拼接正文文本，忽略东亚文字的拼音标注；转义只解码一次以保留字面 _xNNNN_。
def excel_text(element):
    ns = namespace(element)
    pieces = []
    for child in element:
        if child.tag == ns + "t":
            pieces.append(child.text or "")
        elif child.tag == ns + "r":
            pieces.extend(t.text or "" for t in child.findall(ns + "t"))
    return ESCAPE.sub(lambda match: chr(int(match[1], 16)), "".join(pieces))


# 保留原始单元格类型及公式标志；不使用 Excel 的公式缓存结果。
def read_sheet(path, name, root, shared):
    sheet = Sheet(path, name, {})
    ns = namespace(root)
    for row in root.findall(f"{ns}sheetData/{ns}row"):
        for element in row.findall(ns + "c"):
            address = element.get("r", "")
            match = ADDRESS.fullmatch(address)
            if not match:
                sheet.fail(1, 1, f"单元格地址无效: {address!r}")
            col = 0
            for char in match[1]:
                col = col * 26 + ord(char) - 64
            number = int(match[2])
            if col > 16384 or number > 1048576 or row.get("r", str(number)) != str(number):
                sheet.fail(number, col, "单元格地址超出 Excel 范围或与所在行不一致")
            if (number, col) in sheet.cells:
                sheet.fail(number, col, "重复的单元格地址")
            kind = element.get("t", "n")
            value = element.findtext(ns + "v", "")
            if kind == "s":
                if not re.fullmatch(r"[0-9]+", value) or int(value) >= len(shared):
                    sheet.fail(number, col, "共享字符串引用无效")
                value = shared[int(value)]
                kind = "text"
            elif kind == "inlineStr":
                body = element.find(ns + "is")
                value = excel_text(body) if body is not None else ""
                kind = "text"
            elif kind == "str":
                value = ESCAPE.sub(lambda m: chr(int(m[1], 16)), value)
                kind = "text"
            sheet.cells[number, col] = Cell(kind, value, element.find(ns + "f") is not None)
    return sheet


# 用关系 ID 找到工作表和共享字符串，不假定 sheetId 等于文件编号。
def read_workbook(path):
    current = "工作簿"
    try:
        with ZipFile(path) as archive:
            base = "xl/workbook.xml"
            workbook = ET.fromstring(archive.read(base))
            rels = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
            relationships = {rel.get("Id"): rel for rel in rels}
            shared = []
            for rel in rels:
                if rel.get("Type", "").endswith("/sharedStrings"):
                    root = ET.fromstring(archive.read(part_target(base, rel)))
                    shared = [excel_text(si) for si in root.findall(namespace(root) + "si")]
            ns = namespace(workbook)
            for entry in workbook.findall(f"{ns}sheets/{ns}sheet"):
                current = entry.get("name", "")
                if "|" not in current:
                    continue
                rel_id = next((v for k, v in entry.attrib.items() if k.endswith("}id")), None)
                rel = relationships.get(rel_id)
                if rel is None or not rel.get("Type", "").endswith("/worksheet"):
                    raise ConfigError("缺少有效的 worksheet 关系")
                root = ET.fromstring(archive.read(part_target(base, rel)))
                yield read_sheet(path, current, root, shared)
    except ConfigError as error:
        if str(error).startswith(str(path)):
            raise
        raise ConfigError(f"{path} [{current}]!A1: {error}") from error
    except (OSError, BadZipFile, KeyError, ET.ParseError, RuntimeError, ValueError) as error:
        raise ConfigError(f"{path} [{current}]!A1: 无法读取 Excel: {error}") from error


# 先拒绝公式和 Excel 错误值，再处理字段类型。
def checked_cell(sheet, row, column):
    cell = sheet.cell(row, column)
    if cell.formula:
        sheet.fail(row, column, "不允许公式，请填写常量（不读取缓存值）")
    if cell.kind == "e":
        sheet.fail(row, column, f"Excel 错误值: {cell.text}")
    return cell


# 表头必须为非空文本；去掉边缘空白以兼容人工录入。
def header(sheet, row, column):
    cell = checked_cell(sheet, row, column)
    if cell.kind != "text" or not cell.text.strip():
        sheet.fail(row, column, "表头必须填写非空文本")
    return cell.text.strip()


# 校验连续四行字段定义；只对服务端列要求已支持的值类型。
def read_fields(sheet):
    columns = sorted(col for (row, col), cell in sheet.cells.items()
                     if row == 3 and (cell.text or cell.formula))
    if not columns:
        sheet.fail(3, 1, "缺少字段名")
    for expected, actual in enumerate(columns, 1):
        if expected != actual:
            sheet.fail(3, expected, "字段必须从 A 列连续声明，不能插入空列")
    for (row, col), cell in sheet.cells.items():
        if row == 4 and col > len(columns) and (cell.text or cell.formula):
            sheet.fail(3, col, "存在导出标记却缺少字段名")
    fields = []
    seen = set()
    for col in columns:
        header(sheet, 1, col)
        kind = header(sheet, 2, col)
        name = header(sheet, 3, col)
        if not IDENTIFIER.fullmatch(name) or name in seen:
            sheet.fail(3, col, f"英文字段名无效或重复: {name!r}")
        seen.add(name)
        tokens = [flag.strip() for flag in header(sheet, 4, col).split("/")]
        flags = frozenset(tokens)
        if not flags <= FLAGS or len(flags) != len(tokens) or not flags & {"server", "client"}:
            sheet.fail(4, col, "标记只支持 server/client/key/index，以 / 分隔且不得重复")
        if "server" in flags and kind not in ("int32", "string"):
            sheet.fail(2, col, f"不支持服务端类型 {kind!r}，只支持 int32/string")
        fields.append(Field(col, name, kind, flags))
    return fields


# 数值单元格允许等值的整数表示（如 1.0、1E2），文本只接受规范十进制整数。
def convert(sheet, row, field):
    cell = checked_cell(sheet, row, field.column)
    if field.kind == "string":
        if cell.text == "" and cell.kind in ("n", "text"):
            return ""
        if cell.kind != "text":
            sheet.fail(row, field.column, f"{field.name}: string 必须是文本单元格")
        try:
            return cell.text.encode("utf-16-le", "surrogatepass").decode("utf-16-le")
        except UnicodeError:
            sheet.fail(row, field.column, f"{field.name}: 文本含无效 Unicode 字符")
    pattern = INTEGER_TEXT if cell.kind == "text" else EXCEL_NUMBER
    if cell.kind not in ("text", "n") or not pattern.fullmatch(cell.text):
        sheet.fail(row, field.column, f"{field.name}: 必须填写 int32 整数，不能留空")
    try:
        number = Decimal(cell.text)
        if (not number.is_finite() or not -2147483648 <= number <= 2147483647
                or number != number.to_integral_value()):
            raise ValueError()
        return int(number)
    except (InvalidOperation, ValueError, OverflowError):
        sheet.fail(row, field.column, f"{field.name}: 必须为 -2147483648～2147483647 的整数")


# 以主键聚合行；客户端列也参与空行判断，避免漏掉只填了名称的半行数据。
def parse_table(sheet):
    names = sheet.name.split("|")
    if (len(names) != 2 or not names[0].strip() or not IDENTIFIER.fullmatch(names[-1])
            or names[-1].casefold() in RESERVED):
        sheet.fail(1, 1, "工作表须为 中文名|EnglishName，英文名不能是 Windows 保留文件名")
    fields = read_fields(sheet)
    server = [field for field in fields if "server" in field.flags]
    if not server:
        return None
    keys = [field for field in fields if "key" in field.flags]
    if len(keys) != 1 or "server" not in keys[0].flags or keys[0].kind != "int32":
        sheet.fail(4, 1, "必须且只能有一个导出的 int32 主键（server/key）")
    key = keys[0]
    rows = {}
    positions = {}
    data_rows = sorted({row for (row, col), cell in sheet.cells.items()
                        if row >= 5 and col <= len(fields) and (cell.text or cell.formula)})
    for row in data_rows:
        identity = convert(sheet, row, key)
        if identity <= 0 or identity in rows:
            detail = f"，与第 {positions[identity]} 行重复" if identity in rows else ""
            sheet.fail(row, key.column, f"主键必须为唯一正整数: {identity}{detail}")
        rows[identity] = {field.name: convert(sheet, row, field) for field in server}
        positions[identity] = row
    return Table(names[1], sheet, server, rows)


# 全目录读取、校验完毕后才允许发布，表名按大小写不敏感规则判重。
def scan_workbooks(source):
    source = Path(source)
    if not source.is_dir():
        raise ConfigError(f"输入目录不存在或不是目录: {source}")
    return sorted((p for p in source.rglob("*") if p.is_file() and p.suffix.lower() == ".xlsx"
                   and not p.name.startswith("~$")), key=lambda p: p.as_posix())


# 全目录或策划选定的工作簿均走同一条校验路径，未选中的文件不读取。
def collect_tables(source, *, workbooks=None):
    paths = scan_workbooks(source) if workbooks is None else workbooks
    tables = []
    seen = {}
    for path in paths:
        for sheet in read_workbook(path):
            table = parse_table(sheet)
            if table is None:
                continue
            name = table.name.casefold()
            if name in seen:
                old = seen[name]
                sheet.fail(1, 1, f"输出文件名冲突: {table.name}.lua；已用于 {old.path} [{old.name}]")
            seen[name] = sheet
            tables.append(table)
    if not tables:
        raise ConfigError(f"{source}: 没有可导出的服务端配置表")
    return sorted(tables, key=lambda table: table.name)


# 固定三位十进制转义避免控制字符后跟数字时被 Lua 合并解释；不使用 JSON 的 \u 转义。
def lua_string(value):
    pieces = ['"']
    for char in value:
        code = ord(char)
        if char in ('"', "\\"):
            pieces.append("\\" + char)
        elif code < 32 or code == 127:
            pieces.append(f"\\{code:03d}")
        else:
            pieces.append(char)
    return "".join(pieces) + '"'


# 排序只影响输出，不修改源表；不写时间戳或机器路径，保证可复现。
def render_lua(table):
    if not table.rows:
        return b"return {}\n"
    lines = ["-- 自动生成，请修改 Excel 源表。", "return {"]
    for identity, row in sorted(table.rows.items()):
        lines.append(f"    [{identity}] = {{")
        for field in table.fields:
            value = row[field.name]
            literal = lua_string(value) if isinstance(value, str) else str(value)
            lines.append(f"        [{lua_string(field.name)}] = {literal},")
        lines.append("    },")
    lines.extend(("}", ""))
    return "\n".join(lines).encode("utf-8")


# --check 与正式导出经过相同的读取、校验和序列化流程，只跳过发布。
def export_config(source, output, *, check=False, workbooks=None):
    tables = collect_tables(source, workbooks=workbooks)
    files = [(Path(output) / (table.name + ".lua"), render_lua(table)) for table in tables]
    if not check:
        PUBLISH.publish_files(files)
    return tables


# 相对路径统一相对仓库根目录，便于从任意工作目录调用。
def repo_path(value):
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def main(argv=None):
    parser = argparse.ArgumentParser(description="将 design Excel 配置导出为服务端 Lua（不接入运行时）")
    parser.add_argument("--source", type=repo_path, default=ROOT / "design", help="Excel 目录，默认 design")
    parser.add_argument("--output", type=repo_path, default=ROOT / "server/build/draft-cfg",
                        help="草稿 Lua 输出目录，默认 server/build/draft-cfg")
    parser.add_argument("--check", action="store_true", help="仅校验，不创建输出文件或目录")
    args = parser.parse_args(argv)
    try:
        tables = export_config(args.source, args.output, check=args.check)
    except (ConfigError, OSError, ValueError) as error:
        print(f"导表失败: {error}", file=sys.stderr)
        return 1
    books = len({table.sheet.path for table in tables})
    records = sum(len(table.rows) for table in tables)
    action = "校验通过" if args.check else "导出完成"
    print(f"{action}: {books} 个工作簿，{len(tables)} 张表，{records} 条记录")
    if not args.check:
        print(f"输出目录: {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

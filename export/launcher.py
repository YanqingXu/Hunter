# 策划双击入口：读取目标清单、导出选中工作簿，并显示中文结果窗口。
import argparse
from dataclasses import dataclass
from datetime import datetime
import os
from pathlib import Path, PurePosixPath
import sys
import traceback
import tkinter as tk
from tkinter import messagebox, scrolledtext, ttk

import export as cfg
import gameplay


TARGET_NAME = "导出目标.txt"
LOG_NAME = "导出结果.log"


@dataclass
class Report:
    ok: bool
    title: str
    detail: str
    root: Path
    draft: bool = False


# 支持记事本常见的 UTF-8、带 BOM 的 UTF-16 以及旧版中文 Windows 的 GB18030。
def read_targets(path):
    try:
        data = path.read_bytes()
    except OSError as error:
        raise cfg.ConfigError(f"无法读取 {path}\n请保留 EXE 旁边的 {TARGET_NAME}。\n{error}") from error
    encodings = ["utf-16"] if data.startswith((b"\xff\xfe", b"\xfe\xff")) else ["utf-8-sig", "gb18030"]
    for encoding in encodings:
        try:
            text = data.decode(encoding)
            break
        except UnicodeError:
            continue
    else:
        raise cfg.ConfigError(f"{path}: 无法识别文字编码，请在记事本中另存为 UTF-8。")
    entries = [(number, line.strip()) for number, line in enumerate(text.splitlines(), 1)
               if line.strip() and not line.lstrip().startswith("#")]
    if not entries:
        raise cfg.ConfigError(f"{path}: 清单为空，请每行填写一个 Excel 文件名。")
    return entries


# 清单只允许选 design 目录内的工作簿；重复或歧义名称提示行号，不静默猜测。
def select_workbooks(source, target_file):
    entries = read_targets(target_file)
    available = cfg.scan_workbooks(source)
    selected = []
    seen = {}
    for line, name in entries:
        location = f"{target_file.name} 第 {line} 行：{name}"
        name = name.replace("\\", "/")
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or ":" in name or "\0" in name:
            raise cfg.ConfigError(f"{location}\n请填写 design 内的 Excel 文件名或相对路径。")
        if not path.suffix:
            path = path.with_suffix(".xlsx")
        if path.suffix.casefold() != ".xlsx" or path.name.startswith("~$"):
            raise cfg.ConfigError(f"{location}\n只支持 .xlsx 文件，不能选择 Office 临时文件。")
        key = path.as_posix().casefold()
        matches = [p for p in available if (p.name.casefold() if len(path.parts) == 1
                   else p.relative_to(source).as_posix().casefold()) == key]
        if not matches:
            raise cfg.ConfigError(f"{location}\n没有找到文件。请确认文件位于 design 目录，且名称与清单一致。")
        if len(matches) > 1:
            choices = "\n".join(str(p.relative_to(source)) for p in matches)
            raise cfg.ConfigError(f"{location}\n有多个同名文件，请改填相对路径：\n{choices}")
        chosen = matches[0]
        if not chosen.resolve().is_relative_to(source.resolve()):
            raise cfg.ConfigError(f"{location}\n文件指向了 design 目录之外，无法导出。")
        identity = str(chosen.resolve()).casefold()
        if identity in seen:
            raise cfg.ConfigError(f"{location}\n与第 {seen[identity]} 行重复，请只保留一行。")
        seen[identity] = line
        selected.append(chosen)
    return selected


# 导出与窗口分离，便于无交互验收打包后的程序；目标错误发生在发布之前。
def run_export(root, *, check=False, draft=False):
    root = Path(root)
    source = root / "design"
    output = root / ("server/build/draft-cfg" if draft else "server/build/generated/cfg")
    target_file = root / "export" / TARGET_NAME
    try:
        if draft:
            workbooks = select_workbooks(source, target_file)
            tables = cfg.export_config(source, output, check=check, workbooks=workbooks)
        else:
            manifest = source / "demo_sources.json"
            tables, _ = gameplay.export_content(source=manifest, output=output.parent / "content.json",
                header=output.parent / "ContentId.h", cfg=output, check=check)
            tables = list(tables.values())
            workbooks = sorted({table.sheet.path for table in tables})
        action = "检查通过" if check else "导出完成"
        records = sum(len(table.rows) for table in tables)
        lines = [f"本次选择 {len(workbooks)} 个 Excel 文件，生成 {len(tables)} 张表，共 {records} 条记录。",
                 "", "本次目标：", *[f"  {p.relative_to(source)}" for p in workbooks], "",
                 "配置文件：", *[f"  {t.name}.lua（{len(t.rows)} 条）" for t in tables], "",
                 "输出目录：", str(output), "",
                 "草稿导出不会进入游戏；正式导出请使用默认入口。" if draft else
                 "已通过 Luax 玩法校验。服务端构建将加载相同源表生成的 Lua 配置。"]
        if check:
            lines[0] = lines[0].replace("生成", "可生成")
            lines.append("本次只检查，没有写入 Lua 文件。")
        report = Report(True, action, "\n".join(lines), root, draft)
    except (cfg.ConfigError, OSError, ValueError) as error:
        report = Report(False, "导出失败", f"{error}\n\n请按提示修改后再次双击“导出配置.exe”。", root, draft)
    except Exception:
        report = Report(False, "导出失败", "发生意外错误，请将下列信息交给开发人员：\n\n" + traceback.format_exc(), root, draft)
    log = root / "export" / LOG_NAME
    try:
        log.write_text(f"{datetime.now():%Y-%m-%d %H:%M:%S}  {report.title}\n\n{report.detail}\n", encoding="utf-8")
    except OSError as error:
        report.detail += f"\n\n无法保存结果日志：{error}"
    return report


# 结果窗口保留可复制的错误详情，策划可直接打开清单修改或查看输出目录。
def show_report(report):
    window = tk.Tk()
    window.title("Hunter 配置导出")
    window.geometry("820x580")
    window.minsize(640, 420)
    window.option_add("*Font", ("Microsoft YaHei UI", 10))
    container = ttk.Frame(window, padding=20)
    container.pack(fill="both", expand=True)
    title = tk.Label(container, text=report.title, anchor="w", font=("Microsoft YaHei UI", 18, "bold"),
                     foreground="#176739" if report.ok else "#B3261E")
    title.pack(fill="x", pady=(0, 8))
    description = ("草稿由导出目标.txt 选择，输出到独立 draft-cfg 目录。" if report.draft else
                   "正式内容由 design/demo_sources.json 选择，修改源表后重新导出。")
    ttk.Label(container, text=description).pack(anchor="w", pady=(0, 12))
    detail = scrolledtext.ScrolledText(container, wrap="word", height=18, font=("Microsoft YaHei UI", 10))
    detail.insert("1.0", report.detail)
    detail.configure(state="disabled")
    detail.pack(fill="both", expand=True)
    buttons = ttk.Frame(container)
    buttons.pack(fill="x", pady=(16, 0))

    def open_path(path):
        try:
            os.startfile(path)
        except OSError as error:
            messagebox.showerror("无法打开", str(error), parent=window)

    target = report.root / ("export/" + TARGET_NAME if report.draft else "design/demo_sources.json")
    ttk.Button(buttons, text="打开草稿清单" if report.draft else "打开生产清单",
               command=lambda: open_path(target)).pack(side="left")
    output = report.root / ("server/build/draft-cfg" if report.draft else "server/build/generated/cfg")
    if output.is_dir():
        ttk.Button(buttons, text="打开输出目录", command=lambda: open_path(output)).pack(side="left", padx=10)
    ttk.Button(buttons, text="关闭", command=window.destroy).pack(side="right")
    window.mainloop()


def main(argv=None):
    parser = argparse.ArgumentParser(description="Hunter 策划配置导出工具")
    parser.add_argument("--no-ui", action="store_true", help="开发验收：仅写结果日志，不打开窗口")
    parser.add_argument("--check", action="store_true", help="开发验收：只检查，不写 Lua 文件（仍写结果日志）")
    parser.add_argument("--draft", action="store_true", help="仅导出旧目标清单草稿，不作为生产内容")
    args = parser.parse_args(argv)
    report = run_export(cfg.ROOT, check=args.check, draft=args.draft)
    if not args.no_ui:
        show_report(report)
    elif sys.stdout is not None:
        print(report.title + "\n" + report.detail)
    return 0 if report.ok else 1


if __name__ == "__main__":
    sys.exit(main())

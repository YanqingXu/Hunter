# 用临时源表与同一服务程序证明 Lua 导表数值驱动实际移动，失败构建保留旧制品。
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import xml.etree.ElementTree as ET
from zipfile import ZipFile

from process_test import Server, enter_game, entity, input_frame, signed, wait_msg

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "export"))
sys.path.insert(0, str(ROOT / "server/tools"))
import gameplay
import bundle


# 对不可重编译的程序及规范内容使用同一 SHA-256 算法取证。
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


# 临时复制显式来源闭包，禁止测试修改工作区中的真实工作簿。
def copy_sources(target):
    manifest = ROOT / "design/demo_sources.json"
    document = json.loads(manifest.read_text(encoding="utf-8"))
    target.mkdir(parents=True)
    for name in {source["file"] for source in document["sources"]}:
        shutil.copyfile(manifest.parent / name, target / name)
    shutil.copyfile(manifest, target / manifest.name)
    return target / manifest.name


# 在自动回归夹具内改一个数值单元格，保留原工作簿其他 ZIP 部件与样式。
def change_speed(manifest, value):
    source = next(row for row in json.loads(manifest.read_text(encoding="utf-8"))["sources"]
                  if row["sheet"].endswith("|Player"))
    path = manifest.parent / source["file"]
    sheet = next(item for item in gameplay.sheets.read_workbook(path)
                 if item.name == source["sheet"])
    columns = gameplay.sheets.read_fields(sheet)
    key = next(field for field in columns if "key" in field.flags)
    speed = next(field for field in columns if field.name == "Speed")
    row = next(index for index, column in sorted(sheet.cells)
               if index > 4 and column == key.column
               and gameplay.sheets.convert(sheet, index, key) == 1)
    address = gameplay.sheets.column_name(speed.column) + str(row)
    with ZipFile(path) as archive:
        files = {part.filename: (part, archive.read(part.filename)) for part in archive.infolist()}
    book = ET.fromstring(files["xl/workbook.xml"][1])
    ns = gameplay.sheets.namespace(book)
    entry = next(item for item in book.findall(f"{ns}sheets/{ns}sheet")
                 if item.get("name") == sheet.name)
    rel_id = next(text for name, text in entry.attrib.items() if name.endswith("}id"))
    rels = ET.fromstring(files["xl/_rels/workbook.xml.rels"][1])
    rel = next(item for item in rels if item.get("Id") == rel_id)
    part_name = gameplay.sheets.part_target("xl/workbook.xml", rel)
    document = ET.fromstring(files[part_name][1])
    ns = gameplay.sheets.namespace(document)
    cell = next(item for item in document.iter(ns + "c") if item.get("r") == address)
    for child in list(cell):
        cell.remove(child)
    cell.attrib.pop("t", None)
    if value is not None:
        ET.SubElement(cell, ns + "v").text = str(value)
    info, _ = files[part_name]
    files[part_name] = (info, ET.tostring(document, encoding="utf-8", xml_declaration=True))
    with ZipFile(path, "w") as archive:
        for info, data in files.values():
            archive.writestr(info, data)


# 工具以参数数组执行，错误正文保留用于定位导表或编译失败。
def run(command, success=True):
    result = subprocess.run([str(value) for value in command], capture_output=True, timeout=60)
    if success:
        assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    else:
        assert result.returncode != 0, "invalid configuration unexpectedly published"
    return result


# 仅组装现有 Lua 表并按发布模式签名，服务程序不会参与编译。
def assemble_config(target, tools, args):
    assemble = [sys.executable, "-B", ROOT / "server/tools/assemble.py", "--manifest",
                ROOT / "server/lua/modules.json", "--cfg", target / "cfg",
                "--output", target / "game.lua", "--map", target / "game.map.json"]
    run(assemble)
    artifact = target / "game.lua"
    if args.mode == "bundle":
        artifact = target / "game.luxb"
        seed = target / "test-only.seed"
        seed.write_bytes(bytes.fromhex(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"))
        try:
            bundle.build(tools["luaxc"], tools["bundle_tool"], target / "game.lua",
                         args.policy, seed, artifact)
        finally:
            seed.unlink()
    return artifact, assemble


# 生产导表、组装及签名全部在临时目录完成。
def build(manifest, target, tools, args):
    target.mkdir(parents=True)
    command = [sys.executable, "-B", ROOT / "export/gameplay.py", "--source", manifest,
               "--output", target / "content.json", "--header", target / "ContentId.h",
               "--cfg", target / "cfg", "--luax", tools["luax"]]
    run(command)
    artifact, assemble = assemble_config(target, tools, args)
    return artifact, command, assemble


# 通过原始协议观察两个快照的位移与 Tick 差，客户端没有内嵌内容摘要假设。
def measure(args, artifact, expected, content):
    options = SimpleNamespace(exe=str(args.exe), source=str(artifact),
                              bundle=str(artifact) if args.mode == "bundle" else None,
                              policy=str(args.policy) if args.policy else None)
    server = Server(options)
    try:
        ready = server.start()
        assert ready["content_version"] == "gameplay-v4:" + digest(content)
        conn = server.connect()
        initial = enter_game(conn)
        conn.sendall(input_frame(1, -1, match=initial[4], world=initial[7]))
        ack = wait_msg(conn, 8)
        observations = []
        deadline = time.monotonic() + 3
        while len(observations) < 2 and time.monotonic() < deadline:
            snapshot = wait_msg(conn, 9)
            player = entity(snapshot, snapshot[8])
            if snapshot.get(2) == 1 and signed(player.get(5, 0)) == -expected:
                observations.append((snapshot[1], signed(player.get(3, 0))))
        assert len(observations) == 2, "Lua source speed did not reach the native snapshot"
        ticks = observations[1][0] - observations[0][0]
        distance = observations[0][1] - observations[1][1]
        assert ticks > 0 and distance == ticks * expected, (observations, expected)
        assert observations[0][0] >= ack[4], "snapshot precedes applied input"
        return ready["content_version"]
    finally:
        server.stop()


# 验证失败导出与缺失 Lua 表都不能覆盖上一份有效制品。
def reject_incomplete(manifest, target, export_command, assemble_command):
    before = {path.relative_to(target): path.read_bytes()
              for path in target.rglob("*") if path.is_file()}
    change_speed(manifest, None)
    run(export_command, False)
    assert before == {path.relative_to(target): path.read_bytes()
                      for path in target.rglob("*") if path.is_file()}
    player = target / "cfg/Player.lua"
    value = player.read_bytes()
    player.unlink()
    try:
        run(assemble_command, False)
        assert (target / "game.lua").read_bytes() == before[Path("game.lua")]
    finally:
        player.write_bytes(value)


# 绕过导表校验模拟损坏配置，真实 Runtime 必须在开放连接和存档前拒绝启动。
def reject_corrupt_config(source, target, tools, args):
    shutil.copytree(source / "cfg", target / "cfg")
    player = target / "cfg/Player.lua"
    text = player.read_text(encoding="utf-8")
    original = '["Speed"] = 37,'
    assert text.count(original) == 1, "selected Player speed is missing from the Lua table"
    player.write_text(text.replace(original, '["Speed"] = 0,', 1), encoding="utf-8")
    artifact, _ = assemble_config(target, tools, args)
    options = SimpleNamespace(exe=str(args.exe), source=str(artifact),
                              bundle=str(artifact) if args.mode == "bundle" else None,
                              policy=str(args.policy) if args.policy else None)
    server = Server(options)
    events = []
    try:
        server.cmd("Start")
        while not events or events[-1].get("type") != "Stopped":
            events.append(server.event())
            assert len(events) <= 16, events
        errors = [event for event in events if event.get("type") == "Error"]
        assert len(errors) == 1, events
        error = errors[0]
        assert error["req_id"] == "start" and error["code"] == "start_failed", events
        assert error["state"] == "Faulted" and "player.speed" in error["detail"], events
        assert events[-1]["state"] == "Faulted", events
        assert all(event.get("type") != "Ready" for event in events), events
        assert server.proc.wait(timeout=7) != 0, events
        assert not list(Path(server.save_dir.name).iterdir()), "invalid config allocated a save"
    finally:
        server.cleanup()
# 两种加载模式共享真实源表回归，并记录修改前后程序哈希与内容身份。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--tools", type=Path, required=True)
    parser.add_argument("--mode", choices=("source", "bundle"), required=True)
    parser.add_argument("--policy", type=Path)
    args = parser.parse_args()
    assert args.mode != "bundle" or args.policy is not None
    tools = json.loads(args.tools.read_text(encoding="utf-8-sig"))
    original = digest(args.exe)
    with tempfile.TemporaryDirectory(prefix="hunter-lua-source-") as folder:
        root = Path(folder)
        manifest = copy_sources(root / "design")
        baseline, _, _ = build(manifest, root / "baseline", tools, args)
        initial = json.loads((root / "baseline/content.json").read_text(encoding="utf-8"))
        speed = initial["players"]["1"]["speed"]
        assert speed != 37
        first = measure(args, baseline, speed, root / "baseline/content.json")
        change_speed(manifest, 37)
        changed, command, assemble = build(manifest, root / "changed", tools, args)
        second = measure(args, changed, 37, root / "changed/content.json")
        assert first != second, "selected source value must change content identity"
        reject_incomplete(manifest, root / "changed", command, assemble)
        reject_corrupt_config(root / "changed", root / "corrupt", tools, args)
    assert digest(args.exe) == original, "configuration update changed the server executable"
    print(json.dumps({"mode": args.mode, "server_sha256": original,
                      "speed_before": speed, "speed_after": 37,
                      "invalid_runtime_config": "rejected_before_ready_and_storage",
                      "content_before": first, "content_after": second}, ensure_ascii=False))


if __name__ == "__main__":
    main()

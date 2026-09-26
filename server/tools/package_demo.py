# 打包可移动的 Windows 首版联调资源，逐文件记录版本与摘要，不携带用户存档或令牌。
import argparse
import hashlib
import json
import os
import tempfile
import zipfile
from pathlib import Path


# 仅收集明确白名单资源，禁止把构建目录、测试故障宿主或测试私钥打包。
def files(args):
    root = Path(__file__).resolve().parents[1]
    build = args.build.resolve(strict=True)
    selected = {
        "hunter_server_desktop.exe": build / "Release/hunter_server_desktop.exe",
        "hunter_client.exe": build / "Release/hunter_client.exe",
        "content.json": build / "generated/content.json",
        "Hunter.cs": build / "generated/csharp/Hunter.cs",
        "hunter.proto": root.parent / "protobuf/hunter.proto",
    }
    if args.bundle:
        selected["game.luxb"] = args.bundle
        selected["policy.json"] = args.policy
        command = "--bundle game.luxb --policy policy.json"
    else:
        selected["game.lua"] = build / "generated/game.lua"
        selected["game.map.json"] = build / "generated/game.map.json"
        command = "--source game.lua"
    data = {name: path.read_bytes() for name, path in selected.items()}
    note = ("Hunter 验证 DEMO 服务端 Windows 联调包\n"
            "需要 x64 Windows 和兼容 VS 2026 的 VC++ 运行库。\n"
            f"在解压目录执行：.\\hunter_server_desktop.exe {command} --save saves/demo.sqlite\n"
            '向宿主 stdin 输入：{"cmd":"Start","req_id":"start"}\n'
            "同时读取 stdout 和 stderr。将 Ready 的完整 JSON 传给 hunter_client.exe 的第一行，"
            "随后 login/start/input/switch_weapon/select_tool/melee/use/interact/pickup/"
            "status/result/stash。\n"
            "协议 v5、内容 v4、Host/状态 v6；SQLite V1。Start 可带完整免费配装，"
            "缺省使用默认配装。Action 重试必须保留原 action_seq。\n"
            "Boss 死亡后回到出生地附近出口，读条成功后等待 Committed。\n"
            "永久仓库只读；停止使用宿主 Stop。实例令牌只用于本次回环握手，不写共享日志。\n"
            "Unity、Android 与完整 Demo 尚未联调；Hunter.cs 为协议产物。\n")
    if args.test_signature:
        note += "本 Bundle 使用公开测试向量签名，只用于本地验收，不能作为正式发行签名。\n"
    data["START.txt"] = note.encode("utf-8-sig")
    manifest = {
        "release": "hunter-server-v1-windows",
        "mode": "bundle" if args.bundle else "source",
        "protocol": 5, "content": 4, "host_state": 6, "sqlite": 1,
        "content_key": "gameplay-v4:" + hashlib.sha256(data["content.json"]).hexdigest(),
        "test_signature": args.test_signature,
        "files": {name: {"bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}
                  for name, value in sorted(data.items())},
    }
    data["manifest.json"] = json.dumps(manifest, ensure_ascii=False, indent=2).encode()
    return data


# 先完整生成和校验 ZIP，再发布到指定文件，失败不替换旧联调包。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--policy", type=Path)
    parser.add_argument("--test-signature", action="store_true")
    args = parser.parse_args()
    if bool(args.bundle) != bool(args.policy) or args.test_signature and not args.bundle:
        parser.error("bundle and policy must be supplied together")
    data = files(args)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=args.output.parent, delete=False) as temp:
        staged = Path(temp.name)
    try:
        with zipfile.ZipFile(staged, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in sorted(data.items()):
                info = zipfile.ZipInfo(name, (2000, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, value)
        with zipfile.ZipFile(staged) as archive:
            assert archive.testzip() is None
            assert all(archive.read(name) == value for name, value in data.items())
        os.replace(staged, args.output)
    finally:
        staged.unlink(missing_ok=True)
    print(args.output)


if __name__ == "__main__":
    main()

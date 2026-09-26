# 打包可移动的 Windows 首版联调资源，逐文件记录版本与摘要，不携带用户存档或令牌。
import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
import zipfile
from pathlib import Path

import assemble
import bundle

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / "export"))
import gameplay


# 内容必须保持规范 JSON 字节，摘要与只含身份的构建头严格一致。
def content_identity(data, header):
    value = json.loads(data.decode("utf-8"), object_pairs_hook=gameplay.unique_pairs)
    canonical, _ = gameplay.content_bytes(value)
    if canonical != data or type(value) is not dict or value.get("v") != 4:
        raise ValueError("content.json must be canonical content v4")
    identity = "gameplay-v4:" + hashlib.sha256(data).hexdigest()
    text = header.decode("utf-8")
    match = re.search(r'inline constexpr std::string_view version = "([^"]+)";', text)
    if not match or match.group(1) != identity or "json_text" in text:
        raise ValueError("content.json and ContentId.h identity mismatch")
    return identity


# 仅收集配置清单所选文件，沿用组装器的命名空间和真实路径边界检查。
def config_files(folder):
    entry, modules = assemble.load_manifest(folder / "Manifest.json", data=True)
    if entry != "cfg.tables":
        raise ValueError("invalid configuration entry")
    result = {"Manifest.json": (folder / "Manifest.json").read_bytes()}
    for module in modules.values():
        result[module["path"].relative_to(folder.resolve()).as_posix()] = module["path"].read_bytes()
    return result


# 发布工具可显式指定固定二进制，默认先使用当前构建再查找开发构建。
def bundle_tool(args, build):
    supplied = getattr(args, "bundle_tool", None)
    if supplied is not None:
        return Path(supplied).resolve(strict=True)
    for path in (build / "_deps/luax-build/Release/luax-bundle.exe",
                 ROOT / "build/win-dev/_deps/luax-build/Release/luax-bundle.exe"):
        if path.is_file():
            return path
    raise ValueError("fixed luax-bundle tool required; supply --bundle-tool")


# 验证离线 Bundle 的签名、当前契约身份与确切聚合源码，阻止不同构建混装。
def verify_bundle(args, build, source_hash):
    fields = bundle.verify(bundle_tool(args, build), args.bundle, args.policy)
    if fields.get("source_sha256") != source_hash:
        raise ValueError("Bundle source identity differs from generated/game.lua")
    policy = bundle.load_policy(args.policy)
    contract = json.loads((ROOT / "lua/contract.json").read_text(encoding="utf-8"))
    if contract["version"] != 7:
        raise ValueError("package requires Host/state contract v7")
    for key in bundle.KEYS[5:]:
        expected = bundle.digest({"identity": key, "source": {
            "contract_version": contract["version"], "schema": contract[key]}})
        if policy["identities"][key] != expected:
            raise ValueError("Bundle policy differs from current contract: " + key)


# 反向校验共享内容、生成 Lua 与源码映射，任何不一致都不能生成联调包。
def validate_build(args, build):
    generated = build / "generated"
    data = (generated / "content.json").read_bytes()
    header = (generated / "ContentId.h").read_bytes()
    identity = content_identity(data, header)
    configs = config_files(generated / "cfg")
    actual = gameplay.content_bytes(gameplay.evaluate(configs, getattr(args, "luax", None)))[0]
    if actual != data:
        raise ValueError("Lua configuration differs from shared content.json")
    source, mapping = assemble.assemble(ROOT / "lua/modules.json", generated / "cfg")
    source_bytes = source.encode("utf-8")
    if (generated / "game.lua").read_bytes() != source_bytes:
        raise ValueError("generated/game.lua differs from current Lua modules or configuration")
    built_map = json.loads((generated / "game.map.json").read_text(encoding="utf-8"))
    if built_map != mapping:
        raise ValueError("generated/game.map.json differs from assembled sources")
    if args.bundle:
        verify_bundle(args, build, mapping["source_sha256"])
    return identity, mapping["source_sha256"], configs


# 仅收集明确白名单资源，禁止把构建目录、测试故障宿主或测试私钥打包。
def files(args):
    build = args.build.resolve(strict=True)
    identity, source_hash, configs = validate_build(args, build)
    selected = {
        "hunter_server_desktop.exe": build / "Release/hunter_server_desktop.exe",
        "hunter_client.exe": build / "Release/hunter_client.exe",
        "content.json": build / "generated/content.json",
        "ContentId.h": build / "generated/ContentId.h",
        "Hunter.cs": build / "generated/csharp/Hunter.cs",
        "hunter.proto": ROOT.parent / "protobuf/hunter.proto",
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
    if not args.bundle:
        data.update({"cfg/" + name: value for name, value in configs.items()})
    note = ("Hunter 验证 DEMO 服务端 Windows 联调包\n"
            "需要 x64 Windows 和兼容 VS 2026 的 VC++ 运行库。\n"
            f"在解压目录执行：.\\hunter_server_desktop.exe {command} --save saves/demo.sqlite\n"
            '向宿主 stdin 输入：{"cmd":"Start","req_id":"start"}\n'
            "同时读取 stdout 和 stderr。将 Ready 的完整 JSON 传给 hunter_client.exe 的第一行，"
            "随后 login/start/input/switch_weapon/select_tool/melee/use/interact/pickup/"
            "status/result/stash。\n"
            "协议 v5、内容 v4、Host/状态 v7；SQLite V1。Start 可带完整免费配装，"
            "缺省使用默认配装。Action 重试必须保留原 action_seq。\n"
            "Boss 死亡后回到出生地附近出口，读条成功后等待 Committed。\n"
            "永久仓库只读；停止使用宿主 Stop。实例令牌只用于本次回环握手，不写共享日志。\n"
            "Unity、Android 与完整 Demo 尚未联调；Hunter.cs 为协议产物。\n")
    note += ("content.json 是客户端共享内容，ContentId.h 仅供核对身份；服务端由 Lua 加载配置。\n"
             + ("配置和玩法共同包含在签名 Bundle 内；本包不提供编译器或私钥。\n" if args.bundle else
                "cfg/ 包含导出的原字段 Lua 表和清单用于核对；实际加载已聚合的 game.lua。\n"))
    if args.test_signature:
        note += "本 Bundle 使用公开测试向量签名，只用于本地验收，不能作为正式发行签名。\n"
    data["START.txt"] = note.encode("utf-8-sig")
    manifest = {
        "release": "hunter-server-v1-windows",
        "mode": "bundle" if args.bundle else "source",
        "protocol": 5, "content": 4, "host_state": 7, "sqlite": 1,
        "content_key": identity,
        "script_source_sha256": source_hash,
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
    parser.add_argument("--luax", type=Path)
    parser.add_argument("--bundle-tool", type=Path)
    args = parser.parse_args()
    if bool(args.bundle) != bool(args.policy) or args.test_signature and not args.bundle:
        parser.error("bundle and policy must be supplied together")
    try:
        data = files(args)
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"package: {error}\n")
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

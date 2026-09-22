# 在显式连接的设备运行原生探针并留存设备信息；没有 adb 或设备时明确失败。
import argparse
import hashlib
import json
import subprocess
import uuid
from pathlib import Path


# 执行一个有超时的 adb 命令；失败信息保留在证据中。
def adb_run(adb, args, timeout=30):
    result = subprocess.run(adb + args, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=timeout)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return result.stdout.strip()


# 推送原生探针及公开制品到独立临时目录，退出时回收本次文件。
def main():
    parser = argparse.ArgumentParser(description="运行 Android ARM64 Bundle 探针")
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--serial")
    args = parser.parse_args()
    adb = [args.adb] + (["-s", args.serial] if args.serial else [])
    remote = "/data/local/tmp/hunter-probe-" + uuid.uuid4().hex
    evidence = {"status": "not_verified", "kind": "native_probe"}
    created = False
    try:
        adb_run(adb, ["get-state"])
        evidence["serial"] = adb_run(adb, ["get-serialno"])
        for key, prop in [("model", "ro.product.model"), ("abi", "ro.product.cpu.abi"),
                          ("android", "ro.build.version.release")]:
            evidence[key] = adb_run(adb, ["shell", "getprop", prop])
        if evidence["abi"] != "arm64-v8a":
            raise RuntimeError("探针要求 arm64-v8a 设备")
        adb_run(adb, ["shell", "mkdir", remote])
        created = True
        for path, name in [(args.exe, "probe"), (args.bundle, "game.luxb"),
                           (args.policy, "policy.json")]:
            evidence[name + "_sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
            adb_run(adb, ["push", str(path), remote + "/" + name])
        adb_run(adb, ["shell", "chmod", "700", remote + "/probe"])
        output = adb_run(adb, ["shell", remote + "/probe", remote + "/game.luxb",
                              remote + "/policy.json"])
        result = json.loads(output)
        if result.get("passed") is not True:
            raise RuntimeError("原生探针未返回成功证据")
        evidence.update(status="passed", result=result)
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        evidence["error"] = str(error)
    finally:
        if created:
            try:
                adb_run(adb, ["shell", "rm", "-r", remote])
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                evidence["cleanup_error"] = str(error)
                evidence["status"] = "failed"
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        args.evidence.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n",
                                 encoding="utf-8")
    print(json.dumps(evidence, ensure_ascii=False))
    return 0 if evidence["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())

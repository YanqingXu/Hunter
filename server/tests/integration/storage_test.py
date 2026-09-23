# 用真实子进程强杀验证事务边界，所有数据库都位于独占临时目录。
import argparse
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import subprocess
import tempfile


# 执行正常探针并保留输出，异常或超时均使测试失败。
def run(exe, *args):
    result = subprocess.run([exe, *map(str, args)], capture_output=True, text=True,
                            encoding="utf-8", timeout=20)
    if result.returncode:
        raise RuntimeError(f"{args}: {result.stdout}\n{result.stderr}")


# 等待确切事务检查点再强杀，不使用随机延迟猜测 COMMIT 时机。
def kill_at(exe, path, stage):
    child = subprocess.Popen([exe, "commit", str(path), stage], stdin=subprocess.PIPE,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, encoding="utf-8")
    reader = ThreadPoolExecutor(max_workers=1)
    try:
        line = reader.submit(child.stdout.readline).result(timeout=15).strip()
        if line != "checkpoint:" + stage:
            raise RuntimeError(f"missing checkpoint {stage}: {line}")
        child.kill()
        child.wait(timeout=5)
        if child.returncode == 0:
            raise RuntimeError("child was not killed")
    finally:
        if child.poll() is None:
            child.kill()
        child.communicate(timeout=5)
        reader.shutdown(wait=True)


# 覆盖事务前、中、提交后以及真实 SQLite 写满，不触碰服务端正式存档。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-storage-crash-") as folder:
        for stage in ("before_txn", "in_txn", "after_commit"):
            path = Path(folder) / (stage + ".db")
            run(args.exe, "seed", path)
            kill_at(args.exe, path, stage)
            expected = "committed" if stage == "after_commit" else "absent"
            run(args.exe, "verify", path, expected)
            print(stage + ": atomic recovery and idempotent retry passed")
        path = Path(folder) / "full.db"
        run(args.exe, "seed", path)
        run(args.exe, "full", path)
        print("SQLITE_FULL: rollback and retry passed")


if __name__ == "__main__":
    main()

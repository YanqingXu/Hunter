# 验证显式 SQLite 配置及未知后端在创建存档目录之前被拒绝。
import argparse
import os
import subprocess
import tempfile
from pathlib import Path

from process_test import Server


# 两种后端入口读取同一文件，省略后端参数继续使用 SQLite。
def sqlite_config(args, root):
    path = root / "explicit-save" / "save.sqlite"
    for backend in (["--storage", "sqlite"], []):
        srv = Server(args, extra=backend + ["--save", str(path)])
        try:
            srv.start()
            assert path.is_file(), "SQLite save missing after Ready"
            srv.stop()
        finally:
            srv.cleanup()


# 未实现和未知后端都不能创建显式目录、默认目录或修改既有存档。
def unsupported(args, root):
    local = root / "local"
    existing = root / "existing.sqlite"
    original = b"keep this existing save unchanged"
    existing.write_bytes(original)
    env = os.environ | {"LOCALAPPDATA": str(local)}
    for backend in ("mysql", "unknown", ""):
        for path in (None, root / "never-created" / "save.sqlite", existing):
            command = [args.exe, "--storage", backend]
            if path is not None:
                command += ["--save", str(path)]
            result = subprocess.run(command, input="", capture_output=True,
                                    text=True, timeout=10, env=env)
            assert result.returncode != 0, result
            assert "storage_backend_unsupported" in result.stderr, result.stderr
            assert "Ready" not in result.stdout, result.stdout
            assert not local.exists() and not (root / "never-created").exists()
            assert existing.read_bytes() == original


# 使用隔离临时目录运行真实宿主配置验证。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-storage-cfg-") as directory:
        root = Path(directory)
        unsupported(args, root)
        sqlite_config(args, root)
    print("storage configuration passed")


if __name__ == "__main__":
    main()

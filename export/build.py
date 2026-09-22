# 仅供开发人员重新打包 Windows 单文件程序；策划不需要 Python 或打包依赖。
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def main():
    if sys.platform != "win32":
        raise SystemExit("请在 Windows 上打包策划使用的 EXE。")
    build = ROOT / "server/build/export-tool"
    build.mkdir(parents=True, exist_ok=True)
    subprocess.run([
        sys.executable, "-m", "PyInstaller", "--noconfirm", "--onefile", "--windowed",
        "--noupx", "--name", "导出配置", "--log-level", "WARN",
        "--paths", str(ROOT / "export"), "--paths", str(ROOT / "server/tools"),
        "--distpath", str(ROOT / "export"), "--workpath", str(build / "work"),
        "--specpath", str(build), str(ROOT / "export/launcher.py"),
    ], check=True, cwd=ROOT)
    print(f"已生成: {ROOT / 'export/导出配置.exe'}")


if __name__ == "__main__":
    main()

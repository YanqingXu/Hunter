# 暂存完整制品后发布；捕获异常时回滚，崩溃或回滚失败时保留恢复材料。
import json
import os
import shutil
import tempfile
import warnings
from pathlib import Path


# 写完并同步临时文件；最终路径从不接收分段写入。
def _write_file(path, data):
    with path.open("xb") as stream:
        if stream.write(data) != len(data):
            raise OSError(f"short artifact write: {path}")
        stream.flush()
        os.fsync(stream.fileno())


# 仅清理本次创建的临时目录和锁；失败时返回仍需处理的路径。
def _cleanup(items, locks):
    errors = []
    for item in items:
        folder = item.get("folder")
        if folder is None:
            continue
        try:
            resolved = folder.resolve(strict=True)
            if resolved != folder or resolved.parent != item["path"].parent:
                raise OSError("staging directory moved")
            if not resolved.name.startswith(".hunter-publish-"):
                raise OSError("unexpected staging directory")
            shutil.rmtree(resolved)
        except OSError as error:
            errors.append(f"{folder}: {error}")
    for lock in reversed(locks):
        try:
            lock.unlink()
        except OSError as error:
            errors.append(f"{lock}: {error}")
    return errors


# 恢复所有可能已变更的目标；排他创建不删除与暂存文件无关的后来文件。
def _rollback(items, replace):
    errors = []
    for item in reversed(items):
        if not item.get("attempted"):
            continue
        path = item["path"]
        try:
            if item["existed"]:
                os.replace(item["backup"], path)
            elif path.exists() and (replace or os.path.samefile(item["stage"], path)):
                path.unlink()
        except OSError as error:
            errors.append(f"{path}: {error}")
    return errors


# 发布一组拥有字节数据的制品；替换模式回滚旧内容，排他模式拒绝已有文件。
# 调用者不得在发布期间读取或绕过锁改写目标；多路径不提供崩溃原子性。
def publish_files(files, *, replace=True):
    items = []
    seen = set()
    for raw_path, data in files:
        path = Path(raw_path).absolute()
        if os.name == "nt" and path.name.endswith((".", " ")):
            raise ValueError(f"artifact target uses a Windows filename alias: {path}")
        if path.name.casefold().endswith(".hunter-publish.lock"):
            raise ValueError(f"artifact target uses reserved publisher lock name: {path}")
        if path.is_symlink():
            raise ValueError(f"artifact target cannot be a symlink: {path}")
        path.parent.mkdir(parents=True, exist_ok=True)
        path = path.parent.resolve(strict=True) / path.name
        key = os.path.normcase(str(path))
        if key in seen:
            raise ValueError(f"duplicate artifact target: {path}")
        seen.add(key)
        if path.exists() and (not path.is_file() or not replace):
            raise ValueError(f"artifact target already exists or is not a file: {path}")
        if not isinstance(data, bytes):
            raise TypeError("artifact contents must be bytes")
        items.append({"path": path, "data": data})
    if not items:
        raise ValueError("artifact set cannot be empty")

    locks = []
    journal = None
    try:
        for path in sorted((item["path"] for item in items), key=str):
            lock = path.with_name(path.name + ".hunter-publish.lock")
            stream = lock.open("xb")
            locks.append(lock)
            stream.close()
        for item in items:
            path = item["path"]
            if path.is_symlink() or (path.exists() and (not path.is_file() or not replace)):
                raise ValueError(f"artifact target changed before staging: {path}")
            folder = Path(tempfile.mkdtemp(prefix=".hunter-publish-", dir=path.parent))
            item["folder"] = folder
            item["stage"] = folder / "prepared"
            item["backup"] = folder / "previous"
            item["existed"] = path.exists()
            _write_file(item["stage"], item["data"])
            if item["existed"]:
                shutil.copy2(path, item["backup"])
                with item["backup"].open("r+b") as stream:
                    os.fsync(stream.fileno())
        journal = items[0]["folder"] / "recovery.json"
        recovery = [{"target": str(item["path"]), "existed": item["existed"],
                     "backup": str(item["backup"]), "prepared": str(item["stage"])}
                    for item in items]
        _write_file(journal, json.dumps(recovery, indent=2).encode("utf-8"))
        for item in items:
            item["attempted"] = True
            if replace:
                os.replace(item["stage"], item["path"])
            else:
                os.link(item["stage"], item["path"])
    except BaseException as error:
        errors = _rollback(items, replace)
        if errors:
            raise OSError(f"artifact rollback failed; preserve locks and recover from {journal}; "
                          + "; ".join(errors)) from error
        errors = _cleanup(items, locks)
        if errors:
            raise OSError("artifact publication failed; originals restored; cleanup failed: "
                          + "; ".join(errors)) from error
        raise
    errors = _cleanup(items, locks)
    if errors:
        warnings.warn("artifact set committed; temporary cleanup incomplete: " + "; ".join(errors),
                      RuntimeWarning)

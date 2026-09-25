# 验证正式 Runtime 启动失败、空奖励放弃、旧世界输入和仓库分页边界。
import argparse
from contextlib import closing
import hashlib
import json
import sqlite3
import tempfile
import time
from pathlib import Path
from demo_test import Demo
from process_test import Server


# 损坏和陌生版本只报告失败，原文件逐字节保留。
def bad_save(args, root):
    for name in ("corrupt", "version"):
        path = root / (name + ".sqlite")
        if name == "corrupt":
            path.write_bytes(b"this is not a sqlite save" * 100)
        else:
            with closing(sqlite3.connect(path)) as db:
                db.execute("PRAGMA user_version=99")
        before = hashlib.sha256(path.read_bytes()).digest()
        srv = Server(args, extra=["--save", str(path)])
        try:
            srv.cmd("Start")
            assert srv.event("Error")["code"] == "start_failed"
            srv.event("Stopped")
            assert srv.proc.wait(timeout=5) != 0
            assert hashlib.sha256(path.read_bytes()).digest() == before
        finally:
            srv.cleanup()


# 在同一会话重复开局和放弃，奖励始终为空；旧世界命令有请求关联。
def abandon(args, root):
    path = root / "abandon.sqlite"
    demo = Demo(args, path)
    try:
        old_world = "0"
        for index in range(10):
            demo.start()
            if old_world != "0":
                demo.cli.send({"cmd": "bag", "req_id": "old-world", "match_id": demo.match,
                               "world_id": old_world})
                error = demo.cli.wait("error")
                assert error["req_id"] == "old-world" and error["code"] == "stale_match"
            old_world = demo.world
            req = {"cmd": "abandon", "req_id": "abandon", "world_id": demo.world,
                   "match_id": demo.match}
            demo.cli.send(req)
            demo.cli.wait("action_rsp")
            for _ in range(40):
                saved = demo.query("status")
                if saved["state"] == "Committed":
                    break
                time.sleep(.025)
            assert saved["state"] == "Committed"
            result = json.loads(saved["result_json"])
            assert result["outcome"] == "Abandoned" and result["items"] == []
            demo.cli.send(req)
            assert demo.cli.wait("error")["code"] == "invalid_state"
        assert not demo.query("stash", revision="0", cursor="0", limit=128)["items"]
        demo.start()
        unfinished = demo.match
    finally:
        demo.close()
    recovered = Demo(args, path)
    try:
        recovered.cli.send({"cmd": "result", "req_id": "unfinished", "match_id": unfinished})
        assert recovered.cli.wait("error")["code"] == "result_not_found"
    finally:
        recovered.close()


# 在测试存档扩充已提交奖励以覆盖超过单帧仓库，正式查询仍经 TCP 编解码。
def pages(args, root):
    path = root / "pages.sqlite"
    demo = Demo(args, path)
    try:
        demo.start()
        saved = demo.extract()
    finally:
        demo.close()
    with closing(sqlite3.connect(path)) as db:
        for _ in range(300):
            db.execute("INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) "
                       "VALUES(1,2800001,1,?)", (int(saved["match_id"]),))
        db.commit()
    demo = Demo(args, path)
    try:
        cursor, revision, ids = "0", "0", []
        while True:
            page = demo.query("stash", revision=revision, cursor=cursor, limit=128)
            assert len(page["items"]) <= 128
            revision = page["revision"]
            ids.extend(v["item_uid"] for v in page["items"])
            cursor = page["next_cursor"]
            if cursor == "0":
                break
        assert len(ids) > 300 and len(set(ids)) == len(ids)
        demo.cli.send({"cmd": "stash", "req_id": "stale-page", "revision": "9999",
                       "cursor": "128", "limit": 128})
        error = demo.cli.wait("error")
        assert error["req_id"] == "stale-page" and error["code"] == "stale_revision_or_page"
    finally:
        demo.close()


# 所有文件和进程均归本测试所有，不修改默认用户存档。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-edges-") as directory:
        root = Path(directory)
        bad_save(args, root)
        abandon(args, root)
        pages(args, root)
    print("Runtime save, abandonment and pagination edges passed")


if __name__ == "__main__":
    main()

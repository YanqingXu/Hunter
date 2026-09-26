# 用隔离 SQLite 写锁验证异步开局跨越暂停和恢复后仍然作废。
import argparse
import sqlite3
import tempfile
import time
from pathlib import Path
from process_test import Client, Server


# 把全部持锁等待限制在 SQLite 的两秒忙等待之前，失败路径也立即释放写锁。
def remaining(deadline):
    value = deadline - time.monotonic()
    assert value > 0, "pending start did not reach pause boundary before lock deadline"
    return value


# 等到 Preparing 证明请求已经进入异步分配，再暂停并恢复，不能靠竞速碰巧清掉队列。
def start_pause(args):
    with tempfile.TemporaryDirectory(prefix="hunter-start-pause-") as directory:
        path = Path(directory) / "save.sqlite"
        srv = Server(args, extra=["--save", str(path)])
        cli = None
        try:
            cli = Client(args, srv.start())
            cli.send({"cmd": "login", "req_id": "pause-login"})
            cli.wait("login_rsp")
            locked = sqlite3.connect(path)
            try:
                locked.execute("BEGIN IMMEDIATE")
                deadline = time.monotonic() + 1.5
                cli.send({"cmd": "start", "req_id": "before-pause", "after_match_id": "0"})
                while True:
                    state = cli.wait("snapshot", timeout=remaining(deadline))
                    if state["phase"] == "Preparing":
                        break
                assert state["match_id"] == "0", state
                srv.cmd("Pause", "alloc-pause")
                srv.event("Rsp", "alloc-pause", timeout=remaining(deadline))
                srv.cmd("Resume", "alloc-resume")
                srv.event("Rsp", "alloc-resume", timeout=remaining(deadline))
            finally:
                locked.rollback()
                locked.close()

            response = cli.wait(("start_rsp", "error"))
            assert response["type"] == "error" and response["code"] == "paused" \
                and response["req_id"] == "before-pause", \
                f"pre-pause async start survived resume: {response}"

            # 已分配但作废的局号允许跳过，恢复后必须由新的请求显式创建世界。
            cli.send({"cmd": "start", "req_id": "after-pause", "after_match_id": "0"})
            started = cli.wait("start_rsp")
            assert started["req_id"] == "after-pause" and started["match_id"] == "2", started
            assert started["phase"] == "Playing", started
            print("pending start remains discarded after pause/resume", flush=True)
        finally:
            try:
                srv.stop()
            finally:
                if cli is not None:
                    cli.close()


# 两种 Runtime 可独立运行，也可由完整玩法 TCP 契约调用同一入口。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    start_pause(parser.parse_args())


if __name__ == "__main__":
    main()

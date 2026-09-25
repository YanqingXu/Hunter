# 在桌面 Runtime + TCP + Storage 路径强杀，不用孤立数据库测试替代流程证据。
import argparse
import json
import os
import tempfile
import time
from pathlib import Path
from demo_test import Demo


# 有界等待真正事务边界，不能用任意睡眠猜测提交时机。
def checkpoint(marker):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        if marker.exists():
            return
        time.sleep(.01)
    raise AssertionError("Runtime never reached storage checkpoint")


# 完成一局后在指定提交边界杀掉真实宿主，重启只读查询已提交结果。
def crash(args, root, stage):
    path = root / (stage + ".sqlite")
    marker = root / stage
    os.environ["HUNTER_TEST_FAULT"] = stage
    os.environ["HUNTER_TEST_MARKER"] = str(marker)
    demo = Demo(args, path)
    try:
        demo.start()
        demo.extract(confirm=False)
        checkpoint(marker)
        match = demo.match
        demo.srv.proc.kill()
        demo.srv.proc.wait(timeout=5)
    finally:
        demo.srv.cleanup()
        demo.cli.close(allowed_codes=(0, 1))
        os.environ.pop("HUNTER_TEST_FAULT", None)
    recovered = Demo(args, path)
    try:
        stash = recovered.query("stash", revision="0", cursor="0", limit=128)
        if stage == "after_commit":
            saved = recovered.query("result", match_id=match)
            assert saved["state"] == "Committed" and stash["items"]
            assert json.loads(saved["result_json"])["outcome"] == "Extracted"
        else:
            assert not stash["items"] and stash["last_match_id"] == "0"
            recovered.cli.send({"cmd": "result", "req_id": "missing", "match_id": match})
            assert recovered.cli.wait("error")["code"] == "result_not_found"
        recovered.start()
        assert int(recovered.match) > int(match), "MatchId reused after crash"
    finally:
        recovered.close()
    print(stage + ": atomic recovery passed", flush=True)


# 未知提交查询原结果；写失败按冻结请求重试，暂停不阻塞保存回调。
def retry(args, root, mode):
    os.environ["HUNTER_TEST_FAULT"] = mode
    demo = Demo(args, root / (mode + ".sqlite"))
    try:
        demo.start()
        demo.extract(confirm=False)
        demo.srv.cmd("Pause")
        demo.srv.event("Rsp", "pause")
        for _ in range(20):
            state = demo.query("status")
            if state["state"] in ("Unknown", "Failed"):
                break
            time.sleep(.05)
        assert state["state"] == ("Unknown" if mode == "unknown" else "Failed"), state
        demo.query("retry", match_id=demo.match)
        for _ in range(20):
            state = demo.query("status")
            if state["state"] == "Committed":
                break
            time.sleep(.05)
        assert state["state"] == "Committed", state
        original = state["result_json"]
        assert demo.query("retry", match_id=demo.match)["result_json"] == original
        stash = demo.query("stash", revision="0", cursor="0", limit=128)
        assert len(stash["items"]) == len(json.loads(original)["items"])
    finally:
        demo.close()
        os.environ.pop("HUNTER_TEST_FAULT", None)
    print(mode + ": frozen retry and paused callback passed", flush=True)


# Stop 必须等待已接受写入和通知排空；断连时仍只提交已冻结的结果。
def draining(args, root, disconnect):
    name = "disconnect" if disconnect else "stop"
    path, marker = root / (name + ".sqlite"), root / name
    os.environ["HUNTER_TEST_FAULT"] = "in_txn"
    os.environ["HUNTER_TEST_MARKER"] = str(marker)
    demo = Demo(args, path)
    try:
        demo.start()
        demo.extract(confirm=False)
        checkpoint(marker)
        if disconnect:
            demo.cli.close()
        demo.srv.cmd("Stop")
        time.sleep(.1)
        assert demo.srv.proc.poll() is None, "Stop did not drain accepted storage work"
        Path(str(marker) + ".release").write_text("release")
        demo.srv.event("Stopped", "stop")
        assert demo.srv.proc.wait(timeout=5) == 0
    finally:
        Path(str(marker) + ".release").write_text("release")
        demo.srv.cleanup()
        if not disconnect:
            demo.cli.close()
        os.environ.pop("HUNTER_TEST_FAULT", None)
    recovered = Demo(args, path)
    try:
        result = recovered.query("result", match_id=demo.match)
        assert result["state"] == "Committed"
        assert recovered.query("stash", revision="0", cursor="0", limit=128)["items"]
    finally:
        recovered.close()
    print(name + ": accepted transaction drained", flush=True)


# 流程故障与普通撤离使用同一客户端和规则，仅测试链接的事务钩子不同。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-runtime-fault-") as directory:
        root = Path(directory)
        for stage in ("before_txn", "in_txn", "after_commit"):
            crash(args, root, stage)
        for mode in ("unknown", "write"):
            retry(args, root, mode)
        for disconnect in (False, True):
            draining(args, root, disconnect)


if __name__ == "__main__":
    main()

# 用隔离配置、真实 TCP 和 SQLite 验证永久猎人交易、出战、终局及重启回退。
import argparse
import json
import sqlite3
import subprocess
import sys
import tempfile
from contextlib import closing
from pathlib import Path
from types import SimpleNamespace

from process_test import Server, blob, number, frame, receive, wait_msg, fields, entity

ROOT = Path(__file__).resolve().parents[2]


# 控制面失败也清理所属进程，避免文件句柄掩盖最初业务断言。
def stop(srv):
    if sys.exc_info()[0] is not None:
        try:
            print(srv.event("Error", timeout=2), file=sys.stderr, flush=True)
        except AssertionError:
            pass
        print({"events": list(srv.events.queue), "diagnostics": srv.errors},
              file=sys.stderr, flush=True)
    try:
        srv.stop()
    finally:
        srv.cleanup()


# 等待业务响应，错误即时上报而不把关闭或超时误当通过。
def response(conn, tag):
    while True:
        kind, value = receive(conn)
        if kind == tag:
            return value
        if kind == 6:
            raise AssertionError(value)


# 读取正式永久档案，JSON 中领域整数由测试保持精确。
def profile(conn):
    conn.sendall(frame(20, blob(1, "profile")))
    return json.loads(response(conn, 21)[7])


# 只发送身份、槽位和版本，客户端没有价额或奖励参数。
def mutation(conn, kind, op, revision, hunter=0, cfg=0, skill=0, slot=0, item=0):
    request = frame(20, blob(1, op) + blob(2, op) + number(3, kind)
                    + number(4, revision) + number(5, hunter) + number(6, cfg)
                    + number(7, skill) + number(8, slot) + number(9, item))
    conn.sendall(request)
    value = response(conn, 21)
    return request, value, json.loads(value[7])


# 所有配置数值仅属于显式测试夹具，生产工作簿保持不变。
def fixture(args, folder, enabled=True):
    content = json.loads(Path(args.content).read_text(encoding="utf-8"))
    content["career"] = {"levels": [0, 10], "kill_xp": {
        key: 0 for key in content["monsters"]}, "extract_xp": 10,
        "bounty_xp": 0, "bounty_currency": 0, "retire_xp": 20} if enabled else False
    content["players"]["2"]["skill_points"] = 3
    content["skills"]["1"] = {"name": "交易回归技能", "target": "player",
        "category": "numeric", "kind": "passive", "cost": 1,
        "effects": [{"stat": "weight", "op": "add", "value": 1}]}
    content["extracts"][0]["hold_ticks"] = 180
    source = folder / "fixture.json"
    source.write_text(json.dumps(content, ensure_ascii=False), encoding="utf-8")
    result = folder / "result.json"
    command = [sys.executable, "-B", str(ROOT / "tests/scripts/build_cfg.py"),
               "--tools", args.tools, "--input", str(source), "--result", str(result),
               "--mode", args.mode]
    if args.mode == "bundle":
        command += ["--policy", args.policy]
    built = subprocess.run(command, capture_output=True, timeout=120)
    if built.returncode:
        raise AssertionError(built.stdout.decode("utf-8", errors="replace")
                             + built.stderr.decode("utf-8", errors="replace"))
    artifact = json.loads(result.read_text(encoding="utf-8"))["path"]
    return SimpleNamespace(exe=args.exe, source=artifact if args.mode == "source" else None,
                           bundle=artifact if args.mode == "bundle" else None,
                           policy=args.policy)


# 仅在隔离档案中种入真实历史资产，免费测试装备不被转换成资产。
def seed(path):
    with closing(sqlite3.connect(path)) as db, db:
        assert db.execute("PRAGMA user_version").fetchone()[0] == 2
        items = [{"item_uid": 1, "cfg_id": 10001, "count": 1, "acquired_match_id": 1},
                 {"item_uid": 2, "cfg_id": 30002, "count": 1, "acquired_match_id": 1}]
        request = {"v": 1, "match_id": 1, "player_id": 1, "expected_revision": 1,
                   "outcome": "Extracted", "content_key": "legacy-fixture", "items": []}
        result = {"v": 1, "match_id": 1, "player_id": 1, "revision_before": 1,
                  "revision_after": 2, "committed_at_ms": 1, "outcome": "Extracted",
                  "content_key": "legacy-fixture", "items": items}
        db.execute("INSERT INTO match_result VALUES(1,1,'Extracted','legacy-fixture',?,?,1,2,1)",
                   (json.dumps(request), json.dumps(result)))
        db.execute("INSERT INTO player_item VALUES(1,1,10001,1,1)")
        db.execute("INSERT INTO player_item VALUES(2,1,30002,1,1)")
        db.execute("UPDATE player_save SET revision=2,last_match_id=1")
        db.execute("UPDATE save_meta SET int_value=2 WHERE key='next_match_id'")


# 建立已登录的真实连接，不绕过握手和逻辑 Tick。
def connect(args, path):
    srv = Server(args, extra=["--save", str(path)])
    srv.start()
    conn = srv.connect()
    conn.sendall(frame(10, blob(1, "login")))
    response(conn, 11)
    return srv, conn


# 永久开局不携带免费 loadout，第一份快照必须已经受真实物资数量约束。
def start(conn, hunter, revision, op="deploy"):
    conn.sendall(frame(12, blob(1, op) + number(4, hunter) + number(5, revision)))
    accepted = response(conn, 13)
    assert accepted[7] == hunter and not accepted.get(8, 0), accepted
    snap = response(conn, 9)
    player = entity(snap, snap[8])
    tools = player.get(25, [])
    tools = [tools] if isinstance(tools, bytes) else tools
    assert len(tools) == 1 and fields(tools[0])[3] == 1, tools
    return accepted


# 真实撤离提交后重启，验证技能、升级、重复操作以及满级退役。
def exercise(args, path):
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        request, first, created = mutation(conn, 1, "recruit", current["revision"], cfg=2)
        hunter = created["hunter_id"]
        conn.sendall(request)
        replay = response(conn, 21)
        assert replay[6] == 1 and replay[7] == first[7]
        _, _, bought = mutation(conn, 3, "buy", created["revision"], hunter=hunter, skill=1)
        _, _, removed = mutation(conn, 4, "remove", bought["revision"], hunter=hunter, skill=1)
        assert removed["profile"]["hunters"][0]["points"] == 3
        _, _, bought = mutation(conn, 3, "buy-again", removed["revision"], hunter=hunter, skill=1)
        _, _, armed = mutation(conn, 2, "equip", bought["revision"], hunter=hunter, slot=1, item=1)
        _, _, armed = mutation(conn, 2, "medkit", armed["revision"], hunter=hunter, slot=3, item=2)
        started = start(conn, hunter, armed["revision"])
        conn.settimeout(10)
        while True:
            saved = response(conn, 19)
            assert saved[3] not in (b"Failed", b"Unknown"), saved
            if saved[3] == b"Committed":
                break
        result = json.loads(saved[4])
        assert result["v"] == 2 and result["outcome"] == "Extracted", result
        assert result["match_id"] == started[2] and result["level"] == 2
        assert result["skills"][0]["paid_cost"] == 1
        current = profile(conn)
        assert current["hunters"][0]["level"] == 2
        assert current["hunters"][0]["points"] == 3
    finally:
        stop(srv)
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        assert current["hunters"][0]["hunter_id"] == hunter
        conn.sendall(request)
        replay = response(conn, 21)
        assert replay[6] == 1 and replay[7] == first[7], "重启后不重新招募"
        retirement, reply, result = mutation(conn, 5, "retire", current["revision"], hunter=hunter)
        assert not result["profile"]["hunters"] and len(result["profile"]["stash"]) == 2
        assert result["profile"]["account_xp"] == 20
        conn.sendall(retirement)
        assert response(conn, 21)[7] == reply[7], "已退役人物仍能精确重放"
    finally:
        stop(srv)


# 出战后强杀恢复原技能和装备；主动放弃提交后重启不能复原人物。
def interrupted(args, path):
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        _, _, value = mutation(conn, 1, "next-recruit", current["revision"], cfg=2)
        hunter = value["hunter_id"]
        _, _, value = mutation(conn, 3, "next-skill", value["revision"], hunter=hunter, skill=1)
        _, _, value = mutation(conn, 2, "next-equip", value["revision"], hunter=hunter, slot=1, item=1)
        _, _, value = mutation(conn, 2, "next-medkit", value["revision"], hunter=hunter, slot=3, item=2)
        first = start(conn, hunter, value["revision"], "interrupted")
        srv.proc.kill()
        srv.proc.wait(timeout=5)
    finally:
        srv.cleanup()
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        value = current["hunters"][0]
        assert value["state"] == "ready" and len(value["skills"]) == 1
        second = start(conn, hunter, current["revision"], "abandon-start")
        assert second[2] > first[2]
        conn.sendall(frame(16, blob(1, "abandon") + number(2, second[4])
                           + number(3, second[2]) + number(4, 2) + number(6, 1)))
        response(conn, 17)
        while True:
            saved = response(conn, 19)
            assert saved[3] not in (b"Failed", b"Unknown"), saved
            if saved[3] == b"Committed":
                break
        assert json.loads(saved[4])["outcome"] == "Abandoned"
    finally:
        stop(srv)
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        assert current["hunters"] == [] and current["stash"] == [], current
    finally:
        stop(srv)


# 缺少正式成长表时仍可免费招募，但出战必须拒绝且不占用人物或资产。
def missing_career(args, path):
    srv, conn = connect(args, path)
    try:
        current = profile(conn)
        _, _, value = mutation(conn, 1, "no-career-recruit", current["revision"], cfg=2)
        hunter = value["hunter_id"]
        conn.sendall(frame(12, blob(1, "no-career") + number(4, hunter)
                           + number(5, value["revision"])))
        error = response(conn, 6)
        assert error[1] == b"career_not_configured", error
        current = profile(conn)
        assert current["hunters"][0]["state"] == "ready" and not current["raids"]
        assert current["revision"] == value["revision"]
    finally:
        stop(srv)


# 统一为开发源码或签名 Bundle 构建显式夹具并执行真实业务链路。
def main():
    parser = argparse.ArgumentParser()
    for name in ("exe", "content", "tools", "mode"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--policy")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-runtime-v2-") as temporary:
        folder = Path(temporary)
        runtime = fixture(args, folder)
        path = folder / "save.sqlite"
        srv = Server(runtime, extra=["--save", str(path)])
        try:
            srv.start()
        finally:
            stop(srv)
        seed(path)
        exercise(runtime, path)
        interrupted(runtime, path)
        disabled = fixture(args, folder, False)
        missing_career(disabled, folder / "missing.sqlite")
    print("永久猎人 TCP 交易/撤离/重启/退役/强杀/放弃验证通过", flush=True)


if __name__ == "__main__":
    main()

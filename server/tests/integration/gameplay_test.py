# 使用正式 TCP 和生成客户端验证配装、动作重放、暂停及免费物资隔离。
import argparse
import json
import sqlite3
import tempfile
import time
from contextlib import closing
from pathlib import Path
from process_test import Server, Client, blob, number, frame, wait_msg
from start_pause_test import start_pause


# 等到服务端确认指定条件，忽略此前已在网络队列中的快照。
def snapshot(cli, predicate):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        value = cli.wait("snapshot")
        player = next(v for v in value["entities"] if v["id"] == value["player_entity_id"])
        if predicate(value, player):
            return value, player
    raise AssertionError("snapshot condition not reached")


# 验证一次完整连接中失败不分配对局、重放不消费、缓存淘汰仍保留水位。
def exercise(args, path):
    srv = Server(args, extra=["--save", str(path)])
    cli = Client(args, srv.start())
    try:
        assert srv.ready["protocol_version"] == 5
        assert srv.ready["content_version"].startswith("gameplay-v4:")
        cli.send({"cmd": "login", "req_id": "login"})
        cli.wait("login_rsp")
        invalid = {"player_cfg_id": 1, "health_segments": [50, 50, 25, 25],
                   "weapons": [{"cfg_id": 1, "ammo_cfg_id": 1},
                               {"cfg_id": 2, "ammo_cfg_id": 2}],
                   "tools": [], "consumables": []}
        cli.send({"cmd": "start", "req_id": "invalid", "after_match_id": "0",
                  "loadout": invalid})
        error = cli.wait("error")
        assert error["req_id"] == "invalid", error
        start = {"cmd": "start", "req_id": "start", "after_match_id": "0"}
        cli.send(start)
        accepted = cli.wait("start_rsp")
        assert accepted["match_id"] == "1", "invalid loadout allocated a match"
        assert accepted["loadout"]["weapons"] == [
            {"cfg_id": 1, "ammo_cfg_id": 1}, {"cfg_id": 3, "ammo_cfg_id": 3}]
        assert accepted["loadout"]["health_segments"] == [50, 50, 25, 25]
        cli.send(start)
        assert cli.wait("start_rsp") == accepted
        changed = dict(accepted["loadout"], player_cfg_id=2)
        cli.send(dict(start, loadout=changed))
        assert cli.wait("error")["code"] == "request_conflict"
        context = {"world_id": accepted["world_id"], "match_id": accepted["match_id"]}

        # 所有消费序号都由调用方显式指定，重发保留请求全部字段。
        def action(seq, cmd, **values):
            request = {"cmd": cmd, "req_id": "a-" + str(seq),
                       "action_seq": str(seq), **context, **values}
            cli.send(request)
            return request, cli.wait(("action_rsp", "error"))

        first, response = action(1, "switch_weapon", slot=2)
        assert response["type"] == "action_rsp", response
        cli.send(first)
        assert cli.wait("action_rsp") == response
        cli.send(dict(first, slot=1))
        assert cli.wait("error")["code"] == "request_conflict"
        _, player = snapshot(cli, lambda s, p: p["active_weapon"] == 2)
        assert len(player["weapons"]) == 2 and len(player["tools"]) == 4

        # 零瞄准向量按前向接受；姿态意图重复发送不能变回站立。
        movement = {"cmd": "input", "seq": "1", **context, "move_x": 0,
                    "aim_x": 0, "aim_y": 0, "jump": False, "fire": False,
                    "reload": False, "move_y": 0, "run": False, "prone": True}
        cli.send(movement)
        cli.wait("ack")
        cli.send(dict(movement, seq="2"))
        cli.wait("ack")
        _, player = snapshot(cli, lambda s, p: p["prone"])
        assert player["width"] == 1000 and player["height"] == 600

        # 开始投掷后暂停冻结前摇；暂停期间新动作必须缓存拒绝并作废。
        use, response = action(2, "use", slot=6)
        assert response["type"] == "action_rsp", response
        cli.send(use)
        assert cli.wait("action_rsp") == response
        before, player = snapshot(cli, lambda s, p: p["use_slot"] == 6)
        assert player["use_ticks"] > 0
        srv.cmd("Pause")
        srv.event("Rsp", "pause")
        cli.wait("pause")
        paused, error = action(3, "switch_weapon", slot=1)
        assert error["code"] == "paused"
        srv.cmd("Resume")
        srv.event("Rsp", "resume")
        resumed = cli.wait("pause")
        assert not resumed["paused"] and int(resumed["discard_action_seq"]) >= 3
        cli.send(paused)
        assert cli.wait("error") == error
        _, response = action(4, "switch_weapon", slot=1)
        assert response["type"] == "action_rsp", response
        after, player = snapshot(cli, lambda s, p: p["use_slot"] == 0)
        assert not after["projectiles"]
        assert next(v for v in player["tools"] if v["slot"] == 6)["count"] == 1

        # 完成投掷后可补给一次；精确重发只重放响应，新序号不能再次补给。
        _, error = action(5, "switch_weapon", slot=8)
        assert error["code"] == "invalid_slot"
        _, response = action(6, "use", slot=6)
        assert response["type"] == "action_rsp", response
        snapshot(cli, lambda s, p: all(v["slot"] != 6 for v in p["tools"]))
        supply, response = action(7, "interact", target_id=2)
        assert response["type"] == "action_rsp", response
        cli.send(supply)
        assert cli.wait("action_rsp") == response
        _, error = action(8, "interact", target_id=2)
        assert error["code"] == "already_used", error
        _, player = snapshot(cli, lambda s, p: int(s["action_seq"]) >= 8)
        assert next(v for v in player["tools"] if v["slot"] == 6)["count"] == 1

        # 顺序受理超过缓存容量，旧序号即使找不到响应也不能再次执行。
        for seq in range(9, 140):
            _, response = action(seq, "bag")
            assert response["type"] == "action_rsp", response
        cli.send(first)
        assert cli.wait("error")["code"] == "stale_action"
        _, response = action(140, "abandon")
        assert response["type"] == "action_rsp", response
        for _ in range(40):
            cli.send({"cmd": "status", "req_id": "status"})
            saved = cli.wait("save_rsp")
            if saved["req_id"] == "status" and saved["state"] == "Committed":
                break
            time.sleep(.025)
        assert saved["state"] == "Committed", saved
        result = json.loads(saved["result_json"])
        assert result["outcome"] == "Abandoned" and result["items"] == []
        cli.send({"cmd": "stash", "req_id": "stash", "revision": "0",
                  "cursor": "0", "limit": 128})
        assert cli.wait("save_rsp")["items"] == []
        print("v5 loadout/action/pause/free-equipment TCP contract passed", flush=True)
    finally:
        srv.stop()
        cli.close()


# 绕过客户端参数门禁验证原始线格式的超大槽位，业务错误不能中止 Runtime。
def raw_action(args):
    srv = Server(args)
    try:
        srv.start()
        conn = srv.connect()
        conn.sendall(frame(10, blob(1, "raw-login")))
        wait_msg(conn, 11)
        conn.sendall(frame(12, blob(1, "raw-start")))
        started = wait_msg(conn, 13)
        context = number(2, started[4]) + number(3, started[2])
        request = frame(16, blob(1, "raw-invalid") + context + number(4, 4)
                        + number(6, 1) + number(7, 4294967295))
        conn.sendall(request)
        rejected = wait_msg(conn, 6)
        assert rejected[1] == b"invalid_slot"
        conn.sendall(request)
        assert wait_msg(conn, 6) == rejected
        conn.sendall(frame(16, blob(1, "raw-valid") + context + number(6, 2)))
        assert wait_msg(conn, 17)[4] == 2
        srv.stop()
    finally:
        srv.cleanup()


# 将隔离存档布置为首版 V1 记录，验证新 Runtime 不改写历史结果或战利品身份。
def old_save(args, path):
    key = "demo-v3:79b1b12379ed4969c2bad2bdcec1577d0a53eee3e1451f3b4b031b59c9a5fde4"
    with closing(sqlite3.connect(path)) as db:
        assert db.execute("PRAGMA user_version").fetchone()[0] == 1
        request, result = db.execute(
            "SELECT request_json,result_json FROM match_result WHERE match_id=1").fetchone()
        request, result = json.loads(request), json.loads(result)
        request.update(content_key=key, outcome="Extracted")
        result.update(content_key=key, outcome="Extracted")
        request["items"] = [{"cfg_id": 2800001, "count": 2}, {"cfg_id": 2800002, "count": 1}]
        for item in request["items"]:
            db.execute("INSERT INTO player_item(player_id,cfg_id,count,acquired_match_id) "
                       "VALUES(1,?,?,1)", (item["cfg_id"], item["count"]))
        rows = db.execute("SELECT item_uid,cfg_id,count,acquired_match_id FROM player_item "
                          "ORDER BY item_uid").fetchall()
        result["items"] = [dict(zip(("item_uid", "cfg_id", "count", "acquired_match_id"), row))
                           for row in rows]
        frozen = json.dumps(result, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
        db.execute("UPDATE match_result SET content_key=?,outcome='Extracted',"
                   "request_json=?,result_json=? WHERE match_id=1",
                   (key, json.dumps(request), frozen))
        db.commit()
    srv = Server(args, extra=["--save", str(path)])
    cli = Client(args, srv.start())
    try:
        cli.send({"cmd": "login", "req_id": "old-login"})
        cli.wait("login_rsp")
        cli.send({"cmd": "result", "req_id": "old-result", "match_id": "1"})
        assert cli.wait("save_rsp")["result_json"] == frozen
        cli.send({"cmd": "stash", "req_id": "old-stash", "revision": "0",
                  "cursor": "0", "limit": 128})
        items = cli.wait("save_rsp")["items"]
        assert [(int(v["cfg_id"]), v["count"]) for v in items] == [(2800001, 2), (2800002, 1)]
        print("old V1 loot identities and historical result preserved", flush=True)
    finally:
        srv.stop()
        cli.close()


# 两种 Runtime 使用同一测试入口及隔离的临时存档。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-gameplay-") as directory:
        path = Path(directory) / "save.sqlite"
        exercise(args, path)
        old_save(args, path)
        raw_action(args)
        start_pause(args)


if __name__ == "__main__":
    main()

# 通过正式桌面 Runtime、生成客户端和 TCP 完成撤离、保存、重启与连续游玩。
import argparse
import json
import tempfile
import time
from pathlib import Path
from process_test import Server, Client


class Demo:
    # 每次启动使用明确持久路径；保留宿主输出与正式协议身份。
    def __init__(self, args, path):
        self.srv = Server(args, extra=["--save", str(path)])
        self.cli = Client(args, self.srv.start())
        self.cli.send({"cmd": "login", "req_id": "login"})
        self.login = self.cli.wait("login_rsp")
        self.seq = 0
        self.match = "0"
        self.world = "0"

    # 关联查询避免异步保存通知与请求响应相互混淆。
    def query(self, cmd, **values):
        self.cli.send({"cmd": cmd, "req_id": "q-" + cmd, **values})
        while True:
            rsp = self.cli.wait("save_rsp")
            if rsp["req_id"] == "q-" + cmd:
                return rsp

    # 开局返回的世界与控制实体是后续命令唯一来源。
    def start(self):
        req = {"cmd": "start", "req_id": "start-" + self.match,
               "after_match_id": self.match}
        self.cli.send(req)
        value = self.cli.wait("start_rsp")
        self.match, self.world = value["match_id"], value["world_id"]
        assert int(value["player_entity_id"]) > 0 and int(self.world) > 0
        self.cli.send(req)
        assert self.cli.wait("start_rsp")["match_id"] == self.match

    # 每帧只发送控制意图；测试无传送、伤害或奖励注入接口。
    def input(self, player, move=0, target=None, fire=False):
        self.seq += 1
        dx = target["x"] - player["x"] if target else 1000
        dy = target["y"] - player["y"] - 100 if target else 0
        scale = max(1, abs(dx), abs(dy))
        ax, ay = int(dx * 1000 / scale), int(dy * 1000 / scale)
        self.cli.send({"cmd": "input", "seq": str(self.seq), "match_id": self.match,
                       "world_id": self.world, "move_x": move, "aim_x": ax or int(not ay),
                       "aim_y": ay, "jump": bool(player["grounded"] and move),
                       "fire": fire, "reload": player["ammo"] == 0})

    # 跑跳穿过灰盒地形，射杀普通怪和 Boss，拾取全部可达掉落后回到出口。
    def extract(self, confirm=True):
        deadline = time.monotonic() + 65
        picked = set()
        while time.monotonic() < deadline:
            snap = self.cli.wait("snapshot")
            if snap["match_id"] != self.match:
                continue
            if snap["phase"] != "Playing":
                assert snap["player_state"] == "Extracted", snap
                break
            player = next(v for v in snap["entities"] if v["id"] == snap["player_entity_id"])
            enemies = [v for v in snap["entities"] if v["kind"] == "enemy" and v["alive"]]
            if enemies:
                enemy = min(enemies, key=lambda e: abs(e["x"] - player["x"]))
                fire = enemy["cfg_id"] == "1002" and enemy["x"] < 15000 or player["x"] > 15800
                self.input(player, 0 if fire else 1, enemy, fire)
                continue
            ground = [v for v in snap["items"] if v["place"] == "Ground"
                      and v["item_id"] not in picked]
            if ground:
                item = min(ground, key=lambda e: abs(e["x"] - player["x"]))
                if (item["x"] - player["x"]) ** 2 + (item["y"] - player["y"]) ** 2 <= 1450 ** 2:
                    self.input(player)
                    self.cli.send({"cmd": "pickup", "req_id": "pick-" + item["item_id"],
                                   "world_id": self.world, "match_id": self.match,
                                   "item_id": item["item_id"]})
                    response = self.cli.wait(("action_rsp", "error"))
                    if response["type"] == "action_rsp":
                        picked.add(item["item_id"])
                    else:
                        assert response["code"] == "out_of_range", response
                else:
                    self.input(player, 1 if item["x"] > player["x"] else -1)
            else:
                self.input(player, -1 if player["x"] > 2500 else 0)
        else:
            raise AssertionError(f"extraction timeout: {snap}")
        if not confirm:
            return
        for _ in range(20):
            saved = self.query("status")
            if saved["state"] == "Committed":
                result = json.loads(saved["result_json"])
                assert result["outcome"] == "Extracted" and result["items"], result
                replay = self.query("retry", match_id=self.match)
                assert replay["result_json"] == saved["result_json"]
                return saved
            time.sleep(.05)
        raise AssertionError(saved)

    # 关闭接受任务后等待数据库排空，先停宿主可避免断连中止诊断混入结果。
    def close(self):
        self.srv.stop()
        self.cli.close()


# 同一 Runtime 连续十局完整撤离，再跨十次启停检查永久 UID 和结果一致。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source")
    parser.add_argument("--bundle")
    parser.add_argument("--policy")
    parser.add_argument("--rounds", type=int, default=10)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="hunter-demo-") as directory:
        path = Path(directory) / "save.sqlite"
        previous = None
        count = 0
        demo = Demo(args, path)
        try:
            for index in range(args.rounds):
                demo.start()
                if previous:
                    assert int(demo.match) > int(previous["match_id"])
                previous = demo.extract()
                count += len(json.loads(previous["result_json"])["items"])
                stash = demo.query("stash", revision="0", cursor="0", limit=128)
                assert len(stash["items"]) == count
                assert len({i["item_uid"] for i in stash["items"]}) == count
                print(f"round {index + 1}: committed; stash items={count}", flush=True)
        finally:
            demo.close()
        for index in range(args.rounds):
            demo = Demo(args, path)
            try:
                stash = demo.query("stash", revision="0", cursor="0", limit=128)
                assert len(stash["items"]) == count
                if previous:
                    result = demo.query("result", match_id=previous["match_id"])
                    assert result["result_json"] == previous["result_json"]
                    assert stash["revision"] == previous["revision"]
                print(f"restart {index + 1}: persistent result unchanged", flush=True)
            finally:
                demo.close()
    print("Runtime TCP demo integration passed")


if __name__ == "__main__":
    main()

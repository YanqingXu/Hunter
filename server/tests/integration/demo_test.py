# 通过正式桌面 Runtime、生成客户端和 TCP 完成撤离、保存、重启与连续游玩。
import argparse
import hashlib
import json
import tempfile
import time
from pathlib import Path
from process_test import Server, Client


# 只读取与当前服务端身份一致的已生成内容，源码和 Bundle 共用同一测试路径。
def content_bytes(args):
    candidates = []
    if getattr(args, "content", None):
        candidates.append(Path(args.content))
    if args.source:
        candidates.append(Path(args.source).parent / "content.json")
    root = Path(args.exe).parent
    candidates.extend((root / "generated/content.json", root.parent / "generated/content.json"))
    path = next((value for value in candidates if value.is_file()), None)
    assert path is not None, "generated production content.json is required for the TCP bot"
    return path.read_bytes()


# 射线瞄准实际身体中心，场景矩形只帮助测试决定移动，不代替服务端命中判定。
def clear_shot(content, player, enemy):
    x, y = player["x"], player["y"] + player["height"] / 2
    dx = enemy["x"] - x
    dy = enemy["y"] + enemy["height"] / 2 - y
    solids = content["map"]["solids"] + [scene for scene in content["scenes"]
                                               if scene["kind"] == "cover"]
    for solid in solids:
        near, far = 0.0, 1.0
        for origin, delta, low, high in ((x, dx, solid["x"], solid["x"] + solid["w"]),
                                       (y, dy, solid["y"], solid["y"] + solid["h"])):
            if delta == 0:
                if origin < low or origin > high:
                    near, far = 1.0, 0.0
                    break
            else:
                first, last = sorted(((low - origin) / delta, (high - origin) / delta))
                near, far = max(near, first), min(far, last)
        if near <= far and near < 1.0 and far > 0.0:
            return False
    return True


class Demo:
    # 每次启动使用明确持久路径；保留宿主输出与正式协议身份。
    def __init__(self, args, path):
        data = content_bytes(args)
        self.content = json.loads(data)
        self.srv = Server(args, extra=["--save", str(path)])
        self.cli = Client(args, self.srv.start())
        assert self.srv.ready["content_version"] == "gameplay-v6:" + hashlib.sha256(data).hexdigest()
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
    def input(self, player, move=0, target=None, fire=False, jump=None):
        self.seq += 1
        dx = target["x"] - player["x"] if target else 1000
        dy = target["y"] + target["height"] / 2 - player["y"] - player["height"] / 2 \
            if target else 0
        scale = max(1, abs(dx), abs(dy))
        ax, ay = int(dx * 1000 / scale), int(dy * 1000 / scale)
        self.cli.send({"cmd": "input", "seq": str(self.seq), "match_id": self.match,
                       "world_id": self.world, "move_x": move, "aim_x": ax or int(not ay),
                       "aim_y": ay, "jump": bool(player["grounded"] and move)
                           if jump is None else bool(player["grounded"] and jump),
                       "fire": fire, "reload": player["ammo"] == 0})

    # 地面寻路只在前方体型会碰墙时起跳，避免在掉落正上方反复跳回平台。
    def obstacle_jump(self, player, move):
        edge = player["x"] + move * (player["width"] / 2 + 600)
        solids = self.content["map"]["solids"] + [scene for scene in self.content["scenes"]
                                                      if scene["kind"] == "cover"]
        return any(player["y"] < solid["y"] + solid["h"]
                   and player["y"] + player["height"] > solid["y"]
                   and (player["x"] <= solid["x"] <= edge if move > 0
                        else edge <= solid["x"] + solid["w"] <= player["x"])
                   for solid in solids) if move else False

    # 根据真实枪弹、掩体和波次持续清怪，拾取可容纳掉落后返回配置中的出口。
    def extract(self, confirm=True):
        deadline = time.monotonic() + 65
        picked = set()
        descent = None
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
                if player["ammo"] == 0 and player["reserve"] == 0:
                    other = next((weapon for weapon in player["weapons"]
                                  if weapon["slot"] != player["active_weapon"]
                                  and weapon["ammo"] + weapon["reserve"] > 0), None)
                    assert other is not None, "both guns exhausted before clearing the raid"
                    self.input(player)
                    self.cli.send({"cmd": "switch_weapon", "req_id": "ammo-switch",
                                   "world_id": self.world, "match_id": self.match,
                                   "slot": other["slot"]})
                    assert self.cli.wait(("action_rsp", "error"))["type"] == "action_rsp"
                    continue
                weapon = self.content["weapons"][str(next(w["cfg_id"] for w in player["weapons"]
                    if w["slot"] == player["active_weapon"]))]
                monster = self.content["monsters"][enemy["cfg_id"]]
                distance = abs(enemy["x"] - player["x"])
                direction = 1 if enemy["x"] >= player["x"] else -1
                fire = distance <= weapon["range"] and clear_shot(self.content, player, enemy)
                inside_exit = any(point["x"] <= player["x"] <= point["x"] + point["w"]
                    and point["y"] <= player["y"] <= point["y"] + point["h"]
                    for point in self.content["extracts"])
                safe = monster["attack_range"] + 1500
                move = direction if not fire or inside_exit else (-direction if distance < safe else 0)
                self.input(player, move, enemy, fire)
                continue
            ground = [v for v in snap["items"] if v["place"] == "Ground"
                      and v["item_id"] not in picked]
            if ground:
                item = next((value for value in ground if descent
                             and value["item_id"] == descent["item_id"]), None)
                item = item or min(ground, key=lambda e: abs(e["x"] - player["x"]))
                if descent and player["grounded"] and player["y"] <= item["y"]:
                    descent = None
                if not descent and player["y"] - item["y"] > 1450:
                    platforms = [solid for solid in self.content["map"]["solids"]
                                 if solid["y"] > item["y"]
                                 and player["y"] >= solid["y"] + solid["h"]
                                 and solid["x"] <= item["x"] <= solid["x"] + solid["w"]]
                    if platforms:
                        solid = max(platforms, key=lambda value: value["y"])
                        edges = (solid["x"] - player["width"] // 2 - 100,
                                 solid["x"] + solid["w"] + player["width"] // 2 + 100)
                        descent = {"item_id": item["item_id"],
                                   "x": min(edges, key=lambda x: abs(x - player["x"]))}
                if descent:
                    move = 0 if abs(player["x"] - descent["x"]) <= 100 \
                        else (1 if player["x"] < descent["x"] else -1)
                    self.input(player, move, jump=False)
                    continue
                if (item["x"] - player["x"]) ** 2 + (item["y"] - player["y"]) ** 2 <= 1450 ** 2:
                    self.input(player)
                    self.cli.send({"cmd": "pickup", "req_id": "pick-" + item["item_id"],
                                   "world_id": self.world, "match_id": self.match,
                                   "item_id": item["item_id"]})
                    response = self.cli.wait(("action_rsp", "error"))
                    if response["type"] == "action_rsp":
                        picked.add(item["item_id"])
                    else:
                        assert response["code"] in ("out_of_range", "already_known_skill",
                                                    "consumable_full", "bag_full"), response
                        if response["code"] != "out_of_range":
                            picked.add(item["item_id"])
                else:
                    move = 1 if item["x"] > player["x"] else -1
                    self.input(player, move, jump=self.obstacle_jump(player, move))
            else:
                point = self.content["extracts"][0]
                target_x = point["x"] + point["w"] // 2
                move = 0 if point["x"] <= player["x"] <= point["x"] + point["w"] \
                    else (1 if player["x"] < target_x else -1)
                self.input(player, move, jump=self.obstacle_jump(player, move))
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
    parser.add_argument("--content")
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

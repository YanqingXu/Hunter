# 启动真实桌面服务，通过独立 Protobuf 编解码探针验证进程、TCP 和生命周期契约。
import argparse
import json
import queue
import socket
import struct
import subprocess
import tempfile
import threading
import time
from pathlib import Path


# 编码无符号 Protobuf varint；测试端保持完整 uint64 精度。
def varint(value):
    data = bytearray()
    while value > 127:
        data.append((value & 127) | 128)
        value >>= 7
    data.append(value)
    return bytes(data)


# 编码长度字段，可用于字符串或嵌套消息。
def blob(tag, value):
    if isinstance(value, str):
        value = value.encode()
    return varint(tag * 8 + 2) + varint(len(value)) + value


# 编码整数字段，零值遵循 proto3 默认值省略规则。
def number(tag, value):
    return varint(tag * 8) + varint(value) if value else b""


# 将信封加上网络序长度头。
def frame(tag, body):
    payload = blob(tag, body)
    return struct.pack("!I", len(payload)) + payload


# 独立编码动作输入，验证生成客户端之外的真实线格式。
def input_frame(seq, move=0, match=1, aim_x=1000, aim_y=0, jump=False,
                fire=False, reload=False):
    return frame(7, number(1, seq) + number(3, match)
                 + number(4, (move << 1) ^ (move >> 31))
                 + number(5, (aim_x << 1) ^ (aim_x >> 31))
                 + number(6, (aim_y << 1) ^ (aim_y >> 31))
                 + number(7, int(jump)) + number(8, int(fire)) + number(9, int(reload)))


# 有界解码 Protobuf varint，拒绝截断和超宽字段。
def read_varint(data, pos):
    value = 0
    for shift in range(0, 70, 7):
        if pos >= len(data):
            raise AssertionError("truncated varint")
        byte = data[pos]
        pos += 1
        if shift == 63 and byte > 1:
            raise AssertionError("varint overflow")
        value |= (byte & 127) << shift
        if not byte & 128:
            return value, pos
    raise AssertionError("varint overflow")


# 读取探针使用的字段类型，未知线型立即失败以发现协议漂移。
def fields(data):
    result = {}
    pos = 0
    while pos < len(data):
        key, pos = read_varint(data, pos)
        tag, kind = key >> 3, key & 7
        assert tag > 0
        if kind == 0:
            value, pos = read_varint(data, pos)
        elif kind == 2:
            size, pos = read_varint(data, pos)
            assert pos + size <= len(data)
            value = data[pos:pos + size]
            pos += size
        else:
            raise AssertionError(f"unexpected wire type {kind}")
        if tag in result:
            if not isinstance(result[tag], list):
                result[tag] = [result[tag]]
            result[tag].append(value)
        else:
            result[tag] = value
    return result


# 恢复 ZigZag 编码的坐标和方向。
def signed(value):
    return (value >> 1) ^ -(value & 1)


# 从 TCP 精确读取要求字节数，连接关闭时抛出明确异常。
def exact(conn, size):
    data = bytearray()
    while len(data) < size:
        part = conn.recv(size - len(data))
        if not part:
            raise EOFError("connection closed")
        data.extend(part)
    return bytes(data)


# 解码完整的服务端信封并检查分帧限制。
def receive(conn):
    size = struct.unpack("!I", exact(conn, 4))[0]
    assert 0 < size <= 65536
    outer = fields(exact(conn, size))
    assert len(outer) == 1
    tag, body = next(iter(outer.items()))
    return tag, fields(body)


# 跳过快照等其他消息，直到观察到指定信封。
def wait_msg(conn, expected, limit=3):
    deadline = time.monotonic() + limit
    while time.monotonic() < deadline:
        conn.settimeout(max(0.01, deadline - time.monotonic()))
        tag, body = receive(conn)
        if tag == expected:
            return body
    raise AssertionError(f"missing message {expected}")


# 确认对端关闭或重置连接，不能把超时当作拒绝通过。
def closed(conn):
    conn.settimeout(3)
    try:
        while conn.recv(4096):
            pass
    except (ConnectionResetError, ConnectionAbortedError):
        pass


class Server:
    # 启动独立服务进程，读取线程只收集有界测试期间的标准输出。
    def __init__(self, args, extra=(), source=None):
        cmd = [args.exe]
        if source is not None:
            cmd += ["--source", source]
        elif args.bundle:
            cmd += ["--bundle", args.bundle, "--policy", args.policy]
        else:
            cmd += ["--source", args.source]
        self.argv = cmd + list(extra)
        self.proc = subprocess.Popen(self.argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.PIPE, text=True, bufsize=1)
        self.events = queue.Queue()
        self.errors = []
        self.reader = threading.Thread(target=self.read_events, daemon=True)
        self.logger = threading.Thread(target=self.read_errors, daemon=True)
        self.reader.start()
        self.logger.start()
        self.ready = None
        self.conn = None

    # 将真实标准输出行解码成事件，保留异常供主测试报告。
    def read_events(self):
        try:
            for line in self.proc.stdout:
                self.events.put(json.loads(line))
        except Exception as err:
            self.events.put(err)

    # 排空诊断管道，防止测试本身制造无关阻塞。
    def read_errors(self):
        self.errors.extend(self.proc.stderr)

    # 发送带关联 ID 的控制命令。
    def cmd(self, name, req=None):
        req = req or name.lower()
        self.proc.stdin.write(json.dumps({"cmd": name, "req_id": req}) + "\n")
        self.proc.stdin.flush()
        return req

    # 等待指定控制事件，默认时限覆盖初始化编译与取消收尾。
    def event(self, kind=None, req=None, timeout=10):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                evt = self.events.get(timeout=max(.01, deadline - time.monotonic()))
            except queue.Empty as err:
                raise AssertionError(f"missing {kind}/{req}; {self.errors}") from err
            if isinstance(evt, Exception):
                raise evt
            if (kind is None or evt.get("type") == kind) and (req is None or evt.get("req_id") == req):
                return evt
        raise AssertionError(f"missing {kind}/{req}")

    # 完成 Start 并验证凭据形状与回环端口。
    def start(self):
        self.cmd("Start")
        self.ready = self.event("Ready", "start")
        assert self.ready["state"] == "Ready" and self.ready["session"] == "Idle"
        assert 0 < self.ready["port"] <= 65535
        assert len(self.ready["instance"]) == 32 and len(self.ready["token"]) == 64
        return self.ready

    # 构建认证请求，允许单项变异以测试各校验字段。
    def hello(self, **override):
        ready = self.ready | override
        return frame(1, number(1, ready["protocol_version"])
                     + blob(2, ready["content_version"]) + blob(3, ready["instance"])
                     + blob(4, ready["token"]))

    # 建立连接并可选执行逐字节拆包握手。
    def connect(self, split=False):
        self.conn = socket.create_connection(("127.0.0.1", self.ready["port"]), 3)
        hello = self.hello()
        if split:
            for byte in hello:
                self.conn.sendall(bytes([byte]))
        else:
            self.conn.sendall(hello)
        ack = wait_msg(self.conn, 2)
        assert ack[1] == 3 and ack[3].decode() == self.ready["instance"]
        return self.conn

    # 请求退出并确保旧端口不再监听；stdin 保持打开以覆盖阻塞读取取消。
    def stop(self):
        if self.proc.poll() is None:
            self.cmd("Stop")
            evt = self.event("Stopped", "stop")
            assert evt["state"] == "Stopped"
        assert self.proc.wait(timeout=7) == 0, self.errors
        if self.ready:
            try:
                probe = socket.create_connection(("127.0.0.1", self.ready["port"]), .3)
            except OSError:
                pass
            else:
                probe.close()
                raise AssertionError("listener leaked after process exit")
        self.cleanup()

    # 失败路径也释放测试创建的进程、套接字与管道。
    def cleanup(self):
        if self.conn:
            self.conn.close()
        if self.proc.poll() is None:
            self.proc.kill()
            self.proc.wait(timeout=3)
        self.reader.join(timeout=2)
        self.logger.join(timeout=2)
        self.proc.stdin.close()
        self.proc.stdout.close()
        self.proc.stderr.close()


# 登录并进入第一局，保留真实请求响应的关联校验。
def enter_game(conn):
    conn.sendall(frame(10, blob(1, "login")))
    login = wait_msg(conn, 11)
    assert login[1] == b"login" and login[2] == 1 and login[4] == b"Lobby"
    conn.sendall(frame(12, blob(1, "start")))
    start = wait_msg(conn, 13)
    assert start[1] == b"start" and start[2] == 1 and start[3] == b"Playing"
    return wait_msg(conn, 9)


# 读取某个稳定实体，检查重复实体字段未被解码器覆盖。
def entity(snap, entity_id=1):
    values = snap.get(6, [])
    values = [values] if isinstance(values, bytes) else values
    for value in values:
        item = fields(value)
        if item.get(1) == entity_id:
            return item
    raise AssertionError(f"missing entity {entity_id}")


# 在同一真实会话验证登录开局、输入去重、暂停清理及恢复不追赶。
def round_trip(args, index):
    srv = Server(args)
    try:
        ready = srv.start()
        srv.cmd("Start", "again")
        assert srv.event("Ready", "again")["instance"] == ready["instance"]
        conn = srv.connect(split=index == 0)
        conn.sendall(frame(12, blob(1, "premature")))
        assert wait_msg(conn, 6)[1] == b"not_logged_in"
        initial = enter_game(conn)
        conn.sendall(frame(10, blob(1, "again")))
        assert wait_msg(conn, 11)[2] == 1
        conn.sendall(frame(12, blob(1, "start")))
        assert wait_msg(conn, 13)[2] == 1
        conn.sendall(input_frame(1, 1) + input_frame(2, 1))
        first = wait_msg(conn, 8)
        second = wait_msg(conn, 8)
        assert first[1] == 1 and second[1] == 2 and second[3] == 1
        conn.sendall(input_frame(2, -1))
        duplicate = wait_msg(conn, 8)
        assert duplicate == second
        snap = wait_msg(conn, 9)
        assert snap[2] == 2
        assert signed(entity(snap).get(3, 0)) > signed(entity(initial).get(3, 0))
        srv.cmd("Pause")
        assert srv.event("Rsp", "pause")["session"] == "Paused"
        conn.settimeout(.08)
        while True:
            try:
                tag, body = receive(conn)
                if tag == 9:
                    snap = body
            except socket.timeout:
                break
        conn.sendall(input_frame(3, 1, fire=True, jump=True))
        rejected = wait_msg(conn, 6)
        assert rejected[1] == b"paused" and rejected[4] == 3
        srv.cmd("Resume")
        assert srv.event("Rsp", "resume")["session"] == "Running"
        resumed = wait_msg(conn, 9)
        assert resumed[1] - snap[1] <= 6, "paused wall time was caught up"
        stopped_x = signed(entity(resumed).get(3, 0))
        later = wait_msg(conn, 9)
        assert signed(entity(later).get(3, 0)) == stopped_x, "held movement survived pause"
        assert entity(later).get(9) == 6, "paused fire was replayed"
        conn.sendall(input_frame(3, 1))
        assert wait_msg(conn, 6)[1] == b"input_discarded"
        extra = socket.create_connection(("127.0.0.1", ready["port"]), 3)
        closed(extra)
        extra.close()
        conn.sendall(input_frame(4, 0))
        assert wait_msg(conn, 8)[1] == 4
        srv.stop()
    finally:
        srv.cleanup()


# 每种坏握手使用独立服务实例，验证失败不能重新续局。
def rejection(args, payload=None, override=None, extra=()):
    srv = Server(args, extra)
    try:
        srv.start()
        srv.conn = socket.create_connection(("127.0.0.1", srv.ready["port"]), 3)
        if override is not None:
            srv.conn.sendall(srv.hello(**override))
        elif payload is not None:
            srv.conn.sendall(payload)
        closed(srv.conn)
        evt = srv.event("Error")
        assert evt["session"] == "Aborted"
        srv.stop()
    finally:
        srv.cleanup()


# 宿主可以先暂停再接入客户端，空输出不得触发未连接套接字发送。
def pause_before_hello(args):
    srv = Server(args)
    try:
        srv.start()
        srv.cmd("Pause")
        assert srv.event("Rsp", "pause")["session"] == "Paused"
        conn = srv.connect()
        assert wait_msg(conn, 15)[1] == 1
        srv.cmd("Resume")
        assert srv.event("Rsp", "resume")["session"] == "Running"
        enter_game(conn)
        srv.stop()
    finally:
        srv.cleanup()


# 验证运行中输入饱和与发送容量不足都明确中止，不留下无界缓存。
def saturation(args):
    srv = Server(args, ["--queue-count", "2"])
    try:
        srv.start()
        conn = srv.connect()
        conn.sendall(b"".join(input_frame(i, 1) for i in range(1, 257)))
        closed(conn)
        assert srv.event("Error")["code"] == "input_backpressure"
        srv.stop()
    finally:
        srv.cleanup()
    srv = Server(args, ["--send-bytes", "1"])
    try:
        srv.start()
        srv.conn = socket.create_connection(("127.0.0.1", srv.ready["port"]), 3)
        srv.conn.sendall(srv.hello())
        closed(srv.conn)
        assert srv.event("Error")["code"] == "send_backpressure"
        srv.stop()
    finally:
        srv.cleanup()


# 使用真实小接收窗口的客户端停止读包，让操作系统发送缓存与服务端队列逐步饱和。
def slow_reader(args):
    srv = Server(args, ["--queue-count", "4096", "--send-bytes", "1024"])
    try:
        srv.start()
        conn = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        conn.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1024)
        conn.settimeout(3)
        conn.connect(("127.0.0.1", srv.ready["port"]))
        srv.conn = conn
        conn.sendall(srv.hello())
        assert wait_msg(conn, 2)[1] == 3
        enter_game(conn)
        seq = 0
        for _ in range(10):
            conn.sendall(b"".join(input_frame(seq + i, 0) for i in range(1, 9)))
            for _ in range(8):
                seq += 1
                assert wait_msg(conn, 8)[1] == seq

        first_unread = seq + 1
        deadline = time.monotonic() + 10
        failure = None
        conn.settimeout(.2)
        while time.monotonic() < deadline:
            try:
                conn.sendall(b"".join(input_frame(seq + i, 0) for i in range(1, 9)))
                seq += 8
            except (OSError, socket.timeout):
                failure = srv.event("Error", timeout=3)
                break
            try:
                evt = srv.events.get_nowait()
            except queue.Empty:
                pass
            else:
                if isinstance(evt, Exception):
                    raise evt
                if evt.get("type") == "Error":
                    failure = evt
                    break
            time.sleep(.004)

        assert failure is not None, "slow TCP reader did not produce bounded backpressure"
        assert failure["code"] == "send_backpressure", failure
        assert seq - first_unread >= 256, "failed before the slow-reader buffers filled"
        closed(conn)
        srv.stop()
    finally:
        srv.cleanup()


# 验证 EOF、初始化失败和输出阻塞时进程均在有限时间内退出。
def host_failures(args):
    srv = Server(args)
    try:
        srv.start()
        srv.proc.stdin.close()
        assert srv.proc.wait(timeout=7) == 0
    finally:
        srv.cleanup()
    srv = Server(args, source="missing-hunter-script.lua")
    try:
        srv.cmd("Start")
        evt = srv.event("Error", "start")
        assert evt["state"] == "Faulted" and evt["code"] == "start_failed"
        assert srv.proc.wait(timeout=7) != 0
    finally:
        srv.cleanup()

    proc = subprocess.Popen([args.exe, "--stop-ms", "100"], stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

    # 故意不读取 stdout，让实际管道写入进入阻塞后验证取消路径。
    def flood():
        try:
            line = json.dumps({"cmd": "Unknown", "req_id": "x" * 128}).encode() + b"\n"
            for _ in range(20000):
                proc.stdin.write(line)
                proc.stdin.flush()
        except (OSError, ValueError):
            pass

    sender = threading.Thread(target=flood, daemon=True)
    sender.start()
    try:
        proc.wait(timeout=7)
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait(timeout=3)
        sender.join(timeout=2)
        try:
            proc.stdin.close()
        except OSError:
            pass
        proc.stdout.close()


# 通过生成协议的命令行客户端验证可交付的人工联调入口。
class Client:
    # 启动客户端并通过标准输入交付本次启动凭据。
    def __init__(self, args, ready):
        self.proc = subprocess.Popen([args.client], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     text=True, bufsize=1)
        self.events = queue.Queue()
        self.errors = []
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.logger = threading.Thread(target=self.log, daemon=True)
        self.reader.start()
        self.logger.start()
        try:
            self.send(ready)
            self.wait("hello_ack")
        except BaseException:
            self.close()
            raise

    # 收集每条协议输出，解析错误作为测试失败传播。
    def read(self):
        try:
            for line in self.proc.stdout:
                self.events.put(json.loads(line))
        except Exception as err:
            self.events.put(err)

    # 持续读取诊断，避免测试创建管道背压。
    def log(self):
        self.errors.extend(self.proc.stderr)

    # 写入一个完整操作对象。
    def send(self, cmd):
        self.proc.stdin.write(json.dumps(cmd) + "\n")
        self.proc.stdin.flush()

    # 等待指定消息，任何客户端本身的失败都立即上报。
    def wait(self, kind, timeout=5):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                evt = self.events.get(timeout=max(.01, deadline - time.monotonic()))
            except queue.Empty as err:
                raise AssertionError(f"CLI missing {kind}: {self.errors}") from err
            if isinstance(evt, Exception):
                raise evt
            assert evt["type"] != "client_error", evt
            if evt["type"] == kind:
                return evt
        raise AssertionError(f"CLI missing {kind}")

    # 用 EOF 正常退出，失败时也只清理本测试创建的进程。
    def close(self):
        try:
            if self.proc.poll() is None:
                self.proc.stdin.close()
                assert self.proc.wait(timeout=5) == 0, self.errors
        finally:
            if self.proc.poll() is None:
                self.proc.kill()
                self.proc.wait(timeout=3)
            self.reader.join(timeout=2)
            self.logger.join(timeout=2)
            for stream in (self.proc.stdin, self.proc.stdout, self.proc.stderr):
                stream.close()


# 驱动真实 Tick 完成死亡和清怪两局，并检查旧局输入与请求不能污染重开。
def combat_rounds(args):
    srv = Server(args)
    cli = None
    try:
        cli = Client(args, srv.start())
        cli.send({"cmd": "login", "req_id": "login"})
        assert cli.wait("login_rsp")["player_id"] == "1"
        seq = 0
        after = "0"
        for expected in ("Dead", "Cleared"):
            cli.send({"cmd": "start", "req_id": "round-" + expected,
                      "after_match_id": after})
            current = cli.wait("start_rsp")["match_id"]
            assert current != after
            if after != "0":
                seq += 1
                cli.send({"cmd": "input", "seq": str(seq), "match_id": after,
                          "move_x": 0, "aim_x": 1000, "aim_y": 0,
                          "jump": False, "fire": True, "reload": False})
                assert cli.wait("error")["code"] == "stale_match"
                cli.send({"cmd": "start", "req_id": "round-Dead", "after_match_id": "0"})
                assert cli.wait("error")["code"] in ("stale_match", "invalid_state")
            deadline = time.monotonic() + 25
            while time.monotonic() < deadline:
                snap = cli.wait("snapshot")
                assert snap["match_id"] == current
                if snap["phase"] != "Playing":
                    assert snap["phase"] == expected, snap
                    break
                player = next(item for item in snap["entities"] if item["kind"] == "player")
                assert all(item["cfg_id"] == "1" for item in snap["entities"])
                enemies = [item for item in snap["entities"]
                           if item["kind"] == "enemy" and item["alive"]]
                assert enemies
                target = min(enemies, key=lambda item: abs(item["x"] - player["x"]))
                dx, dy = target["x"] - player["x"], target["y"] - player["y"] - 100
                scale = max(1, abs(dx), abs(dy))
                aim_x, aim_y = int(dx * 1000 / scale), int(dy * 1000 / scale)
                if aim_x == 0 and aim_y == 0:
                    aim_x = 1000
                fire = expected == "Cleared" and (target["id"] == "2" or player["x"] > 15800)
                move = (1 if player["x"] < 11500 else 0) if expected == "Dead" else (
                    0 if fire else 1)
                seq += 1
                cli.send({"cmd": "input", "seq": str(seq), "match_id": current,
                          "move_x": move, "aim_x": aim_x, "aim_y": aim_y,
                          "jump": player["grounded"] and move != 0,
                          "fire": fire, "reload": player["ammo"] == 0})
            else:
                raise AssertionError(f"combat did not reach {expected}: {snap}")
            after = current
        cli.send({"cmd": "start", "req_id": "third", "after_match_id": after})
        assert cli.wait("start_rsp")["match_id"] == "3"
        fresh = cli.wait("snapshot")
        player = next(item for item in fresh["entities"] if item["kind"] == "player")
        assert player["hp"] == 100 and player["ammo"] == 6 and player["reserve"] == 30
        cli.close()
        cli = None
        srv.stop()
    finally:
        if cli:
            cli.close()
        srv.cleanup()


# 在 stdin 仍打开时断开服务端，客户端也必须取消管道读取并退出。
def client_lifecycle(args):
    srv = Server(args)
    cli = None
    try:
        ready = srv.start()
        bad = ready | {"content_version": "wrong"}
        result = subprocess.run([args.client], input=json.dumps(bad) + "\n",
                                capture_output=True, text=True, timeout=5)
        assert result.returncode != 0
        assert any(json.loads(line).get("type") == "client_error"
                   for line in result.stdout.splitlines()), result
        cli = Client(args, ready)
        srv.stop()
        code = cli.proc.wait(timeout=5)
        assert code in (0, 1), cli.errors
        cli.reader.join(timeout=2)
        if code == 1:
            observed = []
            while not cli.events.empty():
                observed.append(cli.events.get_nowait())
            assert any(evt.get("type") == "client_error" and evt.get("detail") == "receive_failed"
                       for evt in observed), observed
    finally:
        if cli:
            cli.close()
        srv.cleanup()


# 创建有完整中文说明的七入口脚本，仅用于开发 Runtime 的故障注入。
def script_fixture(folder, name, init="return true", event="return true",
                   shutdown="return true"):
    entries = (("init", "ctx", init), ("on_event", "id, payload", event),
               ("tick", "id, dt", "return true"), ("export_state", "", "return '{}'"),
               ("import_state", "value", "return true"),
               ("validate_state", "", "return true"), ("shutdown", "reason", shutdown))
    lines = ["-- 验证宿主失败与调度边界的隔离探针，不实现玩法。"]
    for entry, params, body in entries:
        lines += ["", "-- 执行本场景指定的同步入口行为。", f"function {entry}({params})"]
        lines += ["    " + line for line in body.splitlines()]
        lines += ["end"]
    path = Path(folder) / (name + ".lua")
    path.write_text("\n".join(lines) + "\nreturn true\n", encoding="utf-8")
    return str(path)


# 拒绝不完整十进制、无符号负值、零与越界，并接受公开上下界。
def numeric_args(args):
    limits = {"--handshake-ms": 60000, "--stop-ms": 60000,
              "--queue-count": 65536, "--send-bytes": 16777216}
    for flag, maximum in limits.items():
        for value in ("-1", "+1", "0", "5junk", " 1", "1 ", "", str(maximum + 1),
                      "18446744073709551616"):
            result = subprocess.run([args.exe, flag, value], input=b"", capture_output=True,
                                    timeout=3)
            assert result.returncode != 0, (flag, value, result.stdout)
            assert b"Ready" not in result.stdout
        for value in ("1", str(maximum)):
            result = subprocess.run([args.exe, flag, value],
                                    input=b'{"cmd":"Stop","req_id":"stop"}\n',
                                    capture_output=True, timeout=3)
            assert result.returncode == 0, (flag, value, result.stderr)


# 在 stderr 真正阻塞时再送入超限行，验证输入错误不依赖诊断写入完成。
def blocked_stderr(args, folder):
    source = script_fixture(folder, "blocked_stderr",
                            init="diagnostics.log(string.rep('x', 60000)); return true")
    for drain in (False, True):
        proc = subprocess.Popen([args.exe, "--source", source, "--stop-ms", "100"],
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE)
        logger = None
        if drain:
            logger = threading.Thread(target=proc.stderr.read, daemon=True)
            logger.start()
        proc.stdin.write(b'{"cmd":"Start","req_id":"start"}\n')
        proc.stdin.flush()
        time.sleep(.2)

        # 独立发送线程防止测试本身被已停止读取的 stdin 反压阻塞。
        def send_large():
            try:
                proc.stdin.write(b"x" * 66000 + b"\n")
                proc.stdin.flush()
            except OSError:
                pass

        sender = threading.Thread(target=send_large, daemon=True)
        sender.start()
        try:
            assert proc.wait(timeout=2) != 0, "input failure must set a nonzero exit status"
        finally:
            if proc.poll() is None:
                proc.kill()
                proc.wait(timeout=3)
            sender.join(timeout=2)
            if logger:
                logger.join(timeout=2)
            for stream in (proc.stdin, proc.stdout, proc.stderr):
                try:
                    stream.close()
                except OSError:
                    pass


# 验证退出入口 false、抛错及正常诊断均经真实控制与 stderr 通道交付。
def shutdown_results(args, folder):
    for name, body, failed in (("false", "return false", True),
                               ("error", "error('shutdown_probe_failure')", True),
                               ("log", "return true", False)):
        source = script_fixture(folder, "shutdown_" + name,
                                shutdown="diagnostics.log('shutdown_probe_log'); " + body)
        srv = Server(args, source=source)
        try:
            srv.start()
            srv.cmd("Stop")
            if failed:
                evt = srv.event("Error")
                assert evt["code"] == "shutdown_failed" and evt["state"] == "Faulted"
            stopped = srv.event("Stopped", "stop")
            assert stopped["state"] == ("Faulted" if failed else "Stopped")
            assert (srv.proc.wait(timeout=3) != 0) == failed
            srv.logger.join(timeout=1)
            assert any("shutdown_probe_log" in line for line in srv.errors), srv.errors
        finally:
            srv.cleanup()

    source = script_fixture(folder, "abort_shutdown",
                            shutdown="diagnostics.log('abort_shutdown_log'); return false")
    srv = Server(args, source=source)
    try:
        srv.start()
        srv.connect().close()
        evt = srv.event("Error")
        assert evt["code"] == "shutdown_failed" and evt["state"] == "Faulted"
        assert srv.event("Stopped")["state"] == "Faulted"
        assert srv.proc.wait(timeout=3) != 0
        srv.logger.join(timeout=1)
        assert any("abort_shutdown_log" in line for line in srv.errors), srv.errors
    finally:
        srv.cleanup()


# 用合法但昂贵的输入验证暂停打断积压，并作废尚未执行的动作。
def bounded_dispatch(args, folder):
    event = """if id ~= 1 then return true end
local item = json.decode(payload)
local work = 0
for i = 1, 10000 do
    work = work + 1
end
assert(work == 10000)
net.emit('ack', json.encode({v=3, seq=item.seq, match_id=item.match_id,
    applied_tick=item.applied_tick}))
return true"""
    srv = Server(args, source=script_fixture(folder, "bounded_dispatch", event=event))
    try:
        srv.start()
        conn = srv.connect()
        conn.sendall(b"".join(input_frame(seq, 0) for seq in range(1, 257)))
        assert wait_msg(conn, 8)[1] == 1
        started = time.monotonic()
        srv.cmd("Pause")
        assert srv.event("Rsp", "pause", timeout=1)["session"] == "Paused"
        elapsed = time.monotonic() - started
        seen = [1]
        conn.settimeout(.1)
        while True:
            try:
                tag, body = receive(conn)
            except socket.timeout:
                break
            if tag == 8:
                seen.append(body[1])
        assert len(seen) < 256, "one dispatch drained the complete input backlog"
        assert elapsed < .5, f"pause latency under expensive inputs was {elapsed:.3f}s"
        srv.cmd("Resume")
        srv.event("Rsp", "resume")
        conn.sendall(input_frame(257, 0))
        assert wait_msg(conn, 8)[1] == 257, "discarded backlog was replayed"
        assert seen == list(range(1, len(seen) + 1)), "dispatch changed accepted input order"
        srv.stop()
    finally:
        srv.cleanup()


# 运行十次完整启停以及协议与宿主故障集，失败保留具体断言。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", required=True)
    parser.add_argument("--client", required=True)
    parser.add_argument("--source", default="")
    parser.add_argument("--bundle", default="")
    parser.add_argument("--policy", default="")
    args = parser.parse_args()
    assert input_frame(1, -1) == bytes.fromhex("0000000b3a0908011801200128d00f")
    assert fields(bytes.fromhex("08011001")) == {1: 1, 2: 1}
    for index in range(10):
        round_trip(args, index)
    for override in ({"protocol_version": 1}, {"protocol_version": 2},
                     {"content_version": "wrong"},
                     {"instance": "wrong"}, {"token": "wrong"}):
        rejection(args, override=override)
    for payload in (input_frame(1, 1), b"\0\0\0\0", struct.pack("!I", 65537),
                    b"\0\0\0\1\xff"):
        rejection(args, payload=payload)
    rejection(args, extra=["--handshake-ms", "100"])
    saturation(args)
    pause_before_hello(args)
    slow_reader(args)
    combat_rounds(args)
    client_lifecycle(args)
    host_failures(args)
    numeric_args(args)
    if not args.bundle:
        with tempfile.TemporaryDirectory(prefix="hunter-host-contract-") as folder:
            blocked_stderr(args, folder)
            shutdown_results(args, folder)
            bounded_dispatch(args, folder)
    print("process integration passed: 10 full cycles, framing, auth, pause, limits, "
          "slow TCP reader, pipe cleanup")


if __name__ == "__main__":
    main()

# 校验唯一灰盒配置源并发布规范 JSON、内容摘要和服务端编译常量。
import argparse
import hashlib
import importlib.util
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("hunter_publish", ROOT / "server/tools/publish.py")
PUBLISH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLISH)
MAX_BYTES = 60 * 1024


# 拒绝重复 JSON 键，避免不同解析器对内容身份产生不同解释。
def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


# 精确匹配对象字段，任何未知或缺失字段都属于不支持的配置。
def fields(value, expected, name):
    if type(value) is not dict or set(value) != set(expected.split()):
        raise ValueError(f"{name}: expected exactly {expected}")


# 校验真正的 JSON 整数及范围，布尔和浮点不能伪装成整数。
def integer(value, minimum, maximum, name):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"{name}: expected integer [{minimum},{maximum}]")
    return value


# 校验有界正十进制 ID，并在全内容范围拒绝重复实体或障碍标识。
def identity(value, seen, name):
    if (type(value) is not str or not re.fullmatch(r"[1-9][0-9]{0,9}", value)
            or int(value) > 2147483647 or value in seen):
        raise ValueError(f"{name}: expected unique positive decimal ID")
    seen.add(value)


# 校验实体尺寸与数值上限；偶数尺寸使脚底中心与半宽计算保持整数。
def actor(cfg, extra, name):
    fields(cfg, "width height hp speed " + extra, name)
    for key in ("width", "height"):
        integer(cfg[key], 2, 10000, name + "." + key)
        if cfg[key] % 2:
            raise ValueError(f"{name}.{key}: expected even millimeters")
    integer(cfg["hp"], 1, 1000000, name + ".hp")
    integer(cfg["speed"], 1, 1000, name + ".speed")


# 按脚底中心把角色转换为左、下、右、上四条碰撞边界。
def actor_box(pos, cfg):
    half = cfg["width"] // 2
    return (pos["x"] - half, pos["y"], pos["x"] + half, pos["y"] + cfg["height"])


# 接触边缘不算穿透，只有两个矩形的内部相交才产生出生冲突。
def overlap(left, right):
    return (left[0] < right[2] and right[0] < left[2]
            and left[1] < right[3] and right[1] < left[3])


# 校验脚底出生点位于地图内且不穿透障碍，并要求处于地面或平台顶面。
def spawn(pos, cfg, map_cfg, boxes, name):
    integer(pos["x"], 0, map_cfg["width"], name + ".x")
    integer(pos["y"], 0, map_cfg["height"], name + ".y")
    box = actor_box(pos, cfg)
    if box[0] < 0 or box[2] > map_cfg["width"] or box[3] > map_cfg["height"]:
        raise ValueError(f"{name}: actor extends outside map")
    if any(overlap(box, solid) for solid in boxes):
        raise ValueError(f"{name}: actor overlaps a solid")
    if pos["y"] != 0 and not any(
            solid[3] == pos["y"] and solid[0] <= box[0] and solid[2] >= box[2]
            for solid in boxes):
        raise ValueError(f"{name}: actor has no complete spawn support")
    return box


# 校验巡逻扫掠区不穿墙，且整个巡逻区具有连续的脚底支撑。
def patrol(enemy, cfg, map_cfg, boxes):
    minimum = integer(enemy["patrol_min"], 0, map_cfg["width"], "enemy.patrol_min")
    maximum = integer(enemy["patrol_max"], 0, map_cfg["width"], "enemy.patrol_max")
    if not minimum <= enemy["x"] <= maximum or minimum == maximum:
        raise ValueError("enemy: spawn must be inside a nonempty patrol interval")
    half = cfg["width"] // 2
    swept = (minimum - half, enemy["y"], maximum + half, enemy["y"] + cfg["height"])
    if swept[0] < 0 or swept[2] > map_cfg["width"]:
        raise ValueError("enemy: patrol extends outside map")
    if any(overlap(swept, solid) for solid in boxes):
        raise ValueError("enemy: patrol intersects a solid")
    if enemy["y"] == 0:
        return
    supported = swept[0]
    for solid in sorted(boxes):
        if solid[3] == enemy["y"] and solid[0] <= supported <= solid[2]:
            supported = max(supported, solid[2])
    if supported < swept[2]:
        raise ValueError("enemy: patrol crosses unsupported space")


# 检查所有值域、几何边界和跨对象关系，返回唯一可发布的配置对象。
def validate(doc):
    fields(doc, "v tick_hz map player weapon enemy", "content")
    integer(doc["v"], 1, 1, "v")
    integer(doc["tick_hz"], 60, 60, "tick_hz")
    actor(doc["player"], "jump_speed gravity", "player")
    integer(doc["player"]["jump_speed"], 1, 1000, "player.jump_speed")
    integer(doc["player"]["gravity"], 1, 1000, "player.gravity")
    actor(doc["enemy"], "detect_range attack_range damage attack_ticks", "enemy")
    integer(doc["enemy"]["detect_range"], 1, 100000, "enemy.detect_range")
    integer(doc["enemy"]["attack_range"], 1, doc["enemy"]["detect_range"], "enemy.attack_range")
    integer(doc["enemy"]["damage"], 1, 1000000, "enemy.damage")
    integer(doc["enemy"]["attack_ticks"], 1, 3600, "enemy.attack_ticks")
    weapon = doc["weapon"]
    fields(weapon, "range damage magazine reserve fire_ticks reload_ticks", "weapon")
    integer(weapon["range"], 1, 100000, "weapon.range")
    integer(weapon["damage"], 1, 1000000, "weapon.damage")
    integer(weapon["magazine"], 1, 1000, "weapon.magazine")
    integer(weapon["reserve"], 0, 100000, "weapon.reserve")
    for key in ("fire_ticks", "reload_ticks"):
        integer(weapon[key], 1, 3600, "weapon." + key)
    map_cfg = doc["map"]
    fields(map_cfg, "width height solids spawn enemies", "map")
    for key in ("width", "height"):
        integer(map_cfg[key], 1000, 100000, "map." + key)
    if type(map_cfg["solids"]) is not list or not 0 <= len(map_cfg["solids"]) <= 128:
        raise ValueError("map.solids: expected at most 128 rectangles")
    if type(map_cfg["enemies"]) is not list or not 1 <= len(map_cfg["enemies"]) <= 32:
        raise ValueError("map.enemies: expected 1..32 enemies")
    seen, boxes = {"1"}, []
    for solid in map_cfg["solids"]:
        fields(solid, "id x y w h", "solid")
        identity(solid["id"], seen, "solid.id")
        for pos, size, bound in (("x", "w", "width"), ("y", "h", "height")):
            integer(solid[pos], 0, map_cfg[bound], "solid." + pos)
            integer(solid[size], 1, map_cfg[bound], "solid." + size)
            if solid[pos] + solid[size] > map_cfg[bound]:
                raise ValueError("solid extends outside map")
        box = (solid["x"], solid["y"], solid["x"] + solid["w"], solid["y"] + solid["h"])
        if any(overlap(box, other) for other in boxes):
            raise ValueError("solid rectangles overlap")
        boxes.append(box)
    fields(map_cfg["spawn"], "x y", "map.spawn")
    spawns = [spawn(map_cfg["spawn"], doc["player"], map_cfg, boxes, "map.spawn")]
    for enemy in map_cfg["enemies"]:
        fields(enemy, "id x y patrol_min patrol_max", "enemy spawn")
        identity(enemy["id"], seen, "enemy.id")
        box = spawn(enemy, doc["enemy"], map_cfg, boxes, "enemy spawn")
        if any(overlap(box, other) for other in spawns):
            raise ValueError("actor spawn rectangles overlap")
        spawns.append(box)
        patrol(enemy, doc["enemy"], map_cfg, boxes)
    return doc


# 规范化对象键与空白；数组顺序属于内容语义，保持策划源中的既定顺序。
def canonical(doc):
    data = json.dumps(validate(doc), ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    if len(data.encode("utf-8")) > MAX_BYTES:
        raise ValueError("content exceeds 60 KiB bridge budget")
    return data


# 从规范字节派生版本并生成可直接嵌入宿主的只读常量。
def artifacts(doc):
    data = canonical(doc)
    version = "combat-v1:" + hashlib.sha256(data.encode("utf-8")).hexdigest()
    header = ("// 从 design/combat_demo.json 校验生成的共享内容及身份；禁止手工修改。\n"
              "#pragma once\n\n#include <string_view>\n\nnamespace hunter::content\n{\n"
              f'inline constexpr std::string_view version = "{version}";\n'
              f'inline constexpr std::string_view json_text = R"CONTENT({data})CONTENT";\n'
              "}\n")
    return data.encode("utf-8"), header.encode("utf-8")


# 完成校验与生成后再暂存发布两个文件；失败恢复旧文件并保留明确异常。
def export(source, output, header):
    if source.stat().st_size > MAX_BYTES:
        raise ValueError("source exceeds 60 KiB limit")
    paths = [path.resolve() for path in (source, output, header)]
    if len(set(paths)) != 3:
        raise ValueError("source, content JSON and header must use distinct paths")
    doc = json.loads(source.read_text(encoding="utf-8"), object_pairs_hook=unique_pairs)
    data, code = artifacts(doc)
    PUBLISH.publish_files([(output, data), (header, code)])


# 提供构建系统与客户端均可调用的离线导出入口。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--header", type=Path, required=True)
    args = parser.parse_args()
    try:
        export(args.source, args.output, args.header)
    except (OSError, ValueError, TypeError) as error:
        parser.exit(1, f"content export failed: {error}\n")


if __name__ == "__main__":
    main()

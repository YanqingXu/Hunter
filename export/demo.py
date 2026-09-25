# 将首版 Excel 映射为共用运行配置，复用读表、几何校验与原子制品发布。
import argparse
import copy
import hashlib
import json
from pathlib import Path

import export as sheets
import combat


# 读取明确的首版工作簿，不扫描仍在演进的策划表和重复表名。
def read_tables(source):
    return {t.name: t for t in sheets.collect_tables(source.parent, workbooks=[source])}


# 按表身份读取必需行，缺失引用保留源工作表上下文。
def row(tables, table, key):
    if table not in tables or key not in tables[table].rows:
        raise ValueError(f"{table}[{key}]: missing referenced row")
    return tables[table].rows[key]


# 按源字段映射到原生配置命名，不修改输入或填充缺失数值。
def pick(value, mapping):
    return {target: value[source] for target, source in mapping.items()}


# 提取表中属于指定对象的稳定顺序子行。
def children(tables, table, field, key):
    return [value for _, value in sorted(tables[table].rows.items()) if value[field] == key]


# 校验首版直接伤害招式，前后摇和冷却只来自技能表。
def skill(tables, key, cast):
    value = row(tables, "Skill", key)
    if value["CastType"] != cast:
        raise ValueError(f"Skill[{key}]: unsupported CastType")
    for field in ["WindupTicks", "RecoveryTicks"]:
        combat.integer(value[field], 0, 3600, f"Skill[{key}].{field}")
    if value["WindupTicks"] + value["RecoveryTicks"] >= value["CooldownTicks"]:
        raise ValueError(f"Skill[{key}]: recovery must end before cooldown")
    if cast == 1 and (value["WindupTicks"] or value["RecoveryTicks"]):
        raise ValueError(f"Skill[{key}]: rifle requires zero windup/recovery")
    return value


# 构造地图二的依赖闭包并验证出生、掉落、容量和撤离资格。
def build(tables):
    map_id = 2
    source = row(tables, "Map", map_id)
    if source["Mode"] != 2:
        raise ValueError("Map[2]: evacuation mode required")
    doc = {"v": 3, "tick_hz": 60, "players": {}, "monsters": {}, "weapons": {}}
    game_map = pick(source, {"width": "Width", "height": "Height", "gravity": "Gravity"})
    game_map["solids"] = [dict(pick(v, {"x": "X", "y": "Y", "w": "W", "h": "H"}),
                               id=str(v["SolidIdx"]))
                          for v in children(tables, "MapSolid", "MapIdx", map_id)]
    spawns = children(tables, "PlayerSpawn", "MapIdx", map_id)
    if len(spawns) != 1:
        raise ValueError("PlayerSpawn: exactly one player required")
    start = spawns[0]
    player = row(tables, "Player", start["PlayerIdx"])
    equip = row(tables, "Equip", start["EquipItemIdx"])
    item = row(tables, "Item", start["EquipItemIdx"])
    if equip["WeaponType"] != 7 or item["Kind"] != 1:
        raise ValueError("Equip: fixed rifle required")
    gun = row(tables, "Weapon", equip["WeaponIdx"])
    attack = skill(tables, gun["AttackSkillIdx"], 1)
    doc["players"][str(player["PlayerIdx"])] = pick(player, {
        "width": "Width", "height": "Height", "hp": "Hp", "speed": "Speed",
        "jump_speed": "JumpSpeed"})
    doc["weapons"][str(gun["WeaponIdx"])] = dict(pick(gun, {
        "magazine": "Magazine", "reserve": "InitialReserve", "reload_ticks": "ReloadTicks"}),
        **pick(attack, {"range": "Range", "damage": "Damage", "fire_ticks": "CooldownTicks"}))
    game_map["spawn"] = dict(pick(start, {"x": "X", "y": "Y"}),
        cfg_id=str(player["PlayerIdx"]), weapon_cfg_id=str(gun["WeaponIdx"]))
    enemies = children(tables, "MonsterSpawn", "MapIdx", map_id)
    game_map["enemies"] = [dict(pick(v, {"x": "X", "y": "Y",
        "patrol_min": "PatrolMin", "patrol_max": "PatrolMax"}),
        spawn_id=str(v["SpawnIdx"]), cfg_id=str(v["MonsterIdx"])) for v in enemies]
    doc["map"] = game_map
    items = {}
    extra = {}
    for spawn in enemies:
        monster = row(tables, "Monster", spawn["MonsterIdx"])
        key = str(monster["MonsterIdx"])
        bindings = children(tables, "MonsterSkill", "MonsterIdx", monster["MonsterIdx"])
        if len(bindings) != 1:
            raise ValueError(f"MonsterSkill[{key}]: exactly one attack required")
        attack = skill(tables, bindings[0]["SkillIdx"], 2)
        doc["monsters"][key] = dict(pick(monster, {"width": "Width", "height": "Height",
            "hp": "Hp", "speed": "Speed", "detect_range": "DetectRange"}),
            **pick(attack, {"attack_range": "Range", "damage": "Damage",
                           "attack_ticks": "CooldownTicks"}))
        row(tables, "Drop", monster["DropIdx"])
        drops = []
        seen = set()
        for entry in children(tables, "DropEntry", "DropIdx", monster["DropIdx"]):
            value = row(tables, "Item", entry["ItemIdx"])
            cfg_id = str(value["ItemIdx"])
            if cfg_id in seen or value["Kind"] != 4:
                raise ValueError(f"DropEntry[{entry['EntryIdx']}]: duplicate or unsupported item")
            seen.add(cfg_id)
            combat.integer(value["MaxStack"], 1, 2147483647, "Item.MaxStack")
            combat.integer(entry["ChanceBp"], 0, 10000, "DropEntry.ChanceBp")
            combat.integer(entry["MinCount"], 1, 2147483647, "DropEntry.MinCount")
            combat.integer(entry["MaxCount"], entry["MinCount"], 2147483647, "DropEntry.MaxCount")
            items[cfg_id] = {"name": value["Name"], "max_stack": value["MaxStack"]}
            drops.append(dict(pick(entry, {"chance": "ChanceBp", "min_count": "MinCount",
                "max_count": "MaxCount"}), cfg_id=cfg_id))
        extra[key] = {"rank": monster["Rank"], "windup": attack["WindupTicks"],
                      "recover": attack["RecoveryTicks"], "drops": drops}
    combat.validate(doc)
    for key, value in extra.items():
        doc["monsters"][key].update(value)
    doc["items"] = items
    bag = row(tables, "Bag", 1)
    combat.integer(bag["Slots"], 1, 32, "Bag.Slots")
    combat.integer(bag["PickupRadius"], 1, 10000, "Bag.PickupRadius")
    doc["bag"] = {"slots": bag["Slots"], "pickup_radius": bag["PickupRadius"]}
    maximum = sum((d["max_count"] + items[d["cfg_id"]]["max_stack"] - 1)
                  // items[d["cfg_id"]]["max_stack"] for spawn in game_map["enemies"]
                  for d in extra[spawn["cfg_id"]]["drops"] if d["chance"])
    if maximum > 64:
        raise ValueError("DropEntry: worst-case item count exceeds World capacity 64")
    points = children(tables, "ExtractPoint", "MapIdx", map_id)
    if len(points) != 1:
        raise ValueError("ExtractPoint: first demo requires one exit")
    doc["extracts"] = []
    for point in points:
        boss = row(tables, "MonsterSpawn", point["NeedBossSpawnIdx"])
        if boss["MapIdx"] != map_id or extra[str(boss["MonsterIdx"])]["rank"] != 2:
            raise ValueError("ExtractPoint: expected Boss in same map")
        combat.integer(point["HoldTicks"], 1, 36000, "ExtractPoint.HoldTicks")
        for pos, size, bound in [("X", "W", "width"), ("Y", "H", "height")]:
            combat.integer(point[pos], 0, game_map[bound], f"ExtractPoint.{pos}")
            combat.integer(point[size], 1, game_map[bound] - point[pos], f"ExtractPoint.{size}")
        doc["extracts"].append(dict(pick(point, {"id": "ExtractIdx", "x": "X", "y": "Y",
            "w": "W", "h": "H", "hold_ticks": "HoldTicks"}),
            boss_spawn_id=str(point["NeedBossSpawnIdx"])))
    return doc


# 内容摘要只取规范运行字节；两端使用完全相同的 JSON 和内容身份。
def artifacts(tables):
    doc = build(tables)
    data = json.dumps(doc, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    if len(data.encode()) > combat.MAX_BYTES:
        raise ValueError("demo content exceeds bridge limit")
    version = "demo-v3:" + hashlib.sha256(data.encode()).hexdigest()
    header = ('// 从首版 Excel 生成的共享配置，禁止手工修改。\n#pragma once\n'
              '#include <string_view>\nnamespace hunter::content\n{\n'
              f'inline constexpr std::string_view version = "{version}";\n'
              f'inline constexpr std::string_view json_text = R"CONTENT({data})CONTENT";\n}}\n')
    return data.encode(), header.encode()


# 为 CMake 提供显式工作簿入口，任何错误均阻止发布新配置。
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--header", type=Path, required=True)
    args = parser.parse_args()
    try:
        data, header = artifacts(read_tables(args.source))
        combat.PUBLISH.publish_files([(args.output, data), (args.header, header)])
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"{args.source}: demo export failed: {error}\n")


if __name__ == "__main__":
    main()

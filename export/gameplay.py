# 从明确的根表清单生成 DEMO v4 内容，旧首版工作簿只供显式回归测试使用。
import argparse
import copy
import hashlib
import json
from pathlib import Path

import combat
import demo
import export as sheets


TABLES = set("Player Weapon Ammunition Item Equip Tool Monster Attack Drop DropEntry "
             "Map MapSolid PlayerSpawn MonsterSpawn ExtractPoint Bag Rules Loadout Scene".split())
PLAYER_FIELDS = {
    "width": "Width", "height": "Height", "hp": "Hp", "speed": "Speed",
    "jump_speed": "JumpSpeed", "run_speed": "Run", "prone_speed": "ProneSpeed",
    "prone_width": "ProneWidth", "prone_height": "ProneHeight", "stamina": "Stamina",
    "stamina_delay": "StaminaDelay", "stamina_rate": "StaminaRate", "run_cost": "RunCost",
    "jump_cost": "JumpCost", "health_delay": "HealthDelay", "health_rate": "HealthRate",
    "weight": "EquipmentLimit",
}
RULE_FIELDS = {
    "weapon_slots": "WeaponSlots", "tool_slots": "ToolSlots",
    "consumable_slots": "ConsumableSlots", "tool_move_percent": "ToolMovePercent",
    "max_scenes": "MaxScenes", "max_projectiles": "MaxProjectiles",
    "melee_stamina": "MeleeStamina", "melee_ticks": "MeleeTicks",
}


# 从规范竖线整数序列读取配置 ID 或血段，禁止空元素与别名数字。
def numbers(text, name):
    if not isinstance(text, str) or not text:
        raise ValueError(f"{name}: expected nonempty integer list")
    values = text.split("|")
    for value in values:
        combat.identity(value, set(), name)
    return [int(value) for value in values]


# 只解析清单选中的记录，未选中的不完整草稿和以井号开头的说明行不进入生产数据。
def select(sheet, ids):
    fields = sheets.read_fields(sheet)
    keys = [field for field in fields if "key" in field.flags]
    if len(keys) != 1 or keys[0].kind != "int32" or "server" not in keys[0].flags:
        sheet.fail(4, 1, "生产表必须有唯一的 server/key int32 主键")
    key = keys[0]
    server = [field for field in fields if "server" in field.flags]
    rows = {}
    for number in sorted({r for r, c in sheet.cells if r >= 5}):
        cell = sheet.cell(number, key.column)
        if not cell.text or cell.text.startswith("#"):
            continue
        identity = sheets.convert(sheet, number, key)
        if identity not in ids:
            continue
        if identity in rows:
            sheet.fail(number, key.column, f"生产主键重复: {identity}")
        rows[identity] = {field.name: sheets.convert(sheet, number, field) for field in server}
    if set(rows) != set(ids):
        sheet.fail(1, 1, f"清单记录不存在: {sorted(set(ids) - set(rows))}")
    return sheets.Table(sheet.name.split("|")[-1], sheet, server, rows)


# 显式清单是唯一生产入口，拒绝目录逃逸、重复表和隐式补入依赖。
def read_tables(source):
    source = Path(source).resolve()
    spec = json.loads(source.read_text(encoding="utf-8"), object_pairs_hook=combat.unique_pairs)
    combat.fields(spec, "v sources", "manifest")
    if spec["v"] != 1 or not isinstance(spec["sources"], list):
        raise ValueError("manifest: unsupported version or sources")
    books, tables = {}, {}
    for entry in spec["sources"]:
        combat.fields(entry, "file sheet ids", "source")
        if not isinstance(entry["file"], str) or not isinstance(entry["sheet"], str):
            raise ValueError("source: file and sheet must be strings")
        path = (source.parent / entry["file"]).resolve()
        if path.parent != source.parent or path.suffix.lower() != ".xlsx":
            raise ValueError("source: only root design workbooks are allowed")
        ids = entry["ids"]
        if not isinstance(ids, list) or not ids or len(set(ids)) != len(ids):
            raise ValueError("source.ids: expected nonempty unique IDs")
        for key in ids:
            combat.integer(key, 1, 2147483647, "source.ids")
        if path not in books:
            books[path] = {sheet.name: sheet for sheet in sheets.read_workbook(path)}
        if entry["sheet"] not in books[path]:
            raise ValueError(f"{path}: missing worksheet {entry['sheet']}")
        table = select(books[path][entry["sheet"]], ids)
        if table.name in tables:
            raise ValueError(f"duplicate production table: {table.name}")
        tables[table.name] = table
    if set(tables) != TABLES:
        raise ValueError(f"manifest: expected tables {sorted(TABLES)}")
    return tables


# 按已固定的字段关系构建唯一的共享运行数据，不读取新版天赋表。
def build(tables):
    row, pick, children = demo.row, demo.pick, demo.children
    for name, ids in (("Map", {2}), ("Bag", {1}), ("Rules", {1}), ("Loadout", {1})):
        if set(tables[name].rows) != ids:
            raise ValueError(f"{name}: unexpected production rows")
    for name in ("MapSolid", "PlayerSpawn", "MonsterSpawn", "ExtractPoint", "Scene"):
        if any(value["MapIdx"] != 2 for value in tables[name].rows.values()):
            raise ValueError(f"{name}: selected records must belong to Map 2")
    for value in tables["DropEntry"].rows.values():
        row(tables, "Drop", value["DropIdx"])
    source = row(tables, "Map", 2)
    if source["Mode"] != 2:
        raise ValueError("Map[2]: extraction mode required")
    doc = {"v": 4, "tick_hz": 60, "players": {}, "weapons": {}, "ammo": {},
           "monsters": {}, "tools": {}, "items": {}}
    for key, value in sorted(tables["Player"].rows.items()):
        doc["players"][str(key)] = dict(pick(value, PLAYER_FIELDS),
            hp_segments=numbers(value["HPSetting"], f"Player[{key}].HPSetting"))
    for key, value in sorted(tables["Item"].rows.items()):
        doc["items"][str(key)] = {"name": value["Name"], "max_stack": value["MaxStack"],
                                 "kind": value["Kind"], "type": value["Type"]}
    for key, value in sorted(tables["Ammunition"].rows.items()):
        item = row(tables, "Item", value["ItemIdx"])
        if item["Kind"] != 3 or value["AdditionalStatus"] != 0:
            raise ValueError(f"Ammunition[{key}]: only default ammunition is supported")
        doc["ammo"][str(key)] = dict(pick(value, {"pellets": "Bullet", "damage": "Bamage",
            "range": "Distance", "spread_deg": "Biffusion", "penetration": "Penetration",
            "loss": "Weaken"}), item_cfg_id=str(value["ItemIdx"]))
    for key, value in sorted(tables["Weapon"].rows.items()):
        ammo = doc["ammo"][str(value["DefaultBullet"])]
        if value["BulletType"] != value["DefaultBullet"]:
            raise ValueError(f"Weapon[{key}]: only its default ammunition is supported")
        doc["weapons"][str(key)] = dict(pick(value, {"magazine": "Magazine",
            "reserve": "InitialReserve", "fire_ticks": "Interval", "reload_ticks": "ReloadTicks",
            "reload_kind": "ReloadType", "weight": "LoadBearing", "melee_damage": "Melee",
            "melee_range": "Scope"}), ammo_cfg_id=str(value["DefaultBullet"]),
            range=ammo["range"], damage=ammo["damage"])
    for value in tables["Equip"].rows.values():
        if row(tables, "Item", value["ItemIdx"])["Kind"] != 2:
            raise ValueError("Equip: gun must reference a gun item")
        row(tables, "Weapon", value["WeaponIdx"])
    doc["rules"] = pick(row(tables, "Rules", 1), RULE_FIELDS)
    kinds = {(1, 1): "knife", (1, 3): "medkit", (2, 5): "needle", (2, 6): "bomb"}
    for key, value in sorted(tables["Tool"].rows.items()):
        item = row(tables, "Item", key)
        kind = kinds.get((value["ToolType"], value["Subdivision"]))
        if item["Kind"] != 4 or kind is None:
            raise ValueError(f"Tool[{key}]: unsupported core tool")
        if kind != "knife" and value["SlowPercent"] != 100 - doc["rules"]["tool_move_percent"]:
            raise ValueError(f"Tool[{key}]: inconsistent use movement limit")
        doc["tools"][str(key)] = {"kind": kind, "uses": value["Quantity"],
            "use_ticks": value["Lag"], "heal": value["Value"] if kind in ("medkit", "needle") else 0,
            "damage": value["Value"] if kind in ("knife", "bomb") else 0,
            "range": value["Scope"] if kind == "knife" else 0,
            "radius": value["Scope"] if kind == "bomb" else 0,
            "throw_range": value["ThrowingDistance"], "speed": value["Speed"],
            "stamina": value["PhysicalExhaustion"],
            "cooldown_ticks": doc["rules"]["melee_ticks"] if kind == "knife" else 0}
    for key, value in sorted(tables["Monster"].rows.items()):
        attack = row(tables, "Attack", value["AttackIdx"])
        row(tables, "Drop", value["DropIdx"])
        drops = []
        for entry in children(tables, "DropEntry", "DropIdx", value["DropIdx"]):
            if row(tables, "Item", entry["ItemIdx"])["Kind"] != 5:
                raise ValueError("DropEntry: only extraction loot may drop")
            drops.append(dict(pick(entry, {"chance": "ChanceBp", "min_count": "MinCount",
                "max_count": "MaxCount"}), cfg_id=str(entry["ItemIdx"])))
        doc["monsters"][str(key)] = dict(pick(value, {"width": "Width", "height": "Height",
            "hp": "Hp", "speed": "Speed", "detect_range": "DetectRange", "rank": "Rank"}),
            **pick(attack, {"attack_range": "Range", "damage": "Damage",
                "attack_ticks": "CooldownTicks", "windup": "WindupTicks", "recover": "RecoveryTicks"}),
            drops=drops)
    starts = children(tables, "PlayerSpawn", "MapIdx", 2)
    if len(starts) != 1:
        raise ValueError("Map[2]: exactly one player spawn is required")
    start = starts[0]
    equip = row(tables, "Equip", start["EquipItemIdx"])
    doc["map"] = dict(pick(source, {"width": "Width", "height": "Height", "gravity": "Gravity"}),
        spawn=dict(pick(start, {"x": "X", "y": "Y"}), cfg_id=str(start["PlayerIdx"]),
            weapon_cfg_id=str(equip["WeaponIdx"])),
        solids=[dict(pick(v, {"x": "X", "y": "Y", "w": "W", "h": "H"}), id=str(v["SolidIdx"]))
                for v in children(tables, "MapSolid", "MapIdx", 2)],
        enemies=[dict(pick(v, {"x": "X", "y": "Y", "patrol_min": "PatrolMin", "patrol_max": "PatrolMax"}),
            spawn_id=str(v["SpawnIdx"]), cfg_id=str(v["MonsterIdx"]))
            for v in children(tables, "MonsterSpawn", "MapIdx", 2)])
    doc["bag"] = pick(row(tables, "Bag", 1), {"slots": "Slots", "pickup_radius": "PickupRadius"})
    doc["extracts"] = [dict(pick(v, {"id": "ExtractIdx", "x": "X", "y": "Y", "w": "W",
        "h": "H", "hold_ticks": "HoldTicks"}), boss_spawn_id=str(v["NeedBossSpawnIdx"]))
        for v in children(tables, "ExtractPoint", "MapIdx", 2)]
    doc["scenes"] = [dict(pick(v, {"kind": "Kind", "x": "X", "y": "Y", "w": "W", "h": "H"}),
        id=str(v["SceneIdx"]), penetrable=bool(v["Penetrable"]))
        for v in children(tables, "Scene", "MapIdx", 2)]
    if len(doc["extracts"]) != 1 or {s["kind"] for s in doc["scenes"]} != {"ladder", "supply", "cover"}:
        raise ValueError("production requires one exit and all three core scene kinds")
    for value in tables["Scene"].rows.values():
        combat.integer(value["Penetrable"], 0, 1, "Scene.Penetrable")
    value = row(tables, "Loadout", 1)
    doc["default_loadout"] = {"player_cfg_id": str(value["PlayerIdx"])}
    for target, field in (("weapons", "Weapons"), ("ammo", "Ammo"), ("tools", "Tools"),
                          ("consumables", "Consumables")):
        doc["default_loadout"][target] = [str(v) for v in numbers(value[field], "Loadout." + field)]
    doc["default_loadout"]["hp_segments"] = list(doc["players"][str(value["PlayerIdx"])]["hp_segments"])
    return validate(doc)


# 复用已有几何和战斗边界校验，仅向旧校验器投影它认识的字段。
def validate_core(doc):
    old = {"v": 3, "tick_hz": doc["tick_hz"], "map": copy.deepcopy(doc["map"])}
    for table, fields in {
        "players": "width height hp speed jump_speed",
        "monsters": "width height hp speed detect_range attack_range damage attack_ticks",
        "weapons": "range damage magazine reserve fire_ticks reload_ticks",
    }.items():
        old[table] = {key: {field: cfg[field] for field in fields.split()}
                      for key, cfg in doc[table].items()}
    combat.validate(old)


# 验证新玩法引用、数值、血段、初装容量、掉落及新增场景与旧巡逻的兼容性。
def validate(doc):
    combat.fields(doc, "v tick_hz map players monsters weapons items bag extracts ammo tools rules scenes default_loadout", "content")
    combat.integer(doc["v"], 4, 4, "content.v")
    validate_core(doc)
    for name in ("items", "ammo"):
        combat.cfg_table(doc[name], name)
    for key, cfg in doc["players"].items():
        for field in ("run_speed", "prone_speed", "stamina", "stamina_rate", "health_rate", "weight"):
            combat.integer(cfg[field], 1, 10000, f"Player[{key}].{field}")
        for field in ("stamina_delay", "health_delay"):
            combat.integer(cfg[field], 0, 36000, f"Player[{key}].{field}")
        for field in ("prone_width", "prone_height"):
            combat.integer(cfg[field], 2, 10000, f"Player[{key}].{field}")
            if cfg[field] % 2:
                raise ValueError("prone dimensions must be even")
        for field in ("run_cost", "jump_cost"):
            combat.integer(cfg[field], 0, 0, f"Player[{key}].{field}")
        if not isinstance(cfg["hp_segments"], list) or not cfg["hp_segments"] or any(
                type(value) is not int or value not in (25, 50) for value in cfg["hp_segments"]):
            raise ValueError("hp_segments: only ordered 25/50 segments are supported")
        if sum(cfg["hp_segments"]) != cfg["hp"]:
            raise ValueError("hp_segments: sum must equal maximum health")
    for key, cfg in doc["items"].items():
        combat.integer(cfg["max_stack"], 1, 2147483647, f"Item[{key}].max_stack")
    for key, cfg in doc["ammo"].items():
        combat.ref(doc["items"], cfg["item_cfg_id"], "ammo.item_cfg_id")
        for field, low, high in (("pellets", 1, 16), ("damage", 1, 1000000), ("range", 1, 100000),
                                 ("spread_deg", 0, 90), ("penetration", 0, 32), ("loss", 0, 1000000)):
            combat.integer(cfg[field], low, high, f"Ammo[{key}].{field}")
    for key, cfg in doc["weapons"].items():
        ammo = combat.ref(doc["ammo"], cfg["ammo_cfg_id"], "weapon.ammo_cfg_id")
        if cfg["range"] != ammo["range"] or cfg["damage"] != ammo["damage"]:
            raise ValueError("weapon projection differs from default ammunition")
        combat.integer(cfg["reload_kind"], 1, 2, "weapon.reload_kind")
        for field in ("weight", "melee_damage", "melee_range"):
            combat.integer(cfg[field], 1, 1000000, f"Weapon[{key}].{field}")
    rules = doc["rules"]
    combat.fields(rules, " ".join(RULE_FIELDS), "rules")
    for field, high in (("weapon_slots", 2), ("tool_slots", 4), ("consumable_slots", 4),
                        ("max_scenes", 32), ("max_projectiles", 16)):
        combat.integer(rules[field], 1, high, "rules." + field)
    combat.integer(rules["tool_move_percent"], 1, 100, "rules.tool_move_percent")
    combat.integer(rules["melee_stamina"], 1, 10000, "rules.melee_stamina")
    combat.integer(rules["melee_ticks"], 1, 3600, "rules.melee_ticks")
    for key, cfg in doc["tools"].items():
        combat.ref(doc["items"], key, "tool.item")
        if cfg["kind"] not in ("knife", "medkit", "needle", "bomb"):
            raise ValueError("tool.kind: unsupported")
        combat.integer(cfg["uses"], 1, 100, "tool.uses")
        for field in ("use_ticks", "heal", "damage", "range", "radius", "throw_range", "speed", "stamina", "cooldown_ticks"):
            combat.integer(cfg[field], 0, 1000000, "tool." + field)
        if cfg["kind"] == "bomb" and min(cfg["radius"], cfg["throw_range"], cfg["speed"]) <= 0:
            raise ValueError("bomb requires positive radius, throw range and speed")
    load = doc["default_loadout"]
    player = combat.ref(doc["players"], load["player_cfg_id"], "loadout.player")
    if load["hp_segments"] != player["hp_segments"]:
        raise ValueError("loadout blood segments differ from selected player defaults")
    if not 1 <= len(load["weapons"]) <= rules["weapon_slots"] or len(load["ammo"]) != len(load["weapons"]):
        raise ValueError("loadout: invalid weapon or ammunition slots")
    weight = 0
    for key, ammo in zip(load["weapons"], load["ammo"]):
        gun = combat.ref(doc["weapons"], key, "loadout.weapon")
        if ammo != gun["ammo_cfg_id"]:
            raise ValueError("loadout: incompatible ammunition")
        weight += gun["weight"]
    if weight > player["weight"]:
        raise ValueError("loadout: weapon weight exceeds player capacity")
    for group, capacity, kinds in (("tools", "tool_slots", ("knife", "medkit")),
                                   ("consumables", "consumable_slots", ("needle", "bomb"))):
        values = load[group]
        if len(values) > rules[capacity] or len(values) != len(set(values)):
            raise ValueError("loadout: too many slots or duplicate tool")
        for key in values:
            if combat.ref(doc["tools"], key, "loadout.tool")["kind"] not in kinds:
                raise ValueError("loadout: wrong tool category")
    if doc["map"]["spawn"]["cfg_id"] != load["player_cfg_id"] or doc["map"]["spawn"]["weapon_cfg_id"] != load["weapons"][0]:
        raise ValueError("map.spawn and default_loadout disagree")
    combat.integer(doc["bag"]["slots"], 1, 32, "bag.slots")
    combat.integer(doc["bag"]["pickup_radius"], 1, 10000, "bag.pickup_radius")
    maximum = 0
    for spawn in doc["map"]["enemies"]:
        cfg = doc["monsters"][spawn["cfg_id"]]
        combat.integer(cfg["rank"], 1, 3, "monster.rank")
        for field in ("windup", "recover"):
            combat.integer(cfg[field], 0, 3600, "monster." + field)
        if cfg["windup"] + cfg["recover"] >= cfg["attack_ticks"]:
            raise ValueError("monster recovery must finish before cooldown")
        seen = set()
        for drop in cfg["drops"]:
            if drop["cfg_id"] in seen:
                raise ValueError("duplicate item in monster drop group")
            seen.add(drop["cfg_id"])
            item = combat.ref(doc["items"], drop["cfg_id"], "drop.item")
            combat.integer(drop["chance"], 0, 10000, "drop.chance")
            combat.integer(drop["min_count"], 1, 2147483647, "drop.min_count")
            combat.integer(drop["max_count"], drop["min_count"], 2147483647, "drop.max_count")
            if drop["chance"]:
                maximum += (drop["max_count"] + item["max_stack"] - 1) // item["max_stack"]
    if maximum > 64:
        raise ValueError("worst-case drop capacity exceeds 64")
    validate_scenes(doc)
    for point in doc["extracts"]:
        matches = [s for s in doc["map"]["enemies"] if s["spawn_id"] == point["boss_spawn_id"]]
        if len(matches) != 1 or doc["monsters"][matches[0]["cfg_id"]]["rank"] != 3:
            raise ValueError("extract requires one Boss spawn from this map")
        combat.integer(point["hold_ticks"], 1, 36000, "extract.hold_ticks")
        rectangle(point, doc["map"], "extract")
    return doc


# 场景和撤离区域使用左下角矩形，所有边界必须处于地图内部。
def rectangle(value, game_map, name):
    for pos, size, bound in (("x", "w", "width"), ("y", "h", "height")):
        combat.integer(value[pos], 0, game_map[bound], name + "." + pos)
        combat.integer(value[size], 1, game_map[bound] - value[pos], name + "." + size)
    return value["x"], value["y"], value["x"] + value["w"], value["y"] + value["h"]


# 新掩体不得改变既有出生和怪物巡逻可达性，梯子上端必须接到平台顶面。
def validate_scenes(doc):
    if not isinstance(doc["scenes"], list) or len(doc["scenes"]) > doc["rules"]["max_scenes"]:
        raise ValueError("scene capacity exceeded")
    seen, covers = set(), []
    game_map = doc["map"]
    solids = [rectangle(v, game_map, "solid") for v in game_map["solids"]]
    for scene in doc["scenes"]:
        combat.identity(scene["id"], seen, "scene.id")
        if scene["kind"] not in ("ladder", "supply", "cover") or type(scene["penetrable"]) is not bool:
            raise ValueError("scene kind or penetrability invalid")
        box = rectangle(scene, game_map, "scene")
        if scene["penetrable"] != (scene["kind"] == "cover"):
            raise ValueError("only cover is penetrable")
        if scene["kind"] == "cover":
            if any(combat.overlap(box, other) for other in solids + covers):
                raise ValueError("cover overlaps another solid")
            covers.append(box)
        if scene["kind"] == "ladder" and not any(
                top == box[3] and left <= box[0] and right >= box[2]
                for left, bottom, right, top in solids):
            raise ValueError("ladder top requires a supporting platform")
    for key, cfg in doc["players"].items():
        combat.spawn(game_map["spawn"], cfg, game_map, solids + covers, "player spawn")
    for spawn in game_map["enemies"]:
        cfg = doc["monsters"][spawn["cfg_id"]]
        combat.spawn(spawn, cfg, game_map, solids + covers, "monster spawn")
        combat.patrol(spawn, cfg, game_map, solids + covers)


# 将显式测试夹具升级为 v4，保留其旧地图、伤害和弹药等基准，不用于生产回退。
def upgrade_fixture(old):
    doc = copy.deepcopy(old)
    doc["v"] = 4
    doc["ammo"], doc["tools"], doc["items"], doc["scenes"] = {}, {}, {}, []
    doc["bag"] = {"slots": 8, "pickup_radius": 1500}
    doc["extracts"] = []
    doc["rules"] = {"weapon_slots": 2, "tool_slots": 4, "consumable_slots": 4,
        "tool_move_percent": 70, "max_scenes": 32, "max_projectiles": 16,
        "melee_stamina": 20, "melee_ticks": 30}
    for cfg in doc["players"].values():
        if cfg["hp"] % 25:
            raise ValueError("fixture health must support 25 point blood segments")
        cfg.update(run_speed=cfg["speed"] * 3 // 2, prone_speed=cfg["speed"] // 2,
            prone_width=1000, prone_height=600, stamina=100, stamina_delay=120,
            stamina_rate=20, run_cost=0, jump_cost=0, health_delay=300, health_rate=5,
            weight=3, hp_segments=[25] * (cfg["hp"] // 25))
    for key, cfg in doc["weapons"].items():
        item = str(20000 + int(key))
        doc["items"][item] = {"name": "回归弹药", "max_stack": 99, "kind": 3, "type": 1}
        doc["ammo"][key] = {"item_cfg_id": item, "pellets": 1, "damage": cfg["damage"],
            "range": cfg["range"], "spread_deg": 0, "penetration": 0, "loss": 0}
        cfg.update(ammo_cfg_id=key, weight=1, reload_kind=1, melee_damage=50, melee_range=900)
    for cfg in doc["monsters"].values():
        cfg.update(rank=1, windup=0, recover=0, drops=[])
    spawn = doc["map"]["spawn"]
    doc["default_loadout"] = {"player_cfg_id": spawn["cfg_id"],
        "weapons": [spawn["weapon_cfg_id"]], "ammo": [spawn["weapon_cfg_id"]],
        "tools": [], "consumables": [], "hp_segments": list(doc["players"][spawn["cfg_id"]]["hp_segments"])}
    return validate(doc)


# 从规范 JSON 字节生成共享身份和只读 C++ 内容，任何校验失败均不发布。
def artifacts(tables):
    return content_artifacts(build(tables))


# 生产与显式测试升级共用同一套 v4 验证和确定性序列化。
def content_artifacts(doc):
    data = json.dumps(validate(doc), ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    if len(data) > combat.MAX_BYTES:
        raise ValueError("gameplay content exceeds 60 KiB")
    version = "gameplay-v4:" + hashlib.sha256(data).hexdigest()
    header = ('// 从根 Excel 生产清单生成的共享配置，禁止手工修改。\n#pragma once\n'
        '#include <string_view>\nnamespace hunter::content\n{\n'
        f'inline constexpr std::string_view version = "{version}";\n'
        f'inline constexpr std::string_view json_text = R"CONTENT({data.decode("utf-8")})CONTENT";\n}}\n')
    return data, header.encode("utf-8")


# 提供正式清单构建、无写入检查和明确区分的旧夹具升级入口。
def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--source", type=Path)
    source.add_argument("--fixture", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--header", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.check and (args.output is None or args.header is None):
        parser.error("--output and --header are required unless --check is used")
    try:
        if args.fixture:
            old = json.loads(args.fixture.read_text(encoding="utf-8"), object_pairs_hook=combat.unique_pairs)
            data, header = content_artifacts(upgrade_fixture(old))
        else:
            data, header = artifacts(read_tables(args.source))
        if not args.check:
            combat.PUBLISH.publish_files([(args.output, data), (args.header, header)])
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"gameplay export failed: {error}\n")


if __name__ == "__main__":
    main()

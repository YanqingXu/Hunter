-- 将导出的原字段配置表转换为玩法共享模型，只在脚本初始化与构建校验时运行。
return function(deps)
    local api = {}

    -- 按数值主键稳定遍历，数组顺序不依赖哈希表布局。
    local function rows(values)
        local keys, result = {}, {}
        for key, value in pairs(values) do
            keys[#keys + 1] = key
        end
        table.sort(keys)
        for _, key in ipairs(keys) do
            result[#result + 1] = {key, values[key]}
        end
        return result
    end

    -- 解析规范的竖线正整数列表，拒绝空项与别名数字。
    local function numbers(text)
        assert(type(text) == "string" and #text > 0, "expected nonempty integer list")
        local result, start = json.array({}), 1
        for index = 1, #text + 1 do
            if index > #text or string.sub(text, index, index) == "|" then
                local part = string.sub(text, start, index - 1)
                assert(string.match(part, "^[1-9][0-9]*$") and #part <= 10,
                    "invalid integer list element")
                local value = integer.create(tonumber(part))
                assert(value and value <= 2147483647, "integer list element overflow")
                result[#result + 1] = value
                start = index + 1
            end
        end
        return result
    end

    -- 投影指定字段，缺值必须在正式配置发布前报错。
    local function pick(value, fields)
        local result = {}
        for name, field in pairs(fields) do
            assert(value[field] ~= nil, "missing source field: " .. field)
            result[name] = value[field]
        end
        return result
    end

    -- 查找显式选中的记录，不隐式补入其他工作表或草稿。
    local function row(tables, name, key)
        assert(tables[name] and tables[name][key], "missing source reference: " .. name
            .. "[" .. tostring(key) .. "]")
        return tables[name][key]
    end

    -- 以原表主键顺序筛选父记录的子项。
    local function children(tables, name, field, key)
        local result = json.array({})
        for _, pair in ipairs(rows(tables[name])) do
            if pair[2][field] == key then
                result[#result + 1] = pair[2]
            end
        end
        return result
    end

    -- 合并独立字段投影，不覆盖原始源表。
    local function merge(target, source)
        for key, value in pairs(source) do
            assert(target[key] == nil, "duplicate projected field")
            target[key] = value
        end
        return target
    end

    -- 从十九张选中源表建立单位、默认弹药和实体引用明确的内容模型。
    function api.build(tables)
        local selected = {Map = 2, Bag = 1, Rules = 1, Loadout = 1}
        for name, key in pairs(selected) do
            assert(#rows(tables[name]) == 1, "unexpected production rows: " .. name)
            row(tables, name, key)
        end
        for _, name in ipairs({"MapSolid", "PlayerSpawn", "MonsterSpawn", "ExtractPoint",
            "Scene"}) do
            for _, pair in ipairs(rows(tables[name])) do
                assert(pair[2].MapIdx == 2, "selected records must belong to Map 2")
            end
        end
        for _, pair in ipairs(rows(tables.DropEntry)) do
            row(tables, "Drop", pair[2].DropIdx)
        end
        local source = row(tables, "Map", 2)
        assert(source.Mode == 2, "Map[2]: extraction mode required")
        local doc = {v = 4, tick_hz = 60, players = json.object({}),
            weapons = json.object({}), ammo = json.object({}), monsters = json.object({}),
            tools = json.object({}), items = json.object({})}
        local player_fields = {width = "Width", height = "Height", hp = "Hp", speed = "Speed",
            jump_speed = "JumpSpeed", run_speed = "Run", prone_speed = "ProneSpeed",
            prone_width = "ProneWidth", prone_height = "ProneHeight", stamina = "Stamina",
            stamina_delay = "StaminaDelay", stamina_rate = "StaminaRate", run_cost = "RunCost",
            jump_cost = "JumpCost", health_delay = "HealthDelay", health_rate = "HealthRate",
            weight = "EquipmentLimit"}
        for _, pair in ipairs(rows(tables.Player)) do
            local cfg = pick(pair[2], player_fields)
            cfg.hp_segments = numbers(pair[2].HPSetting)
            doc.players[tostring(pair[1])] = cfg
        end
        for _, pair in ipairs(rows(tables.Item)) do
            doc.items[tostring(pair[1])] = pick(pair[2], {name = "Name",
                max_stack = "MaxStack", kind = "Kind", type = "Type"})
        end
        for _, pair in ipairs(rows(tables.Ammunition)) do
            local value = pair[2]
            assert(row(tables, "Item", value.ItemIdx).Kind == 3
                and value.AdditionalStatus == 0, "only default ammunition is supported")
            local cfg = pick(value, {pellets = "Bullet", damage = "Bamage", range = "Distance",
                spread_deg = "Biffusion", penetration = "Penetration", loss = "Weaken"})
            cfg.item_cfg_id = tostring(value.ItemIdx)
            doc.ammo[tostring(pair[1])] = cfg
        end
        for _, pair in ipairs(rows(tables.Weapon)) do
            local value = pair[2]
            local ammo = doc.ammo[tostring(value.DefaultBullet)]
            assert(ammo and value.BulletType == value.DefaultBullet,
                "weapon requires its selected default ammunition")
            local cfg = pick(value, {magazine = "Magazine", reserve = "InitialReserve",
                fire_ticks = "Interval", reload_ticks = "ReloadTicks", reload_kind = "ReloadType",
                weight = "LoadBearing", melee_damage = "Melee", melee_range = "Scope"})
            cfg.ammo_cfg_id, cfg.range, cfg.damage = tostring(value.DefaultBullet), ammo.range,
                ammo.damage
            doc.weapons[tostring(pair[1])] = cfg
        end
        for _, pair in ipairs(rows(tables.Equip)) do
            assert(row(tables, "Item", pair[2].ItemIdx).Kind == 2,
                "Equip: gun must reference a gun item")
            row(tables, "Weapon", pair[2].WeaponIdx)
        end
        doc.rules = pick(row(tables, "Rules", 1), {weapon_slots = "WeaponSlots",
            tool_slots = "ToolSlots", consumable_slots = "ConsumableSlots",
            tool_move_percent = "ToolMovePercent", max_scenes = "MaxScenes",
            max_projectiles = "MaxProjectiles", melee_stamina = "MeleeStamina",
            melee_ticks = "MeleeTicks"})
        local kinds = {[1] = {[1] = "knife", [3] = "medkit"},
            [2] = {[5] = "needle", [6] = "bomb"}}
        for _, pair in ipairs(rows(tables.Tool)) do
            local value = pair[2]
            local kind = kinds[value.ToolType] and kinds[value.ToolType][value.Subdivision]
            assert(row(tables, "Item", pair[1]).Kind == 4 and kind, "unsupported core tool")
            assert(kind == "knife" or value.SlowPercent == 100 - doc.rules.tool_move_percent,
                "inconsistent use movement limit")
            doc.tools[tostring(pair[1])] = {kind = kind, uses = value.Quantity,
                use_ticks = value.Lag, heal = (kind == "medkit" or kind == "needle")
                    and value.Value or 0,
                damage = (kind == "knife" or kind == "bomb") and value.Value or 0,
                range = kind == "knife" and value.Scope or 0,
                radius = kind == "bomb" and value.Scope or 0,
                throw_range = value.ThrowingDistance, speed = value.Speed,
                stamina = value.PhysicalExhaustion,
                cooldown_ticks = kind == "knife" and doc.rules.melee_ticks or 0}
        end
        for _, pair in ipairs(rows(tables.Monster)) do
            local value = pair[2]
            local attack = row(tables, "Attack", value.AttackIdx)
            row(tables, "Drop", value.DropIdx)
            local cfg = pick(value, {width = "Width", height = "Height", hp = "Hp",
                speed = "Speed", detect_range = "DetectRange", rank = "Rank"})
            merge(cfg, pick(attack, {attack_range = "Range", damage = "Damage",
                attack_ticks = "CooldownTicks", windup = "WindupTicks", recover = "RecoveryTicks"}))
            cfg.drops = json.array({})
            for _, entry in ipairs(children(tables, "DropEntry", "DropIdx", value.DropIdx)) do
                assert(row(tables, "Item", entry.ItemIdx).Kind == 5,
                    "only extraction loot may drop")
                local drop = pick(entry, {chance = "ChanceBp", min_count = "MinCount",
                    max_count = "MaxCount"})
                drop.cfg_id = tostring(entry.ItemIdx)
                cfg.drops[#cfg.drops + 1] = drop
            end
            doc.monsters[tostring(pair[1])] = cfg
        end
        local starts = children(tables, "PlayerSpawn", "MapIdx", 2)
        assert(#starts == 1, "exactly one player spawn is required")
        local start = starts[1]
        local equip = row(tables, "Equip", start.EquipItemIdx)
        doc.map = pick(source, {width = "Width", height = "Height", gravity = "Gravity"})
        doc.map.spawn = {x = start.X, y = start.Y, cfg_id = tostring(start.PlayerIdx),
            weapon_cfg_id = tostring(equip.WeaponIdx)}
        doc.map.solids, doc.map.enemies = json.array({}), json.array({})
        for _, value in ipairs(children(tables, "MapSolid", "MapIdx", 2)) do
            local cfg = pick(value, {x = "X", y = "Y", w = "W", h = "H"})
            cfg.id = tostring(value.SolidIdx)
            doc.map.solids[#doc.map.solids + 1] = cfg
        end
        for _, value in ipairs(children(tables, "MonsterSpawn", "MapIdx", 2)) do
            local cfg = pick(value, {x = "X", y = "Y", patrol_min = "PatrolMin",
                patrol_max = "PatrolMax"})
            cfg.spawn_id, cfg.cfg_id = tostring(value.SpawnIdx), tostring(value.MonsterIdx)
            doc.map.enemies[#doc.map.enemies + 1] = cfg
        end
        doc.bag = pick(row(tables, "Bag", 1), {slots = "Slots", pickup_radius = "PickupRadius"})
        doc.extracts, doc.scenes = json.array({}), json.array({})
        for _, value in ipairs(children(tables, "ExtractPoint", "MapIdx", 2)) do
            local cfg = pick(value, {id = "ExtractIdx", x = "X", y = "Y", w = "W", h = "H",
                hold_ticks = "HoldTicks"})
            cfg.boss_spawn_id = tostring(value.NeedBossSpawnIdx)
            doc.extracts[#doc.extracts + 1] = cfg
        end
        local scene_kinds = {}
        for _, value in ipairs(children(tables, "Scene", "MapIdx", 2)) do
            assert(value.Penetrable == 0 or value.Penetrable == 1, "invalid Scene.Penetrable")
            local cfg = pick(value, {kind = "Kind", x = "X", y = "Y", w = "W", h = "H"})
            cfg.id, cfg.penetrable = tostring(value.SceneIdx), value.Penetrable == 1
            doc.scenes[#doc.scenes + 1] = cfg
            scene_kinds[cfg.kind] = true
        end
        assert(#doc.extracts == 1 and scene_kinds.ladder and scene_kinds.supply
            and scene_kinds.cover, "production requires one exit and all three scene kinds")
        local value = row(tables, "Loadout", 1)
        doc.default_loadout = {player_cfg_id = tostring(value.PlayerIdx)}
        for field, name in pairs({weapons = "Weapons", ammo = "Ammo", tools = "Tools",
            consumables = "Consumables"}) do
            local result = json.array({})
            for _, key in ipairs(numbers(value[name])) do
                result[#result + 1] = tostring(key)
            end
            doc.default_loadout[field] = result
        end
        local player = doc.players[doc.default_loadout.player_cfg_id]
        assert(player, "default player not selected")
        doc.default_loadout.hp_segments = player.hp_segments
        return doc
    end

    return api
end

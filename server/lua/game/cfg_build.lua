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
        for _, pair in ipairs(rows(tables[name] or {})) do
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

    -- 免费价格显式写零；付费价格必须包含货币 ID 和正整数数量。
    local function price(text)
        assert(type(text) == "string", "recruit price is required")
        if text == "0" then
            return {currency_id = "0", amount = 0}
        end
        local id, amount = string.match(text, "^([1-9][0-9]*):([1-9][0-9]*)$")
        assert(id and amount and #id <= 10 and #amount <= 10, "invalid recruit price")
        local count = integer.create(tonumber(amount))
        assert(count and count <= 2147483647 and tonumber(id) <= 2147483647,
            "recruit price overflow")
        return {currency_id = id, amount = count}
    end

    -- 新机制仅从显式选中表投影，缺少关联和参数直接阻止发布。
    local function additions(tables, doc)
        doc.skills, doc.abilities = json.object({}), json.object({})
        doc.encounter, doc.legacy_ai, doc.career, doc.exploration = false, false, false, false
        if tables.Skill ~= nil then
            assert(tables.SkillEffect ~= nil, "selected skills require SkillEffect")
            for _, pair in ipairs(rows(tables.Skill)) do
                local value = pair[2]
                local cfg = pick(value, {name = "Name", target = "Target", category = "Category",
                    kind = "Lifetime", cost = "Cost"})
                cfg.effects = json.array({})
                for _, effect in ipairs(children(tables, "SkillEffect", "SkillIdx", pair[1])) do
                    cfg.effects[#cfg.effects + 1] = pick(effect,
                        {stat = "Stat", op = "Op", value = "Value"})
                end
                doc.skills[tostring(pair[1])] = cfg
            end
        end
        for _, pair in ipairs(rows(tables.SkillEffect or {})) do
            row(tables, "Skill", pair[2].SkillIdx)
        end
        for _, pair in ipairs(rows(tables.Ability or {})) do
            local value = pair[2]
            local cfg = pick(value, {kind = "Kind", range = "Range", damage = "Damage",
                windup = "WindupTicks", recover = "RecoveryTicks", cooldown = "CooldownTicks",
                despawn_ticks = "DespawnTicks"})
            cfg.summon_cfg_id = tostring(value.SummonMonsterIdx)
            doc.abilities[tostring(pair[1])] = cfg
        end
        for _, pair in ipairs(rows(tables.MonsterAi or {})) do
            local value = pair[2]
            local monster = assert(doc.monsters[tostring(value.MonsterIdx)],
                "AI references unselected monster")
            assert(monster.ai == false, "duplicate monster AI")
            local cfg = pick(value, {alert_speed = "AlertSpeed", disengage_ticks = "DisengageTicks",
                damage_reduction_bp = "DamageReductionBp", rage_ticks = "RageTicks"})
            assert(value.Basic == 0 or value.Basic == 1, "invalid AI Basic")
            cfg.basic, cfg.abilities = value.Basic == 1, json.array({})
            cfg.rage_thresholds = value.RageThresholds == "" and json.array({})
                or numbers(value.RageThresholds)
            cfg.rage_ability_id = tostring(value.RageAbilityIdx)
            for _, bind in ipairs(children(tables, "MonsterAbility", "MonsterIdx",
                value.MonsterIdx)) do
                cfg.abilities[#cfg.abilities + 1] = {cfg_id = tostring(bind.AbilityIdx),
                    phase = bind.Phase, priority = bind.Priority}
            end
            monster.ai = cfg
        end
        for _, pair in ipairs(rows(tables.MonsterAbility or {})) do
            local monster = assert(doc.monsters[tostring(pair[2].MonsterIdx)],
                "ability binding references unselected monster")
            assert(monster.ai ~= false, "ability binding requires explicit monster AI")
        end
        if tables.Encounter ~= nil then
            assert(#rows(tables.Encounter) == 1, "exactly one Encounter is required")
            local value = row(tables, "Encounter", 1)
            assert(value.MapIdx == 2, "Encounter must belong to selected map")
            local cfg = {bounty_cfg_id = tostring(value.BountyItemIdx),
                alert_scale_bp = value.AlertScaleBp, second_wave = json.array({})}
            for _, id in ipairs(numbers(value.SecondWave)) do
                cfg.second_wave[#cfg.second_wave + 1] = tostring(id)
            end
            doc.encounter = cfg
        end
        local scenes = {}
        for _, scene in ipairs(doc.scenes) do
            scenes[scene.id] = scene
        end
        for _, pair in ipairs(rows(tables.SceneAction or {})) do
            local value = pair[2]
            local scene = assert(scenes[tostring(value.SceneIdx)], "unknown action scene")
            assert(scene.interaction == false, "duplicate scene action")
            scene.interaction = pick(value, {mode = "Mode", hold_ticks = "HoldTicks"})
        end
        for _, pair in ipairs(rows(tables.Barrel or {})) do
            local value = pair[2]
            local scene = assert(scenes[tostring(value.SceneIdx)], "unknown barrel scene")
            assert(scene.barrel == false, "duplicate barrel scene")
            scene.barrel = pick(value, {hp = "Hp", fuse_ticks = "FuseTicks",
                radius = "Radius", damage = "Damage"})
        end
        if tables.Region ~= nil then
            doc.exploration = {regions = json.array({})}
            for _, pair in ipairs(rows(tables.Region)) do
                local value = pair[2]
                local cfg = pick(value, {x = "X", y = "Y", w = "W", h = "H"})
                cfg.id = tostring(pair[1])
                cfg.boss_spawns, cfg.clues = json.array({}), json.array({})
                for _, id in ipairs(numbers(value.BossSpawns)) do
                    cfg.boss_spawns[#cfg.boss_spawns + 1] = tostring(id)
                end
                for _, id in ipairs(numbers(value.Clues)) do
                    cfg.clues[#cfg.clues + 1] = tostring(id)
                end
                doc.exploration.regions[#doc.exploration.regions + 1] = cfg
            end
        end
        if tables.Career ~= nil then
            assert(#rows(tables.Career) == 1, "exactly one Career row is required")
            local value = row(tables, "Career", 1)
            local cfg = pick(value, {extract_xp = "ExtractXp", bounty_xp = "BountyXp",
                bounty_currency = "BountyCurrency", retire_xp = "RetireXp"})
            cfg.levels, cfg.kill_xp = json.array({}), json.object({})
            assert(tables.CareerLevel ~= nil and tables.KillXp ~= nil,
                "career requires explicit levels and kill rewards")
            for index, pair in ipairs(rows(tables.CareerLevel)) do
                assert(pair[1] == index, "career level IDs must be consecutive from one")
                cfg.levels[#cfg.levels + 1] = pair[2].Xp
            end
            for _, pair in ipairs(rows(tables.KillXp)) do
                local key = tostring(pair[2].MonsterIdx)
                assert(cfg.kill_xp[key] == nil, "duplicate kill XP")
                cfg.kill_xp[key] = pair[2].Xp
            end
            doc.career = cfg
        else
            assert(tables.CareerLevel == nil and tables.KillXp == nil,
                "career data requires selected Career rules")
        end
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
        local doc = {v = 5, tick_hz = 60, players = json.object({}),
            weapons = json.object({}), ammo = json.object({}), monsters = json.object({}),
            tools = json.object({}), items = json.object({})}
        local player_fields = {width = "Width", height = "Height", hp = "Hp", speed = "Speed",
            jump_speed = "JumpSpeed", run_speed = "Run", prone_speed = "ProneSpeed",
            prone_width = "ProneWidth", prone_height = "ProneHeight", stamina = "Stamina",
            stamina_delay = "StaminaDelay", stamina_rate = "StaminaRate", run_cost = "RunCost",
            jump_cost = "JumpCost", health_delay = "HealthDelay", health_rate = "HealthRate",
            weight = "EquipmentLimit", skill_points = "SkillPoints"}
        for _, pair in ipairs(rows(tables.Player)) do
            local cfg = pick(pair[2], player_fields)
            cfg.hp_segments = numbers(pair[2].HPSetting)
            cfg.recruit_cost = price(pair[2].Cost)
            doc.players[tostring(pair[1])] = cfg
        end
        for _, pair in ipairs(rows(tables.Item)) do
            doc.items[tostring(pair[1])] = pick(pair[2], {name = "Name",
                max_stack = "MaxStack", kind = "Kind", type = "Type"})
            doc.items[tostring(pair[1])].skill_cfg_id = tostring(pair[2].SkillIdx or 0)
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
            local cfg = doc.weapons[tostring(pair[2].WeaponIdx)]
            assert(cfg.item_cfg_id == nil, "weapon requires exactly one equipment item")
            cfg.item_cfg_id = tostring(pair[2].ItemIdx)
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
            cfg.ai = false
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
            cfg.interaction, cfg.barrel = false, false
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
        doc.default_loadout.skills = json.array({})
        additions(tables, doc)
        return doc
    end

    return api
end

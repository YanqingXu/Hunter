-- 将导出的原字段配置表转换为玩法共享模型，只在脚本初始化与构建校验时运行。
return function(deps)
    local skill = deps["game.skill"]
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

    -- 空串与零显式表示无引用，其他列表继续使用严格正整数格式。
    local function references(text)
        if text == "" or text == "0" then
            return json.array({})
        end
        local result = json.array({})
        for _, value in ipairs(numbers(text)) do
            result[#result + 1] = tostring(value)
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

    -- 查找根表中的明确外键，不以固定主键或缺值回退其他配置。
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

    -- 新机制按服务器入口及所属地图投影，缺少关联和参数直接阻止发布。
    local function additions(tables, doc, selected)
        doc.skills, doc.abilities = json.object({}), json.object({})
        doc.encounter, doc.legacy_ai, doc.career, doc.exploration = false, false, false, false
        if tables.Skill ~= nil then
            doc.skills = skill.sources(tables)
        else
            assert(tables.Attribute == nil and tables.SkillEffect == nil,
                "attribute and effect tables require Skill")
        end
        local player_slots = {}
        for _, pair in ipairs(rows(tables.PlayerSkill or {})) do
            local value = pair[2]
            row(tables, "Player", value.PlayerIdx)
            row(tables, "Skill", value.SkillIdx)
            assert(type(value.Slot) == "integer" and value.Slot >= 1 and value.Slot <= 32,
                "invalid player skill slot")
            local key = tostring(value.PlayerIdx) .. ":" .. tostring(value.Slot)
            assert(not player_slots[key], "duplicate player skill slot")
            player_slots[key] = true
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
        if selected.EncounterIdx ~= 0 then
            local value = row(tables, "Encounter", selected.EncounterIdx)
            assert(value.MapIdx == selected.MapIdx, "Encounter must belong to selected map")
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
            local source = row(tables, "Scene", value.SceneIdx)
            if source.MapIdx == selected.MapIdx then
                local scene = assert(scenes[tostring(value.SceneIdx)], "unknown action scene")
                assert(scene.interaction == false, "duplicate scene action")
                scene.interaction = pick(value, {mode = "Mode", hold_ticks = "HoldTicks"})
            end
        end
        for _, pair in ipairs(rows(tables.Barrel or {})) do
            local value = pair[2]
            local source = row(tables, "Scene", value.SceneIdx)
            if source.MapIdx == selected.MapIdx then
                local scene = assert(scenes[tostring(value.SceneIdx)], "unknown barrel scene")
                assert(scene.barrel == false, "duplicate barrel scene")
                scene.barrel = pick(value, {hp = "Hp", fuse_ticks = "FuseTicks",
                    radius = "Radius", damage = "Damage"})
            end
        end
        local regions = children(tables, "Region", "MapIdx", selected.MapIdx)
        if #regions > 0 then
            doc.exploration = {regions = json.array({})}
            for _, value in ipairs(regions) do
                local cfg = pick(value, {x = "X", y = "Y", w = "W", h = "H"})
                cfg.id = tostring(value.RegionIdx)
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
        if selected.CareerIdx ~= 0 then
            local value = row(tables, "Career", selected.CareerIdx)
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
        end
    end

    -- 验证全部随机池引用和数量，并投影当前地图的随机物品点。
    local function item_spawns(tables, doc, selected)
        local pools, totals = {}, {}
        for _, pair in ipairs(rows(tables.SpawnPoolEntry or {})) do
            local value = pair[2]
            assert(type(value.PoolIdx) == "integer" and value.PoolIdx > 0,
                "invalid spawn pool ID")
            assert(type(value.Weight) == "integer" and value.Weight > 0
                and value.Weight <= 2147483647, "invalid spawn pool weight")
            local item = assert(doc.items[tostring(value.ItemIdx)], "unknown spawn pool item")
            assert(type(value.Count) == "integer" and value.Count >= 1
                and value.Count <= item.max_stack, "invalid spawn pool count")
            local tool = doc.tools[tostring(value.ItemIdx)]
            if item.skill_cfg_id ~= "0" then
                assert(doc.skills[item.skill_cfg_id] and value.Count == 1,
                    "spawn skill must be enabled and singular")
            elseif tool ~= nil then
                assert((tool.kind == "needle" or tool.kind == "bomb")
                    and value.Count <= tool.uses, "spawn pool requires a consumable")
            else
                assert(item.kind == 5, "unsupported spawn pool item")
            end
            local pool = pools[value.PoolIdx] or json.array({})
            pool[#pool + 1] = {cfg_id = tostring(value.ItemIdx), count = value.Count,
                weight = value.Weight}
            pools[value.PoolIdx] = pool
            totals[value.PoolIdx] = (totals[value.PoolIdx] or 0) + value.Weight
            assert(totals[value.PoolIdx] <= 2147483647, "spawn pool weight sum exceeds limit")
        end
        doc.item_spawns = json.array({})
        for _, pair in ipairs(rows(tables.ItemSpawn or {})) do
            local value = pair[2]
            local map = row(tables, "Map", value.MapIdx)
            assert(type(value.X) == "integer" and value.X >= 0 and value.X <= map.Width
                and type(value.Y) == "integer" and value.Y >= 0 and value.Y <= map.Height,
                "item spawn outside map")
            assert(type(value.ChanceBp) == "integer" and value.ChanceBp >= 0
                and value.ChanceBp <= 10000, "invalid item spawn chance")
            local choices = assert(pools[value.PoolIdx], "unknown or empty item spawn pool")
            if value.MapIdx == selected.MapIdx then
                doc.item_spawns[#doc.item_spawns + 1] = {id = tostring(pair[1]),
                    x = value.X, y = value.Y, chance = value.ChanceBp, choices = choices}
            end
        end
    end

    -- 从全部根配置表加载内容，并由唯一服务器入口选择地图与规则外键。
    function api.build(tables)
        assert(tables.Server and #rows(tables.Server) == 1,
            "exactly one Server row is required")
        local selected = row(tables, "Server", 1)
        for _, name in ipairs({"Map", "Bag", "Rules", "Loadout", "Career", "Encounter"}) do
            local key = selected[name .. "Idx"]
            assert(type(key) == "integer" and key >= 0 and key <= 2147483647,
                "invalid Server." .. name .. "Idx")
            assert(key ~= 0 or name == "Career" or name == "Encounter",
                "required Server reference: " .. name)
            if key ~= 0 then
                row(tables, name, key)
            end
        end
        for _, name in ipairs({"MapSolid", "PlayerSpawn", "MonsterSpawn", "ExtractPoint",
            "Scene", "Region"}) do
            for _, pair in ipairs(rows(tables[name] or {})) do
                row(tables, "Map", pair[2].MapIdx)
            end
        end
        for _, pair in ipairs(rows(tables.DropEntry)) do
            row(tables, "Drop", pair[2].DropIdx)
        end
        local source = row(tables, "Map", selected.MapIdx)
        assert(source.Mode == 2, "selected map requires extraction mode")
        local doc = {v = 6, tick_hz = 60, players = json.object({}),
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
                spread_deg = "Biffusion", penetration = "Penetration", loss = "Weaken",
                refill_count = "RefillCount", prop_damage = "PropDamage"})
            cfg.prop_ids = json.array({})
            local seen = {}
            for _, id in ipairs(references(value.Prop)) do
                assert(not seen[id], "duplicate ammunition scene reference")
                seen[id] = true
                local scene = row(tables, "Scene", tonumber(id))
                assert(scene.Kind == "cover", "ammunition scene reference must be cover")
                if scene.MapIdx == selected.MapIdx then
                    cfg.prop_ids[#cfg.prop_ids + 1] = id
                end
            end
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
        doc.rules = pick(row(tables, "Rules", selected.RulesIdx), {weapon_slots = "WeaponSlots",
            tool_slots = "ToolSlots", consumable_slots = "ConsumableSlots",
            tool_move_percent = "ToolMovePercent", max_scenes = "MaxScenes",
            max_projectiles = "MaxProjectiles", melee_stamina = "MeleeStamina",
            melee_ticks = "MeleeTicks"})
        local kinds = {[1] = {[1] = "knife", [3] = "medkit"},
            [2] = {[5] = "needle", [6] = "bomb"}}
        for _, pair in ipairs(rows(tables.Tool)) do
            local value = pair[2]
            assert(value.Enabled == 0 or value.Enabled == 1, "invalid Tool.Enabled")
            row(tables, "Item", pair[1])
            if value.Enabled == 1 then
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
                    refill_count = assert(value.RefillCount, "missing source field: RefillCount"),
                    supply_weight = assert(value.SupplyWeight, "missing source field: SupplyWeight"),
                    cooldown_ticks = kind == "knife" and doc.rules.melee_ticks or 0}
            end
        end
        for _, pair in ipairs(rows(tables.Monster)) do
            local value = pair[2]
            assert(value.Enabled == 0 or value.Enabled == 1, "invalid Monster.Enabled")
            if value.Enabled == 1 then
                local attack = row(tables, "Attack", value.AttackIdx)
                assert(type(value.DropIdx) == "integer" and value.DropIdx >= 0,
                    "invalid monster drop ID")
                if value.DropIdx ~= 0 then
                    row(tables, "Drop", value.DropIdx)
                end
                local cfg = pick(value, {width = "Width", height = "Height", hp = "Hp",
                    speed = "Speed", detect_range = "DetectRange", rank = "Rank"})
                merge(cfg, pick(attack, {attack_range = "Range", damage = "Damage",
                    attack_ticks = "CooldownTicks", windup = "WindupTicks",
                    recover = "RecoveryTicks"}))
                cfg.drops = json.array({})
                cfg.ai = false
                for _, entry in ipairs(children(tables, "DropEntry", "DropIdx", value.DropIdx)) do
                    row(tables, "Item", entry.ItemIdx)
                    local drop = pick(entry, {chance = "ChanceBp", min_count = "MinCount",
                        max_count = "MaxCount"})
                    drop.cfg_id = tostring(entry.ItemIdx)
                    cfg.drops[#cfg.drops + 1] = drop
                end
                doc.monsters[tostring(pair[1])] = cfg
            end
        end
        local starts = children(tables, "PlayerSpawn", "MapIdx", selected.MapIdx)
        assert(#starts == 1, "exactly one player spawn is required")
        local start = starts[1]
        local equip = row(tables, "Equip", start.EquipItemIdx)
        doc.map = pick(source, {width = "Width", height = "Height", gravity = "Gravity"})
        doc.map.spawn = {x = start.X, y = start.Y, cfg_id = tostring(start.PlayerIdx),
            weapon_cfg_id = tostring(equip.WeaponIdx)}
        doc.map.solids, doc.map.enemies = json.array({}), json.array({})
        for _, value in ipairs(children(tables, "MapSolid", "MapIdx", selected.MapIdx)) do
            local cfg = pick(value, {x = "X", y = "Y", w = "W", h = "H"})
            cfg.id = tostring(value.SolidIdx)
            doc.map.solids[#doc.map.solids + 1] = cfg
        end
        for _, value in ipairs(children(tables, "MonsterSpawn", "MapIdx", selected.MapIdx)) do
            local cfg = pick(value, {x = "X", y = "Y", patrol_min = "PatrolMin",
                patrol_max = "PatrolMax"})
            cfg.spawn_id, cfg.cfg_id = tostring(value.SpawnIdx), tostring(value.MonsterIdx)
            doc.map.enemies[#doc.map.enemies + 1] = cfg
        end
        doc.bag = pick(row(tables, "Bag", selected.BagIdx),
            {slots = "Slots", pickup_radius = "PickupRadius"})
        doc.extracts, doc.scenes = json.array({}), json.array({})
        for _, value in ipairs(children(tables, "ExtractPoint", "MapIdx", selected.MapIdx)) do
            local cfg = pick(value, {id = "ExtractIdx", x = "X", y = "Y", w = "W", h = "H",
                hold_ticks = "HoldTicks"})
            cfg.boss_spawn_id = tostring(value.NeedBossSpawnIdx)
            doc.extracts[#doc.extracts + 1] = cfg
        end
        for _, value in ipairs(children(tables, "Scene", "MapIdx", selected.MapIdx)) do
            assert(value.Penetrable == 0 or value.Penetrable == 1, "invalid Scene.Penetrable")
            local cfg = pick(value, {kind = "Kind", x = "X", y = "Y", w = "W", h = "H"})
            cfg.id, cfg.penetrable = tostring(value.SceneIdx), value.Penetrable == 1
            cfg.interaction, cfg.barrel = false, false
            doc.scenes[#doc.scenes + 1] = cfg
        end
        local value = row(tables, "Loadout", selected.LoadoutIdx)
        doc.default_loadout = {player_cfg_id = tostring(value.PlayerIdx)}
        for field, name in pairs({weapons = "Weapons", ammo = "Ammo", tools = "Tools",
            consumables = "Consumables"}) do
            doc.default_loadout[field] = references(value[name])
        end
        local player = doc.players[doc.default_loadout.player_cfg_id]
        assert(player, "default player not selected")
        doc.default_loadout.hp_segments = player.hp_segments
        assert(value.Skills ~= nil, "missing source field: Skills")
        doc.default_loadout.skills = references(value.Skills)
        additions(tables, doc, selected)
        item_spawns(tables, doc, selected)
        return doc
    end

    return api
end

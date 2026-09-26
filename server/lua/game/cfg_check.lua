-- 校验规范内容的字段、单位、引用、容量及玩法语义，不依赖原生世界状态。
return function(deps)
    local state = deps["framework.state"]
    local geometry = deps["game.cfg_geometry"]
    local api = {}

    -- 把静态空格分隔字段名转换为可审阅的校验列表。
    local function words(text)
        local result = {}
        for word in string.gmatch(text, "[^ ]+") do
            result[#result + 1] = word
        end
        return result
    end

    -- 对象必须恰好包含支持字段，拒绝漏值及错误字段别名。
    local function fields(value, names, name)
        assert(state.fields(value, words(names)), "invalid fields: " .. name)
    end

    -- 检查配置使用精确整数且处于规定单位范围。
    local function integer_value(value, low, high, name)
        assert(state.integer(value, low, high), "invalid integer: " .. name)
        return value
    end

    -- 配置 ID 只使用规范的正十进制字符串。
    local function identity(value, seen, name)
        assert(state.is_id(value, "2147483647") and value ~= "0" and not seen[value],
            "invalid or duplicate ID: " .. name)
        seen[value] = true
    end

    -- 校验独立配置表，空表仅用于明确允许的工具夹具。
    local function cfg_table(value, name, empty)
        assert(type(value) == "table", "invalid table: " .. name)
        local seen, count = {}, 0
        for key, cfg in pairs(value) do
            identity(key, seen, name)
            assert(type(cfg) == "table", "invalid record: " .. name)
            count = count + 1
        end
        assert(empty or count > 0, "empty table: " .. name)
    end

    -- 检查引用存在，禁止数值 ID、空字符串和隐式默认值。
    local function ref(values, key, name)
        identity(key, {}, name)
        assert(values[key], "unknown configuration: " .. name)
        return values[key]
    end

    -- 数组必须连续、没有混合字段且不超过配置容量。
    local function array(values, low, high, name)
        assert(state.array(values, low, high), "invalid array: " .. name)
    end

    -- 角色站立尺寸必须为偶数毫米，以保持脚底中心的整数表达。
    local function actor(cfg, extra, name)
        fields(cfg, "width height hp speed " .. extra, name)
        for _, key in ipairs({"width", "height"}) do
            integer_value(cfg[key], 2, 10000, name .. "." .. key)
            assert(cfg[key] % 2 == 0, "actor dimensions must be even millimeters")
        end
        integer_value(cfg.hp, 1, 1000000, name .. ".hp")
        integer_value(cfg.speed, 1, 1000, name .. ".speed")
    end

    -- 校验角色恢复、有效体型及有序血段组合。
    local function players(doc)
        for _, cfg in pairs(doc.players) do
            actor(cfg, "jump_speed run_speed prone_speed prone_width prone_height stamina "
                .. "stamina_delay stamina_rate run_cost jump_cost health_delay health_rate "
                .. "weight hp_segments", "player")
            integer_value(cfg.jump_speed, 1, 1000, "player.jump_speed")
            for _, name in ipairs({"run_speed", "prone_speed"}) do
                integer_value(cfg[name], 1, 1000, "player." .. name)
            end
            for _, name in ipairs({"stamina", "stamina_rate", "health_rate", "weight"}) do
                integer_value(cfg[name], 1, 10000, "player." .. name)
            end
            for _, name in ipairs({"stamina_delay", "health_delay"}) do
                integer_value(cfg[name], 0, 36000, "player." .. name)
            end
            for _, name in ipairs({"prone_width", "prone_height"}) do
                integer_value(cfg[name], 2, 10000, "player." .. name)
                assert(cfg[name] % 2 == 0, "prone dimensions must be even")
            end
            integer_value(cfg.run_cost, 0, 0, "player.run_cost")
            integer_value(cfg.jump_cost, 0, 0, "player.jump_cost")
            array(cfg.hp_segments, 1, 40000, "hp_segments")
            local total = 0
            for _, value in ipairs(cfg.hp_segments) do
                assert(type(value) == "integer" and (value == 25 or value == 50),
                    "blood segments must be 25 or 50")
                total = total + value
            end
            assert(total == cfg.hp, "blood segments must sum to maximum health")
        end
    end

    -- 校验枪弹投影一致、射程单位、装填方式和常规工具参数。
    local function equipment(doc)
        for _, cfg in pairs(doc.items) do
            fields(cfg, "name max_stack kind type", "item")
            assert(type(cfg.name) == "string" and #cfg.name > 0, "item.name is required")
            integer_value(cfg.max_stack, 1, 2147483647, "item.max_stack")
            integer_value(cfg.kind, 1, 5, "item.kind")
            integer_value(cfg.type, 0, 2147483647, "item.type")
        end
        for _, cfg in pairs(doc.ammo) do
            fields(cfg, "item_cfg_id pellets damage range spread_deg penetration loss", "ammo")
            assert(ref(doc.items, cfg.item_cfg_id, "ammo.item").kind == 3,
                "ammunition requires an ammunition item")
            for _, range in ipairs({{"pellets", 1, 16}, {"damage", 1, 1000000},
                {"range", 1, 100000}, {"spread_deg", 0, 90}, {"penetration", 0, 32},
                {"loss", 0, 1000000}}) do
                integer_value(cfg[range[1]], range[2], range[3], "ammo." .. range[1])
            end
        end
        for _, cfg in pairs(doc.weapons) do
            fields(cfg, "range damage magazine reserve fire_ticks reload_ticks ammo_cfg_id "
                .. "reload_kind weight melee_damage melee_range", "weapon")
            local ammo = ref(doc.ammo, cfg.ammo_cfg_id, "weapon.ammo")
            assert(cfg.range == ammo.range and cfg.damage == ammo.damage,
                "weapon projection differs from default ammunition")
            integer_value(cfg.magazine, 1, 1000, "weapon.magazine")
            integer_value(cfg.reserve, 0, 100000, "weapon.reserve")
            integer_value(cfg.reload_kind, 1, 2, "weapon.reload_kind")
            for _, name in ipairs({"fire_ticks", "reload_ticks"}) do
                integer_value(cfg[name], 1, 3600, "weapon." .. name)
            end
            for _, name in ipairs({"weight", "melee_damage", "melee_range"}) do
                integer_value(cfg[name], 1, 1000000, "weapon." .. name)
            end
        end
        for key, cfg in pairs(doc.tools) do
            fields(cfg, "kind uses use_ticks heal damage range radius throw_range speed stamina "
                .. "cooldown_ticks", "tool")
            assert(ref(doc.items, key, "tool.item").kind == 4, "tool requires a tool item")
            assert(cfg.kind == "knife" or cfg.kind == "medkit" or cfg.kind == "needle"
                or cfg.kind == "bomb", "unsupported tool kind")
            integer_value(cfg.uses, 1, 100, "tool.uses")
            for _, name in ipairs({"use_ticks", "heal", "damage", "range", "radius",
                "throw_range", "speed", "stamina", "cooldown_ticks"}) do
                integer_value(cfg[name], 0, 1000000, "tool." .. name)
            end
            assert(cfg.kind ~= "bomb" or (cfg.radius > 0 and cfg.throw_range > 0 and cfg.speed > 0),
                "bomb requires positive radius, throw range and speed")
        end
    end

    -- 默认配装必须能够完整接受，所有免费槽位和负重均采用角色配置。
    local function loadout(doc)
        local load, rules = doc.default_loadout, doc.rules
        fields(load, "player_cfg_id hp_segments weapons ammo tools consumables", "loadout")
        local player = ref(doc.players, load.player_cfg_id, "loadout.player")
        array(load.hp_segments, 1, 40000, "loadout.hp_segments")
        assert(#load.hp_segments == #player.hp_segments, "default blood segment mismatch")
        for index, value in ipairs(load.hp_segments) do
            assert(value == player.hp_segments[index], "default blood segment mismatch")
        end
        array(load.weapons, 1, rules.weapon_slots, "loadout.weapons")
        array(load.ammo, #load.weapons, #load.weapons, "loadout.ammo")
        local weight = 0
        for index, key in ipairs(load.weapons) do
            local cfg = ref(doc.weapons, key, "loadout.weapon")
            assert(load.ammo[index] == cfg.ammo_cfg_id, "loadout ammunition mismatch")
            weight = weight + cfg.weight
        end
        assert(weight <= player.weight, "weapon weight exceeds player capacity")
        for _, group in ipairs({{"tools", "tool_slots", "knife", "medkit"},
            {"consumables", "consumable_slots", "needle", "bomb"}}) do
            local values, seen = load[group[1]], {}
            array(values, 0, rules[group[2]], "loadout." .. group[1])
            for _, key in ipairs(values) do
                identity(key, seen, "loadout.tool")
                local cfg = ref(doc.tools, key, "loadout.tool")
                assert(cfg.kind == group[3] or cfg.kind == group[4], "wrong tool category")
            end
        end
        assert(doc.map.spawn.cfg_id == load.player_cfg_id
            and doc.map.spawn.weapon_cfg_id == load.weapons[1], "map default loadout mismatch")
    end

    -- 校验怪物招式和掉落组，最坏情况掉落实例总数必须符合原生容量。
    local function monsters(doc)
        local totals = {}
        for key, cfg in pairs(doc.monsters) do
            actor(cfg, "detect_range attack_range damage attack_ticks rank windup recover drops",
                "monster")
            integer_value(cfg.detect_range, 1, 100000, "monster.detect_range")
            integer_value(cfg.attack_range, 1, cfg.detect_range, "monster.attack_range")
            integer_value(cfg.damage, 1, 1000000, "monster.damage")
            integer_value(cfg.attack_ticks, 1, 3600, "monster.attack_ticks")
            integer_value(cfg.rank, 1, 3, "monster.rank")
            integer_value(cfg.windup, 0, 3600, "monster.windup")
            integer_value(cfg.recover, 0, 3600, "monster.recover")
            assert(cfg.windup + cfg.recover < cfg.attack_ticks,
                "monster recovery must finish before cooldown")
            array(cfg.drops, 0, 64, "monster.drops")
            local total, seen = 0, {}
            for _, drop in ipairs(cfg.drops) do
                fields(drop, "cfg_id chance min_count max_count", "drop")
                identity(drop.cfg_id, seen, "drop.item")
                local item = ref(doc.items, drop.cfg_id, "drop.item")
                assert(item.kind == 5, "only extraction loot may drop")
                integer_value(drop.chance, 0, 10000, "drop.chance")
                integer_value(drop.min_count, 1, 2147483647, "drop.min_count")
                integer_value(drop.max_count, drop.min_count, 2147483647, "drop.max_count")
                if drop.chance > 0 then
                    total = total + (drop.max_count + item.max_stack - 1) // item.max_stack
                end
            end
            totals[key] = total
        end
        local maximum = 0
        for _, spawn in ipairs(doc.map.enemies) do
            ref(doc.monsters, spawn.cfg_id, "spawn.monster")
            maximum = maximum + totals[spawn.cfg_id]
        end
        assert(maximum <= 64, "worst-case drop capacity exceeds 64")
    end

    -- 校验地图记录与场景身份，几何检查在类型检查全部通过后执行。
    local function map_content(doc)
        local map = doc.map
        fields(map, "width height gravity solids spawn enemies", "map")
        integer_value(map.width, 1000, 100000, "map.width")
        integer_value(map.height, 1000, 100000, "map.height")
        integer_value(map.gravity, 1, 1000, "map.gravity")
        fields(map.spawn, "x y cfg_id weapon_cfg_id", "map.spawn")
        ref(doc.players, map.spawn.cfg_id, "map.spawn.player")
        ref(doc.weapons, map.spawn.weapon_cfg_id, "map.spawn.weapon")
        array(map.solids, 0, 128, "map.solids")
        array(map.enemies, 1, 32, "map.enemies")
        local seen, spawns = {}, {}
        for _, solid in ipairs(map.solids) do
            fields(solid, "id x y w h", "solid")
            identity(solid.id, seen, "solid.id")
        end
        for _, enemy in ipairs(map.enemies) do
            fields(enemy, "spawn_id cfg_id x y patrol_min patrol_max", "enemy spawn")
            identity(enemy.spawn_id, spawns, "enemy.spawn_id")
            ref(doc.monsters, enemy.cfg_id, "enemy.cfg_id")
            spawns[enemy.spawn_id] = enemy
        end
        array(doc.scenes, 0, doc.rules.max_scenes, "scenes")
        seen = {}
        for _, scene in ipairs(doc.scenes) do
            fields(scene, "id kind x y w h penetrable", "scene")
            identity(scene.id, seen, "scene.id")
            assert(scene.kind == "ladder" or scene.kind == "supply" or scene.kind == "cover",
                "invalid scene kind")
            assert(type(scene.penetrable) == "boolean"
                and (scene.kind == "cover" or not scene.penetrable), "only cover is penetrable")
        end
        array(doc.extracts, 0, 32, "extracts")
        seen = {}
        for _, point in ipairs(doc.extracts) do
            fields(point, "id x y w h hold_ticks boss_spawn_id", "extract")
            integer_value(point.id, 1, 2147483647, "extract.id")
            assert(not seen[point.id], "duplicate extract ID")
            seen[point.id] = true
            local boss = spawns[point.boss_spawn_id]
            assert(boss and doc.monsters[boss.cfg_id].rank == 3,
                "extract requires one Boss spawn from this map")
            integer_value(point.hold_ticks, 1, 36000, "extract.hold_ticks")
        end
    end

    -- 正式源表和显式测试夹具共用完整的内容语义入口。
    function api.validate(doc)
        fields(doc, "v tick_hz map players monsters weapons items bag extracts ammo tools rules "
            .. "scenes default_loadout", "content")
        integer_value(doc.v, 4, 4, "content.v")
        integer_value(doc.tick_hz, 60, 60, "content.tick_hz")
        for _, name in ipairs({"players", "monsters", "weapons", "items", "ammo", "tools"}) do
            cfg_table(doc[name], name, name == "tools")
        end
        local rules = doc.rules
        fields(rules, "weapon_slots tool_slots consumable_slots tool_move_percent max_scenes "
            .. "max_projectiles melee_stamina melee_ticks", "rules")
        for _, entry in ipairs({{"weapon_slots", 2}, {"tool_slots", 4}, {"consumable_slots", 4},
            {"max_scenes", 32}, {"max_projectiles", 16}}) do
            integer_value(rules[entry[1]], 1, entry[2], "rules." .. entry[1])
        end
        integer_value(rules.tool_move_percent, 1, 100, "rules.tool_move_percent")
        integer_value(rules.melee_stamina, 1, 10000, "rules.melee_stamina")
        integer_value(rules.melee_ticks, 1, 3600, "rules.melee_ticks")
        fields(doc.bag, "slots pickup_radius", "bag")
        integer_value(doc.bag.slots, 1, 32, "bag.slots")
        integer_value(doc.bag.pickup_radius, 1, 10000, "bag.pickup_radius")
        players(doc)
        equipment(doc)
        map_content(doc)
        loadout(doc)
        monsters(doc)
        geometry.validate(doc)
        return doc
    end

    return api
end

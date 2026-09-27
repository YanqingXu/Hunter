-- 校验规范内容的字段、单位、引用、容量及玩法语义，不依赖原生世界状态。
return function(deps)
    local state = deps["framework.state"]
    local geometry = deps["game.cfg_geometry"]
    local skill = deps["game.skill"]
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
                .. "weight hp_segments skill_points recruit_cost", "player")
            integer_value(cfg.skill_points, 0, 10000, "player.skill_points")
            fields(cfg.recruit_cost, "currency_id amount", "player.recruit_cost")
            local price = cfg.recruit_cost
            assert(state.is_id(price.currency_id, "2147483647"), "invalid recruit currency")
            integer_value(price.amount, 0, 2147483647, "player.recruit_cost.amount")
            assert((price.currency_id == "0") == (price.amount == 0), "invalid free price")
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
            fields(cfg, "name max_stack kind type skill_cfg_id", "item")
            assert(cfg.skill_cfg_id == "0" or doc.skills[cfg.skill_cfg_id] ~= nil,
                "item references an unselected skill")
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
                .. "reload_kind weight melee_damage melee_range item_cfg_id", "weapon")
            assert(ref(doc.items, cfg.item_cfg_id, "weapon.item").kind == 2,
                "weapon must reference a gun item")
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
            integer_value(cfg.use_ticks, cfg.kind == "knife" and 0 or 1, 36000, "tool.use_ticks")
            integer_value(cfg.throw_range, 0, 100000, "tool.throw_range")
            integer_value(cfg.speed, 0, 1000, "tool.speed")
            for _, name in ipairs({"heal", "damage", "range", "radius",
                "stamina", "cooldown_ticks"}) do
                integer_value(cfg[name], 0, 1000000, "tool." .. name)
            end
            assert(cfg.kind ~= "bomb" or (cfg.radius > 0 and cfg.throw_range > 0 and cfg.speed > 0),
                "bomb requires positive radius, throw range and speed")
        end
    end

    -- 默认配装必须能够完整接受，所有免费槽位和负重均采用角色配置。
    local function loadout(doc)
        local load, rules = doc.default_loadout, doc.rules
        fields(load, "player_cfg_id hp_segments weapons ammo tools consumables skills", "loadout")
        local player = ref(doc.players, load.player_cfg_id, "loadout.player")
        assert(skill.check(doc, load.player_cfg_id, load.skills), "invalid default skills")
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
            actor(cfg, "detect_range attack_range damage attack_ticks rank windup recover drops ai",
                "monster")
            integer_value(cfg.detect_range, 1, 100000, "monster.detect_range")
            integer_value(cfg.attack_range, 1, cfg.detect_range, "monster.attack_range")
            integer_value(cfg.damage, 1, 1000000, "monster.damage")
            integer_value(cfg.attack_ticks, 1, 3600, "monster.attack_ticks")
            integer_value(cfg.rank, 1, 4, "monster.rank")
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

    -- 新怪物能力必须拥有完整时序、合法召唤引用及有限的实例闭包。
    local function abilities(doc)
        cfg_table(doc.abilities, "abilities", true)
        for _, cfg in pairs(doc.abilities) do
            fields(cfg, "kind range damage windup recover cooldown summon_cfg_id despawn_ticks",
                "ability")
            assert(cfg.kind == "melee" or cfg.kind == "summon", "invalid ability kind")
            integer_value(cfg.range, 1, 100000, "ability.range")
            integer_value(cfg.damage, 0, 1000000, "ability.damage")
            integer_value(cfg.windup, 0, 3600, "ability.windup")
            integer_value(cfg.recover, 0, 3600, "ability.recover")
            integer_value(cfg.cooldown, 1, 36000, "ability.cooldown")
            integer_value(cfg.despawn_ticks, 0, 36000, "ability.despawn_ticks")
            assert(cfg.windup + cfg.recover < cfg.cooldown, "invalid ability timing")
            if cfg.kind == "melee" then
                assert(cfg.damage > 0 and cfg.summon_cfg_id == "0" and cfg.despawn_ticks == 0,
                    "invalid melee ability")
            else
                local summon = ref(doc.monsters, cfg.summon_cfg_id, "ability.summon")
                assert(cfg.damage == 0 and cfg.despawn_ticks > 0 and summon.rank == 4
                    and #summon.drops == 0, "summon requires a rewardless rank-four monster")
            end
        end
        local summons = {}
        for id, monster in pairs(doc.monsters) do
            local ai = monster.ai
            assert(ai == false or type(ai) == "table", "invalid monster AI")
            if ai ~= false then
                fields(ai, "alert_speed disengage_ticks damage_reduction_bp basic abilities "
                    .. "rage_thresholds rage_ticks rage_ability_id", "monster.ai")
                integer_value(ai.alert_speed, 1, 1000, "ai.alert_speed")
                integer_value(ai.disengage_ticks, 1, 36000, "ai.disengage_ticks")
                integer_value(ai.damage_reduction_bp, 0, 10000, "ai.damage_reduction_bp")
                assert(type(ai.basic) == "boolean", "invalid ai.basic")
                array(ai.abilities, 0, 8, "ai.abilities")
                array(ai.rage_thresholds, 0, 16, "ai.rage_thresholds")
                local seen, forced = {}, false
                for _, bind in ipairs(ai.abilities) do
                    fields(bind, "cfg_id phase priority", "ability binding")
                    identity(bind.cfg_id, seen, "ability binding")
                    local cfg = ref(doc.abilities, bind.cfg_id, "ability binding")
                    assert(bind.phase == "alert" or bind.phase == "rage", "invalid ability phase")
                    integer_value(bind.priority, 1, 1000, "ability priority")
                    assert(cfg.range <= monster.detect_range, "ability exceeds alert range")
                    if cfg.kind == "summon" then
                        assert(cfg.summon_cfg_id ~= id, "recursive summon")
                        summons[id] = true
                    end
                    if bind.cfg_id == ai.rage_ability_id then
                        assert(bind.phase == "rage", "rage trigger must use a rage ability")
                        forced = true
                    end
                end
                local previous = monster.hp
                for _, threshold in ipairs(ai.rage_thresholds) do
                    integer_value(threshold, 1, previous - 1, "rage threshold")
                    previous = threshold
                end
                if #ai.rage_thresholds > 0 then
                    assert(monster.rank == 3 and forced, "rage requires Boss and trigger ability")
                    integer_value(ai.rage_ticks, 1, 36000, "ai.rage_ticks")
                else
                    assert(ai.rage_ticks == 0 and ai.rage_ability_id == "0", "unused rage settings")
                end
            end
        end
        for _, cfg in pairs(doc.abilities) do
            assert(cfg.kind ~= "summon" or not summons[cfg.summon_cfg_id],
                "summoned monsters cannot summon")
        end
        local templates, count, drops = {}, 0, 0
        -- 每个召唤者最多保留一个召唤物，尸体仍占出生实例槽位。
        local function include(spawn)
            local cfg = doc.monsters[spawn.cfg_id]
            count = count + 1 + (summons[spawn.cfg_id] and 1 or 0)
            for _, drop in ipairs(cfg.drops) do
                if drop.chance > 0 then
                    local stack = doc.items[drop.cfg_id].max_stack
                    drops = drops + (drop.max_count + stack - 1) // stack
                end
            end
        end
        for _, spawn in ipairs(doc.map.enemies) do
            assert(doc.monsters[spawn.cfg_id].rank ~= 4, "summons cannot be map spawns")
            templates[spawn.spawn_id] = spawn
            include(spawn)
        end
        if doc.encounter ~= false then
            local cfg = doc.encounter
            fields(cfg, "bounty_cfg_id alert_scale_bp second_wave", "encounter")
            assert(ref(doc.items, cfg.bounty_cfg_id, "bounty item").kind == 5,
                "bounty must be extraction loot")
            integer_value(cfg.alert_scale_bp, 10000, 100000, "encounter.alert_scale_bp")
            array(cfg.second_wave, 1, 63, "encounter.second_wave")
            local seen = {}
            for _, id in ipairs(cfg.second_wave) do
                identity(id, seen, "second wave spawn")
                local spawn = assert(templates[id], "unknown second wave spawn")
                assert(doc.monsters[spawn.cfg_id].rank <= 2,
                    "second wave excludes Boss and summons")
                include(spawn)
            end
            for _, monster in pairs(doc.monsters) do
                assert(monster.detect_range * cfg.alert_scale_bp // 10000 <= 100000,
                    "scaled alert range exceeds limit")
            end
        end
        assert(count <= 63, "two-wave and summon actor capacity exceeds 63")
        assert(drops <= 64, "two-wave drop capacity exceeds 64")
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
            fields(scene, "id kind x y w h penetrable interaction barrel", "scene")
            identity(scene.id, seen, "scene.id")
            assert(scene.kind == "ladder" or scene.kind == "supply" or scene.kind == "cover"
                or scene.kind == "clue" or scene.kind == "barrel",
                "invalid scene kind")
            assert(type(scene.penetrable) == "boolean"
                and (scene.kind == "cover" or not scene.penetrable), "only cover is penetrable")
            if scene.interaction ~= false then
                local cfg = scene.interaction
                fields(cfg, "mode hold_ticks", "scene.interaction")
                assert(scene.kind == "supply" or scene.kind == "ladder" or scene.kind == "clue",
                    "scene does not support interaction")
                assert(cfg.mode == "instant" or cfg.mode == "channel", "invalid interaction mode")
                integer_value(cfg.hold_ticks, 0, 36000, "interaction.hold_ticks")
                assert((cfg.mode == "instant") == (cfg.hold_ticks == 0),
                    "invalid interaction delay")
            end
            assert((scene.kind == "barrel") == (scene.barrel ~= false), "barrel config is required")
            if scene.barrel ~= false then
                local cfg = scene.barrel
                fields(cfg, "hp fuse_ticks radius damage", "scene.barrel")
                integer_value(cfg.hp, 1, 1000000, "barrel.hp")
                integer_value(cfg.fuse_ticks, 1, 36000, "barrel.fuse_ticks")
                integer_value(cfg.radius, 1, 100000, "barrel.radius")
                integer_value(cfg.damage, 1, 1000000, "barrel.damage")
            end
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

    -- 区域、候选首领和线索使用唯一关联；所有对象必须位于所属区域。
    local function exploration(doc)
        if doc.exploration == false then
            return
        end
        fields(doc.exploration, "regions", "exploration")
        array(doc.exploration.regions, 2, 16, "exploration.regions")
        local spawns, scenes, bosses, clues, ids = {}, {}, {}, {}, {}
        for _, spawn in ipairs(doc.map.enemies) do
            spawns[spawn.spawn_id] = spawn
        end
        for _, scene in ipairs(doc.scenes) do
            scenes[scene.id] = scene
        end
        for _, cfg in ipairs(doc.exploration.regions) do
            fields(cfg, "id x y w h boss_spawns clues", "region")
            identity(cfg.id, ids, "region.id")
            integer_value(cfg.x, 0, doc.map.width, "region.x")
            integer_value(cfg.y, 0, doc.map.height, "region.y")
            integer_value(cfg.w, 1, doc.map.width - cfg.x, "region.w")
            integer_value(cfg.h, 1, doc.map.height - cfg.y, "region.h")
            array(cfg.boss_spawns, 1, 32, "region.boss_spawns")
            array(cfg.clues, 1, 2, "region.clues")
            for _, id in ipairs(cfg.boss_spawns) do
                identity(id, bosses, "region.boss")
                local spawn = assert(spawns[id], "unknown region Boss spawn")
                assert(doc.monsters[spawn.cfg_id].rank == 3, "region requires Boss templates")
                assert(spawn.x >= cfg.x and spawn.x <= cfg.x + cfg.w
                    and spawn.y >= cfg.y and spawn.y <= cfg.y + cfg.h, "Boss outside region")
            end
            for _, id in ipairs(cfg.clues) do
                identity(id, clues, "region.clue")
                local scene = assert(scenes[id], "unknown region clue")
                assert(scene.kind == "clue" and scene.interaction ~= false,
                    "clue requires an explicit interaction")
                assert(scene.x >= cfg.x and scene.x + scene.w <= cfg.x + cfg.w
                    and scene.y >= cfg.y and scene.y + scene.h <= cfg.y + cfg.h,
                    "clue outside region")
            end
        end
        for id, spawn in pairs(spawns) do
            assert(doc.monsters[spawn.cfg_id].rank ~= 3 or bosses[id], "unassigned Boss template")
        end
        for id, scene in pairs(scenes) do
            assert(scene.kind ~= "clue" or clues[id], "unassigned clue")
        end
    end

    -- 正式源表和显式测试夹具共用完整的内容语义入口。
    function api.validate(doc)
        fields(doc, "v tick_hz map players monsters weapons items bag extracts ammo tools rules "
            .. "scenes default_loadout skills abilities encounter legacy_ai career exploration",
            "content")
        integer_value(doc.v, 5, 5, "content.v")
        assert(type(doc.legacy_ai) == "boolean", "invalid legacy AI flag")
        if doc.career ~= false then
            local cfg = doc.career
            fields(cfg, "levels kill_xp extract_xp bounty_xp bounty_currency retire_xp", "career")
            array(cfg.levels, 2, 100, "career.levels")
            assert(cfg.levels[1] == 0, "career levels must start at zero")
            local previous = -1
            for _, xp in ipairs(cfg.levels) do
                integer_value(xp, previous + 1, 2147483647, "career.level threshold")
                previous = xp
            end
            assert(type(cfg.kill_xp) == "table", "career kill rewards must be explicit")
            for id, xp in pairs(cfg.kill_xp) do
                ref(doc.monsters, id, "career.kill_xp")
                integer_value(xp, 0, 1000000, "career.kill_xp")
            end
            for id, monster in pairs(doc.monsters) do
                assert(monster.rank == 4 or cfg.kill_xp[id] ~= nil, "missing monster XP reward")
            end
            for _, name in ipairs({"extract_xp", "bounty_xp", "bounty_currency", "retire_xp"}) do
                integer_value(cfg[name], 0, 1000000, "career." .. name)
            end
        end
        skill.validate(doc.skills)
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
        abilities(doc)
        exploration(doc)
        geometry.validate(doc)
        return doc
    end

    return api
end

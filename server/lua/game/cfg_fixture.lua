-- 显式升级旧战斗测试夹具，隔离测试默认值，生产源表不会调用此模块。
return function(deps)
    local state = deps["framework.state"]
    local api = {}

    -- 旧版本仅保留历史地图与战斗数值，补充当前契约要求的测试字段。
    function api.upgrade(source)
        local doc = json.decode(json.encode(source))
        if doc.v == 4 then
            return doc
        end
        assert(doc.v == 3, "unsupported fixture content version")
        assert(state.fields(doc, {"v", "tick_hz", "map", "players", "weapons", "monsters"}),
            "invalid legacy fixture fields")
        doc.v = 4
        doc.ammo, doc.tools, doc.items = json.object({}), json.object({}), json.object({})
        doc.scenes, doc.extracts = json.array({}), json.array({})
        doc.bag = {slots = 8, pickup_radius = 1500}
        doc.rules = {weapon_slots = 2, tool_slots = 4, consumable_slots = 4,
            tool_move_percent = 70, max_scenes = 32, max_projectiles = 16,
            melee_stamina = 20, melee_ticks = 30}
        for _, cfg in pairs(doc.players) do
            assert(cfg.hp % 25 == 0, "fixture health requires 25 point blood segments")
            cfg.run_speed, cfg.prone_speed = cfg.speed * 3 // 2, cfg.speed // 2
            cfg.prone_width, cfg.prone_height = 1000, 600
            cfg.stamina, cfg.stamina_delay, cfg.stamina_rate = 100, 120, 20
            cfg.run_cost, cfg.jump_cost = 0, 0
            cfg.health_delay, cfg.health_rate, cfg.weight = 300, 5, 3
            cfg.hp_segments = json.array({})
            for index = 1, cfg.hp // 25 do
                cfg.hp_segments[index] = 25
            end
        end
        for key, cfg in pairs(doc.weapons) do
            local item = tostring(20000 + integer.create(tonumber(key)))
            doc.items[item] = {name = "回归弹药", max_stack = 99, kind = 3, type = 1}
            doc.ammo[key] = {item_cfg_id = item, pellets = 1, damage = cfg.damage,
                range = cfg.range, spread_deg = 0, penetration = 0, loss = 0}
            cfg.ammo_cfg_id, cfg.weight, cfg.reload_kind = key, 1, 1
            cfg.melee_damage, cfg.melee_range = 50, 900
        end
        for _, cfg in pairs(doc.monsters) do
            cfg.rank, cfg.windup, cfg.recover, cfg.drops = 1, 0, 0, json.array({})
        end
        local spawn = doc.map.spawn
        doc.default_loadout = {player_cfg_id = spawn.cfg_id,
            weapons = json.array({spawn.weapon_cfg_id}), ammo = json.array({spawn.weapon_cfg_id}),
            tools = json.array({}), consumables = json.array({}),
            hp_segments = doc.players[spawn.cfg_id].hp_segments}
        return doc
    end

    return api
end

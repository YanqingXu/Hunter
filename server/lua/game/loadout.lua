-- 在分配局号之前只读校验完整配装，并从同一份配置生成原生实体初始化参数。
return function(deps)
    local state = deps["framework.state"]
    local api = {}

    -- 构造独立默认配装，不允许请求污染缓存的只读源表。
    function api.default(content)
        local cfg = content.default_loadout
        local result = {player_cfg_id = cfg.player_cfg_id, health_segments = json.array({}),
            weapons = json.array({}), tools = json.array({}), consumables = json.array({})}
        for _, value in ipairs(cfg.hp_segments) do
            result.health_segments[#result.health_segments + 1] = value
        end
        for index, value in ipairs(cfg.weapons) do
            result.weapons[index] = {cfg_id = value, ammo_cfg_id = cfg.ammo[index]}
        end
        for _, value in ipairs(cfg.tools) do
            result.tools[#result.tools + 1] = value
        end
        for _, value in ipairs(cfg.consumables) do
            result.consumables[#result.consumables + 1] = value
        end
        return result
    end

    -- 仅完整空配装采用默认值，其余缺项均返回原有业务错误。
    function api.check(content, value)
        if state.fields(value, {}) or (state.fields(value,
            {"player_cfg_id", "health_segments", "weapons", "tools", "consumables"})
            and value.player_cfg_id == "0" and state.array(value.health_segments, 0, 0)
            and state.array(value.weapons, 0, 0) and state.array(value.tools, 0, 0)
            and state.array(value.consumables, 0, 0)) then
            value = api.default(content)
        end
        if not state.fields(value,
            {"player_cfg_id", "health_segments", "weapons", "tools", "consumables"}) then
            return nil, "invalid_loadout"
        end
        local player = content.players[value.player_cfg_id]
        if player == nil then
            return nil, "invalid_player_cfg"
        end
        if not state.array(value.health_segments, 1, 6) then
            return nil, "invalid_health_segments"
        end
        local total = 0
        for _, amount in ipairs(value.health_segments) do
            if not state.integer(amount, 25, 50) or (amount ~= 25 and amount ~= 50) then
                return nil, "invalid_health_segments"
            end
            total = total + amount
        end
        if total ~= player.hp then
            return nil, "invalid_health_segments"
        end
        if not state.array(value.weapons, 1, content.rules.weapon_slots) then
            return nil, "invalid_weapon_slots"
        end
        local weight = 0
        for _, choice in ipairs(value.weapons) do
            if not state.fields(choice, {"cfg_id", "ammo_cfg_id"})
                or content.weapons[choice.cfg_id] == nil then
                return nil, "invalid_weapon_cfg"
            end
            local gun = content.weapons[choice.cfg_id]
            if gun.ammo_cfg_id ~= choice.ammo_cfg_id or content.ammo[choice.ammo_cfg_id] == nil then
                return nil, "invalid_ammo_cfg"
            end
            weight = weight + gun.weight
        end
        if weight > player.weight then
            return nil, "loadout_overweight"
        end
        if not state.array(value.tools, 0, content.rules.tool_slots)
            or not state.array(value.consumables, 0, content.rules.consumable_slots) then
            return nil, "invalid_tool_slots"
        end
        local selected = {}
        for group, choices in ipairs({value.tools, value.consumables}) do
            for _, id in ipairs(choices) do
                local tool = content.tools[id]
                if tool == nil or selected[id] then
                    return nil, "invalid_tool_cfg"
                end
                selected[id] = true
                if tool.kind ~= "knife" and tool.kind ~= "medkit" and tool.kind ~= "needle"
                    and tool.kind ~= "bomb" then
                    return nil, "invalid_tool_kind"
                end
                if (tool.kind == "needle" or tool.kind == "bomb") ~= (group == 2) then
                    return nil, "invalid_tool_slot"
                end
            end
        end
        return value, nil
    end

    -- 将冻结配装转换为只含实例初值的参数，原生层不再解析枪械或工具配置。
    function api.player(content, loadout)
        local cfg = content.players[loadout.player_cfg_id]
        local spawn = content.map.spawn
        local result = {cfg_id = loadout.player_cfg_id, x = spawn.x, y = spawn.y,
            hp = cfg.hp, width = cfg.width, height = cfg.height, stamina = cfg.stamina,
            health_segments = loadout.health_segments, weapons = json.array({}), tools = json.array({})}
        for _, choice in ipairs(loadout.weapons) do
            local gun = content.weapons[choice.cfg_id]
            result.weapons[#result.weapons + 1] = {cfg_id = choice.cfg_id,
                ammo_cfg_id = choice.ammo_cfg_id, ammo = gun.magazine, reserve = gun.reserve}
        end
        for group, choices in ipairs({loadout.tools, loadout.consumables}) do
            for slot, id in ipairs(choices) do
                result.tools[#result.tools + 1] = {slot = slot + (group - 1) * 4,
                    cfg_id = id, count = content.tools[id].uses}
            end
        end
        return result
    end

    return api
end

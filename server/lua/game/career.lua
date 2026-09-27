-- 只读计算永久猎人交易，价格、成长和终局资产均由配置及原生快照决定。
return function(deps)
    local loadout = deps["game.loadout"]
    local api = {}

    -- 在存储投影中查找账号实际拥有的猎人。
    local function hunter(profile, id)
        for _, value in ipairs(profile.hunters) do
            if tostring(value.hunter_id) == tostring(id) then
                return value
            end
        end
        return nil
    end

    -- 根据仓库物品映射唯一武器，不接受客户端提供武器属性。
    local function gun(content, item)
        for id, value in pairs(content.weapons) do
            if value.item_cfg_id == tostring(item) then
                return id, value
            end
        end
        return nil
    end

    -- 校验装备槽和物品类型，工具数量以持有实例计数为准。
    local function slot_valid(content, slot, item)
        if slot >= 1 and slot <= 2 then
            return gun(content, item) ~= nil
        end
        local tool = content.tools[tostring(item)]
        if tool == nil then return false end
        local consumable = tool.kind == "bomb" or tool.kind == "needle"
        return (slot >= 3 and slot <= 6 and not consumable)
            or (slot >= 7 and slot <= 10 and consumable)
    end

    -- 由持有资产生成配装；空枪槽不会填入测试默认装备。
    local function owned_loadout(content, value)
        local cfg = content.players[tostring(value.cfg_id)]
        if cfg == nil then return nil, "invalid_player_cfg" end
        local result = {player_cfg_id = tostring(value.cfg_id),
            health_segments = cfg.hp_segments, weapons = json.array({}),
            tools = json.array({}), consumables = json.array({}), skills = json.array({})}
        local equipment = {}
        for _, item in ipairs(value.equipment) do equipment[#equipment + 1] = item end
        table.sort(equipment, function(a, b) return a.slot < b.slot end)
        for _, item in ipairs(equipment) do
            if not slot_valid(content, item.slot, item.cfg_id) then
                return nil, "invalid_equipment"
            end
            if item.slot <= 2 then
                local id, weapon = gun(content, item.cfg_id)
                result.weapons[#result.weapons + 1] = {cfg_id = id,
                    ammo_cfg_id = weapon.ammo_cfg_id}
            else
                local choices = item.slot <= 6 and result.tools or result.consumables
                choices[#choices + 1] = tostring(item.cfg_id)
            end
        end
        for _, selected in ipairs(value.skills) do
            result.skills[#result.skills + 1] = tostring(selected.cfg_id)
        end
        return loadout.check(content, result, true)
    end

    -- 用累计经验曲线计算升级，每升一级增加一个尚未支付的技能点。
    local function growth(career, value, earned)
        local xp, level = value.xp + earned, value.level
        while level < #career.levels and xp >= career.levels[level + 1] do
            level = level + 1
        end
        return level, xp, value.points + level - value.level
    end

    -- 从终局权威状态提取资产，已消费技能不会通过历史基线复活。
    local function finish(content, value, doc)
        local outcome = doc.phase == "Settling" and doc.raid.player_state or doc.phase
        if tostring(doc.hunter_id) ~= tostring(value.hunter_id)
            or (outcome ~= "Extracted" and outcome ~= "Dead"
                and outcome ~= "Abandoned") then
            return nil, "invalid_hunter_result"
        end
        local payload = {match_id = tonumber(doc.match_id), outcome = outcome,
            content_key = doc.content_key, level = 0, xp = 0, points = 0,
            account_xp = 0, currency_gain = 0, skills = json.array({}),
            equipment = json.array({}), items = json.array({})}
        if outcome ~= "Extracted" then return payload end
        if content.career == false then return nil, "career_not_configured" end
        local player = doc.entities[doc.player_entity_id]
        if player == nil then return nil, "invalid_hunter_result" end
        local baseline, earned = {}, content.career.extract_xp
        for _, selected in ipairs(value.skills) do
            baseline[tostring(selected.cfg_id)] = selected
        end
        for _, selected in ipairs(player.demo.skills) do
            if not selected.spent then
                local old = baseline[selected.cfg_id]
                payload.skills[#payload.skills + 1] = {cfg_id = tonumber(selected.cfg_id),
                    paid_cost = old and old.paid_cost or 0, source = old and old.source or "loot"}
            end
        end
        for _, entity in pairs(doc.entities) do
            if entity.kind == "monster" and not entity.health.alive
                and entity.ai.owner_id == "0" then
                earned = earned + (content.career.kill_xp[entity.cfg_id] or 0)
            end
        end
        if doc.bounty_id ~= "0" then
            earned = earned + content.career.bounty_xp
            payload.currency_gain = content.career.bounty_currency
        end
        payload.level, payload.xp, payload.points = growth(content.career, value, earned)
        for _, item in ipairs(value.equipment) do
            local count = item.count
            if item.slot > 2 then
                local used = math.min(item.count, content.tools[tostring(item.cfg_id)].uses)
                count = item.count - used
                for _, tool in ipairs(player.demo.tools) do
                    if tostring(tool.cfg_id) == tostring(item.cfg_id) then
                        count = item.count - used + math.min(used, tool.count)
                    end
                end
            end
            if count > 0 then
                payload.equipment[#payload.equipment + 1] = {item_uid = item.item_uid,
                    count = count}
            end
        end
        for _, item in pairs(doc.items) do
            if item.place == "Bag" and item.owner_player_id == doc.player_id then
                payload.items[#payload.items + 1] = {cfg_id = tonumber(item.cfg_id),
                    count = item.count}
            end
        end
        table.sort(payload.items, function(a, b) return a.cfg_id < b.cfg_id end)
        return payload
    end

    -- 只读入口接收宿主读取的账号投影，返回存储事务的规范参数。
    function api.check(content, request)
        local profile, kind = request.profile, request.kind
        if request.v ~= 8 or type(profile) ~= "table" then
            return {ok = false, error = "invalid_hunter_request"}
        end
        local value = hunter(profile, request.hunter_id or "0")
        local payload, reason
        if kind == "recruit" then
            local cfg = content.players[tostring(request.cfg_id)]
            if cfg == nil or cfg.recruit_cost == nil then
                reason = "invalid_player_cfg"
            elseif cfg.recruit_cost.currency_id ~= "0"
                and cfg.recruit_cost.currency_id ~= "1" then
                reason = "unsupported_currency"
            else
                payload = {cfg_id = tonumber(request.cfg_id), level = 1, xp = 0,
                    points = cfg.skill_points, currency_cost = cfg.recruit_cost.amount,
                    skills = json.array({})}
            end
        elseif value == nil then
            reason = "hunter_not_found"
        elseif kind == "finish_raid" then
            payload, reason = finish(content, value, request.state)
        elseif value.state ~= "ready" then
            reason = "hunter_busy"
        elseif kind == "start" then
            if content.career == false then
                reason = "career_not_configured"
            else
                local accepted
                accepted, reason = owned_loadout(content, value)
                if accepted then
                    payload = {loadout = accepted, tool_counts = json.array({})}
                    for _, item in ipairs(value.equipment) do
                        if item.slot > 2 then
                            payload.tool_counts[#payload.tool_counts + 1] = {
                                cfg_id = tostring(item.cfg_id), count = math.min(item.count,
                                    content.tools[tostring(item.cfg_id)].uses)}
                        end
                    end
                end
            end
        elseif kind == "buy_skill" then
            local cfg = content.skills[tostring(request.skill_id)]
            if cfg == nil then reason = "invalid_skill_cfg"
            else payload = {cfg_id = tonumber(request.skill_id), cost = cfg.cost} end
        elseif kind == "remove_skill" then
            reason = "skill_not_owned"
            for _, selected in ipairs(value.skills) do
                if tostring(selected.cfg_id) == tostring(request.skill_id) then
                    local paid = selected.paid_cost
                    payload = {cfg_id = selected.cfg_id, refund = paid == 1 and 1 or paid // 2}
                    reason = nil
                end
            end
        elseif kind == "retire" then
            if content.career == false then reason = "career_not_configured"
            else payload = {max_level = #content.career.levels,
                account_xp = content.career.retire_xp} end
        elseif kind == "equip" then
            local slot, uid = request.slot, tostring(request.item_uid)
            if type(slot) ~= "integer" or slot < 1 or slot > 10 then
                reason = "invalid_equipment_slot"
            else
                local found = uid == "0"
                for _, item in ipairs(profile.stash) do
                    if tostring(item.item_uid) == uid then
                        found = slot_valid(content, slot, item.cfg_id)
                    end
                end
                for _, item in ipairs(value.equipment) do
                    if tostring(item.item_uid) == uid then
                        found = slot_valid(content, slot, item.cfg_id)
                    end
                end
                if not found then reason = "item_not_owned"
                else
                    payload = {items = json.array({})}
                    for _, item in ipairs(value.equipment) do
                        if item.slot ~= slot and tostring(item.item_uid) ~= uid then
                            payload.items[#payload.items + 1] = {slot = item.slot,
                                item_uid = item.item_uid}
                        end
                    end
                    if uid ~= "0" then
                        payload.items[#payload.items + 1] = {slot = slot, item_uid = tonumber(uid)}
                    end
                end
            end
        else reason = "invalid_hunter_operation" end
        if payload == nil then return {ok = false, error = reason} end
        return {ok = true, payload = payload}
    end
    return api
end

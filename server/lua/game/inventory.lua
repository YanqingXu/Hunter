-- 在 Lua 计算拾取距离、背包叠堆和消耗品槽位，再交给原生批量接口原子提交。
return function(deps)
    local world_api = deps["game.world"]
    local encounter = deps["game.encounter"]
    local cfg_api = deps["game.cfg"]
    local loadout = deps["game.loadout"]
    local api = {}

    -- 创建绑定物品旧数量的候选，不在计算过程中修改原生背包。
    local function item_change(item, count, place)
        return {id = item.id, expected_count = item.count, count = count, place = place}
    end

    -- 返回业务错误或空值；槽满、距离和类型拒绝均不产生局部修改。
    function api.pickup(world, content, player, target_id)
        local items = json.decode(World.inventory(world))
        local source = nil
        for _, item in ipairs(items) do
            if item.id == target_id and item.place == "Ground" then
                source = item
            end
        end
        if source == nil then
            return "already_picked"
        end
        local dx = Entity.get_x(player.pose) - source.x
        local dy = Entity.get_y(player.pose) - source.y
        if dx * dx + dy * dy > content.bag.pickup_radius * content.bag.pickup_radius then
            return "out_of_range"
        end
        local batch = {owner = World.get_player_id(world), items = json.array({}),
            tools = json.array({})}
        local item_cfg = content.items[source.cfg_id]
        if item_cfg ~= nil and item_cfg.skill_cfg_id ~= "0" then
            local candidate = json.decode(World.loadout(world))
            candidate.skills = json.array({})
            for slot = 1, Player.get_skill_count(player.controls) do
                local id = Player.get_skill_id(player.controls, slot)
                if id == item_cfg.skill_cfg_id then
                    return "already_known_skill"
                end
                if not Player.get_skill_spent(player.controls, slot) then
                    candidate.skills[#candidate.skills + 1] = id
                end
            end
            candidate.skills[#candidate.skills + 1] = item_cfg.skill_cfg_id
            local accepted, reason = loadout.check(cfg_api.load(), candidate, true)
            if accepted == nil then
                return reason
            end
            batch.items[1] = item_change(source, 0, "Ground")
            local error = World.commit_skill(world, json.encode(batch), item_cfg.skill_cfg_id)
            return error ~= "" and error or nil
        end
        local cfg = content.tools[source.cfg_id]
        if cfg ~= nil and (cfg.kind == "needle" or cfg.kind == "bomb") then
            local slot = 0
            for index = 5, 4 + content.rules.consumable_slots do
                local current = Player.get_tool_cfg(player.controls, index)
                if current == source.cfg_id then
                    slot = index
                    break
                elseif current == "0" and slot == 0 then
                    slot = index
                end
            end
            if slot == 0 or Player.get_tool_count(player.controls, slot) + source.count > cfg.uses then
                return "consumable_full"
            end
            batch.tools[1] = {slot = slot,
                expected_instance = Player.get_tool_instance(player.controls, slot),
                cfg_id = source.cfg_id, count = Player.get_tool_count(player.controls, slot)
                    + source.count}
            batch.items[1] = item_change(source, 0, "Ground")
        else
            local item_cfg = content.items[source.cfg_id]
            if item_cfg == nil or (item_cfg.kind or 5) ~= 5 then
                return "unsupported_pickup"
            end
            local remaining, used = source.count, 0
            for _, item in ipairs(items) do
                if item.place == "Bag" and item.owner_player_id == batch.owner then
                    used = used + 1
                    if item.cfg_id == source.cfg_id then
                        local amount = math.min(remaining, item_cfg.max_stack - item.count)
                        if amount > 0 then
                            batch.items[#batch.items + 1] = item_change(item, item.count + amount, "Bag")
                            remaining = remaining - amount
                        end
                    end
                end
            end
            if remaining > 0 and used >= content.bag.slots then
                return "bag_full"
            end
            batch.items[#batch.items + 1] = item_change(source, remaining, "Bag")
        end
        local error
        if content.encounter and World.get_wave(world) == 1
            and source.cfg_id == content.encounter.bounty_cfg_id then
            error = World.commit_wave(world, json.encode(batch),
                encounter.wave_specs(content), source.id)
        else
            error = World.commit(world, json.encode(batch))
        end
        return error ~= "" and error or nil
    end

    return api
end

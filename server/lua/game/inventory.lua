-- 在 Lua 计算拾取距离、背包叠堆和消耗品槽位，再交给原生批量接口原子提交。
return function(deps)
    local world_api = deps["game.world"]
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
        local error = World.commit(world, json.encode(batch))
        return error ~= "" and error or nil
    end

    return api
end

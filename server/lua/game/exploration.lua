-- 根据显式区域与线索配置抽选 Boss 并逐步排除区域，不在 Lua 保存探索状态。
return function(deps)
    local api = {}

    -- 开局先选区域再选该区域的出生模板，重复开局请求由上层拦截。
    function api.start(world, content)
        if not content.exploration then
            return
        end
        local regions = content.exploration.regions
        local region = regions[World.roll(world, #regions)]
        local spawn = region.boss_spawns[World.roll(world, #region.boss_spawns)]
        World.set_exploration(world, integer.create(tonumber(region.id)), spawn,
            integer.create(#regions))
    end

    -- 普通与精英全部出生，区域配置存在时只创建已抽中的 Boss 模板。
    function api.selected(world, content, spawn)
        return not content.exploration or content.monsters[spawn.cfg_id].rank ~= 3
            or spawn.spawn_id == World.get_boss_spawn(world)
    end

    -- 成功线索只排除一个非 Boss 未排除区域，原生接口同时消费线索与随机状态。
    function api.clue(world, content, index)
        if not content.exploration or content.scenes[index].kind ~= "clue" then
            return "invalid_clue"
        end
        if World.scene_used(world, index) then
            return "already_used"
        end
        local candidates = json.array({})
        local mask = World.get_excluded_regions(world)
        local boss = World.get_boss_region(world)
        local bit = 1
        for slot, region in ipairs(content.exploration.regions) do
            local id = integer.create(tonumber(region.id))
            if id ~= boss and (mask // bit) % 2 == 0 then
                candidates[#candidates + 1] = {index = slot, id = id}
            end
            bit = bit * 2
        end
        if #candidates == 0 then
            return "no_region_left"
        end
        if World.reveal_region(world, index, json.encode(candidates)) == 0 then
            return "invalid_state"
        end
        return nil
    end

    return api
end

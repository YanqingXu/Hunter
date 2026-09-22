-- 持有唯一局内实体集合、稳定遍历索引与会话状态，集中管理生命周期及严格导入校验。
return function(deps)
    local state = deps["framework.state"]
    local player_api = deps["game.player"]
    local monster_api = deps["game.monster"]
    local api = {}

    -- 按身份查找可参与模拟的实体，待移除对象立即停止参与玩法。
    function api.find(world, id)
        local entity = world.entities[id]
        if entity ~= nil and not entity.pending_remove then
            return entity
        end
        return nil
    end

    -- 跨处理阶段的引用必须同时匹配局次，旧局身份不能命中新局对象。
    function api.resolve(world, ref)
        if not state.fields(ref, {"match_id", "entity_id"})
            or not state.is_id(ref.match_id) or ref.match_id == "0"
            or not state.is_id(ref.entity_id) or ref.entity_id == "0"
            or ref.match_id ~= world.match_id then
            return nil
        end
        return api.find(world, ref.entity_id)
    end

    -- 清除局内玩家的持续及边沿意图；大厅没有需要清理的角色。
    function api.clear_input(world)
        local player = api.find(world, world.player_entity_id)
        if player ~= nil then
            player_api.clear_input(player)
        end
    end

    -- 创建没有活动对局的纯数据会话，配置只保留内容身份。
    function api.new(content)
        return {v = 3, tick_id = "0", seq = "0", match_id = "0", player_id = "0",
            event_id = "0", phase = "Unauthenticated", paused = false,
            content_key = json.encode(content), entities = {}, entity_ids = json.array(),
            player_entity_id = "0", last_entity_id = "0",
            last_start = {req_id = "", after_match_id = "0", match_id = "0"}}
    end

    -- 校验创建条件后才分配身份并发布实例；失败保持集合和分配器不变。
    function api.spawn(world, content, kind, spawn_id)
        if world.phase ~= "Playing" or world.paused then
            return nil, "invalid_state"
        end
        if #world.entity_ids >= 64 then
            return nil, "entity_capacity"
        end
        if world.last_entity_id == "18446744073709551615" then
            return nil, "entity_id_exhausted"
        end
        local spawn = nil
        if kind == "player" then
            if world.player_entity_id ~= "0" or spawn_id ~= nil then
                return nil, "invalid_player"
            end
            spawn = content.map.spawn
            if content.players[spawn.cfg_id] == nil
                or content.weapons[spawn.weapon_cfg_id] == nil then
                return nil, "invalid_cfg"
            end
        elseif kind == "monster" then
            spawn = monster_api.spawn_cfg(content, spawn_id)
            if spawn == nil or content.monsters[spawn.cfg_id] == nil then
                return nil, "invalid_spawn"
            end
        else
            return nil, "invalid_kind"
        end
        local id = state.next_id(world.last_entity_id)
        local entity = nil
        if kind == "player" then
            entity = player_api.new(id, world.player_id, content)
            if not player_api.valid(entity, content) then
                return nil, "invalid_player"
            end
        else
            entity = monster_api.new(id, spawn, content)
            if not monster_api.valid(entity, content) then
                return nil, "invalid_monster"
            end
        end
        world.entities[id] = entity
        world.entity_ids[#world.entity_ids + 1] = id
        world.last_entity_id = id
        if kind == "player" then
            world.player_entity_id = id
        end
        return entity
    end

    -- 只标记活动局怪物，未知、重复或玩家移除请求无副作用。
    function api.remove(world, id)
        local entity = api.find(world, id)
        if world.phase ~= "Playing" or world.paused or entity == nil
            or entity.kind ~= "monster" then
            return false
        end
        entity.pending_remove = true
        return true
    end

    -- 在固定模拟边界移除已标记实例，压紧索引而不改变其余实体顺序。
    function api.flush(world)
        local ids = json.array()
        for _, id in ipairs(world.entity_ids) do
            if world.entities[id].pending_remove then
                world.entities[id] = nil
            else
                ids[#ids + 1] = id
            end
        end
        world.entity_ids = ids
    end

    -- 重建局内身份，保留连接输入序号、事件序号与全局 Tick。
    function api.start(world, content, request)
        world.match_id = state.next_id(world.match_id)
        world.phase = "Playing"
        world.entities = {}
        world.entity_ids = json.array()
        world.player_entity_id = "0"
        world.last_entity_id = "0"
        assert(api.spawn(world, content, "player") ~= nil, "player spawn failed")
        for _, spawn in ipairs(content.map.enemies) do
            assert(api.spawn(world, content, "monster", spawn.spawn_id) ~= nil,
                "monster spawn failed")
        end
        world.last_start = {req_id = request.req_id, after_match_id = request.after_match_id,
            match_id = world.match_id}
    end

    -- 输出有单调标识的可靠事件，由宿主在整个入口成功后提交。
    function api.emit(world, kind, actor_id, target_id, x, y, amount)
        world.event_id = state.next_id(world.event_id)
        net.emit("event", json.encode({v = 3, match_id = world.match_id,
            event_id = world.event_id, tick_id = world.tick_id, kind = kind,
            actor_id = actor_id, target_id = target_id, x = x, y = y, amount = amount}))
    end

    -- 在实体收尾后裁定死亡或清怪终态，冻结动作但不删除尸体。
    function api.finish(world)
        if world.phase ~= "Playing" then
            return
        end
        local player = api.find(world, world.player_entity_id)
        if not player.health.alive then
            world.phase = "Dead"
        else
            local alive = false
            for _, id in ipairs(world.entity_ids) do
                local entity = api.find(world, id)
                if entity ~= nil and entity.kind == "monster" and entity.health.alive then
                    alive = true
                end
            end
            if not alive then
                world.phase = "Cleared"
            end
        end
        if world.phase ~= "Playing" then
            api.clear_input(world)
            for _, id in ipairs(world.entity_ids) do
                world.entities[id].motion.vx = 0
                world.entities[id].motion.vy = 0
            end
            api.emit(world, "end", player.id, "0", player.pose.x, player.pose.y, 0)
        end
    end

    -- 检查身份索引与组件，并返回玩家以及参与终态判断的存活怪物数。
    local function valid_entities(world, content)
        if type(world.entities) ~= "table" or not state.array(world.entity_ids, 0, 64) then
            return nil, 0
        end
        local seen = {}
        local player = nil
        local alive = 0
        for _, id in ipairs(world.entity_ids) do
            local entity = world.entities[id]
            if not state.is_id(id) or id == "0" or seen[id] or type(entity) ~= "table"
                or entity.id ~= id or state.newer(id, world.last_entity_id) then
                return nil, 0
            end
            seen[id] = true
            if entity.kind == "player" then
                if player ~= nil or not player_api.valid(entity, content)
                    or entity.id ~= world.player_entity_id
                    or entity.player_id ~= world.player_id then
                    return nil, 0
                end
                player = entity
            elseif entity.kind == "monster" then
                if not monster_api.valid(entity, content) then
                    return nil, 0
                end
                if entity.health.alive and not entity.pending_remove then
                    alive = alive + 1
                end
            else
                return nil, 0
            end
            if world.phase ~= "Playing"
                and (entity.motion.vx ~= 0 or entity.motion.vy ~= 0) then
                return nil, 0
            end
        end
        for id, entity in pairs(world.entities) do
            if not seen[id] then
                return nil, 0
            end
        end
        return player, alive
    end

    -- 完整验证候选世界；只读取候选数据，不修复或替换活动状态。
    function api.valid(world, content)
        if not state.fields(world, {"v", "tick_id", "seq", "match_id", "player_id",
            "event_id", "phase", "paused", "content_key", "entities", "entity_ids",
            "player_entity_id", "last_entity_id", "last_start"})
            or not state.integer(world.v, 3, 3) or world.content_key ~= json.encode(content)
            or not state.is_tick(world.tick_id) or not state.is_id(world.seq)
            or not state.is_id(world.match_id) or not state.is_id(world.event_id)
            or not state.is_id(world.player_entity_id) or not state.is_id(world.last_entity_id)
            or type(world.paused) ~= "boolean" then
            return false
        end
        local last = world.last_start
        if not state.fields(last, {"req_id", "after_match_id", "match_id"})
            or type(last.req_id) ~= "string" or #last.req_id > 128
            or not state.is_id(last.after_match_id) or not state.is_id(last.match_id) then
            return false
        end
        if world.phase == "Unauthenticated" or world.phase == "Lobby" then
            return world.player_id == (world.phase == "Lobby" and "1" or "0")
                and world.match_id == "0" and state.fields(world.entities, {})
                and state.array(world.entity_ids, 0, 0) and world.player_entity_id == "0"
                and world.last_entity_id == "0" and last.req_id == ""
                and last.after_match_id == "0" and last.match_id == "0"
        end
        if world.phase ~= "Playing" and world.phase ~= "Dead" and world.phase ~= "Cleared" then
            return false
        end
        if world.player_id ~= "1" or world.match_id == "0" or last.req_id == ""
            or last.match_id ~= world.match_id
            or not state.newer(world.match_id, last.after_match_id)
            or state.next_id(last.after_match_id) ~= world.match_id then
            return false
        end
        local player, alive = valid_entities(world, content)
        if player == nil then
            return false
        end
        local input = player.controls
        if (world.paused or world.phase ~= "Playing") and (input.move_x ~= 0 or input.jump
            or input.fire or input.fire_once or input.reload) then
            return false
        end
        return (world.phase == "Dead" and not player.health.alive)
            or (world.phase == "Cleared" and player.health.alive and alive == 0)
            or (world.phase == "Playing" and player.health.alive and alive > 0)
    end

    return api
end

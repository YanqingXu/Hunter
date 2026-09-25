-- 组织原生世界的玩法生命周期，缓存仅包含不可变身份和受代次保护的句柄。
return function(deps)
    local state = deps["framework.state"]
    local player_api = deps["game.player"]
    local monster_api = deps["game.monster"]
    local api = {}
    local cache = {}
    local revision = 0
    local ids = {}

    -- 集合发生变化时刷新只读身份索引，热路径不重复跨界构造 ID 字符串。
    local function sync(world)
        local current = World.get_revision(world)
        if revision ~= current then
            cache = {}
            ids = {}
            revision = current
            for index = 1, World.count(world) do
                ids[index] = World.entity_id(world, index)
            end
        end
    end

    -- 返回原生集合的只读身份视图，仅供固定阶段稳定遍历。
    function api.ids(world)
        sync(world)
        return ids
    end

    -- 按原生身份查找对象，待移除状态始终从原生句柄读取。
    function api.find(world, id)
        sync(world)
        local value = cache[id]
        if value ~= nil then
            return not Entity.get_pending_remove(value.pose) and value or nil
        end
        if not World.contains(world, id) then
            return nil
        end
        local entity = Entity.find(id)
        value = Entity.get_kind(entity) == "player" and player_api.view(id) or monster_api.view(id)
        cache[id] = value
        return value
    end

    -- 首版怪物目标选择入口，目标身份来自世界归属而非固定实体编号。
    function api.target(world, faction)
        if faction ~= "monster" then
            return nil
        end
        return api.find(world, World.find_player(world, World.get_player_id(world)))
    end

    -- 跨阶段引用同时检查局次和实体身份。
    function api.resolve(world, ref)
        if not state.fields(ref, {"match_id", "entity_id"})
            or not state.is_id(ref.match_id) or ref.match_id == "0"
            or not state.is_id(ref.entity_id) or ref.entity_id == "0"
            or ref.match_id ~= World.get_match_id(world) then
            return nil
        end
        return api.find(world, ref.entity_id)
    end

    -- 获取所属会话的唯一原生世界。
    function api.new(content)
        return World.current()
    end

    -- 清除原生玩家输入，暂停和终态共用同一规则。
    function api.clear_input(world)
        World.clear_input(world)
    end

    -- 原生分配成功后建立视图，预期拒绝返回原错误码。
    function api.spawn(world, content, kind, spawn_id)
        local id = World.spawn(world, kind, spawn_id or "")
        if string.sub(id, 1, 1) == ":" then
            return nil, string.sub(id, 2)
        end
        local value = api.find(world, id)
        if kind == "monster" then
            monster_api.activate(value, content)
        end
        return value
    end

    -- 标记怪物移除，不在遍历过程中释放对象。
    function api.remove(world, id)
        return World.remove(world, id)
    end

    -- 原生移除完成后释放对应视图，缓存保持有界。
    function api.flush(world)
        World.flush(world)
        sync(world)
    end

    -- 重建局内对象，保留连接序号和全局 Tick。
    function api.start(world, content, request)
        World.begin(world, request.req_id, request.after_match_id, request.match_id, request.world_id)
        assert(api.spawn(world, content, "player") ~= nil, "player spawn failed")
        for _, spawn in ipairs(content.map.enemies) do
            assert(api.spawn(world, content, "monster", spawn.spawn_id) ~= nil,
                "monster spawn failed")
        end
    end

    -- 通过标量接口暂存事件，身份分配和协议编码均由 C++ 完成。
    function api.emit(world, kind, actor_id, target_id, x, y, amount)
        net.event(kind, actor_id, target_id, x, y, amount)
    end

    -- 在实体收尾后裁定死亡或清怪终态，冻结动作但不删除尸体。
    function api.finish(world)
        if World.get_phase(world) ~= "Playing" then
            return
        end
        local player = api.find(world, World.find_player(world, World.get_player_id(world)))
        if not Unit.get_alive(player.health) then
            World.set_phase(world, "Dead")
        else
            local alive = false
            for _, id in ipairs(api.ids(world)) do
                local entity = api.find(world, id)
                if entity ~= nil and entity.kind == "monster" and Unit.get_alive(entity.health) then
                    alive = true
                end
            end
            if not alive then
                World.set_phase(world, "Cleared")
            end
        end
        if World.get_phase(world) ~= "Playing" then
            api.clear_input(world)
            for _, id in ipairs(api.ids(world)) do
                local motion = api.find(world, id).motion
                Unit.set_vx(motion, 0)
                Unit.set_vy(motion, 0)
            end
            api.emit(world, "end", player.id, "0",
                Entity.get_x(player.pose), Entity.get_y(player.pose), 0)
        end
    end

    -- 检查原生世界完整不变量，不修复状态。
    function api.valid(world, content)
        return World.valid(world)
    end

    return api
end

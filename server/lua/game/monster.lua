-- 构造怪物及出生引用，巡逻范围通过出生 ID 查找而非数组下标关联。
return function(deps)
    local unit = deps["game.unit"]
    local api = {}

    -- 在只读出生记录中解析独立出生 ID，不缓存第二份配置。
    function api.spawn_cfg(content, spawn_id)
        for _, spawn in ipairs(content.map.enemies) do
            if spawn.spawn_id == spawn_id then
                return spawn
            end
        end
        return nil
    end

    -- 组合原生怪物及 AI 句柄，出生身份保持只读。
    function api.view(id)
        local value = unit.view(id)
        value.ai = Monster.find(id)
        value.spawn_id = Monster.get_spawn_id(value.ai)
        return value
    end

    -- 完成原生已校验的出生流程，端点出生朝向巡逻区间内部。
    function api.activate(value, content)
        assert(Monster.get_state(value.ai) == "spawn", "monster already spawned")
        local spawn = api.spawn_cfg(content, value.spawn_id)
        assert(spawn ~= nil and spawn.cfg_id == value.cfg_id, "invalid monster spawn")
        Entity.set_facing(value.pose, spawn.x == spawn.patrol_max and -1 or 1)
        Monster.set_state(value.ai, "patrol")
    end

    return api
end

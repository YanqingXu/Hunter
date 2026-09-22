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

    return api
end

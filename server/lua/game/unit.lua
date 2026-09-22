-- 组合原生实体和单位句柄，运动与生命值始终存储在 C++。
return function(deps)
    local entity = deps["game.entity"]
    local api = {}

    -- 用同一原生单位句柄访问运动和生命属性。
    function api.view(id)
        local value = entity.view(id)
        local unit = Unit.find(id)
        value.motion = unit
        value.health = unit
        return value
    end

    return api
end

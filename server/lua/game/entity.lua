-- 包装原生实体身份和位置句柄，不保存可变状态副本。
return function(deps)
    local api = {}

    -- 读取不可变身份并持有经过代次校验的位置句柄。
    function api.view(id)
        local entity = Entity.find(id)
        return {id = Entity.get_id(entity), kind = Entity.get_kind(entity),
            cfg_id = Entity.get_cfg_id(entity), pose = entity}
    end

    return api
end

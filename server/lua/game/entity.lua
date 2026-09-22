-- 构造世界对象的公共身份与姿态，不持有世界或行为闭包。
return function(deps)
    local state = deps["framework.state"]
    local api = {}

    -- 使用世界分配的身份与已验证的出生参数创建公共状态。
    function api.new(id, kind, cfg_id, spawn)
        return {id = id, kind = kind, cfg_id = cfg_id,
            pose = {x = spawn.x, y = spawn.y, facing = 1}, pending_remove = false}
    end

    -- 校验公共字段；完整实体字段集合由具体构造模块限定。
    function api.valid(entity, kind)
        if type(entity) ~= "table" or not state.is_id(entity.id) or entity.id == "0"
            or entity.kind ~= kind or not state.is_id(entity.cfg_id, "2147483647")
            or entity.cfg_id == "0" or type(entity.pending_remove) ~= "boolean"
            or not state.fields(entity.pose, {"x", "y", "facing"}) then
            return false
        end
        return state.integer(entity.pose.x, 0, 100000)
            and state.integer(entity.pose.y, 0, 100000)
            and (entity.pose.facing == 1 or entity.pose.facing == -1)
            and type(entity.pose.facing) == "integer"
    end

    return api
end

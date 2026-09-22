-- 组合角色公共运动与生命数据，并按具体配置校验几何及生死关系。
return function(deps)
    local state = deps["framework.state"]
    local entity_api = deps["game.entity"]
    local movement = deps["game.movement"]
    local api = {}

    -- 创建角色公共能力，落地状态由地图实际几何决定。
    function api.new(id, kind, cfg_id, spawn, cfg, map)
        local unit = entity_api.new(id, kind, cfg_id, spawn)
        unit.motion = {vx = 0, vy = 0, grounded = false}
        unit.health = {hp = cfg.hp, max_hp = cfg.hp, alive = true}
        unit.motion.grounded = movement.grounded(unit, cfg, map)
        return unit
    end

    -- 校验角色边界、碰撞、速度和生命值，不按实体顺序推断配置。
    function api.valid(unit, kind, cfg, map)
        if cfg == nil or not entity_api.valid(unit, kind)
            or not state.fields(unit.motion, {"vx", "vy", "grounded"})
            or not state.fields(unit.health, {"hp", "max_hp", "alive"}) then
            return false
        end
        local pos = unit.pose
        local motion = unit.motion
        local health = unit.health
        if not state.integer(pos.x, cfg.width // 2, map.width - cfg.width // 2)
            or not state.integer(pos.y, 0, map.height - cfg.height)
            or not state.integer(motion.vx, -cfg.speed, cfg.speed)
            or not state.integer(motion.vy, -1000, cfg.jump_speed or 0)
            or type(motion.grounded) ~= "boolean"
            or (motion.grounded and motion.vy ~= 0)
            or motion.grounded ~= movement.grounded(unit, cfg, map)
            or not state.integer(health.max_hp, cfg.hp, cfg.hp)
            or not state.integer(health.hp, 0, cfg.hp)
            or type(health.alive) ~= "boolean" or health.alive ~= (health.hp > 0)
            or (not health.alive and (motion.vx ~= 0 or motion.vy ~= 0)) then
            return false
        end
        for _, solid in ipairs(map.solids) do
            if pos.x - cfg.width // 2 < solid.x + solid.w
                and pos.x + cfg.width // 2 > solid.x and pos.y < solid.y + solid.h
                and pos.y + cfg.height > solid.y then
                return false
            end
        end
        return true
    end

    return api
end

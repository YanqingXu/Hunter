-- 以整数毫米和固定 Tick 速度执行实心矩形扫掠，角色坐标表示脚底中心。
return function(deps)
    local api = {}

    -- 判断两个开区间是否相交，接触边界不算穿透。
    local function overlap(a, b, c, d)
        return a < d and b > c
    end

    -- 沿水平方向扫到最近的墙面，禁止高速穿过薄墙。
    local function horizontal(entity, shape, map)
        local half = shape.width // 2
        local old = entity.x
        local next_x = math.max(half, math.min(map.width - half, old + entity.vx))
        for _, solid in ipairs(map.solids) do
            if overlap(entity.y, entity.y + shape.height, solid.y, solid.y + solid.h) then
                if entity.vx > 0 and old + half <= solid.x
                    and next_x + half > solid.x then
                    next_x = solid.x - half
                elseif entity.vx < 0 and old - half >= solid.x + solid.w
                    and next_x - half < solid.x + solid.w then
                    next_x = solid.x + solid.w + half
                end
            end
        end
        entity.x = next_x
        if next_x ~= old + entity.vx then
            entity.vx = 0
        end
    end

    -- 沿竖直方向扫到顶板或地面，落地时归零速度。
    local function vertical(entity, shape, map)
        local half = shape.width // 2
        local old = entity.y
        local next_y = math.max(0, math.min(map.height - shape.height, old + entity.vy))
        local landed = old + entity.vy <= 0
        for _, solid in ipairs(map.solids) do
            if overlap(entity.x - half, entity.x + half, solid.x, solid.x + solid.w) then
                if entity.vy <= 0 and old >= solid.y + solid.h
                    and next_y <= solid.y + solid.h then
                    next_y = math.max(next_y, solid.y + solid.h)
                    landed = true
                elseif entity.vy > 0 and old + shape.height <= solid.y
                    and next_y + shape.height > solid.y then
                    next_y = math.min(next_y, solid.y - shape.height)
                end
            end
        end
        entity.y = next_y
        entity.grounded = landed
        if next_y ~= old + entity.vy or landed then
            entity.vy = 0
        end
    end

    -- 按固定次序推进角色；可选步长用于在巡逻端点前缩短水平位移。
    function api.step(entity, shape, content, move_x, jump, distance)
        if not entity.alive then
            return
        end
        entity.vx = move_x * math.min(shape.speed, distance or shape.speed)
        if move_x ~= 0 then
            entity.facing = move_x
        end
        if jump and entity.grounded then
            entity.vy = content.player.jump_speed
            entity.grounded = false
        end
        entity.vy = math.max(-1000, entity.vy - content.player.gravity)
        horizontal(entity, shape, content.map)
        vertical(entity, shape, content.map)
    end

    -- 检查下一步脚前是否仍有同高地面，供不会跳跃的普通怪使用。
    function api.supported(entity, shape, map, direction, distance)
        if entity.y == 0 then
            return true
        end
        local edge = entity.x + direction * (shape.width // 2 + (distance or shape.speed))
        for _, solid in ipairs(map.solids) do
            if entity.y == solid.y + solid.h and edge >= solid.x
                and edge <= solid.x + solid.w then
                return true
            end
        end
        return false
    end

    -- 判断脚底是否与地图地面或平台顶面接触。
    function api.grounded(entity, shape, map)
        if entity.y == 0 then
            return true
        end
        for _, solid in ipairs(map.solids) do
            if entity.y == solid.y + solid.h and overlap(entity.x - shape.width // 2,
                entity.x + shape.width // 2, solid.x, solid.x + solid.w) then
                return true
            end
        end
        return false
    end

    return api
end

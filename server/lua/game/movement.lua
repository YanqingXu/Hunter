-- 以局部标量计算整数运动，每个单位只批量读取和提交一次原生状态。
return function(deps)
    local api = {}

    -- 判断两个开区间是否相交，接触边界不算穿透。
    local function overlap(a, b, c, d)
        return a < d and b > c
    end

    -- 沿水平方向扫到最近墙面，返回位置和裁定后的速度。
    local function horizontal(x, y, vx, shape, map)
        local half = shape.width // 2
        local next_x = math.max(half, math.min(map.width - half, x + vx))
        for _, solid in ipairs(map.solids) do
            if overlap(y, y + shape.height, solid.y, solid.y + solid.h) then
                if vx > 0 and x + half <= solid.x and next_x + half > solid.x then
                    next_x = solid.x - half
                elseif vx < 0 and x - half >= solid.x + solid.w
                    and next_x - half < solid.x + solid.w then
                    next_x = solid.x + solid.w + half
                end
            end
        end
        return next_x, next_x ~= x + vx and 0 or vx
    end

    -- 沿竖直方向扫到顶板或地面，返回位置、速度及落地状态。
    local function vertical(x, y, vy, shape, map)
        local half = shape.width // 2
        local next_y = math.max(0, math.min(map.height - shape.height, y + vy))
        local landed = y + vy <= 0
        for _, solid in ipairs(map.solids) do
            if overlap(x - half, x + half, solid.x, solid.x + solid.w) then
                if vy <= 0 and y >= solid.y + solid.h and next_y <= solid.y + solid.h then
                    next_y = math.max(next_y, solid.y + solid.h)
                    landed = true
                elseif vy > 0 and y + shape.height <= solid.y
                    and next_y + shape.height > solid.y then
                    next_y = math.min(next_y, solid.y - shape.height)
                end
            end
        end
        return next_y, (next_y ~= y + vy or landed) and 0 or vy, landed
    end

    -- 按原有顺序计算并提交运动，局部值不跨 Tick 保存。
    function api.step(entity, shape, content, move_x, jump, distance)
        local x, y, vx, vy, grounded, facing, alive, pending = Unit.read_motion(entity.motion)
        if pending or not alive then
            return 0
        end
        vx = move_x * math.min(shape.speed, distance or shape.speed)
        if move_x ~= 0 then
            facing = move_x
        end
        if jump and grounded then
            vy = shape.jump_speed
        end
        vy = math.max(-1000, vy - content.map.gravity)
        x, vx = horizontal(x, y, vx, shape, content.map)
        y, vy, grounded = vertical(x, y, vy, shape, content.map)
        Unit.write_motion(entity.motion, x, y, vx, vy, grounded, facing)
        return vx
    end

    -- 检查下一步脚前是否仍有同高地面。
    function api.supported(entity, shape, map, direction, distance)
        local x, y = Unit.read_motion(entity.motion)
        if y == 0 then
            return true
        end
        local edge = x + direction * (shape.width // 2 + (distance or shape.speed))
        for _, solid in ipairs(map.solids) do
            if y == solid.y + solid.h and edge >= solid.x and edge <= solid.x + solid.w then
                return true
            end
        end
        return false
    end

    -- 判断脚底是否与地图地面或平台顶面接触。
    function api.grounded(entity, shape, map)
        local x, y = Unit.read_motion(entity.motion)
        if y == 0 then
            return true
        end
        for _, solid in ipairs(map.solids) do
            if y == solid.y + solid.h and overlap(x - shape.width // 2,
                x + shape.width // 2, solid.x, solid.x + solid.w) then
                return true
            end
        end
        return false
    end

    return api
end

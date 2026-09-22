-- 提供射线与实心矩形遮挡计算，不持有实体、武器或伤害状态。
return function(deps)
    local api = {}

    -- 求一个轴的射线参数区间，平行且在范围外时明确拒绝。
    local function slab(origin, direction, low, high)
        if direction == 0 then
            if origin < low or origin > high then
                return nil, nil
            end
            return -math.huge, math.huge
        end
        local first = (low - origin) / direction
        local last = (high - origin) / direction
        return math.min(first, last), math.max(first, last)
    end

    -- 返回射线与矩形的首个非负交点，不使用客户端命中信息。
    function api.ray(x, y, dx, dy, range, left, bottom, width, height)
        local x1, x2 = slab(x, dx, left, left + width)
        local y1, y2 = slab(y, dy, bottom, bottom + height)
        if x1 == nil or y1 == nil then
            return nil
        end
        local near = math.max(0.0, x1, y1)
        local far = math.min(range, x2, y2)
        if near <= far then
            return near
        end
        return nil
    end

    -- 检查两点之间是否被实心地图矩形阻挡。
    function api.visible(content, x, y, target_x, target_y)
        local dx = target_x - x
        local dy = target_y - y
        for _, solid in ipairs(content.map.solids) do
            local distance = api.ray(x, y, dx, dy, 1.0, solid.x, solid.y, solid.w, solid.h)
            if distance ~= nil and distance < 1.0 then
                return false
            end
        end
        return true
    end

    return api
end

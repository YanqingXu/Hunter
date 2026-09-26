-- 校验配置的地图矩形、出生支撑和巡逻扫掠，构建与运行时使用同一份规则。
return function(deps)
    local api = {}

    -- 校验坐标与毫米尺寸的精确整数范围。
    local function integer_value(value, low, high, name)
        assert(type(value) == "integer" and value >= low and value <= high,
            "invalid integer: " .. name)
        return value
    end

    -- 返回位于地图内部的左下右上矩形边界。
    function api.rectangle(value, map, name)
        integer_value(value.x, 0, map.width, name .. ".x")
        integer_value(value.y, 0, map.height, name .. ".y")
        integer_value(value.w, 1, map.width - value.x, name .. ".w")
        integer_value(value.h, 1, map.height - value.y, name .. ".h")
        return {value.x, value.y, value.x + value.w, value.y + value.h}
    end

    -- 接触边缘不算穿透，只有内部相交才阻断出生和巡逻。
    function api.overlap(left, right)
        return left[1] < right[3] and right[1] < left[3]
            and left[2] < right[4] and right[2] < left[4]
    end

    -- 将脚底中心与站立体型转换为边界，并检查完整脚底支撑。
    local function spawn(pos, cfg, map, boxes)
        integer_value(pos.x, 0, map.width, "spawn.x")
        integer_value(pos.y, 0, map.height, "spawn.y")
        local half = cfg.width // 2
        local box = {pos.x - half, pos.y, pos.x + half, pos.y + cfg.height}
        assert(box[1] >= 0 and box[3] <= map.width and box[4] <= map.height,
            "actor extends outside map")
        local supported = pos.y == 0
        for _, solid in ipairs(boxes) do
            assert(not api.overlap(box, solid), "actor overlaps a solid")
            supported = supported or (solid[4] == pos.y and solid[1] <= box[1]
                and solid[3] >= box[3])
        end
        assert(supported, "actor has no complete spawn support")
        return box
    end

    -- 整个巡逻扫掠区必须位于地图内、不穿墙，并拥有连续支撑。
    local function patrol(enemy, cfg, map, boxes)
        local minimum = integer_value(enemy.patrol_min, 0, map.width, "enemy.patrol_min")
        local maximum = integer_value(enemy.patrol_max, 0, map.width, "enemy.patrol_max")
        assert(minimum <= enemy.x and enemy.x <= maximum and minimum ~= maximum,
            "spawn must be inside a nonempty patrol interval")
        local half = cfg.width // 2
        local swept = {minimum - half, enemy.y, maximum + half, enemy.y + cfg.height}
        assert(swept[1] >= 0 and swept[3] <= map.width, "patrol extends outside map")
        local sorted = {}
        for _, box in ipairs(boxes) do
            assert(not api.overlap(swept, box), "patrol intersects a solid")
            sorted[#sorted + 1] = box
        end
        if enemy.y == 0 then
            return
        end
        table.sort(sorted, function(left, right) return left[1] < right[1] end)
        local supported = swept[1]
        for _, box in ipairs(sorted) do
            if box[4] == enemy.y and box[1] <= supported and supported <= box[3] then
                supported = math.max(supported, box[3])
            end
        end
        assert(supported >= swept[3], "patrol crosses unsupported space")
    end

    -- 校验固体、掩体和梯子后验证所有可选角色以及怪物出生和巡逻。
    function api.validate(doc)
        local map, solids, boxes = doc.map, {}, {}
        for _, solid in ipairs(map.solids) do
            local box = api.rectangle(solid, map, "solid")
            for _, other in ipairs(boxes) do
                assert(not api.overlap(box, other), "solid rectangles overlap")
            end
            solids[#solids + 1], boxes[#boxes + 1] = box, box
        end
        for _, scene in ipairs(doc.scenes) do
            local box = api.rectangle(scene, map, "scene")
            if scene.kind == "cover" then
                for _, other in ipairs(boxes) do
                    assert(not api.overlap(box, other), "cover overlaps another solid")
                end
                boxes[#boxes + 1] = box
            elseif scene.kind == "ladder" then
                local supported = false
                for _, solid in ipairs(solids) do
                    supported = supported or (solid[4] == box[4] and solid[1] <= box[1]
                        and solid[3] >= box[3])
                end
                assert(supported, "ladder top requires a supporting platform")
            end
        end
        for _, cfg in pairs(doc.players) do
            spawn(map.spawn, cfg, map, boxes)
        end
        local occupied = {spawn(map.spawn, doc.players[map.spawn.cfg_id], map, boxes)}
        for _, enemy in ipairs(map.enemies) do
            local cfg = doc.monsters[enemy.cfg_id]
            local box = spawn(enemy, cfg, map, boxes)
            for _, other in ipairs(occupied) do
                assert(not api.overlap(box, other), "actor spawn rectangles overlap")
            end
            occupied[#occupied + 1] = box
            patrol(enemy, cfg, map, boxes)
        end
        for _, point in ipairs(doc.extracts) do
            api.rectangle(point, map, "extract")
        end
    end

    return api
end

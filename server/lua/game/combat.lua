-- 推进弹药与冷却，通过最近矩形交点裁定单发射线、墙体遮挡和伤害。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
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

    -- 扣弹并沿当前瞄准方向命中最近目标；同距时墙优先、实体按稳定顺序。
    local function shoot(world, content)
        local player = world.entities[1]
        local input = world.controls
        local length = math.sqrt(input.aim_x * input.aim_x + input.aim_y * input.aim_y)
        local dx = input.aim_x / length
        local dy = input.aim_y / length
        local x = player.x
        local y = player.y + content.player.height // 2
        local nearest = content.weapon.range
        local target = nil
        local blocked = false
        for _, solid in ipairs(content.map.solids) do
            local distance = api.ray(x, y, dx, dy, nearest, solid.x, solid.y, solid.w, solid.h)
            if distance ~= nil then
                nearest = distance
                blocked = true
            end
        end
        for i = 2, #world.entities do
            local enemy = world.entities[i]
            if enemy.alive then
                local distance = api.ray(x, y, dx, dy, nearest,
                    enemy.x - content.enemy.width // 2, enemy.y,
                    content.enemy.width, content.enemy.height)
                if distance ~= nil and (distance < nearest or (distance == nearest
                    and not blocked and (target == nil or state.newer(target.id, enemy.id)))) then
                    target = enemy
                    nearest = distance
                    blocked = false
                end
            end
        end
        player.ammo = player.ammo - 1
        player.shot_ticks = content.weapon.fire_ticks
        if input.aim_x ~= 0 then
            player.facing = input.aim_x > 0 and 1 or -1
        end
        local hit_x = integer.create(math.floor(x + dx * nearest + 0.5))
        local hit_y = integer.create(math.floor(y + dy * nearest + 0.5))
        world_api.emit(world, "shot", player.id, target and target.id or "0", hit_x, hit_y, 0)
        if target ~= nil then
            world_api.damage(world, player, target, content.weapon.damage)
        end
    end

    -- 先完成旧冷却和换弹，再处理当前换弹与射击意图。
    function api.step(world, content)
        local player = world.entities[1]
        if not player.alive then
            return
        end
        if player.shot_ticks > 0 then
            player.shot_ticks = player.shot_ticks - 1
        end
        if player.reload_ticks > 0 then
            player.reload_ticks = player.reload_ticks - 1
            if player.reload_ticks == 0 then
                local amount = math.min(content.weapon.magazine - player.ammo, player.reserve)
                player.ammo = player.ammo + amount
                player.reserve = player.reserve - amount
                world_api.emit(world, "reload", player.id, "0", player.x, player.y, amount)
            end
        end
        if world.controls.reload and player.reload_ticks == 0 and player.reserve > 0
            and player.ammo < content.weapon.magazine then
            player.reload_ticks = content.weapon.reload_ticks
            world_api.emit(world, "reload", player.id, "0", player.x, player.y, 0)
        end
        if (world.controls.fire or world.controls.fire_once) and player.reload_ticks == 0
            and player.shot_ticks == 0 and player.ammo > 0 then
            shoot(world, content)
        end
    end

    return api
end

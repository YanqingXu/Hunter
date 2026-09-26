-- 推进原生拥有的有限炸药飞行物，扫描完整路径并且只对可见怪物爆炸一次。
return function(deps)
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local api = {}

    -- 按最近身体点判定半径，实心地图和掩体遮挡爆炸，不伤害玩家或破坏场景。
    local function explode(world, content, cfg, x, y)
        local source_id = World.find_player(world, World.get_player_id(world))
        local solids = movement.solids(content)
        world_api.emit(world, "explosion", source_id, "0", x, y, cfg.radius)
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and Unit.get_alive(enemy.health) then
                local shape = movement.shape(enemy, content)
                local ex = Entity.get_x(enemy.pose)
                local ey = Entity.get_y(enemy.pose)
                local px = math.max(ex - shape.width // 2, math.min(ex + shape.width // 2, x))
                local py = math.max(ey, math.min(ey + shape.height, y))
                local dx = px - x
                local dy = py - y
                if dx * dx + dy * dy <= cfg.radius * cfg.radius
                    and combat.clear_path(solids, x, y, px, py) then
                    damage.hit(world, source_id, enemy, cfg.damage)
                end
            end
        end
    end

    -- 查找这一段路径的首个实心物或存活怪物，地图边界同样触发爆炸。
    local function impact(world, content, x, y, dx, dy, distance)
        local nearest = distance
        local hit = false
        local near, far = combat.ray(x, y, dx, dy, distance,
            0, 0, content.map.width, content.map.height)
        if near ~= nil and far < nearest then
            nearest = far
            hit = true
        end
        for _, solid in ipairs(movement.solids(content)) do
            local at = combat.ray(x, y, dx, dy, nearest, solid.x, solid.y, solid.w, solid.h)
            if at ~= nil then
                nearest = at
                hit = true
            end
        end
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and Unit.get_alive(enemy.health) then
                local shape = movement.shape(enemy, content)
                local at = combat.ray(x, y, dx, dy, nearest,
                    Entity.get_x(enemy.pose) - shape.width // 2, Entity.get_y(enemy.pose),
                    shape.width, shape.height)
                if at ~= nil then
                    nearest = at
                    hit = true
                end
            end
        end
        return nearest, hit
    end

    -- 只推进 Tick 开始前已经存在的飞行物，新完成投掷从下一 Tick 开始移动。
    function api.step(world, content)
        for slot = 1, content.rules.max_projectiles do
            if World.projectile_alive(world, slot) then
                local cfg_id, x, y, vx, vy, remaining = World.read_projectile(world, slot)
                local cfg = content.tools[cfg_id]
                local length = math.sqrt(vx * vx + vy * vy)
                local dx = vx / length
                local dy = vy / length
                local distance = math.min(cfg.speed, remaining)
                local moved, hit = impact(world, content, x, y, dx, dy, distance)
                local next_x = integer.create(math.floor(x + dx * moved + 0.5))
                local next_y = integer.create(math.floor(y + dy * moved + 0.5))
                if hit or remaining <= distance then
                    World.remove_projectile(world, slot)
                    explode(world, content, cfg, next_x, next_y)
                else
                    World.write_projectile(world, slot, next_x, next_y, remaining - distance)
                end
            end
        end
    end

    -- 完成投掷时消耗已预留的原生槽位，方向只读取服务端已经校验的瞄准输入。
    function api.launch(world, content, player, cfg_id)
        local cfg = content.tools[cfg_id]
        local shape = movement.shape(player, content)
        local x = Player.get_aim_x(player.controls)
        local y = Player.get_aim_y(player.controls)
        local length = math.sqrt(x * x + y * y)
        local vx = integer.create(math.floor(x / length * cfg.speed + 0.5))
        local vy = integer.create(math.floor(y / length * cfg.speed + 0.5))
        World.spawn_projectile(world, cfg_id, Entity.get_x(player.pose),
            Entity.get_y(player.pose) + shape.height // 2, vx, vy, cfg.throw_range)
    end

    return api
end

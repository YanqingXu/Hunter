-- 用固定巡逻与近距离追击驱动普通怪，不含随机、跳跃或寻路状态。
return function(deps)
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local world_api = deps["game.world"]
    local api = {}

    -- 巡逻在端点前缩短步长；区外角色以正常速度逐步返回最近端点。
    local function patrol_move(enemy, spawn, speed)
        if enemy.x < spawn.patrol_min then
            return 1, math.min(speed, spawn.patrol_min - enemy.x)
        elseif enemy.x > spawn.patrol_max then
            return -1, math.min(speed, enemy.x - spawn.patrol_max)
        end
        local direction = enemy.facing
        if enemy.x == spawn.patrol_min then
            direction = 1
        elseif enemy.x == spawn.patrol_max then
            direction = -1
        end
        local distance = direction > 0 and spawn.patrol_max - enemy.x
            or enemy.x - spawn.patrol_min
        return direction, math.min(speed, distance)
    end

    -- 判断近战距离、竖直相交和墙体遮挡是否同时满足。
    local function can_attack(enemy, player, content)
        return math.abs(player.x - enemy.x) <= content.enemy.attack_range
            and player.y < enemy.y + content.enemy.height
            and player.y + content.player.height > enemy.y
            and combat.visible(content, enemy.x, enemy.y + content.enemy.height // 2,
                player.x, player.y + content.player.height // 2)
    end

    -- 在稳定实体顺序下决定方向并移动，不提前执行攻击。
    function api.move(world, content)
        local player = world.entities[1]
        for i = 2, #world.entities do
            local enemy = world.entities[i]
            if enemy.alive then
                local spawn = content.map.enemies[i - 1]
                local direction = enemy.facing
                local distance = content.enemy.speed
                if enemy.attack_ticks > 0 then
                    enemy.attack_ticks = enemy.attack_ticks - 1
                end
                if can_attack(enemy, player, content) then
                    enemy.ai = "attack"
                    enemy.facing = player.x >= enemy.x and 1 or -1
                    direction = 0
                elseif math.abs(player.x - enemy.x) <= content.enemy.detect_range
                    and math.abs(player.y - enemy.y) <= content.enemy.detect_range then
                    enemy.ai = "chase"
                    direction = player.x >= enemy.x and 1 or -1
                else
                    enemy.ai = "patrol"
                    direction, distance = patrol_move(enemy, spawn, content.enemy.speed)
                end
                if direction ~= 0 and enemy.grounded
                    and not movement.supported(enemy, content.enemy, content.map,
                        direction, distance) then
                    direction = 0
                    enemy.facing = -enemy.facing
                end
                movement.step(enemy, content.enemy, content, direction, false, distance)
                if direction ~= 0 and enemy.vx == 0 then
                    enemy.facing = -direction
                end
            end
        end
    end

    -- 玩家射击之后才执行仍然存活的普通怪近战。
    function api.attack(world, content)
        local player = world.entities[1]
        for i = 2, #world.entities do
            local enemy = world.entities[i]
            if player.alive and enemy.alive and enemy.attack_ticks == 0
                and can_attack(enemy, player, content) then
                enemy.ai = "attack"
                enemy.attack_ticks = content.enemy.attack_ticks
                world_api.damage(world, enemy, player, content.enemy.damage)
            end
        end
    end

    return api
end

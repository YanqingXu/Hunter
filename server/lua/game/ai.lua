-- 用固定巡逻与近距离追击驱动普通怪，不含随机、跳跃或寻路状态。
return function(deps)
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local world_api = deps["game.world"]
    local monster_api = deps["game.monster"]
    local damage = deps["game.damage"]
    local api = {}

    -- 巡逻在端点前缩短步长；区外角色以正常速度逐步返回最近端点。
    local function patrol_move(enemy, spawn, speed)
        if enemy.pose.x < spawn.patrol_min then
            return 1, math.min(speed, spawn.patrol_min - enemy.pose.x)
        elseif enemy.pose.x > spawn.patrol_max then
            return -1, math.min(speed, enemy.pose.x - spawn.patrol_max)
        end
        local direction = enemy.pose.facing
        if enemy.pose.x == spawn.patrol_min then
            direction = 1
        elseif enemy.pose.x == spawn.patrol_max then
            direction = -1
        end
        local distance = direction > 0 and spawn.patrol_max - enemy.pose.x
            or enemy.pose.x - spawn.patrol_min
        return direction, math.min(speed, distance)
    end

    -- 判断近战距离、竖直相交和墙体遮挡是否同时满足。
    local function can_attack(enemy, player, content)
        local cfg = content.monsters[enemy.cfg_id]
        local player_cfg = content.players[player.cfg_id]
        return math.abs(player.pose.x - enemy.pose.x) <= cfg.attack_range
            and player.pose.y < enemy.pose.y + cfg.height
            and player.pose.y + player_cfg.height > enemy.pose.y
            and combat.visible(content, enemy.pose.x, enemy.pose.y + cfg.height // 2,
                player.pose.x, player.pose.y + player_cfg.height // 2)
    end

    -- 在稳定实体顺序下决定方向并移动，不提前执行攻击。
    function api.move(world, content)
        local player = world_api.find(world, world.player_entity_id)
        for _, id in ipairs(world.entity_ids) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and enemy.health.alive then
                local cfg = content.monsters[enemy.cfg_id]
                local spawn = monster_api.spawn_cfg(content, enemy.spawn_id)
                local direction = enemy.pose.facing
                local distance = cfg.speed
                if enemy.ai.attack_ticks > 0 then
                    enemy.ai.attack_ticks = enemy.ai.attack_ticks - 1
                end
                if can_attack(enemy, player, content) then
                    enemy.ai.state = "attack"
                    enemy.pose.facing = player.pose.x >= enemy.pose.x and 1 or -1
                    direction = 0
                elseif math.abs(player.pose.x - enemy.pose.x) <= cfg.detect_range
                    and math.abs(player.pose.y - enemy.pose.y) <= cfg.detect_range then
                    enemy.ai.state = "chase"
                    direction = player.pose.x >= enemy.pose.x and 1 or -1
                else
                    enemy.ai.state = "patrol"
                    direction, distance = patrol_move(enemy, spawn, cfg.speed)
                end
                if direction ~= 0 and enemy.motion.grounded
                    and not movement.supported(enemy, cfg, content.map,
                        direction, distance) then
                    direction = 0
                    enemy.pose.facing = -enemy.pose.facing
                end
                movement.step(enemy, cfg, content, direction, false, distance)
                if direction ~= 0 and enemy.motion.vx == 0 then
                    enemy.pose.facing = -direction
                end
            end
        end
    end

    -- 玩家射击之后才执行仍然存活的普通怪近战。
    function api.attack(world, content)
        local player = world_api.find(world, world.player_entity_id)
        for _, id in ipairs(world.entity_ids) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and player.health.alive
                and enemy.health.alive and enemy.ai.attack_ticks == 0
                and can_attack(enemy, player, content) then
                local cfg = content.monsters[enemy.cfg_id]
                enemy.ai.state = "attack"
                enemy.ai.attack_ticks = cfg.attack_ticks
                damage.apply(world, enemy, player, cfg.damage)
            end
        end
    end

    return api
end

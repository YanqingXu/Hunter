-- Lua 决定巡逻、追击和近战规则，以批量读取的局部标量减少原生调用。
return function(deps)
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local world_api = deps["game.world"]
    local monster_api = deps["game.monster"]
    local damage = deps["game.damage"]
    local api = {}

    -- 巡逻在端点前缩短步长，区外对象逐步返回巡逻区间。
    local function patrol_move(x, facing, spawn, speed)
        if x < spawn.patrol_min then
            return 1, math.min(speed, spawn.patrol_min - x)
        elseif x > spawn.patrol_max then
            return -1, math.min(speed, x - spawn.patrol_max)
        end
        local direction = facing
        if x == spawn.patrol_min then
            direction = 1
        elseif x == spawn.patrol_max then
            direction = -1
        end
        local distance = direction > 0 and spawn.patrol_max - x or x - spawn.patrol_min
        return direction, math.min(speed, distance)
    end

    -- 纯标量判定近战距离、竖直相交与墙体遮挡。
    local function can_attack(x, y, px, py, cfg, player_cfg, content)
        return math.abs(px - x) <= cfg.attack_range and py < y + cfg.height
            and py + player_cfg.height > y
            and combat.visible(content, x, y + cfg.height // 2,
                px, py + player_cfg.height // 2)
    end

    -- 按稳定实体顺序移动，局部计算结果立即写回原生对象。
    function api.move(world, content)
        local player = world_api.target(world, "monster")
        local px, py = Unit.read_motion(player.motion)
        local player_alive = Unit.get_alive(player.health)
        local player_cfg = content.players[player.cfg_id]
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" then
                if Monster.get_state(enemy.ai) == "spawn" then
                    monster_api.activate(enemy, content)
                end
                local x, y, vx, vy, grounded, facing, alive = Unit.read_motion(enemy.motion)
                if alive then
                    local cfg = content.monsters[enemy.cfg_id]
                    local spawn = monster_api.spawn_cfg(content, enemy.spawn_id)
                    local direction = facing
                    local distance = cfg.speed
                    local attack_ticks = Monster.get_attack_ticks(enemy.ai)
                    if attack_ticks > 0 then
                        Monster.set_attack_ticks(enemy.ai, attack_ticks - 1)
                    end
                    local state = "patrol"
                    local windup = cfg.windup or 0
                    local recover = cfg.recover or 0
                    local remaining = Monster.get_attack_ticks(enemy.ai)
                    if windup > 0 and remaining > cfg.attack_ticks - windup - recover then
                        state = remaining >= cfg.attack_ticks - windup and "windup" or "recover"
                        direction = 0
                    elseif player_alive and can_attack(x, y, px, py, cfg, player_cfg, content) then
                        state = "attack"
                        facing = px >= x and 1 or -1
                        direction = 0
                    elseif player_alive and math.abs(px - x) <= cfg.detect_range
                        and math.abs(py - y) <= cfg.detect_range then
                        state = "chase"
                        direction = px == x and 0 or (px > x and 1 or -1)
                        distance = math.min(cfg.speed, math.abs(px - x))
                    else
                        direction, distance = patrol_move(x, facing, spawn, cfg.speed)
                    end
                    Monster.set_state(enemy.ai, state)
                    if direction ~= 0 and grounded and not movement.supported(enemy, cfg,
                        content.map, direction, distance) then
                        facing = -direction
                        direction = 0
                    end
                    Entity.set_facing(enemy.pose, facing)
                    local moved = movement.step(enemy, cfg, content, direction, false, distance)
                    if direction ~= 0 and moved == 0 then
                        Entity.set_facing(enemy.pose, -direction)
                    elseif state == "patrol" and moved ~= 0 then
                        if x + moved == spawn.patrol_min then
                            Entity.set_facing(enemy.pose, 1)
                        elseif x + moved == spawn.patrol_max then
                            Entity.set_facing(enemy.pose, -1)
                        end
                    end
                end
            end
        end
    end

    -- 玩家射击之后才执行仍然存活的怪物近战，玩家死亡后停止后续攻击。
    function api.attack(world, content)
        local player = world_api.target(world, "monster")
        if not Unit.get_alive(player.health) then
            return
        end
        local px, py = Unit.read_motion(player.motion)
        local player_cfg = content.players[player.cfg_id]
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" then
                local x, y, vx, vy, grounded, facing, alive = Unit.read_motion(enemy.motion)
                if alive then
                    local cfg = content.monsters[enemy.cfg_id]
                    local remaining = Monster.get_attack_ticks(enemy.ai)
                    local windup = cfg.windup or 0
                    local hit = false
                    if remaining == 0 and can_attack(x, y, px, py, cfg, player_cfg, content) then
                        Monster.set_state(enemy.ai, windup > 0 and "windup" or "attack")
                        Monster.set_attack_ticks(enemy.ai, cfg.attack_ticks)
                        hit = windup == 0
                    elseif windup > 0 and remaining == cfg.attack_ticks - windup then
                        Monster.set_state(enemy.ai, "recover")
                        hit = can_attack(x, y, px, py, cfg, player_cfg, content)
                    end

                    if hit then
                        damage.apply(world, enemy, player, cfg.damage)
                        if not Unit.get_alive(player.health) then
                            return
                        end
                    end
                end
            end
        end
    end

    return api
end

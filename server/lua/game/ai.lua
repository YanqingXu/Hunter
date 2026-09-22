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
        local player = world_api.find(world, World.get_player_entity_id(world))
        local px, py = Unit.read_motion(player.motion)
        local player_cfg = content.players[player.cfg_id]
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" then
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
                    if can_attack(x, y, px, py, cfg, player_cfg, content) then
                        state = "attack"
                        facing = px >= x and 1 or -1
                        direction = 0
                    elseif math.abs(px - x) <= cfg.detect_range
                        and math.abs(py - y) <= cfg.detect_range then
                        state = "chase"
                        direction = px >= x and 1 or -1
                    else
                        direction, distance = patrol_move(x, facing, spawn, cfg.speed)
                    end
                    Monster.set_state(enemy.ai, state)
                    if direction ~= 0 and grounded and not movement.supported(enemy, cfg,
                        content.map, direction, distance) then
                        direction = 0
                        facing = -facing
                    end
                    Entity.set_facing(enemy.pose, facing)
                    local moved = movement.step(enemy, cfg, content, direction, false, distance)
                    if direction ~= 0 and moved == 0 then
                        Entity.set_facing(enemy.pose, -direction)
                    end
                end
            end
        end
    end

    -- 玩家射击之后才执行仍然存活的怪物近战，玩家死亡后停止后续攻击。
    function api.attack(world, content)
        local player = world_api.find(world, World.get_player_entity_id(world))
        if not Unit.get_alive(player.health) then
            return
        end
        local px, py = Unit.read_motion(player.motion)
        local player_cfg = content.players[player.cfg_id]
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" then
                local x, y, vx, vy, grounded, facing, alive = Unit.read_motion(enemy.motion)
                if alive and Monster.get_attack_ticks(enemy.ai) == 0 then
                    local cfg = content.monsters[enemy.cfg_id]
                    if can_attack(x, y, px, py, cfg, player_cfg, content) then
                        Monster.set_state(enemy.ai, "attack")
                        Monster.set_attack_ticks(enemy.ai, cfg.attack_ticks)
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

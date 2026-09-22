-- 推进玩家枪械状态，按射线命中结果调用统一伤害，不持有配置或状态副本。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local api = {}

    -- 扣弹并沿当前瞄准方向命中最近目标；同距时墙优先、实体按稳定顺序。
    local function shoot(world, content)
        local player = world_api.find(world, world.player_entity_id)
        local gun = player.weapon
        local cfg = content.weapons[gun.cfg_id]
        local input = player.controls
        local length = math.sqrt(input.aim_x * input.aim_x + input.aim_y * input.aim_y)
        local dx = input.aim_x / length
        local dy = input.aim_y / length
        local x = player.pose.x
        local y = player.pose.y + content.players[player.cfg_id].height // 2
        local nearest = cfg.range
        local target = nil
        local blocked = false
        for _, solid in ipairs(content.map.solids) do
            local distance = combat.ray(x, y, dx, dy, nearest, solid.x, solid.y, solid.w, solid.h)
            if distance ~= nil then
                nearest = distance
                blocked = true
            end
        end
        for _, id in ipairs(world.entity_ids) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and enemy.health.alive then
                local shape = content.monsters[enemy.cfg_id]
                local distance = combat.ray(x, y, dx, dy, nearest,
                    enemy.pose.x - shape.width // 2, enemy.pose.y,
                    shape.width, shape.height)
                if distance ~= nil and (distance < nearest or (distance == nearest
                    and not blocked and (target == nil or state.newer(target.id, enemy.id)))) then
                    target = enemy
                    nearest = distance
                    blocked = false
                end
            end
        end
        gun.ammo = gun.ammo - 1
        gun.shot_ticks = cfg.fire_ticks
        if input.aim_x ~= 0 then
            player.pose.facing = input.aim_x > 0 and 1 or -1
        end
        local hit_x = integer.create(math.floor(x + dx * nearest + 0.5))
        local hit_y = integer.create(math.floor(y + dy * nearest + 0.5))
        world_api.emit(world, "shot", player.id, target and target.id or "0", hit_x, hit_y, 0)
        if target ~= nil then
            damage.apply(world, player, target, cfg.damage)
        end
    end

    -- 先完成旧冷却和换弹，再处理当前换弹与射击意图。
    function api.step(world, content)
        local player = world_api.find(world, world.player_entity_id)
        local gun = player.weapon
        local cfg = content.weapons[gun.cfg_id]
        if not player.health.alive then
            return
        end
        if gun.shot_ticks > 0 then
            gun.shot_ticks = gun.shot_ticks - 1
        end
        if gun.reload_ticks > 0 then
            gun.reload_ticks = gun.reload_ticks - 1
            if gun.reload_ticks == 0 then
                local amount = math.min(cfg.magazine - gun.ammo, player.reserve)
                gun.ammo = gun.ammo + amount
                player.reserve = player.reserve - amount
                world_api.emit(world, "reload", player.id, "0",
                    player.pose.x, player.pose.y, amount)
            end
        end
        if player.controls.reload and gun.reload_ticks == 0 and player.reserve > 0
            and gun.ammo < cfg.magazine then
            gun.reload_ticks = cfg.reload_ticks
            world_api.emit(world, "reload", player.id, "0", player.pose.x, player.pose.y, 0)
        end
        if (player.controls.fire or player.controls.fire_once) and gun.reload_ticks == 0
            and gun.shot_ticks == 0 and gun.ammo > 0 then
            shoot(world, content)
        end
    end

    return api
end

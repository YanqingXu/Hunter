-- 推进玩家枪械状态，按射线命中结果调用统一伤害，不持有配置或状态副本。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local api = {}

    -- 扣弹并沿当前瞄准方向命中最近目标；同距时墙优先、实体按稳定顺序。
    local function shoot(world, content, player)
        local gun = player.weapon
        local cfg = content.weapons[Weapon.get_cfg_id(gun)]
        local input = player.controls
        local aim_x = Player.get_aim_x(input)
        local aim_y = Player.get_aim_y(input)
        local length = math.sqrt(aim_x * aim_x + aim_y * aim_y)
        local dx = aim_x / length
        local dy = aim_y / length
        local x = Entity.get_x(player.pose)
        local y = Entity.get_y(player.pose) + content.players[player.cfg_id].height // 2
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
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and Unit.get_alive(enemy.health) then
                local shape = content.monsters[enemy.cfg_id]
                local distance = combat.ray(x, y, dx, dy, nearest,
                    Entity.get_x(enemy.pose) - shape.width // 2, Entity.get_y(enemy.pose),
                    shape.width, shape.height)
                if distance ~= nil and (distance < nearest or (distance == nearest
                    and not blocked and (target == nil or state.newer(target.id, enemy.id)))) then
                    target = enemy
                    nearest = distance
                    blocked = false
                end
            end
        end
        Weapon.set_ammo(gun, Weapon.get_ammo(gun) - 1)
        Weapon.set_shot_ticks(gun, cfg.fire_ticks)
        if aim_x ~= 0 then
            Entity.set_facing(player.pose, aim_x > 0 and 1 or -1)
        end
        local hit_x = integer.create(math.floor(x + dx * nearest + 0.5))
        local hit_y = integer.create(math.floor(y + dy * nearest + 0.5))
        world_api.emit(world, "shot", player.id, target and target.id or "0", hit_x, hit_y, 0)
        if target ~= nil then
            damage.apply(world, player, target, cfg.damage)
        end
    end

    -- 先完成旧冷却和换弹，再处理当前换弹与射击意图。
    function api.step(world, content, player)
        player = player or world_api.find(world, World.find_player(world, World.get_player_id(world)))
        local gun = player.weapon
        local cfg = content.weapons[Weapon.get_cfg_id(gun)]
        if not Unit.get_alive(player.health) then
            return
        end
        if Weapon.get_shot_ticks(gun) > 0 then
            Weapon.set_shot_ticks(gun, Weapon.get_shot_ticks(gun) - 1)
        end
        if Weapon.get_reload_ticks(gun) > 0 then
            Weapon.set_reload_ticks(gun, Weapon.get_reload_ticks(gun) - 1)
            if Weapon.get_reload_ticks(gun) == 0 then
                local amount = math.min(cfg.magazine - Weapon.get_ammo(gun),
                    Player.get_reserve(player.controls))
                Weapon.set_ammo(gun, Weapon.get_ammo(gun) + amount)
                Player.set_reserve(player.controls, Player.get_reserve(player.controls) - amount)
                world_api.emit(world, "reload", player.id, "0",
                    Entity.get_x(player.pose), Entity.get_y(player.pose), amount)
            end
        end
        if Player.get_reload(player.controls) and Weapon.get_reload_ticks(gun) == 0
            and Player.get_reserve(player.controls) > 0 and Weapon.get_ammo(gun) < cfg.magazine then
            Weapon.set_reload_ticks(gun, cfg.reload_ticks)
            world_api.emit(world, "reload", player.id, "0",
                Entity.get_x(player.pose), Entity.get_y(player.pose), 0)
        end
        if (Player.get_fire(player.controls) or Player.get_fire_once(player.controls))
            and Weapon.get_reload_ticks(gun) == 0 and Weapon.get_shot_ticks(gun) == 0
            and Weapon.get_ammo(gun) > 0 then
            shoot(world, content, player)
        end
    end

    return api
end

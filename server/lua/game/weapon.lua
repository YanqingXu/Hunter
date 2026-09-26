-- 推进玩家枪械状态，按射线命中结果调用统一伤害，不持有配置或状态副本。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local movement = deps["game.movement"]
    local api = {}

    -- 匍匐只能朝角色当前局部前上方瞄准，负的世界横坐标方向仍可对应左朝向。
    local function aimed(player)
        local x = Player.get_aim_x(player.controls)
        local y = Player.get_aim_y(player.controls)
        return not Player.get_prone(player.controls)
            or (x * Entity.get_facing(player.pose) >= 0 and y >= 0)
    end

    -- 将交点按距离、实心物优先和实体身份插入确定顺序，避免遍历顺序影响穿透。
    local function insert_hit(hits, value)
        local index = #hits + 1
        while index > 1 do
            local prior = hits[index - 1]
            local before = value.distance < prior.distance
                or (value.distance == prior.distance and value.solid and not prior.solid)
                or (value.distance == prior.distance and value.solid == prior.solid
                    and state.newer(prior.id, value.id))
            if not before then
                break
            end
            hits[index] = prior
            index = index - 1
        end
        hits[index] = value
    end

    -- 收集一颗弹丸的独立射线交点，掩体只来自场景清单的一份几何。
    local function intersections(world, content, x, y, dx, dy, range)
        local hits = {}
        for _, solid in ipairs(movement.solids(content)) do
            local at = combat.ray(x, y, dx, dy, range, solid.x, solid.y, solid.w, solid.h)
            if at ~= nil then
                insert_hit(hits, {distance = at, id = solid.id, solid = true,
                    penetrable = solid.penetrable == true})
            end
        end
        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and Unit.get_alive(enemy.health) then
                local shape = movement.shape(enemy, content)
                local at = combat.ray(x, y, dx, dy, range,
                    Entity.get_x(enemy.pose) - shape.width // 2, Entity.get_y(enemy.pose),
                    shape.width, shape.height)
                if at ~= nil then
                    insert_hit(hits, {distance = at, id = id, solid = false,
                        enemy = enemy, boss = (shape.rank or 0) >= 3})
                end
            end
        end
        return hits
    end

    -- 每颗弹丸独立伤害和穿透，掩体衰减伤害但不消费穿怪次数。
    local function pellet(world, content, player, ammo, x, y, dx, dy, range)
        local amount = ammo.damage
        local passes = ammo.penetration
        local last = range
        local target = "0"
        for _, hit in ipairs(intersections(world, content, x, y, dx, dy, range)) do
            last = hit.distance
            if hit.solid then
                if not hit.penetrable then
                    break
                end
            else
                target = hit.id
                damage.apply(world, player, hit.enemy, amount)
                if hit.boss or passes == 0 then
                    break
                end
                passes = passes - 1
            end
            amount = math.max(0, amount - ammo.loss)
            if amount == 0 then
                break
            end
            last = range
        end
        local hit_x = integer.create(math.floor(x + dx * last + 0.5))
        local hit_y = integer.create(math.floor(y + dy * last + 0.5))
        world_api.emit(world, "shot", player.id, target, hit_x, hit_y, 0)
    end

    -- 一次射击扣一发弹药，固定对称散布保证多弹丸结果可复现。
    local function fire(world, content, player, slot, cfg)
        if not aimed(player) then
            return
        end
        local input = player.controls
        local ammo = content.ammo[cfg.ammo_cfg_id]
        local aim_x = Player.get_aim_x(input)
        local aim_y = Player.get_aim_y(input)
        local length = math.sqrt(aim_x * aim_x + aim_y * aim_y)
        local dx = aim_x / length
        local dy = aim_y / length
        local shape = movement.shape(player, content)
        local x = Entity.get_x(player.pose)
        local y = Entity.get_y(player.pose) + shape.height // 2
        Player.set_weapon_ammo(input, slot, Player.get_weapon_ammo(input, slot) - 1)
        Player.set_weapon_shot_ticks(input, slot, cfg.fire_ticks)
        if aim_x ~= 0 then
            Entity.set_facing(player.pose, aim_x > 0 and 1 or -1)
        end
        for index = 1, ammo.pellets do
            local angle = ammo.pellets == 1 and 0
                or (-ammo.spread_deg / 2 + (index - 1) * ammo.spread_deg / (ammo.pellets - 1))
            local radians = angle * math.pi / 180
            local cosine = math.cos(radians)
            local sine = math.sin(radians)
            pellet(world, content, player, ammo, x, y,
                dx * cosine - dy * sine, dx * sine + dy * cosine, cfg.range)
        end
    end

    -- 推进所有枪的冷却，只为当前枪逐轮装填，完成一轮时原子转移弹药。
    local function demo_step(world, content, player)
        local input = player.controls
        local slot = Player.get_active_weapon(input)
        local cfg = content.weapons[Player.get_weapon_cfg(input, slot)]
        for index = 1, Player.get_weapon_count(input) do
            local ticks = Player.get_weapon_shot_ticks(input, index)
            if ticks > 0 then
                Player.set_weapon_shot_ticks(input, index, ticks - 1)
            end
        end
        if Player.get_melee_ticks(input) > 0 then
            Player.set_melee_ticks(input, Player.get_melee_ticks(input) - 1)
        end
        if Player.get_ladder_id(input) ~= 0 or Player.get_selected_slot(input) ~= 0
            or Player.get_use_slot(input) ~= 0 or Player.get_melee(input) then
            return
        end
        local ticks = Player.get_weapon_reload_ticks(input, slot)
        local reloading = ticks > 0
        if ticks > 0 then
            ticks = ticks - 1
            Player.set_weapon_reload_ticks(input, slot, ticks)
            if ticks == 0 then
                local count = Player.get_weapon_ammo(input, slot)
                local reserve = Player.get_weapon_reserve(input, slot)
                local amount = cfg.reload_kind == 2 and count == 0 and cfg.magazine or 1
                amount = math.min(amount, math.min(cfg.magazine - count, reserve))
                Player.set_weapon_ammo(input, slot, count + amount)
                Player.set_weapon_reserve(input, slot, reserve - amount)
                world_api.emit(world, "reload", player.id, "0",
                    Entity.get_x(player.pose), Entity.get_y(player.pose), amount)
                if count + amount < cfg.magazine and reserve > amount then
                    Player.set_weapon_reload_ticks(input, slot, cfg.reload_ticks)
                end
            end
        end
        if Player.get_reload(input) and not reloading
            and Player.get_weapon_ammo(input, slot) < cfg.magazine
            and Player.get_weapon_reserve(input, slot) > 0 then
            Player.set_weapon_reload_ticks(input, slot, cfg.reload_ticks)
            reloading = true
            world_api.emit(world, "reload", player.id, "0",
                Entity.get_x(player.pose), Entity.get_y(player.pose), 0)
        end
        if not reloading and Player.get_weapon_reload_ticks(input, slot) == 0
            and Player.get_weapon_shot_ticks(input, slot) == 0
            and (Player.get_fire(input) or Player.get_fire_once(input))
            and Player.get_weapon_ammo(input, slot) > 0 then
            fire(world, content, player, slot, cfg)
        end
    end

    -- 枪托和匕首共用一次近战，成功起手扣体力，空挥也进入冷却。
    function api.melee(world, content, player)
        if not Player.get_melee(player.controls) then
            return false
        end
        local input = player.controls
        Player.set_melee(input, false)
        if not Unit.get_alive(player.health) or Player.get_ladder_id(input) ~= 0
            or Player.get_use_slot(input) ~= 0 or Player.get_melee_ticks(input) > 0
            or Player.get_stamina(input) < content.rules.melee_stamina then
            return false
        end
        local selected = Player.get_selected_slot(input)
        local amount = 0
        local range = 0
        if selected == 0 then
            local cfg_id = Player.get_weapon_cfg(input, Player.get_active_weapon(input))
            local cfg = content.weapons[cfg_id]
            amount = cfg.melee_damage
            range = cfg.melee_range
        else
            local cfg = content.tools[Player.get_tool_cfg(input, selected)]
            if cfg == nil or cfg.kind ~= "knife" then
                return false
            end
            amount = cfg.damage
            range = cfg.range
        end
        Player.set_stamina(input, Player.get_stamina(input) - content.rules.melee_stamina)
        Player.set_stamina_delay(input, content.players[player.cfg_id].stamina_delay)
        Player.set_stamina_rem(input, 0)
        Player.set_melee_ticks(input, content.rules.melee_ticks)
        local shape = movement.shape(player, content)
        local x = Entity.get_x(player.pose)
        local y = Entity.get_y(player.pose) + shape.height // 2
        local facing = Entity.get_facing(player.pose)
        for _, hit in ipairs(intersections(world, content, x, y, facing, 0, range)) do
            if not hit.solid then
                damage.apply(world, player, hit.enemy, amount)
            end
            break
        end
        world_api.emit(world, "melee", player.id, "0", x, y, 0)
        return true
    end

    -- 只在存活时推进枪械，玩家动作和弹药规则统一使用第四版内容。
    function api.step(world, content, player)
        if Unit.get_alive(player.health) then
            demo_step(world, content, player)
        end
    end

    return api
end

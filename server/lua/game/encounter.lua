-- 编排警戒、Boss 阈值和召唤退场，波次拾取通过原生事务一次发布。
return function(deps)
    local world_api = deps["game.world"]
    local monster_api = deps["game.monster"]
    local movement = deps["game.movement"]
    local api = {}

    -- 第二波阶段按基础警戒范围乘一次系数，旧怪和新怪使用同一视图。
    function api.range(world, content, cfg)
        local range = cfg.detect_range
        if content.encounter and World.get_wave(world) == 2 then
            range = range * content.encounter.alert_scale_bp // 10000
        end
        return range
    end

    -- 警戒圈使用实体脚底坐标间的二维距离，边界属于圈内。
    function api.inside(world, content, enemy, player)
        local x, y = Unit.read_motion(enemy.motion)
        local px, py = Unit.read_motion(player.motion)
        local range = api.range(world, content, content.monsters[enemy.cfg_id])
        local dx, dy = px - x, py - y
        return dx * dx + dy * dy <= range * range
    end

    -- 进入范围立即警戒，离开后保持完整延迟，再恢复非警戒行为。
    function api.sense(world, content, enemy, player, targetable)
        local cfg = content.monsters[enemy.cfg_id]
        local delay = cfg.ai and cfg.ai.disengage_ticks or 0
        if targetable and api.inside(world, content, enemy, player) then
            Monster.set_aware(enemy.ai, true)
            Monster.set_alert_ticks(enemy.ai, delay)
        elseif Monster.get_aware(enemy.ai) then
            local ticks = Monster.get_alert_ticks(enemy.ai)
            if ticks > 0 then
                ticks = ticks - 1
                Monster.set_alert_ticks(enemy.ai, ticks)
            end
            if ticks == 0 then
                Monster.set_aware(enemy.ai, false)
            end
        end
        local ticks = Monster.get_rage_ticks(enemy.ai)
        if ticks > 0 then
            Monster.set_rage_ticks(enemy.ai, ticks - 1)
        end
    end

    -- 玩家伤害全部结束后合并本 Tick 跨阈值，严格低于阈值且从未触发才生效。
    function api.rage(world, enemy, cfg)
        local hp = Unit.get_hp(enemy.health)
        local before = Monster.get_last_hp(enemy.ai)
        Monster.set_last_hp(enemy.ai, hp)
        if not cfg.ai or cfg.rank ~= 3 or not Unit.get_alive(enemy.health) then
            return nil
        end
        local mask = Monster.get_rage_mask(enemy.ai)
        local changed = false
        local bit = 1
        for _, threshold in ipairs(cfg.ai.rage_thresholds) do
            if before >= threshold and hp < threshold and (mask // bit) % 2 == 0 then
                mask = mask + bit
                changed = true
            end
            bit = bit * 2
        end
        if changed then
            Monster.set_rage_mask(enemy.ai, mask)
            Monster.set_rage_ticks(enemy.ai, cfg.ai.rage_ticks)
            Monster.set_aware(enemy.ai, true)
            Monster.set_alert_ticks(enemy.ai, cfg.ai.disengage_ticks)
            net.skill_event("rage", enemy.id, "0", Entity.get_x(enemy.pose),
                Entity.get_y(enemy.pose), cfg.ai.rage_ticks, cfg.ai.rage_ability_id,
                World.get_tick_id(world))
            return cfg.ai.rage_ability_id
        end
        return nil
    end

    -- 死亡、失去本体或持续离开双圈都退场，只有仍存活本体开始完整技能冷却。
    function api.step(world, content)
        local player = world_api.target(world, "monster")
        for _, id in ipairs(world_api.ids(world)) do
            local summon = world_api.find(world, id)
            if summon ~= nil and summon.kind == "monster" and summon.owner_id ~= "0" then
                local owner = world_api.find(world, summon.owner_id)
                local active = owner ~= nil and Unit.get_alive(owner.health)
                local remove = not active or not Unit.get_alive(summon.health)
                local ability = nil
                if active then
                    local cfg = content.monsters[owner.cfg_id]
                    local slot = Monster.get_owner_ability(summon.ai)
                    ability = content.abilities[cfg.ai.abilities[slot].cfg_id]
                    if not remove then
                        local outside = not api.inside(world, content, owner, player)
                            and not api.inside(world, content, summon, player)
                        local ticks = outside and Monster.get_outside_ticks(summon.ai) + 1 or 0
                        Monster.set_outside_ticks(summon.ai, ticks)
                        remove = ticks >= ability.despawn_ticks
                    end
                    if remove then
                        Monster.set_ability_cd(owner.ai, slot, ability.cooldown)
                    end
                end
                if remove then
                    local cfg_id = "0"
                    if owner ~= nil then
                        local cfg = content.monsters[owner.cfg_id]
                        cfg_id = cfg.ai.abilities[Monster.get_owner_ability(summon.ai)].cfg_id
                    end
                    net.skill_event("summon_end", summon.owner_id, id,
                        Entity.get_x(summon.pose), Entity.get_y(summon.pose), 0,
                        cfg_id, World.get_tick_id(world))
                    world_api.remove(world, id)
                end
            end
        end
    end

    -- 构造显式第二波模板，不改变第一波实体或可变配置。
    function api.wave_specs(content)
        local specs = json.array({})
        for _, id in ipairs(content.encounter.second_wave) do
            local spawn = monster_api.spawn_cfg(content, id)
            local cfg = content.monsters[spawn.cfg_id]
            local grounded = spawn.y == 0
            for _, solid in ipairs(movement.solids(content)) do
                grounded = grounded or (spawn.y == solid.y + solid.h
                    and spawn.x - cfg.width // 2 < solid.x + solid.w
                    and spawn.x + cfg.width // 2 > solid.x)
            end
            specs[#specs + 1] = {cfg_id = spawn.cfg_id, spawn_id = spawn.spawn_id,
                x = spawn.x, y = spawn.y, hp = cfg.hp, width = cfg.width,
                height = cfg.height, grounded = grounded}
        end
        return json.encode(specs)
    end

    return api
end

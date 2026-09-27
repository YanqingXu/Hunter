-- 按原生技能槽推进怪物近战与召唤，配置只读且所有跨 Tick 状态由 C++ 持有。
return function(deps)
    local world_api = deps["game.world"]
    local monster_api = deps["game.monster"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local api = {}

    -- 查找技能在本怪物的稳定槽位，槽号与冷却数组保持一致。
    local function slot_for(cfg, id)
        for slot, entry in ipairs(cfg.ai.abilities) do
            if entry.cfg_id == id then
                return slot
            end
        end
        return nil
    end

    -- 召唤者同一时间只拥有一个尚未移除的召唤物。
    function api.has_summon(world, owner)
        for _, id in ipairs(world_api.ids(world)) do
            local value = world_api.find(world, id)
            if value ~= nil and value.kind == "monster" and value.owner_id == owner then
                return true
            end
        end
        return false
    end

    -- 近战和施法起手共用距离、竖直相交与实心遮挡判定。
    local function reachable(content, enemy, player, range)
        local cfg = movement.shape(enemy, content)
        local target = movement.shape(player, content)
        local x, y = Unit.read_motion(enemy.motion)
        local px, py = Unit.read_motion(player.motion)
        return math.abs(px - x) <= range and py < y + cfg.height
            and py + target.height > y
            and combat.visible(content, x, y + cfg.height // 2,
                px, py + target.height // 2)
    end

    -- 在本体两侧依次寻找可站立位置，缺少完整支撑时拒绝生成。
    local function summon_spec(content, enemy, cfg_id)
        local cfg = content.monsters[cfg_id]
        local owner = content.monsters[enemy.cfg_id]
        local ex, y = Unit.read_motion(enemy.motion)
        local facing = Entity.get_facing(enemy.pose)
        for _, direction in ipairs({facing, -facing}) do
            local x = ex + direction * ((owner.width + cfg.width) // 2)
            local supported = y == 0
            for _, solid in ipairs(movement.solids(content)) do
                supported = supported or (solid.y + solid.h == y
                    and solid.x <= x - cfg.width // 2
                    and solid.x + solid.w >= x + cfg.width // 2)
            end
            if supported and movement.fits(x, y, cfg.width, cfg.height, content) then
                return {cfg_id = cfg_id, spawn_id = enemy.spawn_id, x = x, y = y,
                    hp = cfg.hp, width = cfg.width, height = cfg.height, grounded = true}
            end
        end
        return nil
    end

    -- 起手与释放分别判定，释放失败不伪造伤害或制造第二个召唤物。
    local function release(world, content, enemy, player, cfg_id, slot)
        local cfg = content.abilities[cfg_id]
        if cfg.kind == "melee" then
            if Unit.get_alive(player.health) and reachable(content, enemy, player, cfg.range) then
                damage.apply(world, enemy, player, cfg.damage)
            end
        elseif not api.has_summon(world, enemy.id) then
            local spec = summon_spec(content, enemy, cfg.summon_cfg_id)
            if spec ~= nil then
                local id = World.spawn_summon(world, enemy.id, slot, json.encode(spec))
                if string.sub(id, 1, 1) ~= ":" then
                    local summon = world_api.find(world, id)
                    monster_api.activate(summon, content)
                    net.skill_event("summon", enemy.id, id, spec.x, spec.y, 0,
                        cfg_id, World.get_tick_id(world))
                end
            end
        end
        if cfg.recover > 0 then
            Monster.write_ability(enemy.ai, cfg_id, "recover", cfg.recover)
            Monster.set_state(enemy.ai, "recover")
        else
            Monster.write_ability(enemy.ai, "0", "idle", 0)
        end
    end

    -- 所有技能冷却在非所属阶段仍推进，召唤冷却由召唤物退场时启动。
    function api.tick(enemy, cfg)
        for slot, _ in ipairs(cfg.ai.abilities) do
            local ticks = Monster.get_ability_cd(enemy.ai, slot)
            if ticks > 0 then
                Monster.set_ability_cd(enemy.ai, slot, ticks - 1)
            end
        end
    end

    -- 开始一个确定技能，强制狂暴可以替换未释放动作但不撤销既有攻击。
    local function start(world, content, enemy, player, id, slot)
        local cfg = content.abilities[id]
        if cfg.kind == "summon" and api.has_summon(world, enemy.id) then
            return false
        end
        if cfg.kind ~= "summon" then
            Monster.set_ability_cd(enemy.ai, slot, cfg.cooldown)
        end
        Monster.set_attack_ticks(enemy.ai, 0)
        net.skill_event("ability", enemy.id, player.id, Entity.get_x(enemy.pose),
            Entity.get_y(enemy.pose), 0, id, World.get_tick_id(world))
        if cfg.windup > 0 then
            Monster.write_ability(enemy.ai, id, "windup", cfg.windup)
            Monster.set_state(enemy.ai, "windup")
        else
            release(world, content, enemy, player, id, slot)
        end
        return true
    end

    -- 完成旧动作或选择当前组内优先级最高的可用技能，每 Tick 最多新起手一次。
    function api.step(world, content, enemy, player, forced, targetable)
        local cfg = content.monsters[enemy.cfg_id]
        if forced ~= nil and forced ~= "0" and targetable then
            local slot = slot_for(cfg, forced)
            assert(slot ~= nil, "unbound rage ability")
            if start(world, content, enemy, player, forced, slot) then
                return true
            end
        end
        local phase = Monster.get_ability_phase(enemy.ai)
        if phase ~= "idle" then
            local id = Monster.get_active_ability(enemy.ai)
            local ticks = Monster.get_ability_ticks(enemy.ai) - 1
            if ticks > 0 then
                Monster.write_ability(enemy.ai, id, phase, ticks)
            elseif phase == "windup" then
                release(world, content, enemy, player, id, slot_for(cfg, id))
            else
                Monster.write_ability(enemy.ai, "0", "idle", 0)
            end
            return true
        end
        if not targetable or not Monster.get_aware(enemy.ai) then
            return false
        end
        local group = Monster.get_rage_ticks(enemy.ai) > 0 and "rage" or "alert"
        local selected, priority = nil, nil
        for slot, entry in ipairs(cfg.ai.abilities) do
            local ability = content.abilities[entry.cfg_id]
            if entry.phase == group and Monster.get_ability_cd(enemy.ai, slot) == 0
                and reachable(content, enemy, player, ability.range)
                and (ability.kind ~= "summon" or not api.has_summon(world, enemy.id))
                and (priority == nil or entry.priority < priority
                    or (entry.priority == priority and tonumber(entry.cfg_id)
                        < tonumber(cfg.ai.abilities[selected].cfg_id))) then
                selected, priority = slot, entry.priority
            end
        end
        if selected ~= nil then
            return start(world, content, enemy, player,
                cfg.ai.abilities[selected].cfg_id, selected)
        end
        return false
    end

    return api
end

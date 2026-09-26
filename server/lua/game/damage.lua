-- 统一枪械与近战伤害、生死转换和事件输出，死亡保留实体身份及位置。
return function(deps)
    local world_api = deps["game.world"]
    local player_api = deps["game.player"]
    local api = {}

    -- 已经产生的攻击按来源身份伤害存活目标，不要求延迟攻击的来源仍存活。
    function api.hit(world, source_id, target, damage)
        assert(Entity.get_id(target.pose) == target.id, "stale identity")
        if world_api.find(world, target.id) == nil or not Unit.get_alive(target.health) then
            return
        end
        assert(type(damage) == "integer" and damage > 0, "invalid damage")
        local health = target.health
        local amount = math.min(damage, Unit.get_hp(health))
        Unit.set_hp(health, Unit.get_hp(health) - amount)
        World.hurt(world, target.id)
        world_api.emit(world, "hit", source_id, target.id,
            Entity.get_x(target.pose), Entity.get_y(target.pose), amount)
        if Unit.get_hp(health) == 0 then
            Unit.set_alive(health, false)
            Unit.set_vx(target.motion, 0)
            Unit.set_vy(target.motion, 0)
            if target.kind == "player" then
                Weapon.set_reload_ticks(target.weapon, 0)
                Weapon.set_shot_ticks(target.weapon, 0)
                Player.cancel_use(target.controls)
                for slot = 1, Player.get_weapon_count(target.controls) do
                    Player.set_weapon_reload_ticks(target.controls, slot, 0)
                end
                player_api.clear_input(target)
            else
                Monster.set_state(target.ai, "dead")
                Monster.set_attack_ticks(target.ai, 0)
            end
            world_api.emit(world, "death", source_id, target.id,
                Entity.get_x(target.pose), Entity.get_y(target.pose), 0)
        end
    end

    -- 即时攻击必须来自当前世界中的存活实体，具体伤害共用死亡收敛入口。
    function api.apply(world, source, target, amount)
        assert(Entity.get_id(source.pose) == source.id, "stale identity")
        if world_api.find(world, source.id) == nil or not Unit.get_alive(source.health) then
            return
        end
        api.hit(world, source.id, target, amount)
    end

    return api
end

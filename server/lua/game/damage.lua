-- 统一枪械与近战伤害、生死转换和事件输出，死亡保留实体身份及位置。
return function(deps)
    local world_api = deps["game.world"]
    local player_api = deps["game.player"]
    local api = {}

    -- 对仍在世界中的存活目标施加正伤害，死亡仅发生一次。
    function api.apply(world, source, target, damage)
        if world_api.find(world, source.id) ~= source
            or world_api.find(world, target.id) ~= target or not source.health.alive
            or not target.health.alive then
            return
        end
        assert(type(damage) == "integer" and damage > 0, "invalid damage")
        local health = target.health
        local amount = math.min(damage, health.hp)
        health.hp = health.hp - amount
        world_api.emit(world, "hit", source.id, target.id, target.pose.x, target.pose.y, amount)
        if health.hp == 0 then
            health.alive = false
            target.motion.vx = 0
            target.motion.vy = 0
            if target.kind == "player" then
                target.weapon.reload_ticks = 0
                target.weapon.shot_ticks = 0
                player_api.clear_input(target)
            else
                target.ai.state = "dead"
                target.ai.attack_ticks = 0
            end
            world_api.emit(world, "death", source.id, target.id, target.pose.x, target.pose.y, 0)
        end
    end

    return api
end

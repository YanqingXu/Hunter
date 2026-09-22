-- 将内部组件投影为冻结网络字段，不复制可独立修改的权威状态。
return function(deps)
    local api = {}

    -- 按稳定顺序输出仍属于世界的实体，保留旧字段的客户端语义。
    function api.make(world)
        local entities = json.array()
        for _, id in ipairs(world.entity_ids) do
            local entity = world.entities[id]
            if not entity.pending_remove then
                local player = entity.kind == "player"
                local pos = entity.pose
                local motion = entity.motion
                local health = entity.health
                entities[#entities + 1] = {id = id, cfg_id = entity.cfg_id,
                    kind = player and "player" or "enemy", x = pos.x, y = pos.y,
                    facing = pos.facing, vx = motion.vx, vy = motion.vy,
                    grounded = motion.grounded, hp = health.hp, max_hp = health.max_hp,
                    alive = health.alive, ammo = player and entity.weapon.ammo or 0,
                    reserve = player and entity.reserve or 0,
                    reload_ticks = player and entity.weapon.reload_ticks or 0,
                    ai = player and (health.alive and "idle" or "dead") or entity.ai.state}
            end
        end
        return {v = 3, tick_id = world.tick_id, seq = world.seq, match_id = world.match_id,
            phase = world.phase, entities = entities}
    end

    return api
end

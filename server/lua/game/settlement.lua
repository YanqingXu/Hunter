-- 在战斗之后裁定死亡和撤离；只冻结局内结果，持久化由 Runtime 异步编排。
return function(deps)
    local world_api = deps["game.world"]
    local api = {}

    -- 按出生实例查找 Boss 死亡条件，不把实体删除或普通怪清空当作成功。
    local function unlocked(world, point)
        if point.boss_spawn_id == "0" then
            return true
        end

        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster"
                and enemy.spawn_id == point.boss_spawn_id then
                return not Unit.get_alive(enemy.health)
            end
        end

        return false
    end

    -- 死亡优先于撤离完成，正伤害与离区均取消，暂停由调用入口冻结。
    function api.step(world, content)
        if content.extracts == nil then
            world_api.finish(world)
            return
        end

        local player = world_api.find(world, World.find_player(world, World.get_player_id(world)))
        if not Unit.get_alive(player.health) then
            World.set_extract(world, 0, 0, "dead")
            World.finish(world, "Dead")
            return
        end

        if World.hurt_now(world) then
            World.set_extract(world, 0, 0, "hurt")
            return
        end

        local x = Entity.get_x(player.pose)
        local y = Entity.get_y(player.pose)
        local any_open = false
        for _, point in ipairs(content.extracts) do
            if unlocked(world, point) then
                any_open = true
                if x >= point.x and x <= point.x + point.w
                    and y >= point.y and y <= point.y + point.h then
                    local ticks = World.get_extract_ticks(world) + 1
                    if ticks >= point.hold_ticks then
                        World.set_extract(world, point.id, point.hold_ticks, "complete")
                        World.finish(world, "Extracted")
                    else
                        World.set_extract(world, point.id, ticks, "counting")
                    end

                    return
                end
            end
        end

        World.set_extract(world, 0, 0, any_open and "outside" or "locked")
    end

    return api
end

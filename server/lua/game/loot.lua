-- 按怪物死亡身份执行一次配置掉落；随机状态和生成物品均由原生世界持有。
return function(deps)
    local world_api = deps["game.world"]
    local api = {}

    -- 稳定顺序结算死亡掉落；容量与引用由内容构建校验，运行异常中止本局。
    function api.step(world, content)
        if content.items == nil then
            return
        end

        for _, id in ipairs(world_api.ids(world)) do
            local enemy = world_api.find(world, id)
            if enemy ~= nil and enemy.kind == "monster" and not Unit.get_alive(enemy.health)
                and World.drop_once(world, id) then
                local cfg = content.monsters[enemy.cfg_id]
                for _, entry in ipairs(cfg.drops) do
                    if World.roll(world, 10000) <= entry.chance then
                        local count = entry.min_count
                            + World.roll(world, entry.max_count - entry.min_count + 1) - 1
                        World.drop(world, entry.cfg_id, count,
                            Entity.get_x(enemy.pose), Entity.get_y(enemy.pose))
                    end
                end
            end
        end
    end

    return api
end

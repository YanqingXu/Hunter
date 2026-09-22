-- 构造怪物及出生引用，巡逻范围通过出生 ID 查找而非数组下标关联。
return function(deps)
    local state = deps["framework.state"]
    local unit = deps["game.unit"]
    local api = {}

    -- 在只读出生记录中解析独立出生 ID，不缓存第二份配置。
    function api.spawn_cfg(content, spawn_id)
        for _, spawn in ipairs(content.map.enemies) do
            if spawn.spawn_id == spawn_id then
                return spawn
            end
        end
        return nil
    end

    -- 创建怪物公共能力及专属近战计时。
    function api.new(id, spawn, content)
        local monster = unit.new(id, "monster", spawn.cfg_id, spawn,
            content.monsters[spawn.cfg_id], content.map)
        monster.spawn_id = spawn.spawn_id
        monster.ai = {state = "patrol", attack_ticks = 0}
        return monster
    end

    -- 校验怪物引用及 AI，不接受无关的玩家或枪械字段。
    function api.valid(monster, content)
        if not state.fields(monster, {"id", "kind", "cfg_id", "pose", "pending_remove",
            "motion", "health", "spawn_id", "ai"})
            or not state.is_id(monster.spawn_id, "2147483647")
            or monster.spawn_id == "0" then
            return false
        end
        local spawn = api.spawn_cfg(content, monster.spawn_id)
        local cfg = content.monsters[monster.cfg_id]
        if spawn == nil or spawn.cfg_id ~= monster.cfg_id
            or not unit.valid(monster, "monster", cfg, content.map)
            or not state.fields(monster.ai, {"state", "attack_ticks"})
            or not state.integer(monster.ai.attack_ticks, 0, cfg.attack_ticks) then
            return false
        end
        if not monster.health.alive then
            return monster.ai.state == "dead" and monster.ai.attack_ticks == 0
        end
        return monster.ai.state == "patrol" or monster.ai.state == "chase"
            or monster.ai.state == "attack"
    end

    return api
end

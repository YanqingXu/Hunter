-- 从原生技能状态推导只读有效配置，处理复活选择与特殊视角，不保存可变玩法副本。
return function(deps)
    local skill = deps["game.skill"]
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local player_api = deps["game.player"]
    local api = {}
    local cached_key = nil
    local cached_content = nil

    -- 每次从原生槽位读取未消费技能，已消费身份保留在原生对象中。
    function api.active(player)
        local ids = json.array({})
        for slot = 1, Player.get_skill_count(player.controls) do
            if not Player.get_skill_spent(player.controls, slot) then
                ids[#ids + 1] = Player.get_skill_id(player.controls, slot)
            end
        end
        return ids
    end

    -- 缓存仅含只读配置，键由权威配装和技能集合完整决定。
    function api.content(world, base)
        local player = world_api.find(world, World.find_player(world, World.get_player_id(world)))
        if player == nil then
            return base
        end
        local loadout_text = World.loadout(world)
        local ids = api.active(player)
        local key = loadout_text .. json.encode(ids)
        if key ~= cached_key then
            cached_content = skill.effective(base, json.decode(loadout_text), ids)
            cached_key = key
        end
        return cached_content
    end

    -- 根据当前仍可用的技能判定复活资格，不提前消费技能。
    function api.revival(world, base, player)
        return skill.revive(base, json.decode(World.loadout(world)), api.active(player))
    end

    -- 复活时核对死亡序号及原地空间，成功才原子消费技能并恢复生命。
    function api.revive(world, base, player, seq)
        if not Player.get_downed(player.controls)
            or seq ~= Player.get_death_seq(player.controls) then
            return "stale_death"
        end
        local choice = api.revival(world, base, player)
        if choice == nil then
            return "revive_unavailable"
        end
        local shape = movement.shape(player, base)
        local width, height = shape.width, shape.height
        if not movement.fits(Entity.get_x(player.pose), Entity.get_y(player.pose),
            width, height, base) then
            return "revive_blocked"
        end
        local removed = json.array({})
        for _, id in ipairs(api.active(player)) do
            if base.skills[id].kind == "death" then
                removed[#removed + 1] = id
            end
        end
        if not Player.revive(player.controls, seq, choice.hp, choice.stamina,
            choice.skill_id, json.encode(removed)) then
            return "stale_death"
        end
        player_api.clear_input(player)
        net.skill_event("revive", player.id, player.id,
            Entity.get_x(player.pose), Entity.get_y(player.pose), choice.hp,
            choice.skill_id, Player.get_death_seq(player.controls))
        return nil
    end

    -- 暂停时由主循环停止调用，等待复活期间不推进抑制计时。
    function api.tick(player)
        local remaining = Player.get_quiet_ticks(player.controls)
        if remaining > 0 then
            Player.set_quiet_ticks(player.controls, remaining - 1)
        end
    end

    return api
end

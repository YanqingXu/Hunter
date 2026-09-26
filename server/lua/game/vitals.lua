-- 按游戏 Tick 推进体力与分段生命回复，所有累计值保存在原生玩家对象。
return function(deps)
    local api = {}

    -- 从低血量向高血量累计血段，恰处边界时不进入下一段。
    local function ceiling(player)
        local hp = Unit.get_hp(player.health)
        local limit = 0
        for index = 1, Player.get_segment_count(player.controls) do
            limit = limit + Player.get_segment(player.controls, index)
            if hp <= limit then
                return limit
            end
        end
        return Unit.get_max_hp(player.health)
    end

    -- 仅存活玩家在无伤害 Tick 回复，整数余量避免非整除速率产生漂移。
    function api.step(world, content, player, stamina_reset)
        if not Unit.get_alive(player.health) then
            return
        end
        local input = player.controls
        local cfg = content.players[player.cfg_id]
        local delay = Player.get_stamina_delay(input)
        if not stamina_reset then
            if delay > 0 then
                Player.set_stamina_delay(input, delay - 1)
            elseif not Player.get_running(input) and Player.get_stamina(input) < cfg.stamina then
                local rem = Player.get_stamina_rem(input) + cfg.stamina_rate
                Player.set_stamina(input, math.min(cfg.stamina,
                    Player.get_stamina(input) + rem // content.tick_hz))
                Player.set_stamina_rem(input, rem % content.tick_hz)
            end
        end
        if Player.get_stamina(input) == cfg.stamina then
            Player.set_stamina_rem(input, 0)
        end
        if World.hurt_now(world) then
            Player.set_health_delay(input, cfg.health_delay)
            Player.set_health_rem(input, 0)
            return
        end
        delay = Player.get_health_delay(input)
        if delay > 0 then
            Player.set_health_delay(input, delay - 1)
            return
        end
        local limit = ceiling(player)
        local hp = Unit.get_hp(player.health)
        if hp < limit then
            local rem = Player.get_health_rem(input) + cfg.health_rate
            Unit.set_hp(player.health, math.min(limit, hp + rem // content.tick_hz))
            Player.set_health_rem(input, rem % content.tick_hz)
        end
        if Unit.get_hp(player.health) == limit then
            Player.set_health_rem(input, 0)
        end
    end

    return api
end

-- 构造局内玩家的输入、枪械和备用弹药，不承担会话或网络传输。
return function(deps)
    local state = deps["framework.state"]
    local unit = deps["game.unit"]
    local api = {}

    -- 创建中立输入，暂停、死亡与新局复用同一规则。
    function api.clear_input(player)
        player.controls = {move_x = 0, aim_x = 1000, aim_y = 0, jump = false,
            fire = false, fire_once = false, reload = false}
    end

    -- 从出生记录引用的配置创建玩家及唯一枪械状态。
    function api.new(id, player_id, content)
        local spawn = content.map.spawn
        local player = unit.new(id, "player", spawn.cfg_id, spawn,
            content.players[spawn.cfg_id], content.map)
        local cfg = content.weapons[spawn.weapon_cfg_id]
        player.player_id = player_id
        player.weapon = {cfg_id = spawn.weapon_cfg_id, ammo = cfg.magazine,
            shot_ticks = 0, reload_ticks = 0}
        player.reserve = cfg.reserve
        api.clear_input(player)
        return player
    end

    -- 校验输入形状与值域，暂停及终态的中立约束由世界检查。
    local function valid_input(input)
        return state.fields(input, {"move_x", "aim_x", "aim_y", "jump", "fire",
                "fire_once", "reload"}) and state.integer(input.move_x, -1, 1)
            and state.integer(input.aim_x, -1000, 1000)
            and state.integer(input.aim_y, -1000, 1000)
            and (input.aim_x ~= 0 or input.aim_y ~= 0)
            and type(input.jump) == "boolean" and type(input.fire) == "boolean"
            and type(input.fire_once) == "boolean" and type(input.reload) == "boolean"
    end

    -- 验证玩家专属字段与枪械配置，不允许通过导入隐式换装。
    function api.valid(player, content)
        if not state.fields(player, {"id", "kind", "cfg_id", "pose", "pending_remove",
            "motion", "health", "player_id", "controls", "weapon", "reserve"})
            or not unit.valid(player, "player", content.players[player.cfg_id], content.map)
            or player.pending_remove or player.player_id ~= "1"
            or player.cfg_id ~= content.map.spawn.cfg_id or not valid_input(player.controls)
            or not state.fields(player.weapon, {"cfg_id", "ammo", "shot_ticks", "reload_ticks"})
            or player.weapon.cfg_id ~= content.map.spawn.weapon_cfg_id then
            return false
        end
        local gun = player.weapon
        local cfg = content.weapons[gun.cfg_id]
        return cfg ~= nil and state.integer(gun.ammo, 0, cfg.magazine)
            and state.integer(player.reserve, 0, cfg.reserve)
            and state.integer(gun.shot_ticks, 0, cfg.fire_ticks)
            and state.integer(gun.reload_ticks, 0, cfg.reload_ticks)
            and (player.health.alive or (gun.shot_ticks == 0 and gun.reload_ticks == 0))
    end

    return api
end

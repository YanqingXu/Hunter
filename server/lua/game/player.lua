-- 将局内玩家绑定为原生组件视图，输入与枪械不存在脚本数据副本。
return function(deps)
    local unit = deps["game.unit"]
    local api = {}

    -- 组合玩家身份、原生输入和枪械句柄。
    function api.view(id)
        local value = unit.view(id)
        value.controls = Player.find(id)
        value.player_id = Player.get_player_id(value.controls)
        value.weapon = Weapon.find(id)
        return value
    end

    -- 通过原生属性清除持续和边沿输入，不改动备用弹药。
    function api.clear_input(player)
        local input = player.controls
        Player.set_move_x(input, 0)
        Player.set_aim_x(input, Entity.get_facing(player.pose) * 1000)
        Player.set_aim_y(input, 0)
        Player.set_jump(input, false)
        Player.set_fire(input, false)
        Player.set_fire_once(input, false)
        Player.set_reload(input, false)
        Player.set_move_y(input, 0)
        Player.set_run(input, false)
        Player.set_melee(input, false)
    end

    return api
end

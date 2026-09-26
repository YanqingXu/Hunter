-- 执行服务端鉴权后的局内操作，使用、补给与梯子状态始终写回原生世界。
return function(deps)
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local projectile = deps["game.projectile"]
    local state = deps["framework.state"]
    local api = {}

    -- 切换持有物清空旧射击和装填意图，不把被取消的动作带到新槽位。
    local function select_tool(player, slot)
        local input = player.controls
        if slot < 1 or slot > 8 or Player.get_tool_cfg(input, slot) == "0" then
            return "invalid_slot"
        end
        Player.cancel_use(input)
        Player.set_weapon_reload_ticks(input, Player.get_active_weapon(input), 0)
        Player.set_selected_slot(input, slot)
        Player.set_fire(input, false)
        Player.set_fire_once(input, false)
        Player.set_reload(input, false)
        Player.set_melee(input, false)
        return nil
    end

    -- 对当前场景矩形做服务端距离检查，交互不能隔着实心物到达。
    local function reachable(player, scene, content)
        local x = Entity.get_x(player.pose)
        local y = Entity.get_y(player.pose)
        local px = math.max(scene.x, math.min(scene.x + scene.w, x))
        local py = math.max(scene.y, math.min(scene.y + scene.h, y))
        local dx = px - x
        local dy = py - y
        local radius = content.bag.pickup_radius
        return dx * dx + dy * dy <= radius * radius
            and combat.clear_path(movement.solids(content), x, y, px, py)
    end

    -- 补给先形成完整候选，再一次消耗箱子，不能增加任何可结算背包物品。
    local function supply(world, content, player, index)
        if World.scene_used(world, index) then
            return "already_used"
        end
        local input = player.controls
        local changes = {}
        local present = {}
        local free = 0
        for slot = 1, 8 do
            local cfg_id = Player.get_tool_cfg(input, slot)
            if cfg_id ~= "0" then
                present[cfg_id] = true
                local cfg = content.tools[cfg_id]
                local count = Player.get_tool_count(input, slot)
                if slot <= 4 and cfg.kind ~= "knife" and count < cfg.uses then
                    changes[#changes + 1] = {slot = slot, cfg_id = cfg_id, count = count + 1}
                end
            elseif slot > 4 and free == 0 then
                free = slot
            end
        end
        local choices = {}
        if free ~= 0 then
            for cfg_id, cfg in pairs(content.tools) do
                if (cfg.kind == "needle" or cfg.kind == "bomb") and not present[cfg_id] then
                    local at = #choices + 1
                    while at > 1 and state.newer(choices[at - 1], cfg_id) do
                        choices[at] = choices[at - 1]
                        at = at - 1
                    end
                    choices[at] = cfg_id
                end
            end
        end
        if #changes == 0 and #choices == 0 then
            return "supply_full"
        end
        if #choices > 0 then
            local cfg_id = choices[World.roll(world, #choices)]
            changes[#changes + 1] = {slot = free, cfg_id = cfg_id, count = 1}
        end
        for _, change in ipairs(changes) do
            Player.change_tool(input, change.slot, change.cfg_id, change.count)
        end
        World.set_scene_used(world, index, true)
        return nil
    end

    -- 从梯外进入时取消使用并清除所有非移动动作，离梯仅由端点移动触发。
    local function ladder(player, scene, content)
        local input = player.controls
        local cfg = content.players[player.cfg_id]
        local y = Entity.get_y(player.pose)
        local next_x = scene.x + scene.w // 2
        local next_y = math.max(scene.y, math.min(scene.y + scene.h, y))
        Player.cancel_use(input)
        Player.set_weapon_reload_ticks(input, Player.get_active_weapon(input), 0)
        Player.set_prone(input, false)
        Player.set_want_prone(input, false)
        Player.set_ladder_id(input, integer.create(tonumber(scene.id)))
        Unit.write_motion(player.motion, next_x, next_y, 0, 0, false,
            Entity.get_facing(player.pose))
        if Player.get_running(input) then
            Player.set_stamina_delay(input, cfg.stamina_delay)
            Player.set_stamina_rem(input, 0)
        end
        Player.set_running(input, false)
        Player.set_run(input, false)
        Player.set_jump(input, false)
        Player.set_fire(input, false)
        Player.set_fire_once(input, false)
        Player.set_reload(input, false)
        Player.set_melee(input, false)
        Player.set_aim_x(input, Entity.get_facing(player.pose) * 1000)
        Player.set_aim_y(input, 0)
        return nil
    end

    -- 场景身份来自只读清单，原生索引保存本局消耗状态。
    local function interact(world, content, player, target)
        for index, scene in ipairs(content.scenes) do
            if scene.id == target then
                if not reachable(player, scene, content) then
                    return "out_of_range"
                end
                if scene.kind == "ladder" then
                    return ladder(player, scene, content)
                elseif scene.kind == "supply" then
                    return supply(world, content, player, index)
                end
                return "invalid_target"
            end
        end
        return "invalid_target"
    end

    -- 开始读条时绑定工具实例，炸药容量由原生预留保证完成时可发布。
    local function use(player, content, slot)
        local input = player.controls
        slot = slot == 0 and Player.get_selected_slot(input) or slot
        if slot < 1 or slot > 8 then
            return "invalid_slot"
        end
        local cfg_id = Player.get_tool_cfg(input, slot)
        local cfg = content.tools[cfg_id]
        if cfg == nil or Player.get_tool_count(input, slot) == 0 then
            return "tool_empty"
        end
        if cfg.kind == "knife" then
            if Player.get_use_slot(input) ~= 0 or Player.get_melee_ticks(input) ~= 0
                or Player.get_melee(input)
                or Player.get_stamina(input) < content.rules.melee_stamina then
                return "action_locked"
            end
            local error = select_tool(player, slot)
            if error ~= nil then
                return error
            end
            Player.set_melee(input, true)
            return nil
        end
        if Player.get_use_slot(input) ~= 0 then
            return "use_busy"
        end
        if cfg.heal > 0 and Unit.get_hp(player.health) == Unit.get_max_hp(player.health) then
            return "health_full"
        end
        if cfg.kind == "bomb" and Player.get_prone(input)
            and (Player.get_aim_y(input) < 0
                or Player.get_aim_x(input) * Entity.get_facing(player.pose) < 0) then
            return "invalid_aim"
        end
        if not Player.start_use(input, slot, cfg.use_ticks) then
            return "projectile_capacity"
        end
        Player.set_selected_slot(input, slot)
        Player.set_weapon_reload_ticks(input, Player.get_active_weapon(input), 0)
        Player.set_reload(input, false)
        Player.set_melee(input, false)
        return nil
    end

    -- 仅完成合法动作，失败返回业务错误而不将玩家输入提升为脚本异常。
    function api.apply(world, content, req)
        if World.get_phase(world) ~= "Playing" or World.get_paused(world) then
            return "invalid_state"
        end
        local player = world_api.find(world, World.find_player(world, World.get_player_id(world)))
        if player == nil or not Unit.get_alive(player.health) then
            return "invalid_state"
        end
        local input = player.controls
        if Player.get_ladder_id(input) ~= 0 then
            return "action_locked"
        elseif req.kind == "interact" then
            return interact(world, content, player, req.target_id)
        elseif req.kind == "switch_weapon" then
            if not Player.switch_weapon(input, req.slot) then
                return "invalid_slot"
            end
            return nil
        elseif req.kind == "select_tool" then
            return select_tool(player, req.slot)
        elseif req.kind == "use" then
            return use(player, content, req.slot)
        elseif req.kind == "melee" then
            if Player.get_use_slot(input) ~= 0 or Player.get_melee_ticks(input) ~= 0
                or Player.get_melee(input)
                or Player.get_stamina(input) < content.rules.melee_stamina then
                return "action_locked"
            end
            local selected = Player.get_selected_slot(input)
            if selected ~= 0 then
                local cfg = content.tools[Player.get_tool_cfg(input, selected)]
                if cfg == nil or cfg.kind ~= "knife" then
                    return "invalid_tool"
                end
            end
            Player.set_melee(input, true)
            Player.set_fire(input, false)
            Player.set_fire_once(input, false)
            Player.set_weapon_reload_ticks(input, Player.get_active_weapon(input), 0)
            Player.set_reload(input, false)
            return nil
        end
        return "invalid_request"
    end

    -- 怪物攻击之后取消伤亡使用；仍有效的读条到期才提交效果与次数。
    function api.finish(world, content, player)
        local input = player.controls
        local slot = Player.get_use_slot(input)
        if slot == 0 then
            return
        end
        if not Unit.get_alive(player.health) or World.hurt_now(world)
            or Player.get_ladder_id(input) ~= 0 then
            Player.cancel_use(input)
            return
        end
        if Player.get_tool_instance(input, slot) ~= Player.get_use_instance(input) then
            Player.cancel_use(input)
            return
        end
        local ticks = Player.get_use_ticks(input) - 1
        Player.set_use_ticks(input, ticks)
        if ticks > 0 then
            return
        end
        local cfg_id = Player.get_tool_cfg(input, slot)
        local cfg = content.tools[cfg_id]
        if cfg.kind == "bomb" then
            if Player.get_prone(input) and (Player.get_aim_y(input) < 0
                or Player.get_aim_x(input) * Entity.get_facing(player.pose) < 0) then
                Player.cancel_use(input)
                return
            end
            projectile.launch(world, content, player, cfg_id)
        end
        assert(Player.finish_use(input), "use instance changed during completion")
        if cfg.heal > 0 then
            local missing = Unit.get_max_hp(player.health) - Unit.get_hp(player.health)
            local amount = math.min(cfg.heal, missing)
            Unit.set_hp(player.health, Unit.get_hp(player.health) + amount)
            world_api.emit(world, "heal", player.id, player.id,
                Entity.get_x(player.pose), Entity.get_y(player.pose), amount)
        end
        world_api.emit(world, "tool", player.id, "0",
            Entity.get_x(player.pose), Entity.get_y(player.pose), slot)
    end

    return api
end

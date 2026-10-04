-- 执行服务端鉴权后的局内操作，使用、补给与梯子状态始终写回原生世界。
return function(deps)
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local projectile = deps["game.projectile"]
    local state = deps["framework.state"]
    local inventory = deps["game.inventory"]
    local hunt = deps["game.hunt"]
    local scene_api = deps["game.scene"]
    local exploration = deps["game.exploration"]
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
        return scene_api.reachable(player, scene, content)
    end

    -- 收集可补常规工具与空消费品槽，预检不推进随机数。
    local function supply_choices(content, input)
        local regular, choices, free, full = {}, {}, 0, true
        for slot = 1, 4 + content.rules.consumable_slots do
            local cfg_id = Player.get_tool_cfg(input, slot)
            if cfg_id ~= "0" and slot <= content.rules.tool_slots then
                local cfg = content.tools[cfg_id]
                local count = Player.get_tool_count(input, slot)
                if cfg.kind ~= "knife" and count < cfg.uses then
                    full = false
                    if cfg.refill_count > 0 then
                        regular[#regular + 1] = {slot = slot, cfg_id = cfg_id,
                            count = math.min(cfg.uses, count + cfg.refill_count)}
                    end
                end
            elseif cfg_id == "0" and slot > 4 and free == 0 then
                free = slot
            end
        end
        if full and free ~= 0 then
            for cfg_id, cfg in pairs(content.tools) do
                if (cfg.kind == "needle" or cfg.kind == "bomb") and cfg.supply_weight > 0 then
                    local at = #choices + 1
                    while at > 1 and state.newer(choices[at - 1], cfg_id) do
                        choices[at] = choices[at - 1]
                        at = at - 1
                    end
                    choices[at] = cfg_id
                end
            end
        end
        return regular, choices, free
    end

    -- 按弹种汇总装备总量，每种弹药只应用一次补给量并按稳定槽分配备弹。
    local function ammo_changes(content, input)
        local groups, order = {}, {}
        for slot = 1, Player.get_weapon_count(input) do
            local cfg = content.weapons[Player.get_weapon_cfg(input, slot)]
            local id = cfg.ammo_cfg_id
            if groups[id] == nil then
                groups[id] = {held = 0, capacity = 0, slots = {}}
                order[#order + 1] = id
            end
            local group = groups[id]
            local ammo = Player.get_weapon_ammo(input, slot)
            local reserve = Player.get_weapon_reserve(input, slot)
            local capacity = cfg.magazine + cfg.reserve
            group.held, group.capacity = group.held + ammo + reserve, group.capacity + capacity
            group.slots[#group.slots + 1] = {slot = slot, ammo = ammo, reserve = reserve,
                capacity = capacity}
        end
        local changes = json.array({})
        for _, id in ipairs(order) do
            local group = groups[id]
            local amount = math.min(content.ammo[id].refill_count,
                math.max(0, group.capacity - group.held))
            for _, gun in ipairs(group.slots) do
                local added = math.min(amount, math.max(0, gun.capacity - gun.ammo - gun.reserve))
                if added > 0 then
                    changes[#changes + 1] = {slot = gun.slot, expected_ammo = gun.ammo,
                        expected_reserve = gun.reserve, ammo = gun.ammo,
                        reserve = gun.reserve + added}
                    amount = amount - added
                end
            end
        end
        return changes
    end

    -- 补给完成时重新校验候选，再原子提交数量和箱子状态。
    local function supply(world, content, player, index, check_only)
        local input = player.controls
        local batch = {owner = World.get_player_id(world), items = json.array({}),
            tools = json.array({}), scene = {index = index, expected_used = false, used = true}}
        if content.scenes[index].kind == "ammo_supply" then
            batch.weapons = ammo_changes(content, input)
            if #batch.weapons == 0 then
                return "supply_full"
            end
            if check_only then return nil end
        else
            local regular, choices, free = supply_choices(content, input)
            if #regular == 0 and #choices == 0 then
                return "supply_full"
            end
            if check_only then return nil end
            local change
            if #regular > 0 then
                change = regular[World.roll(world, #regular)]
            else
                local weight = 0
                for _, id in ipairs(choices) do
                    weight = weight + content.tools[id].supply_weight
                end
                local picked = World.roll(world, weight)
                for _, id in ipairs(choices) do
                    picked = picked - content.tools[id].supply_weight
                    if picked <= 0 then
                        change = {slot = free, cfg_id = id, count = 1}
                        break
                    end
                end
            end
            change.expected_instance = Player.get_tool_instance(input, change.slot)
            batch.tools[1] = change
        end
        local error = World.commit(world, json.encode(batch))
        return error ~= "" and error or nil
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
        Unit.set_body(player.motion, cfg.width, cfg.height)
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
    local function interact(world, content, player, target, finished)
        for index, scene in ipairs(content.scenes) do
            if scene.id == target then
                if not reachable(player, scene, content) then
                    return "out_of_range"
                end
                if scene.kind == "barrel" or scene.kind == "cover" then
                    return "invalid_target"
                end
                if (scene.kind == "supply" or scene.kind == "ammo_supply" or scene.kind == "clue")
                    and World.scene_used(world, index) then
                    return "already_used"
                end
                if not finished and scene.interaction ~= false
                    and scene.interaction.mode == "channel" then
                    if scene.kind == "supply" or scene.kind == "ammo_supply" then
                        local error = supply(world, content, player, index, true)
                        if error ~= nil then return error end
                    end
                    if Player.get_use_slot(player.controls) ~= 0 then
                        return "action_locked"
                    end
                    if not World.scene_begin(world, index, scene.interaction.hold_ticks) then
                        return "interaction_busy"
                    end
                    return nil
                end
                if scene.kind == "ladder" then
                    return ladder(player, scene, content)
                elseif scene.kind == "supply" or scene.kind == "ammo_supply" then
                    return supply(world, content, player, index)
                elseif scene.kind == "clue" then
                    return exploration.clue(world, content, index)
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
        if not Player.start_use(input, slot, cfg.use_ticks, cfg.kind == "bomb") then
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
        if req.kind == "bag" then
            return nil
        end
        if World.get_phase(world) ~= "Playing" or World.get_paused(world) then
            return "invalid_state"
        end
        local player = world_api.find(world, World.find_player(world, World.get_player_id(world)))
        if player == nil then
            return "invalid_state"
        end
        local input = player.controls
        if req.kind == "abandon" then
            World.finish(world, "Abandoned")
            return nil
        elseif req.kind == "revive" then
            return hunt.revive(world, content, player, req.death_seq)
        elseif not Unit.get_alive(player.health) then
            return "invalid_state"
        elseif req.kind == "vision_off" then
            Player.set_vision(input, false)
            return nil
        elseif Player.get_vision(input) then
            return "action_locked"
        elseif req.kind == "vision_on" then
            if Player.get_ladder_id(input) ~= 0 then
                return "action_locked"
            end
            Player.set_vision(input, true)
            World.scene_cancel(world)
            return nil
        elseif Player.get_ladder_id(input) ~= 0 then
            return "action_locked"
        elseif req.kind == "pickup" then
            return inventory.pickup(world, content, player, req.target_id)
        elseif req.kind == "interact" then
            return interact(world, content, player, req.target_id)
        elseif req.kind == "switch_weapon" then
            if not Player.switch_weapon(input, req.slot) then
                return "invalid_slot"
            end
            World.scene_cancel(world)
            return nil
        elseif req.kind == "select_tool" then
            local error = select_tool(player, req.slot)
            if error == nil then
                World.scene_cancel(world)
            end
            return error
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
            World.scene_cancel(world)
            return nil
        end
        return "invalid_request"
    end

    -- 怪物攻击之后取消伤亡使用；仍有效的读条到期才提交效果与次数。
    function api.finish(world, content, player)
        scene_api.finish(world, content, player, function(target)
            return interact(world, content, player, target, true)
        end)
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
        local remaining = Player.get_tool_count(input, slot) - (cfg.kind == "knife" and 0 or 1)
        local clear = remaining == 0 and (cfg.kind == "needle" or cfg.kind == "bomb")
        assert(Player.finish_use(input, remaining, clear), "use instance changed during completion")
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

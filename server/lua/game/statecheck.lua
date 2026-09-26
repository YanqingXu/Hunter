-- 对独立状态候选验证配置语义；不写入世界，不修正非法状态或推进句柄代际。
return function(deps)
    local state = deps["framework.state"]
    local loadout = deps["game.loadout"]
    local movement = deps["game.movement"]
    local monster = deps["game.monster"]
    local api = {}

    -- 验证枪械配置约束，空槽由原生结构校验负责。
    local function weapon(value, reserve, content)
        local cfg = content.weapons[value.cfg_id]
        assert(cfg ~= nil, "invalid_weapon_cfg")
        assert(state.integer(value.ammo, 0, cfg.magazine)
            and state.integer(value.shot_ticks, 0, cfg.fire_ticks)
            and state.integer(value.reload_ticks, 0, cfg.reload_ticks)
            and state.integer(reserve, 0, cfg.reserve), "invalid_weapon_state")
    end

    -- 验证有效体型、移动速度、生命上限与地图碰撞的一致性。
    local function unit(value, cfg, content)
        local pose, motion, hp = value.pose, value.motion, value.health
        local width, height, speed = cfg.width, cfg.height, cfg.speed
        local ladder = false
        if value.kind == "player" then
            local demo = value.demo
            if demo.prone then
                width, height, speed = cfg.prone_width, cfg.prone_height, cfg.prone_speed
            elseif demo.running then
                speed = cfg.run_speed
            end
            ladder = demo.ladder_id ~= 0
        end
        assert(value.body.width == width and value.body.height == height, "invalid_body")
        assert(state.integer(pose.x, width // 2, content.map.width - width // 2)
            and state.integer(pose.y, 0, content.map.height - height), "invalid_position")
        assert(state.integer(motion.vx, -speed, speed)
            and state.integer(motion.vy, -1000, cfg.jump_speed or 0), "invalid_motion")
        assert(hp.max_hp == cfg.hp and state.integer(hp.hp, 0, cfg.hp), "invalid_health")
        local grounded = pose.y == 0
        for _, solid in ipairs(movement.solids(content)) do
            local overlap = pose.x - width // 2 < solid.x + solid.w
                and pose.x + width // 2 > solid.x
            assert(ladder or not (overlap and pose.y < solid.y + solid.h
                and pose.y + height > solid.y), "state_collision")
            grounded = grounded or (overlap and pose.y == solid.y + solid.h)
        end
        assert(ladder or motion.grounded == grounded, "invalid_grounded")
    end

    -- 校验工具类别、容量与使用实例，常规零次留槽而耗尽消耗品必须清槽。
    local function tools(value, content)
        local demo = value.demo
        local seen = {}
        for index, entry in ipairs(demo.tools) do
            if entry.cfg_id ~= "0" then
                local cfg = content.tools[entry.cfg_id]
                assert(cfg ~= nil and not seen[entry.cfg_id], "invalid_tool_cfg")
                seen[entry.cfg_id] = true
                local consumable = cfg.kind == "needle" or cfg.kind == "bomb"
                assert(consumable == (index > 4) and (index <= content.rules.tool_slots
                    or (index > 4 and index <= 4 + content.rules.consumable_slots)),
                    "invalid_tool_slot")
                assert(state.integer(entry.count, consumable and 1 or 0, cfg.uses),
                    "invalid_tool_count")
            end
        end
        if demo.use_slot ~= 0 then
            local entry = demo.tools[demo.use_slot]
            local cfg = content.tools[entry.cfg_id]
            assert(cfg ~= nil and cfg.kind ~= "knife"
                and demo.ladder_id == 0
                and state.integer(demo.use_ticks, 1, cfg.use_ticks), "invalid_active_use")
            assert((demo.reserved_projectile > 0) == (cfg.kind == "bomb"),
                "invalid_projectile_reservation")
        end
    end

    -- 校验玩家恢复参数、冻结配装及梯子位置；原生层另验句柄与结构关系。
    local function player(value, content, accepted)
        local cfg, demo = content.players[value.cfg_id], value.demo
        assert(state.integer(demo.stamina, 0, cfg.stamina)
            and state.integer(demo.stamina_delay, 0, cfg.stamina_delay)
            and state.integer(demo.health_delay, 0, cfg.health_delay)
            and state.integer(demo.melee_ticks, 0, content.rules.melee_ticks),
            "invalid_player_timers")
        weapon(value.weapon, value.reserve, content)
        if demo.weapon_count == 2 then
            weapon(demo.other_weapon, demo.other_reserve, content)
        end
        for index = 1, demo.weapon_count do
            local gun = index == demo.active_weapon and value.weapon or demo.other_weapon
            assert(content.weapons[gun.cfg_id].ammo_cfg_id == tostring(demo.ammo_cfg_ids[index]),
                "invalid_ammo_reference")
            assert(accepted.weapons[index].cfg_id == gun.cfg_id
                and accepted.weapons[index].ammo_cfg_id == tostring(demo.ammo_cfg_ids[index]),
                "loadout_mismatch")
        end
        local total = 0
        for index, amount in ipairs(demo.health_segments) do
            assert((amount == 25 or amount == 50)
                and amount == accepted.health_segments[index], "invalid_health_segments")
            total = total + amount
        end
        assert(total == cfg.hp and #demo.health_segments == #accepted.health_segments,
            "invalid_health_segments")
        tools(value, content)
        if demo.ladder_id > 0 then
            local found = false
            for _, scene in ipairs(content.scenes) do
                if scene.id == tostring(demo.ladder_id) then
                    found = scene.kind == "ladder" and not demo.prone and not demo.running
                        and value.motion.vx == 0 and math.abs(value.motion.vy) <= cfg.speed
                        and value.pose.x == scene.x + scene.w // 2
                        and value.pose.y >= scene.y and value.pose.y <= scene.y + scene.h
                end
            end
            assert(found, "invalid_ladder_state")
        end
    end

    -- 校验怪物的出生记录、AI计时和初生位置。
    local function enemy(value, content)
        local cfg = content.monsters[value.cfg_id]
        local spawn = monster.spawn_cfg(content, value.spawn_id)
        assert(spawn ~= nil and spawn.cfg_id == value.cfg_id, "invalid_spawn_reference")
        assert(state.integer(value.ai.attack_ticks, 0, cfg.attack_ticks), "invalid_attack_ticks")
        if value.ai.state == "spawn" then
            assert(value.pose.x == spawn.x and value.pose.y == spawn.y,
                "invalid_spawn_state")
        end
    end

    -- 冷路径执行完整配置语义校验；语义与结构校验全部成功后宿主才可替换状态。
    function api.validate(doc, content)
        assert(type(doc) == "table" and doc.v == 7, "invalid_state_version")
        local accepted = nil
        if doc.match_id ~= "0" then
            local reason
            accepted, reason = loadout.check(content, doc.loadout)
            assert(accepted ~= nil, reason or "invalid_loadout")
            assert(json.encode(accepted) == json.encode(doc.loadout), "incomplete_loadout")
        end
        for _, value in pairs(doc.entities) do
            local cfgs = value.kind == "player" and content.players or content.monsters
            local cfg = cfgs[value.cfg_id]
            assert(cfg ~= nil, "invalid_actor_cfg")
            unit(value, cfg, content)
            if value.kind == "player" then
                assert(accepted ~= nil and value.cfg_id == accepted.player_cfg_id,
                    "invalid_player_cfg")
                player(value, content, accepted)
            else
                enemy(value, content)
            end
        end
        local count = 0
        for _, item in pairs(doc.items) do
            if item.place ~= "None" then
                local cfg = content.items[item.cfg_id]
                assert(cfg ~= nil and state.integer(item.count, 1, cfg.max_stack),
                    "invalid_stack")
                if item.place == "Bag" then
                    assert((cfg.kind or 5) == 5, "equipment_in_reward_bag")
                    count = count + 1
                end
            end
        end
        assert(count <= content.bag.slots, "invalid_bag_capacity")
        assert(#doc.scene_used == #content.scenes, "invalid_scene_states")
        for index, used in ipairs(doc.scene_used) do
            assert(not used or content.scenes[index].kind == "supply", "invalid_scene_usage")
        end
        for _, shot in ipairs(doc.projectiles) do
            if shot.active then
                local cfg = content.tools[shot.cfg_id]
                assert(cfg ~= nil and cfg.kind == "bomb"
                    and state.integer(shot.remaining, 1, cfg.throw_range), "invalid_projectile")
            end
        end
        if doc.phase == "Playing" then
            local alive = false
            for _, value in pairs(doc.entities) do
                alive = alive or (value.kind == "monster" and value.health.alive
                    and not value.pending_remove)
            end
            assert(alive or content.extracts ~= nil, "invalid_phase_state")
        end
        local open, remaining = false, 0
        if doc.match_id ~= "0" then
            for _, point in ipairs(content.extracts or {}) do
                open = open or point.boss_spawn_id == "0"
                for _, value in pairs(doc.entities) do
                    open = open or (value.kind == "monster"
                        and value.spawn_id == point.boss_spawn_id and not value.health.alive
                        and not value.pending_remove)
                end
                remaining = math.max(0, point.hold_ticks - doc.raid.extract_ticks)
            end
        end
        assert(doc.raid.extract_unlocked == open and doc.raid.extract_remaining == remaining,
            "invalid_extract_projection")
        return true
    end

    return api
end

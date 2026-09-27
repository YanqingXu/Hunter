-- 对独立状态候选验证配置语义；不写入世界，不修正非法状态或推进句柄代际。
return function(deps)
    local state = deps["framework.state"]
    local loadout = deps["game.loadout"]
    local movement = deps["game.movement"]
    local monster = deps["game.monster"]
    local skill = deps["game.skill"]
    local api = {}

    -- 已消费槽仍证明物资曾合法产生；这里只累计增益以形成保守校验上限。
    local function resource_history(content, entries)
        local values = {}
        for _, entry in ipairs(entries) do
            for _, effect in ipairs(content.skills[entry.cfg_id].effects) do
                local value = values[effect.stat] or {add = 0, bp = 10000}
                if effect.op == "add" then
                    value.add = value.add + math.max(0, effect.value)
                else
                    value.bp = value.bp + math.max(0, effect.value - 10000)
                end
                values[effect.stat] = value
            end
        end
        return values
    end

    -- 历史上限只用于存档校验，不能反向用于当前属性或物资发放。
    local function resource_limit(base, name, history, hard)
        local value = history[name]
        return math.min(hard, value and (base + value.add) * value.bp // 10000 or base)
    end

    -- 验证枪械配置约束，空槽由原生结构校验负责。
    local function weapon(value, reserve, content, base, history)
        local cfg = content.weapons[value.cfg_id]
        local original = base.weapons[value.cfg_id]
        assert(cfg ~= nil, "invalid_weapon_cfg")
        assert(state.integer(value.ammo, 0, cfg.magazine)
            and state.integer(value.shot_ticks, 0,
                resource_limit(original.fire_ticks, "shot_ticks", history, 3600))
            and state.integer(value.reload_ticks, 0,
                resource_limit(original.reload_ticks, "reload_ticks", history, 3600))
            and state.integer(reserve, 0,
                resource_limit(original.reserve, "reserve", history, 100000)),
            "invalid_weapon_state")
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
        elseif cfg.ai then
            speed = math.max(speed, cfg.ai.alert_speed)
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
    local function tools(value, content, base, history)
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
            local ticks = base.tools[entry.cfg_id].use_ticks
            if cfg.kind == "medkit" then
                ticks = resource_limit(ticks, "medkit_ticks", history, 36000)
            end
            assert(cfg ~= nil and cfg.kind ~= "knife"
                and demo.ladder_id == 0
                and state.integer(demo.use_ticks, 1, ticks),
                "invalid_active_use")
            assert((demo.reserved_projectile > 0) == (cfg.kind == "bomb"),
                "invalid_projectile_reservation")
        end
    end

    -- 校验玩家恢复参数、冻结配装及梯子位置；原生层另验句柄与结构关系。
    local function player(value, content, accepted, base, history)
        local cfg, demo = content.players[value.cfg_id], value.demo
        assert(state.integer(demo.stamina, 0, cfg.stamina)
            and state.integer(demo.stamina_delay, 0, cfg.stamina_delay)
            and state.integer(demo.health_delay, 0, cfg.health_delay)
            and state.integer(demo.melee_ticks, 0, content.rules.melee_ticks),
            "invalid_player_timers")
        weapon(value.weapon, value.reserve, content, base, history)
        if demo.weapon_count == 2 then
            weapon(demo.other_weapon, demo.other_reserve, content, base, history)
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
        tools(value, content, base, history)
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
    local function enemy(value, content, doc)
        local cfg = content.monsters[value.cfg_id]
        local spawn = monster.spawn_cfg(content, value.spawn_id)
        local ai = value.ai
        assert(spawn ~= nil, "invalid_spawn_reference")
        if ai.owner_id == "0" then
            assert(spawn.cfg_id == value.cfg_id and ai.outside_ticks == 0,
                "invalid_spawn_reference")
        else
            local owner = doc.entities[ai.owner_id]
            assert(owner ~= nil and owner.kind == "monster" and owner.ai.owner_id == "0",
                "invalid_summon_owner")
            local owner_cfg = content.monsters[owner.cfg_id]
            local bind = owner_cfg.ai and owner_cfg.ai.abilities[ai.owner_ability]
            local ability = bind and content.abilities[bind.cfg_id]
            assert(ability ~= nil and ability.kind == "summon"
                and ability.summon_cfg_id == value.cfg_id
                and owner.spawn_id == value.spawn_id and owner.ai.wave == ai.wave
                and state.integer(ai.outside_ticks, 0, ability.despawn_ticks),
                "invalid_summon_reference")
        end
        assert(state.integer(ai.wave, 1, doc.wave)
            and state.integer(ai.last_hp, 0, cfg.hp), "invalid_monster_history")
        if not value.health.alive then
            assert(not ai.aware and ai.alert_ticks == 0 and ai.rage_ticks == 0
                and ai.active_ability == 0 and ai.ability_ticks == 0
                and ai.ability_phase == "idle", "dead_monster_ability")
            for _, ticks in ipairs(ai.ability_cds) do
                assert(ticks == 0, "dead_monster_cooldown")
            end
        end
        assert(state.integer(value.ai.attack_ticks, 0, cfg.attack_ticks), "invalid_attack_ticks")
        if cfg.ai then
            assert(state.integer(ai.alert_ticks, 0, cfg.ai.disengage_ticks)
                and state.integer(ai.rage_ticks, 0, cfg.ai.rage_ticks)
                and state.integer(ai.rage_mask, 0, 2 ^ #cfg.ai.rage_thresholds - 1),
                "invalid_monster_timers")
            local active = nil
            for slot, ticks in ipairs(ai.ability_cds) do
                local bind = cfg.ai.abilities[slot]
                local ability = bind and content.abilities[bind.cfg_id]
                assert(state.integer(ticks, 0, ability and ability.cooldown or 0),
                    "invalid_ability_cooldown")
                if bind and tonumber(bind.cfg_id) == ai.active_ability then
                    active = ability
                end
            end
            if ai.ability_phase ~= "idle" then
                assert(active ~= nil and state.integer(ai.ability_ticks, 1,
                    ai.ability_phase == "windup" and active.windup or active.recover),
                    "invalid_active_ability")
            end
        else
            assert(ai.alert_ticks == 0 and ai.rage_ticks == 0 and ai.rage_mask == 0
                and ai.active_ability == 0 and ai.ability_phase == "idle",
                "unexpected_monster_ability")
            for _, ticks in ipairs(ai.ability_cds) do
                assert(ticks == 0, "unexpected_ability_cooldown")
            end
        end
        if value.ai.state == "spawn" and ai.owner_id == "0" then
            assert(value.pose.x == spawn.x and value.pose.y == spawn.y,
                "invalid_spawn_state")
        end
    end

    -- 冷路径执行完整配置语义校验；语义与结构校验全部成功后宿主才可替换状态。
    function api.validate(doc, content)
        assert(type(doc) == "table" and doc.v == 8, "invalid_state_version")
        local accepted = nil
        if doc.match_id ~= "0" then
            local reason
            accepted, reason = loadout.check(content, doc.loadout, doc.hunter_id ~= "0")
            assert(accepted ~= nil, reason or "invalid_loadout")
            assert(json.encode(accepted) == json.encode(doc.loadout), "incomplete_loadout")
        end
        local active, history = content, {}
        if accepted ~= nil then
            local value = doc.entities[doc.player_entity_id]
            assert(value ~= nil, "missing_player")
            local ids, seen = json.array({}), {}
            for _, entry in ipairs(value.demo.skills) do
                assert(content.skills[entry.cfg_id] ~= nil and not seen[entry.cfg_id],
                    "invalid_skill_state")
                seen[entry.cfg_id] = true
                if not entry.spent then
                    ids[#ids + 1] = entry.cfg_id
                end
            end
            for _, id in ipairs(accepted.skills) do
                assert(seen[id], "missing_initial_skill")
            end
            assert(not value.demo.downed or
                skill.revive(content, accepted, ids) ~= nil, "invalid_revive_state")
            active = skill.effective(content, accepted, ids)
            history = resource_history(content, value.demo.skills)
        end
        local owners, second = {}, {}
        if content.encounter then
            for _, id in ipairs(content.encounter.second_wave) do
                second[id] = true
            end
        end
        assert(doc.match_id == "0" and doc.wave == 0 or doc.match_id ~= "0"
            and state.integer(doc.wave, 1, content.encounter and 2 or 1), "invalid_wave_state")
        if doc.wave == 2 then
            assert(state.is_id(doc.bounty_id) and doc.bounty_id ~= "0"
                and not state.newer(doc.bounty_id, doc.last_item_id), "invalid_bounty_identity")
            local bounty = doc.items[doc.bounty_id]
            assert(bounty == nil or bounty.cfg_id == content.encounter.bounty_cfg_id
                and bounty.place == "Bag", "invalid_bounty_item")
        end
        for _, value in pairs(doc.entities) do
            local source = value.kind == "player" and active or content
            local cfgs = value.kind == "player" and active.players or content.monsters
            local cfg = cfgs[value.cfg_id]
            assert(cfg ~= nil, "invalid_actor_cfg")
            unit(value, cfg, source)
            if value.kind == "player" then
                assert(accepted ~= nil and value.cfg_id == accepted.player_cfg_id,
                    "invalid_player_cfg")
                player(value, active, accepted, content, history)
            else
                enemy(value, content, doc)
                if value.ai.owner_id == "0" then
                    assert(value.ai.wave == 1 or second[value.spawn_id],
                        "invalid_wave_spawn")
                else
                    assert(not owners[value.ai.owner_id], "duplicate_active_summon")
                    owners[value.ai.owner_id] = true
                end
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
        local clue_count = 0
        for index, used in ipairs(doc.scene_used) do
            local cfg = content.scenes[index]
            assert(not used or cfg.kind == "supply" or cfg.kind == "clue",
                "invalid_scene_usage")
            clue_count = clue_count + (used and cfg.kind == "clue" and 1 or 0)
            local barrel = doc.scenes_state.barrels[index]
            if doc.match_id == "0" or not cfg.barrel then
                assert(barrel.hp == 0 and barrel.fuse == 0 and not barrel.exploded,
                    "unexpected_barrel_state")
            else
                assert(state.integer(barrel.hp, 0, cfg.barrel.hp)
                    and state.integer(barrel.fuse, 0, cfg.barrel.fuse_ticks)
                    and (barrel.hp > 0 or barrel.fuse > 0 or barrel.exploded),
                    "invalid_barrel_state")
            end
        end
        local interaction = doc.scenes_state.interaction
        if interaction.index > 0 then
            local cfg = content.scenes[interaction.index]
            local player = doc.entities[doc.player_entity_id]
            assert(cfg ~= nil and cfg.interaction and cfg.interaction.mode == "channel"
                and state.integer(interaction.ticks, 1, cfg.interaction.hold_ticks)
                and not doc.scene_used[interaction.index] and doc.phase == "Playing"
                and player.health.alive and not player.demo.downed
                and not player.demo.vision and player.demo.ladder_id == 0,
                "invalid_scene_interaction")
        end
        if content.exploration and doc.match_id ~= "0" then
            local found, bit, count = false, 1, 0
            assert(doc.region_count == #content.exploration.regions
                and state.integer(doc.excluded_regions, 0,
                2 ^ #content.exploration.regions - 1), "invalid_excluded_regions")
            for _, region in ipairs(content.exploration.regions) do
                local excluded = (doc.excluded_regions // bit) % 2 == 1
                if tonumber(region.id) == doc.boss_region then
                    assert(not excluded, "boss_region_excluded")
                    for _, id in ipairs(region.boss_spawns) do
                        found = found or id == tostring(doc.boss_spawn)
                    end
                end
                count = count + (excluded and 1 or 0)
                bit = bit * 2
            end
            assert(found and count == clue_count, "invalid_exploration_state")
            for _, value in pairs(doc.entities) do
                if value.kind == "monster" and content.monsters[value.cfg_id].rank == 3 then
                    assert(value.spawn_id == tostring(doc.boss_spawn), "unselected_boss_spawn")
                end
            end
        else
            assert(doc.boss_region == 0 and doc.boss_spawn == 0 and doc.region_count == 0
                and doc.excluded_regions == 0 and clue_count == 0,
                "unexpected_exploration_state")
        end
        for _, shot in ipairs(doc.projectiles) do
            if shot.active then
                local cfg = active.tools[shot.cfg_id]
                local original = content.tools[shot.cfg_id]
                assert(cfg ~= nil and cfg.kind == "bomb"
                    and state.integer(shot.remaining, 1,
                        resource_limit(original.throw_range, "throw_range", history, 100000)),
                    "invalid_projectile")
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
                open = open or not point.legacy_gate or point.boss_spawn_id == "0"
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

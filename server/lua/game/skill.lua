-- 校验玩家天赋并计算独立有效配置；跨 Tick 的消费与复活状态由原生玩家持有。
return function(deps)
    local state = deps["framework.state"]
    local api = {}
    local supported = {["1"] = true, ["2"] = true, ["4"] = true, ["5"] = true,
        ["7"] = true, ["8"] = true, ["9"] = true, ["10"] = true, ["11"] = true,
        ["12"] = true, ["13"] = true}
    local stats = {weight = true, run_speed = true, melee_damage = true, reserve = true,
        shot_ticks = true, reload_ticks = true, medkit_ticks = true, medkit_heal = true,
        throw_range = true, revive_hp = true, revive_stamina = true, revive_full_hp = true,
        run_cost = true, movement_noise_radius = true, crate_bonus_count = true,
        tool_capacity = true}
    local attributes = {
        run_cost = {"player", "stamina_per_second", "modifier"},
        weight = {"player", "weight_point", "modifier"},
        run_speed = {"player", "mm_per_tick", "modifier"},
        movement_noise_radius = {"player", "mm", "modifier"},
        melee_damage = {"weapon", "hp", "modifier"},
        reserve = {"weapon", "round", "modifier"},
        shot_ticks = {"weapon", "tick", "modifier"},
        reload_ticks = {"weapon", "tick", "modifier"},
        medkit_heal = {"tool", "hp", "modifier"},
        throw_range = {"tool", "mm", "modifier"},
        medkit_ticks = {"tool", "tick", "modifier"},
        revive_hp = {"player", "hp", "absolute"},
        revive_stamina = {"player", "stamina", "absolute"},
        revive_full_hp = {"player", "bool", "flag"},
        crate_bonus_count = {"player", "item", "modifier"},
        tool_capacity = {"tool", "use", "modifier"}}

    -- 所有技能检查结构与参数，只有启用候选需要通过已实现处理器门禁。
    function api.validate(skills, implemented)
        assert(type(skills) == "table", "skills must be a table")
        for id, cfg in pairs(skills) do
            assert(implemented == false or supported[id],
                "unsupported player skill: " .. tostring(id))
            assert(state.fields(cfg, {"name", "target", "category", "kind", "cost", "effects"}),
                "invalid skill fields")
            assert(type(cfg.name) == "string" and #cfg.name > 0, "skill name is required")
            assert(cfg.target == "player" or cfg.target == "weapon" or cfg.target == "tool",
                "invalid skill target")
            assert(cfg.category == "numeric" or cfg.category == "mechanic",
                "invalid skill category")
            assert(cfg.kind == "passive" or cfg.kind == "once" or cfg.kind == "death",
                "invalid skill lifetime")
            assert(state.integer(cfg.cost, 0, 10000), "invalid skill cost")
            assert(state.array(cfg.effects, 1, 8), "skill effects are required")
            local seen = {}
            for _, effect in ipairs(cfg.effects) do
                assert(state.fields(effect, {"stat", "op", "value"}) and stats[effect.stat],
                    "unsupported skill effect")
                assert(implemented == false or (effect.stat ~= "run_cost"
                    and effect.stat ~= "movement_noise_radius"
                    and effect.stat ~= "crate_bonus_count" and effect.stat ~= "tool_capacity"),
                    "unimplemented skill effect")
                assert(not seen[effect.stat], "duplicate skill effect")
                seen[effect.stat] = true
                assert(effect.op == "add" or effect.op == "mul_bp", "invalid skill operation")
                assert(state.integer(effect.value, -1000000, 1000000), "invalid skill value")
                if effect.op == "mul_bp" then
                    assert(effect.value >= 0 and effect.value <= 100000, "invalid multiplier")
                end
                if string.sub(effect.stat, 1, 7) == "revive_" then
                    assert(effect.op == "add" and effect.value >= 0,
                        "revive requires explicit absolute values")
                end
                if effect.stat == "revive_full_hp" then
                    assert(effect.value == 1, "full revive effect must be one")
                end
                if effect.stat == "revive_hp" then
                    assert(effect.value > 0, "revive health must be positive")
                end
                if effect.stat == "crate_bonus_count" then
                    assert(effect.op == "add" and effect.value >= 0,
                        "crate bonus requires a nonnegative count")
                end
            end
            assert((seen.revive_hp == nil) == (seen.revive_stamina == nil),
                "revive health and stamina must both be configured")
            if seen.revive_hp then
                assert(cfg.kind == "once" and cfg.category == "mechanic"
                    and cfg.target == "player", "revive requires a one-use player mechanic")
            end
        end
        return skills
    end

    -- 属性字典与全部源行先验证，禁用行仍检查外键，但不进入运行候选。
    function api.sources(tables)
        local result, dictionary, codes = json.object({}), {}, {}
        assert(tables.Skill ~= nil and tables.Attribute ~= nil and tables.SkillEffect ~= nil,
            "skills require Attribute and SkillEffect tables")
        for id, value in pairs(tables.Attribute) do
            assert(state.integer(integer.create(id), 1, 2147483647), "invalid attribute ID")
            local registered = attributes[value.Stat]
            assert(registered and not codes[value.Stat], "unknown or duplicate attribute code")
            assert(type(value.Name) == "string" and #value.Name > 0,
                "attribute name is required")
            assert(value.Target == registered[1] and value.Unit == registered[2]
                and value.ValueMode == registered[3], "attribute semantics mismatch")
            assert(type(value.AllowedOps) == "string" and #value.AllowedOps > 0,
                "attribute operations are required")
            local ops, start = {}, 1
            for index = 1, #value.AllowedOps + 1 do
                if index > #value.AllowedOps
                    or string.sub(value.AllowedOps, index, index) == "|" then
                    local op = string.sub(value.AllowedOps, start, index - 1)
                    assert((op == "add" or op == "mul_bp") and not ops[op],
                        "invalid attribute operation")
                    assert(value.ValueMode == "modifier" or op == "add",
                        "absolute attributes require add")
                    ops[op], start = true, index + 1
                end
            end
            dictionary[id] = {source = value, ops = ops}
            codes[value.Stat] = true
        end
        local candidates = {}
        for id, value in pairs(tables.Skill) do
            assert(state.integer(integer.create(id), 1, 2147483647), "invalid skill ID")
            assert(value.Enabled == 0 or value.Enabled == 1, "invalid Skill.Enabled")
            candidates[id] = {name = value.Name, target = value.Target,
                category = value.Category, kind = value.Lifetime, cost = value.Cost,
                effects = json.array({})}
        end
        local ids = {}
        for id, value in pairs(tables.SkillEffect) do
            assert(state.integer(integer.create(id), 1, 2147483647), "invalid skill effect ID")
            ids[#ids + 1] = id
        end
        table.sort(ids)
        for _, id in ipairs(ids) do
            local value = tables.SkillEffect[id]
            assert(state.integer(value.SkillIdx, 1, 2147483647)
                and state.integer(value.AttributeId, 1, 2147483647),
                "invalid skill effect reference")
            local cfg = assert(candidates[value.SkillIdx], "unknown skill effect skill")
            local attribute = assert(dictionary[value.AttributeId], "unknown skill attribute")
            local source = attribute.source
            assert(cfg.target == source.Target, "skill attribute target mismatch")
            assert(attribute.ops[value.Op], "skill operation is not allowed by attribute")
            if source.ValueMode == "flag" then
                assert(value.Op == "add" and value.Value == 1, "invalid flag effect")
            elseif source.ValueMode == "absolute" then
                assert(value.Op == "add" and state.integer(value.Value, 0, 1000000),
                    "invalid absolute effect")
            end
            cfg.effects[#cfg.effects + 1] = {stat = source.Stat, op = value.Op, value = value.Value}
        end
        for id, cfg in pairs(candidates) do
            local key = tostring(id)
            api.validate({[key] = cfg}, false)
            if tables.Skill[id].Enabled == 1 then
                api.validate({[key] = cfg})
                result[key] = cfg
            end
        end
        return result
    end

    -- 校验所选天赋唯一性、支持范围和技能点，总是返回独立且稳定排序的列表。
    function api.check(content, player_id, ids, owned)
        local player = content.players[player_id]
        if player == nil or not state.array(ids, 0, 32) then
            return nil, "invalid_skills"
        end
        local result, seen, cost = json.array({}), {}, 0
        for _, id in ipairs(ids) do
            local cfg = content.skills[id]
            if cfg == nil or seen[id] then
                return nil, "invalid_skill_cfg"
            end
            seen[id], result[#result + 1], cost = true, id, cost + cfg.cost
        end
        if not owned and cost > player.skill_points then
            return nil, "insufficient_skill_points"
        end
        table.sort(result, function(left, right)
            return #left < #right or (#left == #right and left < right)
        end)
        return result, nil
    end

    -- 同属性先汇总固定增量和倍率增量，统一取整，避免技能顺序改变计算结果。
    local function effects(content, ids)
        assert(state.array(ids, 0, 32), "invalid active skill list")
        local result, seen = {}, {}
        for _, id in ipairs(ids) do
            assert(not seen[id], "duplicate active skill")
            seen[id] = true
            local cfg = assert(content.skills[id], "unknown active skill")
            for _, effect in ipairs(cfg.effects) do
                local value = result[effect.stat] or {add = 0, bp = 10000}
                if effect.op == "add" then
                    value.add = value.add + effect.value
                else
                    value.bp = value.bp + effect.value - 10000
                end
                result[effect.stat] = value
            end
        end
        return result
    end

    -- 有效属性超出支持范围时拒绝整个配装，不以截断掩盖错误数值。
    local function apply(values, name, base, low, high)
        local effect = values[name]
        local value = effect and (base + effect.add) * effect.bp // 10000 or base
        assert(state.integer(value, low, high), "effective attribute out of range: " .. name)
        return value
    end

    -- 建立局内只读配置副本；初始备弹仅由初始化调用消费，重算不能再次发放。
    function api.effective(content, loadout, active_ids)
        local ids = active_ids or loadout.skills or json.array({})
        local values = effects(content, ids)
        local result = json.decode(json.encode(content))
        local player = result.players[loadout.player_cfg_id]
        player.weight = apply(values, "weight", player.weight, 1, 10000)
        player.run_speed = apply(values, "run_speed", player.run_speed, 1, 1000)
        local seen = {}
        for _, choice in ipairs(loadout.weapons) do
            if not seen[choice.cfg_id] then
                seen[choice.cfg_id] = true
                local cfg = result.weapons[choice.cfg_id]
                cfg.melee_damage = apply(values, "melee_damage", cfg.melee_damage, 1, 1000000)
                cfg.reserve = apply(values, "reserve", cfg.reserve, 0, 100000)
                assert(cfg.magazine + cfg.reserve <= 100000,
                    "effective ammunition total exceeds limit")
                cfg.fire_ticks = apply(values, "shot_ticks", cfg.fire_ticks, 1, 3600)
                cfg.reload_ticks = apply(values, "reload_ticks", cfg.reload_ticks, 1, 3600)
            end
        end
        seen = {}
        for _, ids in ipairs({loadout.tools, loadout.consumables}) do
            for _, id in ipairs(ids) do
                if not seen[id] then
                    seen[id] = true
                    local cfg = result.tools[id]
                    if cfg.kind == "medkit" then
                        cfg.use_ticks = apply(values, "medkit_ticks", cfg.use_ticks, 1, 36000)
                        cfg.heal = apply(values, "medkit_heal", cfg.heal, 0, 1000000)
                    end
                    if cfg.kind == "bomb" then
                        cfg.throw_range = apply(values, "throw_range", cfg.throw_range, 1, 100000)
                    end
                end
            end
        end
        return result
    end

    -- 从当前仍有效技能求复活参数；是否消费和死亡请求去重由原生原子提交决定。
    function api.revive(content, loadout, active_ids)
        local ids = active_ids or loadout.skills or json.array({})
        local values = effects(content, ids)
        local player = content.players[loadout.player_cfg_id]
        if values.revive_hp == nil then
            return nil
        end
        local id = nil
        for _, key in ipairs(ids) do
            for _, effect in ipairs(content.skills[key].effects) do
                if effect.stat == "revive_hp" then
                    assert(id == nil, "multiple revive skills are unsupported")
                    id = key
                end
            end
        end
        local hp = apply(values, "revive_hp", 0, 1, player.hp)
        local stamina = apply(values, "revive_stamina", 0, 0, player.stamina)
        if values.revive_full_hp then
            hp = player.hp
        end
        return {hp = hp, stamina = stamina, aggro_ticks = 180, skill_id = id}
    end

    return api
end

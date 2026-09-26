-- 组装本机会话与基础战斗切片，唯一活动世界由七个同步入口访问。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local weapon = deps["game.weapon"]
    local snapshot_api = deps["game.snapshot"]
    local ai = deps["game.ai"]
    local loot = deps["game.loot"]
    local settlement = deps["game.settlement"]
    local actions = deps["game.actions"]
    local projectile = deps["game.projectile"]
    local vitals = deps["game.vitals"]
    local api = {}
    local world = nil
    local content = nil

    -- 发送统一业务拒绝，不把正常操作错误升级为脚本故障。
    local function reject(code, req_id, seq)
        net.emit("error", json.encode({v = 5, code = code, detail = "",
            req_id = req_id or "", seq = seq or "0", match_id = World.get_match_id(world)}))
    end

    -- 返回当前局的完整权威快照。
    local function snapshot()
        snapshot_api.emit()
    end

    -- 本机令牌握手后显式建立玩家，重复登录保持同一身份和当前局。
    local function login(payload)
        assert(state.fields(payload, {"v", "req_id", "player_id"}) and payload.v == 6
            and state.req(payload.req_id), "invalid login contract")
        if World.get_phase(world) == "Unauthenticated" then
            World.login(world, payload.player_id)
        end
        net.emit("login", json.encode({v = 5, req_id = payload.req_id,
            player_id = World.get_player_id(world), match_id = World.get_match_id(world),
            phase = World.get_phase(world)}))
    end

    -- 只从大厅或终态开局，重复成功请求返回原局而不重置实体。
    local function start(payload)
        assert(state.fields(payload, {"v", "req_id", "after_match_id", "match_id", "world_id"})
            and payload.v == 6
            and state.req(payload.req_id) and state.is_id(payload.after_match_id),
            "invalid start contract")
        if World.get_phase(world) == "Unauthenticated" then
            reject("not_logged_in", payload.req_id)
            return
        end
        if payload.req_id == World.get_last_req(world) then
            if payload.after_match_id ~= World.get_last_after(world) then
                reject("request_conflict", payload.req_id)
                return
            end
        elseif payload.after_match_id ~= World.get_match_id(world) then
            reject("stale_match", payload.req_id)
            return
        elseif World.get_phase(world) == "Playing" or World.get_phase(world) == "Settling"
            or World.get_paused(world) then
            reject("invalid_state", payload.req_id)
            return
        else
            world_api.start(world, content, payload)
        end
        local req_id = payload.req_id
        local match_id = World.get_match_id(world)
        local phase = World.get_phase(world)
        net.emit("start", json.encode({v = 5, req_id = req_id,
            match_id = match_id, phase = phase}))
        snapshot()
    end

    -- 验证只读上下文并取得尚无活动局的原生世界。
    function api.init(ctx_json)
        assert(world == nil, "session already initialized")
        local ctx = json.decode(ctx_json)
        assert(state.fields(ctx, {"v", "snapshot_every", "content"}) and ctx.v == 6
            and state.integer(ctx.snapshot_every, 1, 3600), "invalid context")
        assert(json.encode(ctx) == json.encode(json.decode(cfg.get())), "context mismatch")
        content = ctx.content
        assert(type(content) == "table" and content.v == 4
            and content.tick_hz == 60,
            "invalid content")
        world = world_api.new(content)
        assert(world_api.valid(world, content), "invalid initial state")
        diagnostics.log("combat session initialized")
        return true
    end

    -- 分发已鉴权宿主送达的登录、开局与暂停事件。
    function api.on_event(event_id, payload_json)
        assert(world ~= nil, "session not initialized")
        local payload = json.decode(payload_json)
        if event_id == 2 then
            login(payload)
        elseif event_id == 3 then
            start(payload)
        elseif event_id == 4 then
            assert(state.fields(payload, {"v", "paused"}) and payload.v == 6
                and type(payload.paused) == "boolean", "invalid pause contract")
            World.set_paused(world, payload.paused)
            world_api.clear_input(world)
        elseif event_id == 5 then
            assert(state.fields(payload,
                {"v", "req_id", "kind", "slot", "target_id", "action_seq"})
                and payload.v == 6 and state.req(payload.req_id)
                and state.integer(payload.slot, 0, 8) and state.is_id(payload.target_id)
                and state.is_id(payload.action_seq), "invalid action contract")
            local error = actions.apply(world, content, payload)
            if error ~= nil then
                reject(error, payload.req_id, payload.action_seq)
            else
                net.emit("action", json.encode({v = 5, req_id = payload.req_id,
                    world_id = World.get_world_id(world),
                    match_id = World.get_match_id(world),
                    action_seq = payload.action_seq}))
            end
        else
            error("unsupported event")
        end
        return true
    end

    -- 玩家伤害先于怪物反击；伤亡取消使用后才允许治疗和自然回复，再裁定终态。
    function api.tick(tick_id, dt_seconds)
        assert(world ~= nil and type(tick_id) == "integer" and tick_id > 0,
            "invalid tick")
        assert(type(dt_seconds) == "number" and math.abs(dt_seconds - 1.0 / 60.0) < 0.000001,
            "invalid timestep")
        local tick_text = tostring(tick_id)
        assert(tick_text == World.get_tick_id(world), "native tick mismatch")
        if World.get_phase(world) == "Playing" and not World.get_paused(world) then
            local player_id = World.find_player(world, World.get_player_id(world))
            local player = world_api.find(world, player_id)
            local moved, stamina_reset = movement.player(player, content)
            ai.move(world, content)
            weapon.step(world, content, player)
            stamina_reset = weapon.melee(world, content, player) or stamina_reset
            projectile.step(world, content)
            ai.attack(world, content)
            actions.finish(world, content, player)
            vitals.step(world, content, player, stamina_reset)
            loot.step(world, content)
            world_api.flush(world)
            settlement.step(world, content)
            Player.set_jump(player.controls, false)
            Player.set_fire_once(player.controls, false)
            Player.set_reload(player.controls, false)
            Player.set_melee(player.controls, false)
        end
        state.gc_step()
        return true
    end

    -- 导出全部权威状态，包含输入边沿、冷却和最近开局幂等记录。
    function api.export_state()
        assert(world_api.valid(world, content), "invalid session state")
        return World.save(world)
    end

    -- 完整验证独立候选之后才替换活动世界。
    function api.import_state(snapshot_json)
        World.load(world, snapshot_json)
        return true
    end

    -- 检查当前原生状态，不发送网络输出。
    function api.validate_state()
        assert(world_api.valid(world, content), "invalid session state")
        return true
    end

    -- 清理脚本会话视图，原生宿主负责排空持久化与释放世界资源。
    function api.shutdown(reason)
        assert(type(reason) == "string", "invalid shutdown reason")
        world = nil
        content = nil
        return true
    end

    return api
end

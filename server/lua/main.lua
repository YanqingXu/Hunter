-- 组装本机会话与基础战斗切片，唯一活动世界由七个同步入口访问。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local weapon = deps["game.weapon"]
    local snapshot_api = deps["game.snapshot"]
    local ai = deps["game.ai"]
    local api = {}
    local world = nil
    local content = nil

    -- 发送统一业务拒绝，不把正常操作错误升级为脚本故障。
    local function reject(code, req_id, seq)
        net.emit("error", json.encode({v = 3, code = code, detail = "",
            req_id = req_id or "", seq = seq or "0", match_id = World.get_match_id(world)}))
    end

    -- 返回当前局的完整权威快照。
    local function snapshot()
        snapshot_api.emit()
    end

    -- 本机令牌握手后显式建立玩家，重复登录保持同一身份和当前局。
    local function login(payload)
        assert(state.fields(payload, {"v", "req_id"}) and payload.v == 4
            and state.req(payload.req_id), "invalid login contract")
        if World.get_phase(world) == "Unauthenticated" then
            World.login(world)
        end
        net.emit("login", json.encode({v = 3, req_id = payload.req_id,
            player_id = World.get_player_id(world), match_id = World.get_match_id(world),
            phase = World.get_phase(world)}))
    end

    -- 只从大厅或终态开局，重复成功请求返回原局而不重置实体。
    local function start(payload)
        assert(state.fields(payload, {"v", "req_id", "after_match_id"}) and payload.v == 4
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
        elseif World.get_phase(world) == "Playing" or World.get_paused(world) then
            reject("invalid_state", payload.req_id)
            return
        else
            world_api.start(world, content, payload)
        end
        local req_id = payload.req_id
        local match_id = World.get_match_id(world)
        local phase = World.get_phase(world)
        net.emit("start", json.encode({v = 3, req_id = req_id,
            match_id = match_id, phase = phase}))
        snapshot()
    end

    -- 验证只读上下文并取得尚无活动局的原生世界。
    function api.init(ctx_json)
        assert(world == nil, "session already initialized")
        local ctx = json.decode(ctx_json)
        assert(state.fields(ctx, {"v", "snapshot_every", "content"}) and ctx.v == 4
            and state.integer(ctx.snapshot_every, 1, 3600), "invalid context")
        assert(json.encode(ctx) == json.encode(json.decode(cfg.get())), "context mismatch")
        content = ctx.content
        assert(type(content) == "table" and content.v == 2 and content.tick_hz == 60,
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
            assert(state.fields(payload, {"v", "paused"}) and payload.v == 4
                and type(payload.paused) == "boolean", "invalid pause contract")
            World.set_paused(world, payload.paused)
            world_api.clear_input(world)
        else
            error("unsupported event")
        end
        return true
    end

    -- 固定先移动、玩家射击、怪物攻击再裁定终态，只有 Playing 推进战斗。
    function api.tick(tick_id, dt_seconds)
        assert(world ~= nil and type(tick_id) == "integer" and tick_id > 0,
            "invalid tick")
        assert(type(dt_seconds) == "number" and math.abs(dt_seconds - 1.0 / 60.0) < 0.000001,
            "invalid timestep")
        local tick_text = tostring(tick_id)
        assert(tick_text == World.get_tick_id(world), "native tick mismatch")
        if World.get_phase(world) == "Playing" and not World.get_paused(world) then
            local player = world_api.find(world, World.get_player_entity_id(world))
            local controls = player.controls
            local move_x = Player.get_move_x(controls)
            local jump = Player.get_jump(controls)
            movement.step(player, content.players[player.cfg_id], content, move_x, jump)
            ai.move(world, content)
            weapon.step(world, content)
            ai.attack(world, content)
            world_api.flush(world)
            world_api.finish(world)
            Player.set_jump(player.controls, false)
            Player.set_fire_once(player.controls, false)
            Player.set_reload(player.controls, false)
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

    -- 清理脚本会话，持久化不属于本轮切片。
    function api.shutdown(reason)
        assert(type(reason) == "string", "invalid shutdown reason")
        world = nil
        content = nil
        return true
    end

    return api
end

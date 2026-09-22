-- 组装本机会话与基础战斗切片，唯一活动世界由七个同步入口访问。
return function(deps)
    local state = deps["framework.state"]
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local ai = deps["game.ai"]
    local api = {}
    local world = nil
    local content = nil
    local snapshot_every = 3

    -- 发送统一业务拒绝，不把正常操作错误升级为脚本故障。
    local function reject(code, req_id, seq)
        net.emit("error", json.encode({v = 2, code = code, detail = "",
            req_id = req_id or "", seq = seq or "0", match_id = world.match_id}))
    end

    -- 返回当前局的完整权威快照。
    local function snapshot()
        net.emit("snapshot", json.encode(world_api.snapshot(world)))
    end

    -- 本机令牌握手后显式建立玩家，重复登录保持同一身份和当前局。
    local function login(payload)
        assert(state.fields(payload, {"v", "req_id"}) and payload.v == 2
            and state.req(payload.req_id), "invalid login contract")
        if world.phase == "Unauthenticated" then
            world.player_id = "1"
            world.phase = "Lobby"
        end
        net.emit("login", json.encode({v = 2, req_id = payload.req_id,
            player_id = world.player_id, match_id = world.match_id, phase = world.phase}))
    end

    -- 只从大厅或终态开局，重复成功请求返回原局而不重置实体。
    local function start(payload)
        assert(state.fields(payload, {"v", "req_id", "after_match_id"}) and payload.v == 2
            and state.req(payload.req_id) and state.is_id(payload.after_match_id),
            "invalid start contract")
        if world.phase == "Unauthenticated" then
            reject("not_logged_in", payload.req_id)
            return
        end
        local last = world.last_start
        if payload.req_id == last.req_id then
            if payload.after_match_id ~= last.after_match_id then
                reject("request_conflict", payload.req_id)
                return
            end
        elseif payload.after_match_id ~= world.match_id then
            reject("stale_match", payload.req_id)
            return
        elseif world.phase == "Playing" or world.paused then
            reject("invalid_state", payload.req_id)
            return
        else
            world_api.start(world, content, payload)
        end
        net.emit("start", json.encode({v = 2, req_id = payload.req_id,
            match_id = world.match_id, phase = world.phase}))
        snapshot()
    end

    -- 消费连接单调序号并锁存输入意图；真实动作只在下一次 Tick 裁定。
    local function input(payload)
        assert(state.fields(payload, {"v", "seq", "match_id", "applied_tick", "move_x",
            "aim_x", "aim_y", "jump", "fire", "reload"}) and payload.v == 2
            and state.is_id(payload.seq) and payload.seq ~= "0"
            and state.is_id(payload.match_id) and state.is_tick(payload.applied_tick)
            and state.integer(payload.move_x, -1, 1)
            and state.integer(payload.aim_x, -1000, 1000)
            and state.integer(payload.aim_y, -1000, 1000)
            and (payload.aim_x ~= 0 or payload.aim_y ~= 0)
            and type(payload.jump) == "boolean" and type(payload.fire) == "boolean"
            and type(payload.reload) == "boolean", "invalid input contract")
        if not state.newer(payload.seq, world.seq) then
            reject("stale_input", "", payload.seq)
            return
        end
        world.seq = payload.seq
        if world.phase == "Unauthenticated" then
            reject("not_logged_in", "", payload.seq)
            return
        elseif payload.match_id ~= world.match_id then
            reject("stale_match", "", payload.seq)
            return
        elseif world.phase ~= "Playing" or world.paused then
            reject("invalid_state", "", payload.seq)
            return
        end
        assert(payload.applied_tick == state.next_id(world.tick_id), "invalid applied tick")
        local controls = world.controls
        controls.move_x = payload.move_x
        controls.aim_x = payload.aim_x
        controls.aim_y = payload.aim_y
        controls.jump = controls.jump or payload.jump
        controls.fire = payload.fire
        controls.fire_once = controls.fire_once or payload.fire
        controls.reload = controls.reload or payload.reload
        net.emit("ack", json.encode({v = 2, seq = world.seq, match_id = world.match_id,
            applied_tick = payload.applied_tick}))
    end

    -- 验证只读上下文并初始化无活动局的纯数据世界。
    function api.init(ctx_json)
        assert(world == nil, "session already initialized")
        local ctx = json.decode(ctx_json)
        assert(state.fields(ctx, {"v", "snapshot_every", "content"}) and ctx.v == 2
            and state.integer(ctx.snapshot_every, 1, 3600), "invalid context")
        assert(json.encode(ctx) == json.encode(json.decode(cfg.get())), "context mismatch")
        content = ctx.content
        assert(type(content) == "table" and content.v == 1 and content.tick_hz == 60,
            "invalid content")
        snapshot_every = ctx.snapshot_every
        world = world_api.new(content)
        assert(world_api.valid(world, content), "invalid initial state")
        diagnostics.log("combat session initialized")
        return true
    end

    -- 只分发已鉴权宿主送达的输入、登录、开局与暂停事件。
    function api.on_event(event_id, payload_json)
        assert(world ~= nil, "session not initialized")
        local payload = json.decode(payload_json)
        if event_id == 1 then
            input(payload)
        elseif event_id == 2 then
            login(payload)
        elseif event_id == 3 then
            start(payload)
        elseif event_id == 4 then
            assert(state.fields(payload, {"v", "paused"}) and payload.v == 2
                and type(payload.paused) == "boolean", "invalid pause contract")
            world.paused = payload.paused
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
        assert(state.newer(tick_text, world.tick_id), "stale tick")
        world.tick_id = tick_text
        local previous_phase = world.phase
        if world.phase == "Playing" and not world.paused then
            movement.step(world.entities[1], content.player, content,
                world.controls.move_x, world.controls.jump)
            ai.move(world, content)
            combat.step(world, content)
            ai.attack(world, content)
            world_api.finish(world)
            world.controls.jump = false
            world.controls.fire_once = false
            world.controls.reload = false
        end
        if world.phase ~= previous_phase or tick_id % snapshot_every == 0 then
            snapshot()
        end
        return true
    end

    -- 导出全部权威状态，包含输入边沿、冷却和最近开局幂等记录。
    function api.export_state()
        assert(world_api.valid(world, content), "invalid session state")
        return json.encode(world)
    end

    -- 完整验证独立候选之后才替换活动世界。
    function api.import_state(snapshot_json)
        local candidate = json.decode(snapshot_json)
        assert(world_api.valid(candidate, content), "invalid imported state")
        world = candidate
        return true
    end

    -- 检查当前纯数据状态，不发送网络输出。
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

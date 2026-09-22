-- 组装框架探针并持有唯一纯数据状态，七个入口由构建工具导出。
return function(deps)
    local state = deps["framework.state"]
    local api = {}
    local world = nil
    local snapshot_every = 3

    -- 根据有版本的只读上下文创建全新会话。
    function api.init(ctx_json)
        assert(world == nil, "session already initialized")
        local ctx = json.decode(ctx_json)
        local host_cfg = json.decode(cfg.get())
        assert(type(ctx) == "table" and ctx.v == 1, "invalid context version")
        assert(type(ctx.snapshot_every) == "integer" and ctx.snapshot_every >= 1
            and ctx.snapshot_every <= 3600, "invalid snapshot interval")
        assert(host_cfg.v == ctx.v and host_cfg.snapshot_every == ctx.snapshot_every,
            "context differs from host config")
        snapshot_every = ctx.snapshot_every
        world = state.new()
        diagnostics.log("framework session initialized")
        return true
    end

    -- 处理已在 Tick 边界排序的输入，确认输出由宿主调用事务暂存。
    function api.on_event(event_id, payload_json)
        assert(world ~= nil, "session not initialized")
        assert(event_id == 1, "unsupported event")
        local ack = state.input(world, json.decode(payload_json))
        net.emit("ack", json.encode(ack))
        return true
    end

    -- 推进一次单调 Tick，并按配置间隔生成框架状态快照。
    function api.tick(tick_id, dt_seconds)
        assert(world ~= nil, "session not initialized")
        assert(type(tick_id) == "integer" and tick_id > 0, "invalid tick")
        assert(type(dt_seconds) == "number" and dt_seconds > 0
            and dt_seconds <= 1, "invalid timestep")
        local tick_text = tostring(tick_id)
        assert(#tick_text > #world.tick_id
            or (#tick_text == #world.tick_id and tick_text > world.tick_id), "stale tick")
        world.tick_id = tick_text

        if tick_id % snapshot_every == 0 then
            net.emit("snapshot", json.encode(world))
        end

        return true
    end

    -- 导出经过校验的纯数据快照，返回拥有内容的 JSON 字符串。
    function api.export_state()
        assert(state.valid(world), "invalid session state")
        return json.encode(world)
    end

    -- 完整校验候选快照后才替换活动状态。
    function api.import_state(snapshot_json)
        local candidate = json.decode(snapshot_json)
        assert(state.valid(candidate), "invalid imported state")
        world = candidate
        return true
    end

    -- 只检查状态，不发送消息或修改活动会话。
    function api.validate_state()
        assert(state.valid(world), "invalid session state")
        return true
    end

    -- 释放脚本状态，不依赖退出回调持久化数据。
    function api.shutdown(reason)
        assert(type(reason) == "string", "invalid shutdown reason")
        world = nil
        return true
    end

    return api
end

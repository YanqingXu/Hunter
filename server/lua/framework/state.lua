-- 维护可导出的纯数据状态规则；工厂和函数不保存可变世界状态。
return function(deps)
    local api = {}
    local max_seq = "18446744073709551615"
    local max_tick = "9223372036854775807"

    -- 验证规范十进制字符串，避免 uint64 经浮点转换损失精度。
    function api.is_id(value, limit)
        if type(value) ~= "string" or #value == 0 or #value > #limit then
            return false
        end

        if value == "0" then
            return true
        end

        return string.match(value, "^[1-9][0-9]*$") ~= nil
            and (#value < #limit or value <= limit)
    end

    -- 校验状态所有字段；非法状态不会被导入到活动会话。
    function api.valid(world)
        if type(world) ~= "table" or world.v ~= 1 then
            return false
        end

        if not api.is_id(world.seq, max_seq) or not api.is_id(world.tick_id, max_tick) then
            return false
        end

        if type(world.count) ~= "integer" or world.count < -1000000000
            or world.count > 1000000000 then
            return false
        end

        for key, value in pairs(world) do
            if key ~= "v" and key ~= "seq" and key ~= "tick_id" and key ~= "count" then
                return false
            end
        end

        return true
    end

    -- 创建只含 JSON 可表达数据的初始状态。
    function api.new()
        return {v = 1, tick_id = "0", seq = "0", count = 0}
    end

    -- 校验新输入；序号通过十进制长度和字典序比较保持精确。
    function api.input(world, payload)
        assert(type(payload) == "table" and payload.v == 1, "invalid input version")
        assert(api.is_id(payload.seq, max_seq) and payload.seq ~= "0", "invalid input seq")
        assert(#payload.seq > #world.seq
            or (#payload.seq == #world.seq and payload.seq > world.seq), "stale input seq")
        assert(type(payload.value) == "integer" and payload.value >= -1000
            and payload.value <= 1000, "invalid input value")
        local count = world.count + payload.value
        assert(count >= -1000000000 and count <= 1000000000, "count overflow")
        world.count = count
        world.seq = payload.seq
        return {v = 1, seq = world.seq, count = world.count}
    end

    return api
end

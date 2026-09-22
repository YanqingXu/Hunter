-- 提供纯数据字段、范围与精确十进制标识校验，不持有活动世界。
return function(deps)
    local api = {}
    local max_id = "18446744073709551615"
    local max_tick = "9223372036854775807"

    -- 验证规范十进制字符串，保持完整的整数精度。
    function api.is_id(value, limit)
        limit = limit or max_id
        if type(value) ~= "string" or #value == 0 or #value > #limit then
            return false
        end
        return value == "0" or (string.match(value, "^[1-9][0-9]*$") ~= nil
            and (#value < #limit or value <= limit))
    end

    -- 验证宿主可表达的 Tick 标识。
    function api.is_tick(value)
        return api.is_id(value, max_tick)
    end

    -- 按长度与字典序比较两个已校验的十进制标识。
    function api.newer(value, previous)
        return #value > #previous or (#value == #previous and value > previous)
    end

    -- 对纯字符串标识加一，避免 uint64 穿过浮点数。
    function api.next_id(value)
        assert(api.is_id(value) and value ~= max_id, "identifier exhausted")
        local result = ""
        local carry = true
        for i = #value, 1, -1 do
            local digit = string.sub(value, i, i)
            if carry then
                if digit == "9" then
                    digit = "0"
                else
                    digit = tostring(tonumber(digit) + 1)
                    carry = false
                end
            end
            result = digit .. result
        end
        if carry then
            result = "1" .. result
        end
        return result
    end

    -- 检查精确整数的闭区间。
    function api.integer(value, low, high)
        return type(value) == "integer" and value >= low and value <= high
    end

    -- 检查对象恰好包含约定字段；名字列表由模块内无重复的字段常量构造。
    function api.fields(value, names)
        if type(value) ~= "table" then
            return false
        end
        for _, name in ipairs(names) do
            if value[name] == nil then
                return false
            end
        end
        local count = 0
        for key, item in pairs(value) do
            count = count + 1
        end
        return count == #names
    end

    -- 检查连续数组，禁止洞和混合键。
    function api.array(value, low, high)
        if type(value) ~= "table" or #value < low or #value > high then
            return false
        end
        local count = 0
        for key, item in pairs(value) do
            -- 固定运行时的 pairs 会把数组键返回为 number，只接受可无损转回的整数键。
            local index = integer.create(key)
            if index == nil or index < 1 or index > #value then
                return false
            end
            count = count + 1
        end
        return count == #value
    end

    -- 验证请求标识的非空长度。
    function api.req(value)
        return type(value) == "string" and #value >= 1 and #value <= 128
    end

    return api
end

-- 仅请求原生快照，不在 Lua 遍历实体或构造网络 JSON。
return function(deps)
    local api = {}

    -- 请求开局即时快照，按调用级输出事务提交。
    function api.emit()
        net.snapshot()
    end

    return api
end

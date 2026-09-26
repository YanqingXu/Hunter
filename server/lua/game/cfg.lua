-- 从显式注入的 Lua 数据表加载并校验唯一只读玩法配置，禁止隐式文件或旧内容回退。
return function(deps)
    local source = deps["cfg.tables"]
    local build = deps["game.cfg_build"]
    local check = deps["game.cfg_check"]
    local fixture = deps["game.cfg_fixture"]
    local content = nil
    local api = {}

    -- 构建校验和运行时入口共享完全相同的字段与玩法语义检查。
    function api.validate(value)
        return check.validate(value)
    end

    -- 首次按来源构造内容，后续调用只返回已通过校验的只读配置。
    function api.load()
        if content == nil then
            if source.Fixture ~= nil then
                content = api.validate(fixture.upgrade(source.Fixture))
            else
                content = api.validate(build.build(source))
            end
        end
        return content
    end

    return api
end

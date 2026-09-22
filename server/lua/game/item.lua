-- 提供最小物品实例接口；物品由原生世界拥有，不含拾取或背包规则。
return function(deps)
    local api = {}

    -- 创建具有非零配置引用和正数量的物品实例。
    function api.new(cfg_id, count)
        return Item.new(cfg_id, count)
    end

    -- 按实例身份查询物品，已销毁对象返回空值。
    function api.find(world, id)
        if not World.has_item(world, id) then
            return nil
        end
        return Item.find(id)
    end

    -- 销毁物品，由 C++ 同步使旧句柄失效。
    function api.remove(world, id)
        return World.remove_item(world, id)
    end

    return api
end

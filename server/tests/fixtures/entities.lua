-- 隔离夹具使用真实原生句柄验证生命周期、物品和失效行为，不进入正式入口。
return function(deps)
    local game = deps["main"]
    local world_api = deps["game.world"]
    local weapon = deps["game.weapon"]
    local ai = deps["game.ai"]
    local damage = deps["game.damage"]
    local movement = deps["game.movement"]
    local api = {}
    local content = nil

    -- 校验延迟移除、组件代次、重开和原生物品实例。
    local function lifecycle(world)
        local player = world_api.find(world, World.get_player_entity_id(world))
        local removed = world_api.find(world, "2")
        local survivor = world_api.find(world, "3")
        local ref = {match_id = World.get_match_id(world), entity_id = removed.id}
        assert(world_api.resolve(world, ref).id == removed.id, "valid reference")
        local before = World.save(world)
        local value, code = world_api.spawn(world, content, "monster", "999")
        assert(value == nil and code == "invalid_spawn" and World.save(world) == before,
            "invalid spawn preserves allocator")
        assert(not world_api.remove(world, player.id), "player cannot be removed")
        Entity.set_x(removed.pose, Entity.get_x(player.pose) + 800)
        assert(world_api.remove(world, removed.id), "first removal accepted")
        assert(not world_api.remove(world, removed.id), "duplicate removal ignored")
        assert(not world_api.remove(world, "999"), "unknown removal ignored")
        assert(world_api.find(world, removed.id) == nil, "pending entity unavailable")
        local hp = Unit.get_hp(removed.health)
        local pos = Entity.get_x(removed.pose)
        movement.step(removed, content.monsters[removed.cfg_id], content, 1, false)
        damage.apply(world, player, removed, 1)
        assert(Unit.get_hp(removed.health) == hp and Entity.get_x(removed.pose) == pos,
            "pending entity excluded")
        local spawn_id = removed.spawn_id
        world_api.flush(world)
        assert(not pcall(function() return Unit.get_hp(removed.health) end), "removed handle stale")
        assert(World.entity_id(world, 2) == survivor.id, "survivor order preserved")
        local fresh = world_api.spawn(world, content, "monster", spawn_id)
        assert(fresh.id == "4", "fresh identity")
        local saved = json.decode(World.save(world))
        saved.entity_ids = json.array({survivor.id, player.id, fresh.id})
        World.load(world, json.encode(saved))
        assert(not pcall(function() return Player.get_move_x(player.controls) end),
            "import stales components")
        player = world_api.find(world, World.get_player_entity_id(world))
        survivor = world_api.find(world, "3")
        local old_x = Entity.get_x(survivor.pose)
        ai.move(world, content)
        assert(Entity.get_x(survivor.pose) == old_x + content.monsters[survivor.cfg_id].speed,
            "patrol uses spawn identity after reorder")
        local item = Item.new("42", 2)
        Item.set_count(item, 3)
        assert(Item.get_count(Item.find(Item.get_id(item))) == 3, "one native item state")
        assert(not pcall(function() Item.set_count(item, 0) end), "positive item count")
        assert(not pcall(function() item.id = "9" end), "identity readonly")
        assert(not pcall(function() World.contains(item, "1") end), "wrong class rejected")
        local item_id = Item.get_id(item)
        saved = World.save(world)
        World.load(world, saved)
        assert(not pcall(function() return Item.get_count(item) end), "import stales item")
        item = Item.find(item_id)
        assert(Item.get_count(item) == 3, "item roundtrip")
        assert(World.remove_item(world, item_id), "remove item")
        assert(not pcall(function() return Item.get_count(item) end), "destroyed item stale")
        local old_player = Player.find(World.get_player_entity_id(world))
        local old_ref = {match_id = World.get_match_id(world),
            entity_id = World.get_player_entity_id(world)}
        world_api.start(world, content,
            {req_id = "restart", after_match_id = World.get_match_id(world)})
        assert(not pcall(function() return Player.get_move_x(old_player) end),
            "restart stales player")
        assert(world_api.resolve(world, old_ref) == nil, "old match rejected")
        assert(World.valid(world), "valid restarted world")
    end

    -- 验证完整容量和 uint64 分配边界，失败不消费身份。
    local function capacity(world)
        assert(World.count(world) == 64, "full world")
        local before = World.save(world)
        local value, code = world_api.spawn(world, content, "monster",
            content.map.enemies[1].spawn_id)
        assert(value == nil and code == "entity_capacity" and World.save(world) == before,
            "capacity atomic")
        assert(world_api.remove(world, World.get_last_entity_id(world)), "remove last entity")
        world_api.flush(world)
        local saved = json.decode(World.save(world))
        saved.last_entity_id = "18446744073709551614"
        World.load(world, json.encode(saved))
        local last = world_api.spawn(world, content, "monster", content.map.enemies[1].spawn_id)
        assert(last.id == "18446744073709551615", "uint64 maximum")
        assert(world_api.remove(world, last.id), "remove maximum")
        world_api.flush(world)
        before = World.save(world)
        value, code = world_api.spawn(world, content, "monster", content.map.enemies[1].spawn_id)
        assert(value == nil and code == "entity_id_exhausted" and World.save(world) == before,
            "exhaustion atomic")
    end

    -- 复用正式初始化并保存只读配置。
    function api.init(ctx_json)
        content = json.decode(ctx_json).content
        return game.init(ctx_json)
    end

    -- 测试专属入口只存在于隔离制品。
    function api.on_event(event_id, payload_json)
        if event_id ~= 5 and event_id ~= 6 and event_id ~= 7 and event_id ~= 8 then
            return game.on_event(event_id, payload_json)
        end
        local world = World.current()
        if event_id == 8 then
            net.event("shot", "1", "0", 0, 0, 0)
            assert(not pcall(function() net.event("hit", "1", "2", 0, 0, -1) end),
                "invalid native event rejected")
        elseif event_id == 5 then
            lifecycle(world)
        elseif event_id == 7 then
            capacity(world)
        else
            for index = 1, 8 do
                if World.count(world) < 64 then
                    assert(world_api.spawn(world, content, "monster",
                        content.map.enemies[1].spawn_id) ~= nil, "spawn within capacity")
                end
            end
        end
        return true
    end

    api.tick = game.tick
    api.export_state = game.export_state
    api.import_state = game.import_state
    api.validate_state = game.validate_state
    api.shutdown = game.shutdown
    return api
end

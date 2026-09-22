-- 仅测试制品包装正式入口，直接验证实体生命周期；不进入生产模块清单。
return function(deps)
    local game = deps["main"]
    local world_api = deps["game.world"]
    local weapon = deps["game.weapon"]
    local ai = deps["game.ai"]
    local damage = deps["game.damage"]
    local snapshot = deps["game.snapshot"]
    local movement = deps["game.movement"]
    local api = {}
    local content = nil

    -- 校验标记立即生效、移除不影响其他身份以及同出生点再次分配新 ID。
    local function lifecycle(world)
        local player = world_api.find(world, world.player_entity_id)
        local removed = world_api.find(world, "2")
        local survivor = world_api.find(world, "3")
        local ref = {match_id = world.match_id, entity_id = removed.id}
        assert(world_api.resolve(world, ref) == removed, "valid reference")
        local before = json.encode(world)
        local value, code = world_api.spawn(world, content, "monster", "999")
        assert(value == nil and code == "invalid_spawn" and json.encode(world) == before,
            "invalid spawn preserves world and allocator")
        assert(not world_api.remove(world, player.id), "player cannot be removed")
        removed.pose.x = player.pose.x + 800
        assert(world_api.remove(world, removed.id), "first removal accepted")
        assert(not world_api.remove(world, removed.id), "duplicate removal ignored")
        assert(not world_api.remove(world, "999"), "unknown removal ignored")
        assert(world_api.find(world, removed.id) == nil
            and world_api.resolve(world, ref) == nil, "pending entity unavailable")
        assert(#snapshot.make(world).entities == 2, "pending entity excluded from snapshot")
        local hp = removed.health.hp
        local player_hp = player.health.hp
        local pos = removed.pose.x
        movement.step(removed, content.monsters[removed.cfg_id], content, 1, false)
        player.controls.fire = true
        weapon.step(world, content)
        player.controls.fire = false
        damage.apply(world, player, removed, 1)
        ai.move(world, content)
        ai.attack(world, content)
        assert(removed.health.hp == hp and removed.pose.x == pos and player.health.hp == player_hp,
            "pending entity excluded from damage movement and attacks")
        world_api.flush(world)
        assert(world.entities[removed.id] == nil and world.entity_ids[2] == survivor.id,
            "flush preserves survivor order")
        local fresh = world_api.spawn(world, content, "monster", removed.spawn_id)
        assert(fresh ~= nil and fresh.id == "4" and fresh.spawn_id == removed.spawn_id,
            "same spawn allocates fresh identity")
        world.entity_ids = json.array({survivor.id, player.id, fresh.id})
        local old_x = survivor.pose.x
        ai.move(world, content)
        assert(world_api.find(world, world.player_entity_id) == player,
            "player identity does not depend on array position")
        assert(survivor.pose.x == old_x + content.monsters[survivor.cfg_id].speed,
            "survivor uses original spawn patrol after removal and reorder")
        assert(world_api.valid(world, content), "lifecycle produces valid world")
        local old_ref = {match_id = world.match_id, entity_id = player.id}
        world_api.start(world, content, {req_id = "restart", after_match_id = world.match_id})
        assert(world_api.resolve(world, old_ref) == nil, "old match reference rejected")
        assert(world_api.valid(world, content), "restarted world valid")
    end

    -- 验证容量和完整 uint64 分配边界，任何失败均不消耗身份或修改集合。
    local function capacity(world)
        assert(#world.entity_ids == 64, "fixture filled world")
        local before = json.encode(world)
        local value, code = world_api.spawn(world, content, "monster",
            content.map.enemies[1].spawn_id)
        assert(value == nil and code == "entity_capacity" and json.encode(world) == before,
            "capacity failure is atomic")
        assert(world_api.valid(world, content), "full world valid")
        local old_id = world.last_entity_id
        assert(world_api.remove(world, old_id), "remove last entity")
        world_api.flush(world)
        local fresh = world_api.spawn(world, content, "monster", content.map.enemies[1].spawn_id)
        assert(fresh.id ~= old_id, "removed identity never reused")
        assert(world_api.remove(world, fresh.id), "make one allocator test slot")
        world_api.flush(world)
        world.last_entity_id = "18446744073709551614"
        local last = world_api.spawn(world, content, "monster", content.map.enemies[1].spawn_id)
        assert(last.id == "18446744073709551615", "exact uint64 maximum allocated")
        assert(world_api.remove(world, last.id), "free slot after final identity")
        world_api.flush(world)
        before = json.encode(world)
        value, code = world_api.spawn(world, content, "monster", content.map.enemies[1].spawn_id)
        assert(value == nil and code == "entity_id_exhausted" and json.encode(world) == before,
            "allocator exhaustion is atomic")
        assert(world_api.valid(world, content), "maximum allocator state valid")
    end

    -- 复用正式初始化，并保留只读内容供测试调用正式功能模块。
    function api.init(ctx_json)
        content = json.decode(ctx_json).content
        return game.init(ctx_json)
    end

    -- 测试专属事件只存在于隔离夹具，普通事件继续走正式分发。
    function api.on_event(event_id, payload_json)
        if event_id ~= 5 and event_id ~= 6 and event_id ~= 7 then
            return game.on_event(event_id, payload_json)
        end
        local world = json.decode(game.export_state())
        if event_id == 5 then
            lifecycle(world)
        elseif event_id == 7 then
            capacity(world)
        else
            for i = 1, 8 do
                if #world.entity_ids < 64 then
                    assert(world_api.spawn(world, content, "monster",
                        content.map.enemies[1].spawn_id) ~= nil, "spawn within capacity")
                end
            end
        end
        return game.import_state(json.encode(world))
    end

    api.tick = game.tick
    api.export_state = game.export_state
    api.import_state = game.import_state
    api.validate_state = game.validate_state
    api.shutdown = game.shutdown
    return api
end

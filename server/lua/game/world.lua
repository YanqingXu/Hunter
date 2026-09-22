-- 管理本机玩家、局次与可导出的唯一世界，提供严格校验和可靠玩法事件。
return function(deps)
    local state = deps["framework.state"]
    local movement = deps["game.movement"]
    local api = {}
    local entity_fields = {"id", "kind", "x", "y", "vx", "vy", "hp", "max_hp", "ammo",
        "reserve", "reload_ticks", "grounded", "alive", "facing", "ai"}
    local saved_fields = {"id", "kind", "x", "y", "vx", "vy", "hp", "max_hp", "ammo",
        "reserve", "reload_ticks", "grounded", "alive", "facing", "ai", "shot_ticks",
        "attack_ticks"}

    -- 创建中立输入，暂停和新局都使用同一清理规则。
    function api.clear_input(world)
        world.controls = {move_x = 0, aim_x = 1000, aim_y = 0, jump = false,
            fire = false, fire_once = false, reload = false}
    end

    -- 创建等待本机会话登录的纯数据世界。
    function api.new(content)
        local world = {v = 2, tick_id = "0", seq = "0", match_id = "0",
            player_id = "0", event_id = "0", phase = "Unauthenticated", paused = false,
            content_key = json.encode(content), entities = json.array(),
            last_start = {req_id = "", after_match_id = "0", match_id = "0"}}
        api.clear_input(world)
        return world
    end

    -- 创建一名具有完整网络字段与局内冷却的角色。
    local function actor(id, kind, spawn, shape, content)
        local entity = {id = id, kind = kind, x = spawn.x, y = spawn.y, vx = 0, vy = 0,
            hp = shape.hp, max_hp = shape.hp, ammo = 0, reserve = 0, reload_ticks = 0,
            grounded = false, alive = true, facing = 1, ai = "patrol", shot_ticks = 0,
            attack_ticks = 0}
        entity.grounded = movement.grounded(entity, shape, content.map)
        if kind == "player" then
            entity.ammo = content.weapon.magazine
            entity.reserve = content.weapon.reserve
            entity.ai = "idle"
        end
        return entity
    end

    -- 重建局内实体，保留连接输入序号和全局 Tick。
    function api.start(world, content, request)
        world.match_id = state.next_id(world.match_id)
        world.phase = "Playing"
        world.entities = json.array()
        world.entities[1] = actor("1", "player", content.map.spawn, content.player, content)
        for _, spawn in ipairs(content.map.enemies) do
            world.entities[#world.entities + 1] = actor(spawn.id, "enemy", spawn,
                content.enemy, content)
        end
        api.clear_input(world)
        world.last_start = {req_id = request.req_id, after_match_id = request.after_match_id,
            match_id = world.match_id}
    end

    -- 仅投影协议字段，不把内部输入或冷却实现泄漏给客户端。
    function api.snapshot(world)
        local entities = json.array()
        for _, entity in ipairs(world.entities) do
            local copy = {}
            for _, key in ipairs(entity_fields) do
                copy[key] = entity[key]
            end
            entities[#entities + 1] = copy
        end
        return {v = 2, tick_id = world.tick_id, seq = world.seq, match_id = world.match_id,
            phase = world.phase, entities = entities}
    end

    -- 输出有单调标识的局内事件，宿主会在入口成功后统一提交。
    function api.emit(world, kind, actor_id, target_id, x, y, amount)
        world.event_id = state.next_id(world.event_id)
        net.emit("event", json.encode({v = 2, match_id = world.match_id,
            event_id = world.event_id, tick_id = world.tick_id, kind = kind,
            actor_id = actor_id, target_id = target_id, x = x, y = y, amount = amount}))
    end

    -- 结算一次伤害与死亡，死亡实体保留稳定位置和标识。
    function api.damage(world, source, target, damage)
        if not target.alive then
            return
        end
        local amount = math.min(damage, target.hp)
        target.hp = target.hp - amount
        api.emit(world, "hit", source.id, target.id, target.x, target.y, amount)
        if target.hp == 0 then
            target.alive = false
            target.ai = "dead"
            target.vx = 0
            target.vy = 0
            target.reload_ticks = 0
            target.shot_ticks = 0
            target.attack_ticks = 0
            api.emit(world, "death", source.id, target.id, target.x, target.y, 0)
        end
    end

    -- 在唯一位置裁定玩家死亡或清怪终态，并冻结全部动作。
    function api.finish(world)
        if world.phase ~= "Playing" then
            return
        end
        if not world.entities[1].alive then
            world.phase = "Dead"
        else
            local alive = false
            for i = 2, #world.entities do
                alive = alive or world.entities[i].alive
            end
            if not alive then
                world.phase = "Cleared"
            end
        end
        if world.phase ~= "Playing" then
            api.clear_input(world)
            for _, entity in ipairs(world.entities) do
                entity.vx = 0
                entity.vy = 0
            end
            local player = world.entities[1]
            api.emit(world, "end", player.id, "0", player.x, player.y, 0)
        end
    end

    -- 检查一名角色的完整字段、配置边界和生死关系。
    local function valid_actor(entity, index, content)
        if not state.fields(entity, saved_fields) then
            return false
        end
        local player = index == 1
        local shape = player and content.player or content.enemy
        local id = player and "1" or content.map.enemies[index - 1].id
        if entity.id ~= id or entity.kind ~= (player and "player" or "enemy")
            or not state.integer(entity.x, shape.width // 2, content.map.width - shape.width // 2)
            or not state.integer(entity.y, 0, content.map.height - shape.height)
            or not state.integer(entity.vx, -shape.speed, shape.speed)
            or not state.integer(entity.vy, -1000, content.player.jump_speed)
            or not state.integer(entity.max_hp, shape.hp, shape.hp)
            or not state.integer(entity.hp, 0, shape.hp)
            or type(entity.alive) ~= "boolean" or entity.alive ~= (entity.hp > 0)
            or type(entity.grounded) ~= "boolean" or (entity.grounded and entity.vy ~= 0)
            or not state.integer(entity.facing, -1, 1) or entity.facing == 0 then
            return false
        end
        if entity.grounded ~= movement.grounded(entity, shape, content.map) then
            return false
        end
        for _, solid in ipairs(content.map.solids) do
            if entity.x - shape.width // 2 < solid.x + solid.w
                and entity.x + shape.width // 2 > solid.x
                and entity.y < solid.y + solid.h and entity.y + shape.height > solid.y then
                return false
            end
        end
        if not state.integer(entity.ammo, 0, player and content.weapon.magazine or 0)
            or not state.integer(entity.reserve, 0, player and content.weapon.reserve or 0)
            or not state.integer(entity.reload_ticks, 0,
                player and content.weapon.reload_ticks or 0)
            or not state.integer(entity.shot_ticks, 0, player and content.weapon.fire_ticks or 0)
            or not state.integer(entity.attack_ticks, 0,
                player and 0 or content.enemy.attack_ticks) then
            return false
        end
        if not entity.alive then
            return entity.ai == "dead" and entity.vx == 0 and entity.vy == 0
                and entity.reload_ticks == 0 and entity.shot_ticks == 0 and entity.attack_ticks == 0
        end
        if player then
            return entity.ai == "idle"
        end
        return entity.ai == "patrol" or entity.ai == "chase" or entity.ai == "attack"
    end

    -- 完整检查导入状态，内容身份、实体顺序与阶段都不得漂移。
    function api.valid(world, content)
        if not state.fields(world, {"v", "tick_id", "seq", "match_id", "player_id",
            "event_id", "phase", "paused", "content_key", "entities", "controls", "last_start"})
            or not state.integer(world.v, 2, 2) or world.content_key ~= json.encode(content)
            or not state.is_tick(world.tick_id) or not state.is_id(world.seq)
            or not state.is_id(world.match_id) or not state.is_id(world.event_id)
            or type(world.paused) ~= "boolean" then
            return false
        end
        local input = world.controls
        if not state.fields(input, {"move_x", "aim_x", "aim_y", "jump", "fire", "fire_once",
            "reload"}) or not state.integer(input.move_x, -1, 1)
            or not state.integer(input.aim_x, -1000, 1000)
            or not state.integer(input.aim_y, -1000, 1000)
            or (input.aim_x == 0 and input.aim_y == 0)
            or type(input.jump) ~= "boolean" or type(input.fire) ~= "boolean"
            or type(input.fire_once) ~= "boolean" or type(input.reload) ~= "boolean" then
            return false
        end
        local last = world.last_start
        if not state.fields(last, {"req_id", "after_match_id", "match_id"})
            or type(last.req_id) ~= "string" or #last.req_id > 128
            or not state.is_id(last.after_match_id) or not state.is_id(last.match_id) then
            return false
        end
        if (world.paused or world.phase ~= "Playing") and (input.move_x ~= 0 or input.jump
            or input.fire or input.fire_once or input.reload) then
            return false
        end
        if world.phase == "Unauthenticated" or world.phase == "Lobby" then
            return world.player_id == (world.phase == "Lobby" and "1" or "0")
                and world.match_id == "0" and state.array(world.entities, 0, 0)
                and last.req_id == "" and last.after_match_id == "0" and last.match_id == "0"
        end
        if world.phase ~= "Playing" and world.phase ~= "Dead" and world.phase ~= "Cleared" then
            return false
        end
        if world.player_id ~= "1" or world.match_id == "0" or last.req_id == ""
            or last.match_id ~= world.match_id
            or not state.newer(world.match_id, last.after_match_id)
            or state.next_id(last.after_match_id) ~= world.match_id
            or not state.array(world.entities,
                #content.map.enemies + 1, #content.map.enemies + 1) then
            return false
        end
        local alive = 0
        for index, entity in ipairs(world.entities) do
            if not valid_actor(entity, index, content) then
                return false
            end
            if world.phase ~= "Playing" and (entity.vx ~= 0 or entity.vy ~= 0) then
                return false
            end
            if index > 1 and entity.alive then
                alive = alive + 1
            end
        end
        return (world.phase == "Dead" and not world.entities[1].alive)
            or (world.phase == "Cleared" and world.entities[1].alive and alive == 0)
            or (world.phase == "Playing" and world.entities[1].alive and alive > 0)
    end

    return api
end

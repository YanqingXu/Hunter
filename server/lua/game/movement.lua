-- 以局部标量计算整数运动，每个单位只批量读取和提交一次原生状态。
return function(deps)
    local api = {}

    -- 判断两个开区间是否相交，接触边界不算穿透。
    local function overlap(a, b, c, d)
        return a < d and b > c
    end

    -- 返回地图实心物和本局掩体的只读几何视图，不复制可变状态。
    function api.solids(content)
        if content.scenes == nil then
            return content.map.solids
        end
        local solids = {}
        for _, solid in ipairs(content.map.solids) do
            solids[#solids + 1] = solid
        end
        for _, scene in ipairs(content.scenes) do
            if scene.kind == "cover" then
                solids[#solids + 1] = scene
            end
        end
        return solids
    end

    -- 按原生姿态取得本次计算使用的体型，站立配置本身保持只读。
    function api.shape(entity, content)
        local cfg = content[entity.kind == "player" and "players" or "monsters"][entity.cfg_id]
        if entity.kind ~= "player" or not Player.get_prone(entity.controls) then
            return cfg
        end
        local shape = {}
        for key, value in pairs(cfg) do
            shape[key] = value
        end
        shape.width = cfg.prone_width
        shape.height = cfg.prone_height
        shape.speed = cfg.prone_speed
        return shape
    end

    -- 检查以脚底中心为锚点的完整体型能否放置，用于原地切换姿态。
    function api.fits(x, y, width, height, content)
        local half = width // 2
        if x < half or x + half > content.map.width or y < 0
            or y + height > content.map.height then
            return false
        end
        for _, solid in ipairs(api.solids(content)) do
            if overlap(x - half, x + half, solid.x, solid.x + solid.w)
                and overlap(y, y + height, solid.y, solid.y + solid.h) then
                return false
            end
        end
        return true
    end

    -- 沿水平方向扫到最近墙面，返回位置和裁定后的速度。
    local function horizontal(x, y, vx, shape, map)
        local half = shape.width // 2
        local next_x = math.max(half, math.min(map.width - half, x + vx))
        for _, solid in ipairs(map.solids) do
            if overlap(y, y + shape.height, solid.y, solid.y + solid.h) then
                if vx > 0 and x + half <= solid.x and next_x + half > solid.x then
                    next_x = solid.x - half
                elseif vx < 0 and x - half >= solid.x + solid.w
                    and next_x - half < solid.x + solid.w then
                    next_x = solid.x + solid.w + half
                end
            end
        end
        return next_x, next_x ~= x + vx and 0 or vx
    end

    -- 沿竖直方向扫到顶板或地面，返回位置、速度及落地状态。
    local function vertical(x, y, vy, shape, map)
        local half = shape.width // 2
        local next_y = math.max(0, math.min(map.height - shape.height, y + vy))
        local landed = y + vy <= 0
        for _, solid in ipairs(map.solids) do
            if overlap(x - half, x + half, solid.x, solid.x + solid.w) then
                if vy <= 0 and y >= solid.y + solid.h and next_y <= solid.y + solid.h then
                    next_y = math.max(next_y, solid.y + solid.h)
                    landed = true
                elseif vy > 0 and y + shape.height <= solid.y
                    and next_y + shape.height > solid.y then
                    next_y = math.min(next_y, solid.y - shape.height)
                end
            end
        end
        return next_y, (next_y ~= y + vy or landed) and 0 or vy, landed
    end

    -- 按原有顺序计算并提交运动，局部值不跨 Tick 保存。
    function api.step(entity, shape, content, move_x, jump, distance)
        local x, y, vx, vy, grounded, facing, alive, pending = Unit.read_motion(entity.motion)
        if pending or not alive then
            return 0
        end
        vx = move_x * math.min(shape.speed, distance or shape.speed)
        if move_x ~= 0 then
            facing = move_x
        end
        if jump and grounded then
            vy = shape.jump_speed
        end
        vy = math.max(-1000, vy - content.map.gravity)
        local map = content.map
        if content.scenes ~= nil then
            map = {width = map.width, height = map.height, gravity = map.gravity,
                solids = api.solids(content)}
        end
        x, vx = horizontal(x, y, vx, shape, map)
        y, vy, grounded = vertical(x, y, vy, shape, map)
        Unit.write_motion(entity.motion, x, y, vx, vy, grounded, facing)
        return vx
    end

    -- 梯上只接受移动，朝端点移动且站立空间足够时自动离梯，受阻则保持攀爬。
    local function climb(player, content, ladder)
        local input = player.controls
        local x, y, vx, vy, grounded, facing = Unit.read_motion(player.motion)
        local cfg = content.players[player.cfg_id]
        local direction = Player.get_move_y(input)
        local next_y = math.max(ladder.y, math.min(ladder.y + ladder.h,
            y + direction * cfg.speed))
        local next_x = ladder.x + ladder.w // 2
        Unit.write_motion(player.motion, next_x, next_y, 0, next_y - y, false, facing)
        local leaving = (direction < 0 and next_y == ladder.y)
            or (direction > 0 and next_y == ladder.y + ladder.h)
        if leaving and api.fits(next_x, next_y, cfg.width, cfg.height, content) then
            Player.set_ladder_id(input, 0)
            local map = {solids = api.solids(content)}
            Unit.write_motion(player.motion, next_x, next_y, 0, 0,
                api.grounded(player, cfg, map), facing)
        end
        Player.set_jump(input, false)
        Player.set_fire(input, false)
        Player.set_fire_once(input, false)
        Player.set_reload(input, false)
        Player.set_melee(input, false)
        Player.set_run(input, false)
        Player.set_want_prone(input, false)
        Player.set_aim_x(input, facing * 1000)
        Player.set_aim_y(input, 0)
    end

    -- 统一玩家姿态、跑步、起跳和使用减速，成功动作才修改回复延迟。
    function api.player(player, content)
        local input = player.controls
        local cfg = content.players[player.cfg_id]
        local ladder_id = Player.get_ladder_id(input)
        if ladder_id ~= 0 then
            for _, scene in ipairs(content.scenes) do
                if scene.kind == "ladder" and scene.id == tostring(ladder_id) then
                    climb(player, content, scene)
                    return 0
                end
            end
            error("invalid ladder reference")
        end
        local x, y, vx, vy, grounded = Unit.read_motion(player.motion)
        local prone = Player.get_prone(input)
        local desired = Player.get_want_prone(input)
        local jump = Player.get_jump(input) and grounded
        if jump then
            desired = false
        end
        if desired ~= prone then
            local width = desired and cfg.prone_width or cfg.width
            local height = desired and cfg.prone_height or cfg.height
            if api.fits(x, y, width, height, content) then
                Player.set_prone(input, desired)
                prone = desired
            else
                jump = false
            end
        end
        local running = Player.get_run(input) and Player.get_move_x(input) ~= 0
            and not prone and Player.get_stamina(input) >= 1
        local reset = false
        if Player.get_running(input) and not running then
            Player.set_stamina_delay(input, cfg.stamina_delay)
            Player.set_stamina_rem(input, 0)
            reset = true
        end
        Player.set_running(input, running)
        local shape = api.shape(player, content)
        local speed = prone and cfg.prone_speed or (running and cfg.run_speed or cfg.speed)
        if jump then
            Player.set_want_prone(input, false)
            Player.cancel_use(input)
            Player.set_stamina_delay(input, cfg.stamina_delay)
            Player.set_stamina_rem(input, 0)
            reset = true
        end
        if Player.get_use_slot(input) ~= 0 then
            speed = speed * content.rules.tool_move_percent // 100
        end
        local motion = {width = shape.width, height = shape.height, speed = speed,
            jump_speed = cfg.jump_speed}
        local moved = api.step(player, motion, content, Player.get_move_x(input), jump)
        if prone then
            local facing = Entity.get_facing(player.pose)
            local aim_x = math.max(0, Player.get_aim_x(input) * facing) * facing
            local aim_y = math.max(0, Player.get_aim_y(input))
            if aim_x == 0 and aim_y == 0 then
                aim_x = facing * 1000
            end
            Player.set_aim_x(input, aim_x)
            Player.set_aim_y(input, aim_y)
        end
        return moved, reset
    end

    -- 检查下一步脚前是否仍有同高地面。
    function api.supported(entity, shape, map, direction, distance)
        local x, y = Unit.read_motion(entity.motion)
        if y == 0 then
            return true
        end
        local edge = x + direction * (shape.width // 2 + (distance or shape.speed))
        for _, solid in ipairs(map.solids) do
            if y == solid.y + solid.h and edge >= solid.x and edge <= solid.x + solid.w then
                return true
            end
        end
        return false
    end

    -- 判断脚底是否与地图地面或平台顶面接触。
    function api.grounded(entity, shape, map)
        local x, y = Unit.read_motion(entity.motion)
        if y == 0 then
            return true
        end
        for _, solid in ipairs(map.solids) do
            if y == solid.y + solid.h and overlap(x - shape.width // 2,
                x + shape.width // 2, solid.x, solid.x + solid.w) then
                return true
            end
        end
        return false
    end

    return api
end

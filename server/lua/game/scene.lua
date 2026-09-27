-- 推进原生场景读条和炸药桶，爆炸先标记结算再传播，避免连锁重复伤害。
return function(deps)
    local world_api = deps["game.world"]
    local movement = deps["game.movement"]
    local combat = deps["game.combat"]
    local damage = deps["game.damage"]
    local api = {}

    -- 计算到矩形最近点的距离，实心地图和掩体都参与可达判定。
    local function visible(content, x, y, left, bottom, width, height, radius)
        local px = math.max(left, math.min(left + width, x))
        local py = math.max(bottom, math.min(bottom + height, y))
        local dx, dy = px - x, py - y
        return dx * dx + dy * dy <= radius * radius
            and combat.clear_path(movement.solids(content), x, y, px, py)
    end

    -- 玩家交互使用脚底中心和配置拾取半径，开始与完成时都重新核对。
    function api.reachable(player, scene, content)
        return visible(content, Entity.get_x(player.pose), Entity.get_y(player.pose),
            scene.x, scene.y, scene.w, scene.h, content.bag.pickup_radius)
    end

    -- 开局初始化原生桶生命，不在 Lua 保存第二份计时和爆炸状态。
    function api.init(world, content)
        for index, cfg in ipairs(content.scenes) do
            if cfg.barrel ~= false then
                assert(World.barrel_write(world, index, cfg.barrel.hp, 0, false),
                    "failed to initialize barrel")
            end
        end
    end

    -- 爆炸结算单位和桶的可见身体点；投掷物可明确关闭玩家自伤。
    function api.blast(world, content, source, x, y, radius, amount, hit_player)
        world_api.emit(world, "explosion", source, "0", x, y, radius)
        for _, id in ipairs(world_api.ids(world)) do
            local actor = world_api.find(world, id)
            if actor ~= nil and Unit.get_alive(actor.health)
                and (actor.kind == "monster" or hit_player) then
                local shape = movement.shape(actor, content)
                if visible(content, x, y, Entity.get_x(actor.pose) - shape.width // 2,
                    Entity.get_y(actor.pose), shape.width, shape.height, radius) then
                    damage.hit(world, source, actor, amount)
                end
            end
        end
        for index, cfg in ipairs(content.scenes) do
            if cfg.barrel ~= false and not World.barrel_exploded(world, index)
                and visible(content, x, y, cfg.x, cfg.y, cfg.w, cfg.h, radius) then
                api.hit(world, content, index, amount, true, source)
            end
        end
    end

    -- 先不可逆地标记爆炸，再执行伤害和连锁，因此同一桶最多结算一次。
    local function detonate(world, content, index, source)
        if World.barrel_exploded(world, index) then
            return
        end
        local cfg = content.scenes[index]
        assert(World.barrel_write(world, index, 0, 0, true), "invalid barrel explosion")
        api.blast(world, content, source, cfg.x + cfg.w // 2, cfg.y + cfg.h // 2,
            cfg.barrel.radius, cfg.barrel.damage, true)
    end

    -- 子弹首次击毁后点燃引信；爆炸命中立即引爆，不重置已有引信。
    function api.hit(world, content, index, amount, explosion, source)
        local cfg = content.scenes[index]
        if cfg == nil or cfg.barrel == false or World.barrel_exploded(world, index) then
            return
        end
        if explosion then
            detonate(world, content, index, source)
            return
        end
        if World.barrel_fuse(world, index) > 0 then
            return
        end
        local hp = math.max(0, World.barrel_hp(world, index) - amount)
        assert(World.barrel_write(world, index, hp, hp == 0 and cfg.barrel.fuse_ticks or 0, false),
            "invalid barrel damage")
    end

    -- 世界继续运行时推进引信；暂停和终局由外层 Tick 门禁统一冻结。
    function api.step(world, content)
        for index, cfg in ipairs(content.scenes) do
            if cfg.barrel ~= false and not World.barrel_exploded(world, index) then
                local ticks = World.barrel_fuse(world, index)
                if ticks == 1 then
                    detonate(world, content, index, "0")
                elseif ticks > 1 then
                    assert(World.barrel_write(world, index, 0, ticks - 1, false),
                        "invalid barrel timer")
                end
            end
        end
    end

    -- 交互读条绑定场景索引；其他动作、受伤、死亡或距离改变均取消。
    function api.finish(world, content, player, complete)
        local index = World.scene_index(world)
        if index == 0 then
            return
        end
        local input, cfg = player.controls, content.scenes[index]
        if cfg == nil or not Unit.get_alive(player.health) or World.hurt_now(world)
            or Player.get_move_x(input) ~= 0 or Player.get_move_y(input) ~= 0
            or Player.get_jump(input) or Player.get_fire(input) or Player.get_fire_once(input)
            or Player.get_reload(input) or Player.get_melee(input)
            or Player.get_use_slot(input) ~= 0
            or Player.get_vision(input) or Player.get_ladder_id(input) ~= 0
            or not api.reachable(player, cfg, content) then
            World.scene_cancel(world)
            return
        end
        local ticks = World.scene_ticks(world) - 1
        if ticks > 0 then
            World.scene_set_ticks(world, ticks)
            return
        end
        World.scene_cancel(world)
        complete(cfg.id)
    end

    return api
end

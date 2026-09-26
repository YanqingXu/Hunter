// 提供与生产导表隔离的原生结构夹具；只构造宿主参数，不实现配置玩法规则。
#pragma once

#include "common/Types.h"
#include "game/World.h"

namespace hunter::test
{
// 建立有界地图和场景身份结构，测试不依赖 Excel 或完整配置头文件。
inline void configure(World& world, i32 bag_slots = 8)
{
    world.configure({{"map_width", 24000}, {"map_height", 10000},
        {"bag_slots", bag_slots}, {"scene_ids", {"1", "2", "3"}}}, "native-structure-v7");
}

// 返回已经由上层认可的固定配装，仅用于测试原生结构和状态关系。
inline wire::Loadout loadout()
{
    wire::Loadout result;
    result.set_player_cfg_id(1);

    for (const auto amount : {50, 50, 25, 25})
    {
        result.add_health_segments(amount);
    }

    for (const auto id : {1U, 3U})
    {
        auto& weapon = *result.add_weapons();
        weapon.set_cfg_id(id);
        weapon.set_ammo_cfg_id(id);
    }

    result.add_tools(30001);
    result.add_tools(30002);
    result.add_consumables(30004);
    result.add_consumables(30006);
    return result;
}

// 生成玩家初始化参数；数值仅用于原生容器和接口测试。
inline nlohmann::json player_spec()
{
    return {{"cfg_id", "1"}, {"x", 2000}, {"y", 0}, {"hp", 150},
        {"width", 600}, {"height", 1600}, {"grounded", true}, {"stamina", 100},
        {"health_segments", {50, 50, 25, 25}}, {"weapons", {
            {{"cfg_id", "1"}, {"ammo_cfg_id", "1"}, {"ammo", 5}, {"reserve", 10}},
            {{"cfg_id", "3"}, {"ammo_cfg_id", "3"}, {"ammo", 6}, {"reserve", 12}}}},
        {"tools", {{{"slot", 1}, {"cfg_id", "30001"}, {"count", 1}},
            {{"slot", 2}, {"cfg_id", "30002"}, {"count", 3}},
            {{"slot", 5}, {"cfg_id", "30004"}, {"count", 1}},
            {{"slot", 6}, {"cfg_id", "30006"}, {"count", 1}}}}};
}

// 生成怪物初始化参数，出生引用的业务意义由 Lua 的独立契约验证。
inline nlohmann::json monster_spec(const Str& spawn_id)
{
    return {{"cfg_id", "1"}, {"spawn_id", spawn_id},
        {"x", spawn_id == "3" ? 19000 : 12000}, {"y", 0}, {"hp", 60},
        {"width", 600}, {"height", 1400}, {"grounded", true}};
}

// 通过正式原生参数边界创建固定夹具对象。
inline Str spawn(World& world, const Str& kind, const Str& spawn_id = "")
{
    return world.spawn(kind, (kind == "player" ? player_spec() : monster_spec(spawn_id)).dump());
}
}

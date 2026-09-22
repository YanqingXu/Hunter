// 将逻辑线程独占的七类原生对象注册到同一个 Luax 模块。
#pragma once
#include "game/World.h"
#include <luax/Runtime.hpp>

namespace hunter
{
// 注册受代次和写权限保护的对象属性、方法及查找工厂。
luax::Status register_objects(World& world, const luax::Isolate& isolate,
    luax::ModuleHandle module);
}

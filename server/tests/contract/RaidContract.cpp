// 验证掉落幂等、背包原子转移、替身玩家与撤离终态，不依赖网络时序。
#include "common/Types.h"
#include "game/World.h"
#include "ContentSpec.h"
#include <iostream>
#include <stdexcept>

namespace
{
// 断言可观察状态变化及拒绝语义。
void check(bool valid, const char* reason)
{
    if (!valid)
    {
        throw std::runtime_error(reason);
    }
}
}

// 使用真实原生对象验证一次物品只能属于地面或背包中的一处。
int main()
{
    try
    {
        auto cfg = nlohmann::json::parse(hunter::content::json_text);
        cfg["bag"] = {{"slots", 1}, {"pickup_radius", 1500}};
        cfg["items"] = {{"101", {{"max_stack", 5}}}, {"102", {{"max_stack", 1}}}};
        hunter::World world;
        world.configure(cfg);
        world.access.writable = true;
        world.login("73");
        world.begin("raid", "0", "101", "9");
        const auto player_id = world.spawn("player", "");
        check(world.find_player("73") == player_id, "alternative player lookup");
        check(world.find_player("1") == "0", "no implicit default player");
        const auto& player = world.actors[world.slot(std::stoull(player_id))]->unit();
        world.drop("101", 3, player.x, player.y);
        check(world.pickup("73", "1").empty(), "first pickup");
        check(world.pickup("73", "1") == "already_picked", "duplicate pickup");
        world.drop("101", 4, player.x, player.y);
        const auto before = world.document();
        check(world.pickup("73", "2") == "bag_full", "partial stack cannot fit");
        check(world.document() == before, "capacity rejection leaves both sides untouched");
        world.drop("101", 2, player.x, player.y);
        check(world.pickup("73", "3").empty(), "full merge needs no new slot");
        check(!world.has_item("3"), "merged ground identity invalidated");
        check(world.items[world.item_slot(1)]->value.count == 5, "stack count exact");
        world.drop("102", 1, player.x + 2000, player.y);
        check(world.pickup("73", "4") == "out_of_range", "distance authoritative");
        check(world.pickup("1", "4") == "invalid_player", "pickup operator identity");
        const auto spawn_id = cfg["map"]["enemies"][0]["spawn_id"].get<Str>();
        const auto enemy = world.spawn("monster", spawn_id);
        auto& unit = world.actors[world.slot(std::stoull(enemy))]->unit();
        unit.hp = 0;
        unit.alive = false;
        check(world.drop_once(enemy), "first death reward");
        check(!world.drop_once(enemy), "duplicate death reward rejected");
        world.tick_id = 7;
        world.hurt(player_id);
        check(world.hurt_now(), "positive damage marker");
        ++world.tick_id;
        check(!world.hurt_now(), "damage marker expires next tick");
        world.set_extract(1, 180, "complete");
        world.finish("Extracted");
        check(world.phase == "Settling" && world.raid.player_state == "Extracted",
            "extraction is not committed success");
        check(world.pickup("73", "2") == "invalid_state", "no pickup after extraction");

        for (i32 i = 0; i < 1000; ++i)
        {
            const auto value = world.roll(10000);
            check(value >= 1 && value <= 10000, "bounded deterministic random");
        }

        std::cout << "raid contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

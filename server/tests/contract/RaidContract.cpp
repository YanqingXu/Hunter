// 验证掉落幂等、背包原子转移、替身玩家与撤离终态，不依赖网络时序。
#include "common/Types.h"
#include "game/World.h"
#include "../fixtures/NativeWorld.h"
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
        hunter::World world;
        hunter::test::configure(world, 1);
        world.access.writable = true;
        world.login("73");
        world.prepare_loadout(hunter::test::loadout());
        world.begin("raid", "0", "101", "9");
        const auto player_id = hunter::test::spawn(world, "player");
        check(world.find_player("73") == player_id, "alternative player lookup");
        check(world.find_player("1") == "0", "no implicit default player");
        const auto& player = world.actors[world.slot(std::stoull(player_id))]->unit();
        using Json = nlohmann::json;
        const auto batch = [](Json changes, const Str& owner = "73")
        {
            return Json({{"owner", owner}, {"items", std::move(changes)},
                {"tools", Json::array()}}).dump();
        };
        world.drop("101", "[3]", player.x, player.y);
        check(world.commit(batch({{{"id", "1"}, {"expected_count", 3},
            {"count", 3}, {"place", "Bag"}}})).empty(), "first transfer");
        world.drop("101", "[4]", player.x, player.y);
        const auto before = world.document();
        check(world.commit(batch({{{"id", "1"}, {"expected_count", 3},
            {"count", 5}, {"place", "Bag"}}, {{"id", "2"}, {"expected_count", 4},
            {"count", 2}, {"place", "Bag"}}})) == "bag_full", "partial stack cannot fit");
        check(world.document() == before, "capacity rejection leaves both sides untouched");
        world.drop("101", "[2]", player.x, player.y);
        const auto merge = batch({{{"id", "1"}, {"expected_count", 3},
            {"count", 5}, {"place", "Bag"}}, {{"id", "3"}, {"expected_count", 2},
            {"count", 0}, {"place", "Ground"}}});
        check(world.commit(merge).empty(), "full merge needs no new slot");
        check(!world.has_item("3"), "merged ground identity invalidated");
        check(world.items[world.item_slot(1)]->value.count == 5, "stack count exact");
        const auto merged = world.save();
        check(world.commit(merge) == "stale_item" && world.save() == merged,
            "stale batch cannot duplicate or consume items");
        world.drop("102", "[1]", player.x + 2000, player.y);
        check(world.commit(batch(Json::array(), "1")) == "invalid_player",
            "operator identity checked");
        const auto enemy = hunter::test::spawn(world, "monster", "2");
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
        check(world.commit(batch(Json::array())) == "invalid_state",
            "no resource commit after extraction");

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

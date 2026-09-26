// 验证原生世界的唯一状态、怪物边界、物品边界、候选原子发布及类型化网络输出。
#include "common/Types.h"
#include "game/World.h"
#include "script/Schema.h"
#include "../fixtures/NativeWorld.h"
#include <iostream>
#include <limits>
#include <stdexcept>
#include <thread>

namespace
{
using Json = nlohmann::json;

// 保留失败断言的具体边界名称。
void check(bool value, const Str& detail)
{
    if (!value)
    {
        throw std::runtime_error(detail);
    }
}

// 验证拒绝操作确实抛出错误，不吞掉测试自身的失败。
template <typename F>
void rejects(F&& operation, const Str& detail)
{
    bool rejected = false;
    try
    {
        operation();
    }
    catch (const std::exception&)
    {
        rejected = true;
    }

    check(rejected, detail);
}

// 建立独立的已开局世界，保留怪物创建给各边界用例。
void begin(hunter::World& world)
{
    hunter::test::configure(world);
    world.access.writable = true;
    world.login("1");
    world.prepare_loadout(hunter::test::loadout());
    world.begin("monster_contract", "0", "1", "1");
    check(hunter::test::spawn(world, "player") == "1", "monster test player");
}

// 验证各层借用同一权威状态与门禁，运动提交保持原子性和网络可见性。
void check_views(hunter::World& world)
{
    for (const auto index : world.order)
    {
        auto& actor = *world.actors[index];
        std::visit([&](auto& value)
        {
            hunter::Unit& unit = value;
            hunter::Entity& entity = value;
            const hunter::Actor& view = actor;
            check(&actor.unit() == &unit && &view.unit() == &unit,
                "actor borrows concrete unit base");
            check(&value.access == &unit.access && &unit.access == &entity.access
                && entity.access == &world.access, "inheritance shares active world access");
            const auto x = entity.x + 1;
            entity.set_x(x);
            check(value.x == x && std::get<0>(unit.read_motion()) == x,
                "entity writes visible through unit and concrete views");
            const auto hp = unit.hp - 1;
            unit.set_hp(hp);
            check(value.hp == hp, "unit health visible through concrete view");
            const auto y = entity.y + 1;
            const auto facing = -entity.facing;
            unit.write_motion(x + 1, y, 1, 0, false, facing);
            const auto motion = unit.read_motion();
            check(value.x == x + 1 && value.y == y && value.facing == facing
                && value.vx == 1 && value.vy == 0 && !value.grounded,
                "batch motion updates inherited fields");
            rejects([&] { unit.write_motion(x + 2, y + 1, -1, -1, true, 0); },
                "invalid final motion parameter rejected");
            check(unit.read_motion() == motion, "invalid motion never partially writes");
            world.access.writable = false;
            rejects([&] { entity.set_x(x); }, "entity uses active readonly gate");
            rejects([&] { unit.set_hp(hp); }, "unit uses active readonly gate");
            world.access.writable = true;
        }, actor.value);
    }

    auto& player = std::get<hunter::Player>(world.actors[world.slot(1)]->value);
    auto& monster = std::get<hunter::Monster>(world.actors[world.slot(2)]->value);
    auto& item = world.items[world.item_slot(1)]->value;
    check(player.weapon.access == &world.access && item.access == &world.access,
        "independent components use active world access");
    player.set_move_x(1);
    const auto ammo = player.weapon.ammo - 1;
    player.weapon.set_ammo(ammo);
    monster.set_state("chase");
    monster.set_attack_ticks(1);
    const auto count = item.count + 1;
    item.set_count(count);
    check(player.move_x == 1 && player.weapon.ammo == ammo && monster.state == "chase"
        && monster.attack_ticks == 1 && item.count == count, "concrete setters update views");
    world.access.writable = false;
    rejects([&] { player.set_move_x(0); }, "player uses active readonly gate");
    rejects([&] { monster.set_state("patrol"); }, "monster uses active readonly gate");
    rejects([&] { player.weapon.set_ammo(ammo); }, "weapon uses active readonly gate");
    rejects([&] { item.set_count(count); }, "item uses active readonly gate");
    world.access.writable = true;
    const auto out = world.snapshot(world.player_id);

    for (const auto& value : out.snapshot().entities())
    {
        const auto& unit = world.actors[world.slot(value.id())]->unit();
        check(value.x() == unit.x && value.y() == unit.y && value.facing() == unit.facing
            && value.hp() == unit.hp, "base mutations visible in native snapshot");
    }

    check(world.valid(), "inherited mutations preserve world invariants");
}

// 覆盖创建和候选移动发布后的基类视图，防止门禁借用局部候选。
void inherited_views()
{
    hunter::World world;
    begin(world);

    for (const auto spawn_id : {"2", "3"})
    {
        const auto id = hunter::test::spawn(world, "monster", spawn_id);
        const auto slot = world.slot(hunter::read_id(id));
        auto& monster = std::get<hunter::Monster>(world.actors[slot]->value);
        monster.set_state("patrol");
    }

    check(world.create_item("42", 2) == "1", "view test item");
    check_views(world);
    const auto saved = world.save();
    world.load(saved);
    check(world.save() == saved, "inherited state roundtrip");
    check_views(world);
}

// 验证宿主初始化参数的结构和数值范围，失败不能消费身份或代次。
void spawn_bounds()
{
    hunter::World world;
    begin(world);
    const auto before = world.document();
    const auto revision = world.revision;
    const auto serial = world.serial;

    for (const auto& patch : Vec<Json>{
        {{"spawn_id", "02"}}, {{"spawn_id", "0"}}, {{"spawn_id", 2}},
        {{"spawn_id", "2147483648"}}, {{"x", 12000.5}}, {{"y", false}},
        {{"x", 0}}, {{"x", 24000}}, {{"y", 10000}}, {{"hp", 0}},
        {{"width", 599}}, {{"height", 0}}, {{"grounded", 1}}, {{"extra", 1}}})
    {
        auto spec = hunter::test::monster_spec("2");
        spec.update(patch);
        rejects([&] { world.spawn("monster", spec.dump()); }, "invalid spawn spec rejected");
        check(world.document() == before && world.revision == revision && world.serial == serial,
            "invalid spawn preserves world and allocators");
    }

    check(hunter::test::spawn(world, "monster", "2") == "2",
        "failed spawn does not consume identity");
    auto spec = hunter::test::monster_spec("2147483647");
    spec["x"] = 6000;
    spec["y"] = 1500;
    check(world.spawn("monster", spec.dump()) == "3", "maximum spawn identity");
    const auto saved = world.save();
    world.load(saved);
    check(world.save() == saved, "structural spawn roundtrip");
}

// 持久身份沿用生命周期的有符号范围，失败候选保留活动对象和句柄代次。
void persistent_identity_bounds()
{
    hunter::World world;
    begin(world);
    world.create_item("42", 1);
    const auto saved = world.save();
    const auto revision = world.revision;
    const auto serial = world.serial;
    const auto actor_slot = world.slot(1);
    const auto item_slot = world.item_slot(1);
    const auto* actor = &*world.actors[actor_slot];
    const auto* item = &*world.items[item_slot];
    const auto actor_generation = actor->generation;
    const auto item_generation = item->generation;

    for (const Str field : {"match_id", "player_id"})
    {
        auto candidate = Json::parse(saved);
        candidate[field] = "9223372036854775808";

        if (field == "match_id")
        {
            candidate["last_start"]["match_id"] = candidate[field];
        }
        else
        {
            candidate["entities"]["1"]["player_id"] = candidate[field];
        }

        rejects([&] { world.load(candidate.dump()); }, "persistent identity overflow rejected");
        check(world.save() == saved && world.revision == revision && world.serial == serial,
            "identity rejection preserves world and allocators");
        check(&*world.actors[actor_slot] == actor && &*world.items[item_slot] == item
            && actor->generation == actor_generation && item->generation == item_generation,
            "identity rejection preserves object handles");
    }
}

// 验证原生怪物状态写入、通用冷却上限、死亡收尾与单位不可复活边界。
void monster_bounds()
{
    hunter::World world;
    begin(world);
    check(hunter::test::spawn(world, "monster", "2") == "2", "monster boundary spawn");
    auto& monster = std::get<hunter::Monster>(world.actors[world.slot(2)]->value);
    const i64 cooldown = 3600;
    check(monster.get_state() == "spawn" && monster.get_attack_ticks() == 0,
        "native monster begins in spawn");
    monster.set_state("spawn");
    monster.set_attack_ticks(0);
    rejects([&] { monster.set_attack_ticks(1); }, "spawn cannot have attack cooldown");
    rejects([&] { monster.set_state("unknown"); }, "unknown monster state rejected");
    rejects([&] { monster.set_state("dead"); }, "living monster cannot enter dead");
    check(monster.get_state() == "spawn" && monster.get_attack_ticks() == 0,
        "invalid spawn setters preserve monster");

    for (const auto state : {"patrol", "chase", "attack"})
    {
        monster.set_state(state);
        check(monster.get_state() == state, "living monster state accepted");
        rejects([&] { monster.set_state("spawn"); }, "spawn cannot be reentered");
        check(monster.get_state() == state, "rejected state preserves current state");
    }

    monster.set_attack_ticks(cooldown);
    rejects([&] { monster.set_attack_ticks(cooldown + 1); }, "cfg attack cooldown limit");
    rejects([&] { monster.set_attack_ticks(-1); }, "negative attack cooldown");
    check(monster.get_attack_ticks() == cooldown, "rejected cooldown preserves value");
    monster.set_vx(1);
    monster.set_vy(-1);
    monster.set_hp(0);
    rejects([&] { monster.set_state("patrol"); }, "zero hp cannot enter living state");
    rejects([&] { monster.set_state("dead"); }, "dead state requires alive flag cleared");
    monster.set_alive(false);
    monster.set_state("dead");
    check(monster.get_state() == "dead" && monster.get_attack_ticks() == 0
        && monster.vx == 0 && monster.vy == 0,
        "death clears cooldown and motion");
    monster.set_state("dead");
    monster.set_attack_ticks(0);
    const auto dead = world.document();

    for (const auto state : {"spawn", "patrol", "chase", "attack"})
    {
        rejects([&] { monster.set_state(state); }, "dead state is terminal");
    }

    rejects([&] { monster.set_attack_ticks(1); }, "dead cooldown cannot restart");
    rejects([&] { monster.set_hp(1); }, "dead health cannot recover");
    rejects([&] { monster.set_alive(true); }, "dead alive flag cannot recover");
    rejects([&] { monster.set_vx(1); }, "dead horizontal movement rejected");
    rejects([&] { monster.set_vy(-1); }, "dead vertical movement rejected");
    rejects([&]
    {
        monster.write_motion(monster.x, monster.y, 1, 0, true, monster.facing);
    }, "dead batch movement rejected");
    check(world.document() == dead, "rejected dead mutations preserve monster");
}

// 验证完整快照保留五态，并原子拒绝出生状态和生死关系不一致的候选。
void monster_states()
{
    hunter::World world;
    begin(world);
    hunter::test::spawn(world, "monster", "2");
    hunter::test::spawn(world, "monster", "3");
    const auto saved = world.save();
    world.load(saved);
    check(world.save() == saved, "spawn state roundtrip");

    for (const auto state : {"patrol", "chase", "attack", "dead"})
    {
        auto candidate = Json::parse(saved);
        auto& monster = candidate["entities"]["2"];
        monster["ai"]["state"] = state;

        if (Str(state) == "dead")
        {
            monster["health"]["hp"] = 0;
            monster["health"]["alive"] = false;
        }
        else
        {
            monster["ai"]["attack_ticks"] = 60;
        }

        world.load(candidate.dump());
        check(world.save() == candidate.dump(), "living and dead monster state roundtrip");
    }

    world.load(saved);
    const auto revision = world.revision;
    const auto serial = world.serial;

    for (const auto& patch : Vec<Json>{
        {{"ai", {{"state", "unknown"}}}}, {{"ai", {{"attack_ticks", 1}}}},
        {{"motion", {{"vx", 1}}}},
        {{"health", {{"hp", 59}}}}, {{"motion", {{"grounded", false}}}},
        {{"spawn_id", "02"}}, {{"spawn_id", "2147483648"}},
        {{"ai", {{"state", "dead"}}}},
        {{"ai", {{"state", "patrol"}}}, {"health", {{"hp", 0}, {"alive", false}}}},
        {{"ai", {{"state", "dead"}, {"attack_ticks", 1}}},
            {"health", {{"hp", 0}, {"alive", false}}}},
        {{"ai", {{"state", "attack"}, {"attack_ticks", 3601}}}}})
    {
        auto candidate = Json::parse(saved);
        candidate["entities"]["2"].merge_patch(patch);
        rejects([&] { world.load(candidate.dump()); }, "invalid monster candidate rejected");
        check(world.save() == saved && world.revision == revision && world.serial == serial,
            "invalid monster candidate not published");
    }
}
}

// 使用隔离结构夹具运行原生对象及 Item 的完整状态往返。
int main()
{
    try
    {
        inherited_views();
        spawn_bounds();
        persistent_identity_bounds();
        monster_bounds();
        monster_states();
        hunter::World world;
        hunter::test::configure(world);
        rejects([&] { world.login("1"); }, "readonly gate");
        world.access.writable = true;
        world.login("1");
        world.prepare_loadout(hunter::test::loadout());
        world.begin("start", "0", "1", "1");
        check(hunter::test::spawn(world, "player") == "1", "player identity");

        for (const auto spawn_id : {"2", "3"})
        {
            hunter::test::spawn(world, "monster", spawn_id);
        }

        const auto id = world.create_item("42", 2);
        auto& item = world.items[world.item_slot(hunter::read_id(id))]->value;
        item.set_count(3);
        rejects([&] { item.set_count(0); }, "positive item count");
        check(item.count == 3, "failed setter preserves item");
        rejects([&] { world.create_item("042", 1); }, "canonical cfg identity");
        rejects([&] { world.create_item("2147483648", 1); }, "cfg range");
        const auto saved = world.save();
        const auto old_generation = world.items[world.item_slot(hunter::read_id(id))]->generation;
        world.load(saved);
        check(world.save() == saved, "complete item roundtrip");
        check(world.items[world.item_slot(hunter::read_id(id))]->generation != old_generation,
            "import invalidates item handle generation");

        for (const auto field : {"count", "cfg_id", "extra"})
        {
            auto bad = nlohmann::json::parse(saved);
            bad["items"][id][field] = 0;
            rejects([&] { world.load(bad.dump()); }, "invalid candidate rejected");
            check(world.save() == saved, "invalid candidate not published");
        }

        auto old = nlohmann::json::parse(saved);
        old["v"] = 3;
        rejects([&] { world.load(old.dump()); }, "old state version rejected");

        for (i32 index = 1; index < 64; ++index)
        {
            world.create_item("42", 1);
        }

        const auto full = world.save();
        rejects([&] { world.create_item("42", 1); }, "item capacity");
        check(world.save() == full, "capacity does not consume identity");
        check(world.remove_item(id) && !world.remove_item(id), "idempotent item removal");
        auto boundary = nlohmann::json::parse(world.save());
        boundary["last_item_id"] = "18446744073709551614";
        world.load(boundary.dump());
        const auto maximum = world.create_item("42", 1);
        check(maximum == "18446744073709551615", "exact uint64 item identity");
        world.remove_item(maximum);
        const auto exhausted = world.save();
        rejects([&] { world.create_item("42", 1); }, "item id exhaustion");
        check(world.save() == exhausted, "exhaustion preserves state");
        const hunter::ScriptOut snapshot(world.snapshot(world.player_id));
        check(hunter::validate_output(snapshot, 65536).has_value(),
            "typed native snapshot schema");

        bool wrong_thread = false;
        std::thread other([&]
        {
            try
            {
                world.access.read();
            }
            catch (const std::exception&)
            {
                wrong_thread = true;
            }
        });
        other.join();
        check(wrong_thread, "owner thread boundary");
        world.access.alive = false;
        rejects([&] { world.access.read(); }, "closed world rejected");
        std::cout << "native object contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

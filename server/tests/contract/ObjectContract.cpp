// 验证原生世界的唯一状态、物品边界、候选原子发布及类型化网络输出。
#include "common/Types.h"
#include "game/World.h"
#include "script/Schema.h"
#include "ContentSpec.h"
#include <iostream>
#include <limits>
#include <stdexcept>
#include <thread>

namespace
{
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
}

// 使用正式配置运行原生对象及 Item 的完整状态往返。
int main()
{
    try
    {
        hunter::World world;
        world.configure(nlohmann::json::parse(hunter::content::json_text));
        rejects([&] { world.login(); }, "readonly gate");
        world.access.writable = true;
        world.login();
        world.begin("start", "0");
        check(world.spawn("player", "") == "1", "player identity");

        for (const auto& spawn : world.content["map"]["enemies"])
        {
            check(world.spawn("monster", spawn["spawn_id"]).front() != ':', "monster identity");
        }

        check(world.valid(), "initial native world");
        auto& player = std::get<hunter::Player>(world.actors[world.slot(1)]->value);
        const auto original_hp = player.unit.hp;
        player.unit.set_hp(original_hp - 1);
        check(world.snapshot().snapshot().entities(0).hp() == original_hp - 1,
            "native mutation immediately visible in snapshot");
        hunter::wire::FrameInput input;
        input.set_seq(1);
        input.set_match_id(1);
        input.set_aim_x(1000);
        input.set_jump(true);
        check(world.input(input, 1).has_ack(), "native input ack");
        input.set_seq(2);
        input.set_jump(false);
        input.set_fire(true);
        check(world.input(input, 1).ack().seq() == 2 && player.jump && player.fire_once,
            "input edges coalesce");
        check(world.input(input, 1).error().code() == "stale_input", "native deduplication");
        world.clear_input();

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
        check(hunter::validate_output(hunter::ScriptOut(world.snapshot()), 65536).has_value(),
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

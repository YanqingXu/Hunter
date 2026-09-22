// 在真实 Luax 源码及签名运行时验证登录、运动、战斗、重开和完整状态恢复。
#include "common/Types.h"
#include "core/Cfg.h"
#include "script/Script.h"

#include <nlohmann/json.hpp>

#include <fstream>
#include <iostream>
#include <stdexcept>
#include <utility>

namespace
{
using Json = nlohmann::json;

// 把业务断言失败转成有上下文的契约错误。
void check(bool value, const Str& message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

// 提取预期成功结果，同时保留脚本和桥接的错误详情。
template <typename T>
T take(std::expected<T, Str> result)
{
    if (!result)
    {
        throw std::runtime_error(result.error());
    }

    return std::move(*result);
}

// 读取导出后的真实共享配置。
Json read_content(const Str& path)
{
    std::ifstream stream(path);
    check(static_cast<bool>(stream), "content file missing");
    return Json::parse(stream);
}

// 为单项场景保留正式字段和数值，仅替换出生与碰撞几何。
Json arena(Json content, i64 enemy_x = 5000)
{
    content["map"]["solids"] = Json::array();
    content["map"]["spawn"] = {{"x", 2000}, {"y", 0}};
    content["map"]["enemies"] = Json::array({
        {{"id", "2"}, {"x", enemy_x}, {"y", 0},
            {"patrol_min", enemy_x - 500}, {"patrol_max", enemy_x + 500}}});
    return content;
}

// 在拥有线程直接驱动正式七入口，测试不依赖网络时序。
struct Game
{
    hunter::Script script;
    Json content;
    u64 tick = 0;
    u64 seq = 0;

    // 使用与宿主一致的配置打开真实脚本。
    Game(const hunter::Cfg& cfg, Json data) : content(std::move(data))
    {
        const auto output = take(script.open(cfg,
            Json{{"v", 2}, {"snapshot_every", 3}, {"content", content}}.dump()));
        check(output.empty(), "init must not send network output");
    }

    // 通过正式事件入口提交有版本的请求。
    Vec<hunter::ScriptOut> event(i64 id, Json payload)
    {
        payload["v"] = 2;
        return take(script.event(id, payload.dump()));
    }

    // 显式登录并开启首局。
    void start()
    {
        event(2, {{"req_id", "login"}});
        event(3, {{"req_id", "start"}, {"after_match_id", "0"}});
    }

    // 导出完整权威状态，导出自身也会运行正式状态校验。
    Json state()
    {
        return Json::parse(take(script.export_state()));
    }

    // 导入受控测试状态，并同步测试端全局时钟和输入序号。
    void restore(const Json& saved)
    {
        auto result = script.import_state(saved.dump());
        check(result.has_value(), result ? "" : result.error());
        tick = std::stoull(saved.at("tick_id").get<Str>());
        seq = std::stoull(saved.at("seq").get<Str>());
    }

    // 构造当前局的正式输入，不传入任何客户端权威字段。
    Json input(i32 move = 0, bool jump = false, bool fire = false, bool reload = false)
    {
        return {{"seq", std::to_string(++seq)}, {"match_id", state()["match_id"]},
            {"applied_tick", std::to_string(tick + 1)}, {"move_x", move},
            {"aim_x", 1000}, {"aim_y", 0}, {"jump", jump}, {"fire", fire},
            {"reload", reload}};
    }

    // 推进固定步长并收集这批 Tick 的所有真实宿主输出。
    Vec<hunter::ScriptOut> steps(u32 count = 1)
    {
        Vec<hunter::ScriptOut> output;

        for (u32 index = 0; index < count; ++index)
        {
            auto batch = take(script.tick(++tick, 1.0 / 60.0));

            for (auto& item : batch)
            {
                output.push_back(std::move(item));
            }
        }

        return output;
    }
};

// 查找指定类型输出并按同一 JSON 桥接读取。
Json message(const Vec<hunter::ScriptOut>& output, const Str& kind)
{
    for (const auto& item : output)
    {
        if (item.kind == kind)
        {
            return Json::parse(item.payload);
        }
    }

    throw std::runtime_error("missing output: " + kind);
}

// 统计可靠事件，用于验证一次动作不会重复造成伤害或死亡。
usize events(const Vec<hunter::ScriptOut>& output, const Str& kind)
{
    usize count = 0;

    for (const auto& item : output)
    {
        if (item.kind == "event" && Json::parse(item.payload)["kind"] == kind)
        {
            ++count;
        }
    }

    return count;
}

// 验证本机会话登录、请求幂等、输入去重与普通业务拒绝。
void session(const hunter::Cfg& cfg, const Json& content)
{
    Game game(cfg, content);
    auto reply = game.event(3, {{"req_id", "early"}, {"after_match_id", "0"}});
    check(message(reply, "error")["code"] == "not_logged_in", "login required");
    reply = game.event(2, {{"req_id", "login"}});
    check(message(reply, "login")["player_id"] == "1", "local player allocated");
    reply = game.event(2, {{"req_id", "again"}});
    check(message(reply, "login")["phase"] == "Lobby", "login retry preserves lobby");
    reply = game.event(3, {{"req_id", "start"}, {"after_match_id", "0"}});
    check(message(reply, "start")["match_id"] == "1", "first match allocated");
    check(message(reply, "snapshot")["entities"].size() == 3, "shared map entities loaded");
    const auto request = game.input(1);
    reply = game.event(1, request);
    check(message(reply, "ack")["applied_tick"] == "1", "ack reports simulation tick");
    game.steps();
    check(game.state()["entities"][0]["x"] == 2100, "movement uses server tick");
    reply = game.event(1, request);
    check(message(reply, "error")["code"] == "stale_input", "duplicate sequence rejected");
    reply = game.event(3, {{"req_id", "start"}, {"after_match_id", "0"}});
    check(message(reply, "snapshot")["entities"][0]["x"] == 2100,
        "start retry preserves active world");
    reply = game.event(3, {{"req_id", "start"}, {"after_match_id", "1"}});
    check(message(reply, "error")["code"] == "request_conflict", "conflicting retry rejected");
    reply = game.event(3, {{"req_id", "new"}, {"after_match_id", "1"}});
    check(message(reply, "error")["code"] == "invalid_state", "live match cannot reset");
    auto stale = game.input();
    stale["match_id"] = "0";
    reply = game.event(1, stale);
    check(message(reply, "error")["code"] == "stale_match", "old match input rejected");
    check(game.script.validate_state().has_value(), "business errors preserve usable session");
}

// 验证单跳、顶板、平台扫掠、墙与地图边界。
void movement(const hunter::Cfg& cfg, const Json& base)
{
    auto content = arena(base, 21000);
    content["map"]["solids"] = Json::array({
        {{"id", "101"}, {"x", 4000}, {"y", 1000}, {"w", 2000}, {"h", 300}}});
    Game jump(cfg, content);
    jump.start();
    jump.event(1, jump.input(1, true));
    jump.steps();
    check(jump.state()["entities"][0]["y"] == 228, "jump uses integer fixed step");
    jump.event(1, jump.input(1, true));
    jump.steps();
    check(jump.state()["entities"][0]["vy"] == 216, "airborne jump does not add impulse");
    jump.steps(32);
    auto player = jump.state()["entities"][0];
    check(player["y"] == 1300 && player["grounded"] == true, "jump lands on platform");

    auto saved = jump.state();
    saved["entities"][0]["x"] = 5000;
    saved["entities"][0]["y"] = 3000;
    saved["entities"][0]["vy"] = -1000;
    saved["entities"][0]["grounded"] = false;
    saved["controls"]["move_x"] = 0;
    jump.restore(saved);
    jump.steps(2);
    check(jump.state()["entities"][0]["y"] == 1300, "fast fall cannot tunnel platform");

    saved = jump.state();
    saved["entities"][0]["x"] = 3700;
    saved["entities"][0]["y"] = 0;
    saved["entities"][0]["vy"] = 0;
    jump.restore(saved);
    jump.event(1, jump.input(1));
    jump.steps(4);
    check(jump.state()["entities"][0]["x"] == 3700, "solid side prevents walking through");
    saved = jump.state();
    saved["entities"][0]["x"] = 300;
    jump.restore(saved);
    jump.event(1, jump.input(-1));
    jump.steps();
    check(jump.state()["entities"][0]["x"] == 300, "map clamps body extent");

    content["map"]["solids"] = Json::array({
        {{"id", "101"}, {"x", 1000}, {"y", 1800}, {"w", 2500}, {"h", 100}}});
    Game ceiling(cfg, content);
    ceiling.start();
    ceiling.event(1, ceiling.input(0, true));
    ceiling.steps();
    player = ceiling.state()["entities"][0];
    check(player["y"] == 200 && player["vy"] == 0, "head sweep stops at ceiling");
}

// 验证近目标优先、遮挡、射速、空弹、换弹和暂停时输入清除。
void weapons(const hunter::Cfg& cfg, const Json& base)
{
    auto content = arena(base, 8000);
    content["enemy"]["hp"] = 1000;
    Game game(cfg, content);
    game.start();
    const auto first = game.input(0, false, true);
    game.event(1, first);
    auto reply = game.steps();
    check(events(reply, "shot") == 1 && events(reply, "hit") == 1, "real shot damages enemy");
    check(game.state()["entities"][1]["hp"] == 980, "configured damage applied");
    game.event(1, first);
    game.steps(9);
    check(game.state()["entities"][0]["ammo"] == 5, "duplicate and held input obey fire rate");
    game.steps();
    check(game.state()["entities"][0]["ammo"] == 4, "next shot occurs at exact cooldown");
    game.steps(40);
    check(game.state()["entities"][0]["ammo"] == 0, "magazine limits held fire");
    reply = game.steps(12);
    check(events(reply, "shot") == 0, "empty gun cannot fire");
    game.event(1, game.input(0, false, false, true));
    reply = game.steps();
    check(events(reply, "reload") == 1, "reload start emitted once");
    game.steps(89);
    auto player = game.state()["entities"][0];
    check(player["ammo"] == 0 && player["reload_ticks"] == 1, "reload does not finish early");
    reply = game.steps();
    player = game.state()["entities"][0];
    check(player["ammo"] == 6 && player["reserve"] == 24 && events(reply, "reload") == 1,
        "reload transfers exact reserve after duration");

    game.event(1, game.input(1, true, true));
    game.event(4, {{"paused", true}});
    const auto paused = game.state()["entities"];
    game.steps(5);
    check(game.state()["entities"] == paused, "pause freezes movement ammo and AI");
    game.event(4, {{"paused", false}});
    reply = game.steps();
    check(events(reply, "shot") == 0 && game.state()["entities"][0]["x"] == paused[0]["x"],
        "resume does not replay held fire move or jump");
    check(game.state()["entities"][0]["y"] == paused[0]["y"],
        "pause discards a jump accepted before the boundary");

    content = arena(base, 2800);
    content["map"]["solids"] = Json::array({
        {{"id", "101"}, {"x", 2400}, {"y", 0}, {"w", 100}, {"h", 3000}}});
    Game wall(cfg, content);
    wall.start();
    wall.event(1, wall.input(0, false, true));
    reply = wall.steps();
    check(events(reply, "shot") == 1 && events(reply, "hit") == 0,
        "wall blocks both gun and close enemy melee");
    check(wall.state()["entities"][1]["hp"] == 60, "wall protects enemy behind it");

    content = arena(base, 5000);
    content["map"]["enemies"].push_back({{"id", "3"}, {"x", 8000}, {"y", 0},
        {"patrol_min", 7500}, {"patrol_max", 8500}});
    Game nearest(cfg, content);
    nearest.start();
    nearest.event(1, nearest.input(0, false, true));
    nearest.steps();
    check(nearest.state()["entities"][1]["hp"] == 40
        && nearest.state()["entities"][2]["hp"] == 60, "only nearest live target takes damage");

    Game endpoint(cfg, arena(base, 20265));
    endpoint.start();
    endpoint.event(1, endpoint.input(0, false, true));
    endpoint.steps();
    check(endpoint.state()["entities"][1]["hp"] == 40, "shot includes exact range endpoint");

    content = arena(base, 5000);
    content["map"]["enemies"][0]["id"] = "3";
    auto same_place = content["map"]["enemies"][0];
    same_place["id"] = "2";
    content["map"]["enemies"].push_back(same_place);
    Game tied(cfg, content);
    tied.start();
    tied.event(1, tied.input(0, false, true));
    tied.steps();
    check(tied.state()["entities"][1]["hp"] == 60
        && tied.state()["entities"][2]["hp"] == 40, "equal distance uses numeric entity id");

    Game tap(cfg, arena(base, 8000));
    tap.start();
    tap.event(1, tap.input(0, false, true));
    tap.event(1, tap.input());
    reply = tap.steps();
    check(events(reply, "shot") == 1, "press and release in one tick retains one shot");
    reply = tap.steps(20);
    check(events(reply, "shot") == 0, "released fire does not become held fire");
}

// 验证怪物巡逻追击、攻击间隔、死亡终态以及保持连接序号的重开。
void enemies(const hunter::Cfg& cfg, const Json& base)
{
    Game patrol(cfg, arena(base, 18000));
    patrol.start();
    patrol.steps();
    check(patrol.state()["entities"][1]["ai"] == "patrol"
        && patrol.state()["entities"][1]["x"] == 18035, "distant enemy patrols");
    Game chase(cfg, arena(base, 5000));
    chase.start();
    chase.steps();
    check(chase.state()["entities"][1]["ai"] == "chase"
        && chase.state()["entities"][1]["x"] == 4965, "near enemy chases player");
    chase.steps(20);
    check(chase.state()["entities"][1]["x"].get<i64>() < 4500,
        "chasing enemy may leave patrol interval");

    auto narrow_content = arena(base, 18000);
    narrow_content["enemy"]["speed"] = 1000;
    narrow_content["map"]["enemies"][0]["patrol_min"] = 17997;
    narrow_content["map"]["enemies"][0]["patrol_max"] = 18005;
    Game narrow(cfg, narrow_content);
    narrow.start();
    narrow.steps();
    check(narrow.state()["entities"][1]["x"] == 18005,
        "patrol step stops exactly at right endpoint");
    narrow.steps();
    check(narrow.state()["entities"][1]["x"] == 17997,
        "patrol step stops exactly at left endpoint");

    for (u32 index = 0; index < 10; ++index)
    {
        narrow.steps();
        const auto x = narrow.state()["entities"][1]["x"].get<i64>();
        check(x >= 17997 && x <= 18005, "fast patrol remains inside narrow interval");
    }

    auto outside = narrow.state();
    outside["entities"][1]["x"] = 15000;
    outside["entities"][1]["ai"] = "chase";
    narrow.restore(outside);
    narrow.steps();
    check(narrow.state()["entities"][1]["x"] == 16000,
        "enemy returning from chase does not teleport to patrol interval");
    narrow.steps(2);
    check(narrow.state()["entities"][1]["x"] == 17997,
        "returning enemy stops at nearest patrol endpoint");

    Game attack(cfg, arena(base, 2800));
    attack.start();
    attack.steps();
    check(attack.state()["entities"][0]["hp"] == 90, "enemy applies melee damage");
    attack.steps(59);
    check(attack.state()["entities"][0]["hp"] == 90, "melee respects attack cooldown");
    attack.steps();
    check(attack.state()["entities"][0]["hp"] == 80, "melee resumes at exact cooldown");

    auto content = arena(base, 2800);
    content["player"]["hp"] = 10;
    Game death(cfg, content);
    death.start();
    death.event(1, death.input());
    auto reply = death.steps();
    check(death.state()["phase"] == "Dead" && events(reply, "death") == 1
        && events(reply, "end") == 1, "player death is a single terminal transition");
    check(message(reply, "snapshot")["phase"] == "Dead", "terminal snapshot is immediate");
    const auto final_entities = death.state()["entities"];
    reply = death.steps(10);
    check(reply.size() <= 4 && death.state()["entities"] == final_entities,
        "dead match freezes simulation");
    auto moving_dead = death.state();
    moving_dead["entities"][1]["vx"] = 1;
    Game invalid_dead(cfg, content);
    check(!invalid_dead.script.import_state(moving_dead.dump()),
        "Dead state rejects a living enemy with nonzero horizontal velocity");
    reply = death.event(3, {{"req_id", "restart"}, {"after_match_id", "1"}});
    check(message(reply, "start")["match_id"] == "2" && death.state()["seq"] == "1",
        "restart allocates match while preserving input high water");
    check(death.state()["entities"][0]["hp"] == 10
        && death.state()["entities"][0]["ammo"] == 6, "restart restores health and ammo");
    reply = death.event(3, {{"req_id", "start"}, {"after_match_id", "0"}});
    check(message(reply, "error")["code"] == "stale_match",
        "old start retry cannot reset new match");

    content = arena(base, 2800);
    content["enemy"]["hp"] = 20;
    Game clear(cfg, content);
    clear.start();
    clear.event(1, clear.input(0, false, true));
    reply = clear.steps();
    check(clear.state()["phase"] == "Cleared" && clear.state()["entities"][0]["hp"] == 100,
        "player shot kills enemy before its same tick attack");
    check(events(reply, "death") == 1 && events(reply, "end") == 1,
        "last enemy emits exactly one death and end");
    auto moving_clear = clear.state();
    moving_clear["entities"][0]["y"] = 10;
    moving_clear["entities"][0]["grounded"] = false;
    moving_clear["entities"][0]["vy"] = 1;
    Game invalid_clear(cfg, content);
    check(!invalid_clear.script.import_state(moving_clear.dump()),
        "Cleared state rejects a living airborne player with nonzero vertical velocity");
    clear.event(3, {{"req_id", "clear-restart"}, {"after_match_id", "1"}});
    check(clear.state()["phase"] == "Playing" && clear.state()["entities"][1]["alive"] == true,
        "clear terminal permits fresh living enemies");
}

// 验证完整状态往返的后续演化与非法状态拒绝。
void persistence(const hunter::Cfg& cfg, const Json& base)
{
    auto content = arena(base, 18000);
    Game first(cfg, content);
    first.start();
    first.event(1, first.input(1, true, true));
    first.steps(4);
    first.event(1, first.input(1, false, false, true));
    first.steps(3);
    const auto saved = first.state();
    Game second(cfg, content);
    second.restore(saved);

    for (u32 index = 0; index < 25; ++index)
    {
        first.steps();
        second.steps();
        check(first.state() == second.state(), "restored world must replay identically");
    }

    for (u32 sample = 0; sample < 9; ++sample)
    {
        Game invalid(cfg, content);
        auto corrupt = saved;

        if (sample == 0)
        {
            corrupt["unexpected"] = true;
        }
        else if (sample == 1)
        {
            corrupt["entities"][0]["ammo"] = -1;
        }
        else if (sample == 2)
        {
            corrupt["entities"][0]["hp"] = 0;
        }
        else if (sample == 3)
        {
            corrupt["seq"] = "01";
        }
        else if (sample == 4)
        {
            corrupt["content_key"] = "wrong-content";
        }
        else if (sample == 5)
        {
            corrupt["phase"] = "Unsupported";
        }
        else if (sample == 6)
        {
            corrupt["entities"][1]["id"] = "1";
        }
        else if (sample == 7)
        {
            corrupt["entities"][0]["x"] = 2000.5;
        }
        else
        {
            corrupt["paused"] = true;
            corrupt["controls"]["fire"] = true;
        }

        check(!invalid.script.import_state(corrupt.dump()), "corrupt state must be rejected");
    }

    Game exact(cfg, content);
    exact.start();
    auto request = exact.input();
    request["seq"] = "18446744073709551615";
    auto reply = exact.event(1, request);
    check(message(reply, "ack")["seq"] == "18446744073709551615"
        && exact.state()["seq"] == "18446744073709551615", "full uint64 sequence stays exact");
}

}

// 在源码与生产构建中运行完全相同的权威玩法验收。
int main(int argc, char** argv)
{
    try
    {
        check(argc >= 3, "expected script or bundle and exported content");
        hunter::Cfg cfg;
#if HUNTER_PRODUCTION
        check(argc >= 4, "expected production policy");
        cfg.bundle_path = argv[1];
        cfg.policy_path = argv[3];
#else
        cfg.source_path = argv[1];
#endif
        const auto content = read_content(argv[2]);
        session(cfg, content);
        movement(cfg, content);
        weapons(cfg, content);
        enemies(cfg, content);
        persistence(cfg, content);
        std::cout << "gameplay contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

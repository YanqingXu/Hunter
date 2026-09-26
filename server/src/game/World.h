// 独占会话、实体和物品状态；所有对象在逻辑线程创建、访问和释放。
#pragma once

#include "common/Types.h"
#include "game/Player.h"
#include "game/Monster.h"
#include "game/Item.h"
#include "game/Raid.h"
#include "hunter.pb.h"
#include <array>
#include <optional>
#include <variant>
#include <nlohmann/json.hpp>

namespace hunter
{
struct Actor
{
    u32 generation = 0;
    std::variant<Player, Monster> value;

    // 借用玩家或怪物的单位基类，不转移具体对象所有权。
    Unit& unit();

    // 只读借用单位基类，不复制权威状态。
    const Unit& unit() const;
};

struct ItemSlot
{
    u32 generation = 0;
    Item value;
};

// 保存飞行物或预留槽位；预留尚无身份，正式生成后使用本局唯一身份。
struct Projectile
{
    u64 id = 0;
    u32 cfg_id = 0;
    i32 x = 0;
    i32 y = 0;
    i32 vx = 0;
    i32 vy = 0;
    i32 remaining = 0;
    bool active = false;
    bool reserved = false;
};

class World
{
public:
    // 建立当前逻辑线程拥有的空世界。
    World() = default;

    // 权威对象及其访问门禁不能复制。
    World(const World&) = delete;

    // 禁止赋值产生第二份权威状态。
    World& operator=(const World&) = delete;

    Access access;
    bool configured = false;
    i32 map_width = 0;
    i32 map_height = 0;
    i32 bag_slots = 0;
    Vec<u32> scene_ids;
    Str content_key;
    u64 tick_id = 0;
    u64 seq = 0;
    u64 match_id = 0;
    u64 world_id = 0;
    Raid raid;
    wire::Loadout pending_loadout;
    wire::Loadout active_loadout;
    std::array<bool, 32> used_scenes{};
    std::array<Projectile, 16> projectiles{};
    u64 last_projectile_id = 0;
    u64 action_seq = 0;
    u64 player_id = 0;
    u64 event_id = 0;
    Str phase = "Unauthenticated";
    bool paused = false;
    u64 player_entity_id = 0;
    u64 last_entity_id = 0;
    u64 last_item_id = 0;
    Str last_req;
    u64 last_after = 0;
    u64 last_match = 0;
    u32 serial = 0;
    u32 revision = 1;
    std::array<std::optional<Actor>, 64> actors;
    Vec<u32> order;
    std::array<std::optional<ItemSlot>, 64> items;

    // 保存已经验证的候选配装，由下一次开局消费。
    void prepare_loadout(const wire::Loadout& input);

    // 返回本局冻结的正式接受配装。
    wire::Loadout get_loadout() const;

    // 返回当前世界身份。
    Str get_world_id() const;

    // 返回场景实例数量。
    i64 scene_count() const;

    // 读取一基场景索引的已使用状态。
    bool scene_used(i64 index) const;

    // 写入一基场景索引的已使用状态。
    void set_scene_used(i64 index, bool used);

    // 返回可预留的投掷物容量。
    i64 projectile_free() const;

    // 预留固定槽位，容量不足返回零。
    i32 reserve_projectile();

    // 激活预留或空闲槽位并赋予永不重复的局内身份。
    i64 spawn_projectile(const Str& cfg_id, i64 x, i64 y, i64 vx, i64 vy, i64 remaining);

    // 查询一基投掷物槽位是否在飞行。
    bool projectile_alive(i64 index) const;

    // 批量读取投掷物标量，只用于当前脚本计算。
    std::tuple<Str, i64, i64, i64, i64, i64> read_projectile(i64 index) const;

    // 写入当前飞行位置及剩余距离。
    void write_projectile(i64 index, i64 x, i64 y, i64 remaining);

    // 原子移除飞行物或释放预留。
    void remove_projectile(i64 index);

    // 清理终态所持有的装填、工具和飞行物资源。
    void clear_actions();

    // 仅在旧 VM 已关闭后重建空会话，仍拒绝其他线程调用。
    void reset();

    // 仅一次登记 Lua 提供的有限结构边界和摘要，不保存完整配置。
    void configure(const nlohmann::json& bounds, const Str& key);

    // 为实体或物品分配永不回绕的本会话句柄代次。
    u32 next_generation();

    // 查找身份的槽位；不存在时返回容量值。
    u32 slot(u64 id) const;

    // 查找物品槽位；不存在时返回容量值。
    u32 item_slot(u64 id) const;

    // 查询可参与玩法的实体是否存在。
    bool contains(const Str& id) const;

    // 返回稳定遍历顺序中的实体数量。
    i64 count() const;

    // 按一基索引读取实体身份。
    Str entity_id(i64 index) const;

    // 建立本地玩家会话身份。
    void login(const Str& owner);

    // 重置局内对象并更新请求记录；配置和连接序号保持不变。
    void begin(const Str& req, const Str& after, const Str& match, const Str& world);

    // 按持久玩家身份查找当前世界实体，不存在时返回零身份。
    Str find_player(const Str& owner) const;

    // 标记死亡怪物已生成掉落；重复调用不再产生奖励。
    bool drop_once(const Str& actor);

    // 使用世界拥有的随机状态生成闭区间一至上限的均匀整数。
    i64 roll(i64 maximum);

    // 完整校验 Lua 拆分的各堆及总容量后原子生成地面物品。
    void drop(const Str& cfg_id, const Str& stacks, i64 x, i64 y);

    // 读取低频物品视图，供 Lua 计算拾取和叠堆候选。
    Str inventory() const;

    // 核对实例、旧值和容量后原子提交物品、工具及场景候选。
    Str commit(const Str& batch);

    // 返回当前冻结配装的冷路径 JSON。
    Str loadout() const;

    // 从 Lua 接受规范配装，验证结构后保存为下一局候选。
    void accept_loadout(const Str& text);

    // 记录玩家受到正伤害的 Tick，用于撤离取消裁定。
    void hurt(const Str& actor);

    // 查询当前 Tick 是否发生过玩家受伤。
    bool hurt_now() const;

    // 读取已经累计的撤离游戏 Tick。
    i64 get_extract_ticks() const;

    // 写入服务端裁定的撤离点、进度及原因。
    void set_extract(i64 id, i64 ticks, Str reason);

    // 写入 Lua 计算的撤离展示数据，不执行解锁规则。
    void set_extract_view(bool unlocked, i64 remaining);

    // 冻结局内动作并进入待保存阶段，成功持久化由 Runtime 确认。
    void finish(Str outcome);

    // 按 Lua 提供的初值创建实体，结构或容量拒绝不消费身份。
    Str spawn(const Str& kind, const Str& spec);

    // 标记怪物移除，使其立即停止参与玩法。
    bool remove(const Str& id);

    // 在固定边界释放已标记对象，保持其余对象的遍历顺序。
    void flush();

    // 清除玩家所有持续及边沿输入。
    void clear_input();

    // 原生处理高频输入，返回拥有数据的确认或业务拒绝。
    wire::Envelope input(const wire::FrameInput& input, u64 applied_tick);

    // 直接将当前原生状态投影为网络快照。
    wire::Envelope snapshot(u64 observer) const;

    // 分配事件身份并构造拥有数据的可靠事件。
    wire::Envelope event(const Str& kind, const Str& actor, const Str& target,
        i64 x, i64 y, i64 amount);

    // 创建最小物品实例，配置引用仅检查格式和值域。
    Str create_item(const Str& cfg_id, i64 count);

    // 查询当前物品实例是否存在。
    bool has_item(const Str& id) const;

    // 销毁物品并使其旧句柄立即失效。
    bool remove_item(const Str& id);

    // 导出原生状态；导出前校验原生结构关系。
    Str save() const;

    // 在独立候选完整校验后替换局内数据，旧对象句柄失效。
    void load(const Str& text);

    // 检查当前原生结构，不执行配置语义、不修复状态或发送消息。
    bool valid() const;

    // 构造冷路径序列化数据，不产生第二份可修改的世界。
    nlohmann::json document() const;

    // 读取会话属性；身份在脚本中只读。
    Str get_tick_id() const;

    // 读取已处理输入序号。
    Str get_seq() const;

    // 读取当前局身份。
    Str get_match_id() const;

    // 读取玩家身份。
    Str get_player_id() const;

    // 读取玩家实体身份。
    Str get_player_entity_id() const;

    // 读取实体分配高水位。
    Str get_last_entity_id() const;

    // 读取最近开局请求。
    Str get_last_req() const;

    // 读取最近开局前一局身份。
    Str get_last_after() const;

    // 读取世界内容替换代次。
    i64 get_revision() const;

    // 读取会话阶段。
    Str get_phase() const;

    // 校验并设置玩法阶段。
    void set_phase(Str value);

    // 读取暂停状态。
    bool get_paused() const;

    // 修改暂停状态并清除输入。
    void set_paused(bool value);

private:
    // 仅用于本类将已验证的候选原子移入活动世界。
    World& operator=(World&&) = default;
};
}

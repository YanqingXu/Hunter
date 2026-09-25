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
    nlohmann::json content;
    Str content_key;
    u64 tick_id = 0;
    u64 seq = 0;
    u64 match_id = 0;
    u64 world_id = 0;
    Raid raid;
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

    // 仅在旧 VM 已关闭后重建空会话，仍拒绝其他线程调用。
    void reset();

    // 从只读上下文配置会话，不生成网络输出。
    void configure(const nlohmann::json& cfg);

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

    // 校验总容量后按堆叠上限生成地面物品，坐标以毫米表示。
    void drop(const Str& cfg_id, i64 count, i64 x, i64 y);

    // 原子转移整份地面物品到操作者背包；失败返回错误码且不改变物品。
    Str pickup(const Str& owner, const Str& item_id);

    // 记录玩家受到正伤害的 Tick，用于撤离取消裁定。
    void hurt(const Str& actor);

    // 查询当前 Tick 是否发生过玩家受伤。
    bool hurt_now() const;

    // 读取已经累计的撤离游戏 Tick。
    i64 get_extract_ticks() const;

    // 写入服务端裁定的撤离点、进度及原因。
    void set_extract(i64 id, i64 ticks, Str reason);

    // 冻结局内动作并进入待保存阶段，成功持久化由 Runtime 确认。
    void finish(Str outcome);

    // 创建合法实体；预期拒绝返回带冒号前缀的错误码且不消费身份。
    Str spawn(const Str& kind, const Str& spawn_id);

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

    // 导出原生状态；导出前完整校验状态关系。
    Str save() const;

    // 在独立候选完整校验后替换局内数据，旧对象句柄失效。
    void load(const Str& text);

    // 检查当前原生世界，不修复状态或发送消息。
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

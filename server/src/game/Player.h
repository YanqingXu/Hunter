// 在单位基类上持有玩家身份、输入、姿态恢复和装备使用状态，脚本仅借用句柄。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Unit.h"
#include "game/Weapon.h"
#include <array>

namespace hunter
{

class World;

// 保存固定工具槽的配置身份、次数与实例身份，业务属性只由 Lua 解析。
struct ToolSlot
{
    u32 cfg_id = 0;
    i32 count = 0;
    u64 instance = 0;
};

// 保留本局曾获得的技能及消费标记，避免一次性技能消费后再次拾取。
struct SkillSlot
{
    u32 cfg_id = 0;
    bool spent = false;
};

class Player : public Unit
{
public:
    World* world = nullptr;
    Vec<SkillSlot> skills;
    bool downed = false;
    bool vision = false;
    u64 death_seq = 0;
    i32 quiet_ticks = 0;

    // 返回本局获得过的技能数，含已消费记录。
    i64 get_skill_count() const;
    // 返回一基技能槽的配置身份。
    Str get_skill_id(i64 slot) const;
    // 返回一基技能槽是否已消费。
    bool get_skill_spent(i64 slot) const;
    // 原子添加新技能，重复或容量不足时不修改。
    bool add_skill(const Str& cfg);
    // 返回是否正在等待玩家选择复活。
    bool get_downed() const;
    // 对已经致死的玩家建立一次复活选择，重复调用不增加序号。
    void enter_downed();
    // 返回当前死亡序号，供复活请求关联。
    Str get_death_seq() const;
    // 原子复活并消费指定技能和死亡型技能，失败保持原状态。
    bool revive(const Str& seq, i64 health, i64 energy, const Str& skill,
        const Str& removed);
    // 返回复活后目标获取抑制的剩余 Tick。
    i64 get_quiet_ticks() const;
    // 写入有界的目标获取抑制计时。
    void set_quiet_ticks(i64 ticks);
    // 返回特殊视角状态。
    bool get_vision() const;
    // 切换特殊视角并清理禁止的输入和未完成动作。
    void set_vision(bool enabled);
    Weapon weapon;
    Weapon other_weapon;
    i32 other_reserve = 0;
    i32 weapon_count = 1;
    i32 active_weapon = 1;
    Arr<u32, 2> ammo_cfg_ids{};
    Arr<ToolSlot, 8> tools{};
    Vec<i32> health_segments;
    u64 last_tool_instance = 0;
    u64 use_instance = 0;
    i32 reserved_projectile = 0;
    u64 player_id = 0;
    i32 reserve = 0;
    i32 move_x = 0;
    i32 aim_x = 1000;
    i32 aim_y = 0;
    bool jump = false;
    bool fire = false;
    bool fire_once = false;
    bool reload = false;

    i32 move_y = 0;

    // 读取move_y的权威标量。
    i64 get_move_y() const;

    // 校验并写入move_y，只允许当前可写执行片段。
    void set_move_y(i64 value);

    i32 stamina = 100;

    // 读取stamina的权威标量。
    i64 get_stamina() const;

    // 校验并写入stamina，只允许当前可写执行片段。
    void set_stamina(i64 value);

    i32 stamina_delay = 0;

    // 读取stamina_delay的权威标量。
    i64 get_stamina_delay() const;

    // 校验并写入stamina_delay，只允许当前可写执行片段。
    void set_stamina_delay(i64 value);

    i32 stamina_rem = 0;

    // 读取stamina_rem的权威标量。
    i64 get_stamina_rem() const;

    // 校验并写入stamina_rem，只允许当前可写执行片段。
    void set_stamina_rem(i64 value);

    i32 health_delay = 0;

    // 读取health_delay的权威标量。
    i64 get_health_delay() const;

    // 校验并写入health_delay，只允许当前可写执行片段。
    void set_health_delay(i64 value);

    i32 health_rem = 0;

    // 读取health_rem的权威标量。
    i64 get_health_rem() const;

    // 校验并写入health_rem，只允许当前可写执行片段。
    void set_health_rem(i64 value);

    i32 melee_ticks = 0;

    // 读取melee_ticks的权威标量。
    i64 get_melee_ticks() const;

    // 校验并写入melee_ticks，只允许当前可写执行片段。
    void set_melee_ticks(i64 value);

    i32 ladder_id = 0;

    // 读取ladder_id的权威标量。
    i64 get_ladder_id() const;

    // 校验并写入ladder_id，只允许当前可写执行片段。
    void set_ladder_id(i64 value);

    i32 selected_slot = 0;

    // 读取selected_slot的权威标量。
    i64 get_selected_slot() const;

    // 校验并写入selected_slot，只允许当前可写执行片段。
    void set_selected_slot(i64 value);

    i32 use_slot = 0;

    // 读取use_slot的权威标量。
    i64 get_use_slot() const;

    // 校验并写入use_slot，只允许当前可写执行片段。
    void set_use_slot(i64 value);

    i32 use_ticks = 0;

    // 读取use_ticks的权威标量。
    i64 get_use_ticks() const;

    // 校验并写入use_ticks，只允许当前可写执行片段。
    void set_use_ticks(i64 value);

    bool run = false;

    // 读取run意图或状态。
    bool get_run() const;

    // 写入run，只允许当前可写执行片段。
    void set_run(bool value);

    bool prone = false;

    // 读取prone意图或状态。
    bool get_prone() const;

    // 写入prone，只允许当前可写执行片段。
    void set_prone(bool value);

    bool want_prone = false;

    // 读取want_prone意图或状态。
    bool get_want_prone() const;

    // 写入want_prone，只允许当前可写执行片段。
    void set_want_prone(bool value);

    bool running = false;

    // 读取running意图或状态。
    bool get_running() const;

    // 写入running，只允许当前可写执行片段。
    void set_running(bool value);

    bool melee = false;

    // 读取melee意图或状态。
    bool get_melee() const;

    // 写入melee，只允许当前可写执行片段。
    void set_melee(bool value);

    // 返回已冻结的生命段数量。
    i64 get_segment_count() const;

    // 按一基索引返回生命段宽度。
    i64 get_segment(i64 index) const;

    // 返回武器槽数量。
    i64 get_weapon_count() const;

    // 返回当前武器的一基槽号。
    i64 get_active_weapon() const;

    // 原子切换武器并取消未完成动作，保留各枪已装弹药。
    bool switch_weapon(i64 slot);

    // 按稳定槽位读取配置身份。
    Str get_weapon_cfg(i64 slot) const;

    // 按稳定槽位借用枪械组件，不复制权威数据。
    Weapon& weapon_at(i64 slot);

    // 按稳定槽位只读借用枪械组件。
    const Weapon& weapon_at(i64 slot) const;

    // 按稳定槽位读取备用弹药。
    i64 get_weapon_reserve(i64 slot) const;

    // 按稳定槽位写入备用弹药。
    void set_weapon_reserve(i64 slot, i64 value);

    // 读取工具配置身份，空槽返回零。
    Str get_tool_cfg(i64 slot) const;

    // 读取工具剩余次数。
    i64 get_tool_count(i64 slot) const;

    // 读取工具实例身份，替换槽位时旧实例失效。
    Str get_tool_instance(i64 slot) const;

    // 检查通用范围后修改槽位，同一配置的数量变化保留实例身份。
    void change_tool(i64 slot, const Str& tool_cfg_id, i64 count);

    // 绑定工具实例并为投掷物预留容量，失败不启动使用。
    bool start_use(i64 slot, i64 ticks, bool projectile);

    // 取消使用并释放未消费的投掷物预留。
    void cancel_use();

    // 按 Lua 指定的最终次数与清槽意图原子完成，拒绝已经替换的实例。
    bool finish_use(i64 count, bool clear);

    // 返回正在使用的实例身份。
    Str get_use_instance() const;

    // 按稳定槽位读取ammo。
    i64 get_weapon_ammo(i64 slot) const;

    // 按稳定槽位修改ammo。
    void set_weapon_ammo(i64 slot, i64 value);

    // 按稳定槽位读取shot_ticks。
    i64 get_weapon_shot_ticks(i64 slot) const;

    // 按稳定槽位修改shot_ticks。
    void set_weapon_shot_ticks(i64 slot, i64 value);

    // 按稳定槽位读取reload_ticks。
    i64 get_weapon_reload_ticks(i64 slot) const;

    // 按稳定槽位修改reload_ticks。
    void set_weapon_reload_ticks(i64 slot, i64 value);

    // 读取player_id，不转移对象所有权。
    Str get_player_id() const;

    // 读取reserve，不转移对象所有权。
    i64 get_reserve() const;

    // 校验并修改reserve，只允许当前可写执行片段。
    void set_reserve(i64 value);

    // 读取move_x，不转移对象所有权。
    i64 get_move_x() const;

    // 校验并修改move_x，只允许当前可写执行片段。
    void set_move_x(i64 value);

    // 读取aim_x，不转移对象所有权。
    i64 get_aim_x() const;

    // 校验并修改aim_x，只允许当前可写执行片段。
    void set_aim_x(i64 value);

    // 读取aim_y，不转移对象所有权。
    i64 get_aim_y() const;

    // 校验并修改aim_y，只允许当前可写执行片段。
    void set_aim_y(i64 value);

    // 读取jump，不转移对象所有权。
    bool get_jump() const;

    // 校验并修改jump，只允许当前可写执行片段。
    void set_jump(bool value);

    // 读取fire，不转移对象所有权。
    bool get_fire() const;

    // 校验并修改fire，只允许当前可写执行片段。
    void set_fire(bool value);

    // 读取fire_once，不转移对象所有权。
    bool get_fire_once() const;

    // 校验并修改fire_once，只允许当前可写执行片段。
    void set_fire_once(bool value);

    // 读取reload，不转移对象所有权。
    bool get_reload() const;

    // 校验并修改reload，只允许当前可写执行片段。
    void set_reload(bool value);
};

}

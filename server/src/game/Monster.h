// 在单位基类上增加出生引用和怪物状态边界；行为选择仍由 Lua 负责。
#pragma once

#include "common/Types.h"
#include "game/Value.h"
#include "game/Unit.h"

namespace hunter
{

class Monster : public Unit
{
public:
    u32 spawn_id = 0;
    Str state = "spawn";
    i32 attack_ticks = 0;
    bool aware = false;
    i32 alert_ticks = 0;
    i32 rage_ticks = 0;
    u32 rage_mask = 0;
    i32 last_hp = 0;
    u32 active_ability = 0;
    Str ability_phase = "idle";
    i32 ability_ticks = 0;
    Arr<i32, 8> ability_cds{};
    u64 owner_id = 0;
    i32 owner_ability = 0;
    i32 outside_ticks = 0;
    i32 wave = 1;

    // 读取当前警戒标记。
    bool get_aware() const;

    // 写入警戒标记，死亡对象不得重新警戒。
    void set_aware(bool value);

    // 读取脱战倒计时。
    i64 get_alert_ticks() const;

    // 写入有界脱战倒计时。
    void set_alert_ticks(i64 value);

    // 读取狂暴倒计时。
    i64 get_rage_ticks() const;

    // 写入有界狂暴倒计时。
    void set_rage_ticks(i64 value);

    // 读取本局已经触发的血量阈值位图。
    i64 get_rage_mask() const;

    // 追加已经触发的阈值，禁止回血后重新武装。
    void set_rage_mask(i64 value);

    // 读取上次结算阈值时的生命值。
    i64 get_last_hp() const;

    // 保存本次生命值供下一次阈值比较。
    void set_last_hp(i64 value);

    // 返回当前技能的配置身份。
    Str get_active_ability() const;

    // 返回当前技能阶段。
    Str get_ability_phase() const;

    // 返回当前技能阶段剩余时间。
    i64 get_ability_ticks() const;

    // 完整校验后一次提交当前技能阶段，空闲必须清除身份和计时。
    void write_ability(const Str& ability_cfg, const Str& phase, i64 ticks);

    // 读取一基技能槽的冷却时间。
    i64 get_ability_cd(i64 slot) const;

    // 写入一基技能槽的冷却时间。
    void set_ability_cd(i64 slot, i64 ticks);

    // 返回召唤者实体身份，普通出生返回零。
    Str get_owner_id() const;

    // 返回产生本召唤物的一基技能槽。
    i64 get_owner_ability() const;

    // 读取连续处于两个警戒圈外的时间。
    i64 get_outside_ticks() const;

    // 写入连续处于两个警戒圈外的时间。
    void set_outside_ticks(i64 value);

    // 返回本实体所属刷新波次。
    i64 get_wave() const;

    // 读取spawn_id，不转移对象所有权。
    Str get_spawn_id() const;

    // 读取state，不转移对象所有权。
    Str get_state() const;

    // 校验状态和生死关系，禁止退回出生或离开死亡；死亡同时清除速度和冷却。
    void set_state(Str value);

    // 读取attack_ticks，不转移对象所有权。
    i64 get_attack_ticks() const;

    // 检查通用攻击冷却范围，出生和死亡只接受零值。
    void set_attack_ticks(i64 value);
};

}

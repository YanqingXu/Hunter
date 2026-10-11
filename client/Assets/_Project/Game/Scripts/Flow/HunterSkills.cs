using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    // Persisted identifiers: never reorder or reuse values when the catalogue grows.
    public enum HunterSkillId { EfficientMelee = 1, QuickReload = 2, SteadyAim = 3, Necromancer = 4, Pitcher = 5, EmergencyBandage = 6 }
    public enum HunterSkillLifetime { Regular, Once, Death }
    public enum HunterSkillCategory { Character, Gun, Item }
    public enum HunterSkillEffectKind { Numeric, Mechanic }

    public sealed class HunterSkillDefinition
    {
        public HunterSkillId Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public int Cost { get; private set; }
        public HunterSkillLifetime Lifetime { get; private set; }
        public HunterSkillCategory Category { get; private set; }
        public HunterSkillEffectKind EffectKind { get; private set; }
        public int Refund { get { return Cost == 1 ? 1 : Cost / 2; } }
        public float FireIntervalMultiplier { get; private set; }
        public float ReloadMultiplier { get; private set; }
        public float MeleeStaminaMultiplier { get; private set; }
        public float ThrowRangeMultiplier { get; private set; }

        internal HunterSkillDefinition(HunterSkillId id, string name, string description, int cost,
            float fireInterval, float reload, float meleeStamina, HunterSkillLifetime lifetime = HunterSkillLifetime.Regular,
            HunterSkillCategory category = HunterSkillCategory.Character, HunterSkillEffectKind effectKind = HunterSkillEffectKind.Numeric,
            float throwRange = 1)
        {
            Id = id; Name = name; Description = description; Cost = cost;
            Lifetime = lifetime; Category = category; EffectKind = effectKind; ThrowRangeMultiplier = throwRange;
            FireIntervalMultiplier = fireInterval; ReloadMultiplier = reload; MeleeStaminaMultiplier = meleeStamina;
        }
    }

    /// <summary>Skill catalogue. Prices and multipliers are provisional balance values.</summary>
    public static class HunterSkills
    {
        public const float RevivalHealthFraction = .5f;
        public const float RevivalUndetectedSeconds = 3;
        public const float EmergencyBandageHealing = 50;
        private static readonly IList<HunterSkillDefinition> catalogue = Array.AsReadOnly(new[] {
            new HunterSkillDefinition(HunterSkillId.EfficientMelee, "格斗节能", "人物 · 近战体力消耗降低20%（25 → 20）", 1, 1, 1, .8f),
            new HunterSkillDefinition(HunterSkillId.QuickReload, "快速装填", "枪械 · 换弹时间缩短25%，所有携带枪械生效", 2, 1, .75f, 1, category: HunterSkillCategory.Gun),
            new HunterSkillDefinition(HunterSkillId.SteadyAim, "稳定瞄准", "枪械 · 射击间隔缩短20%，所有携带枪械生效", 3, .8f, 1, 1, category: HunterSkillCategory.Gun),
            new HunterSkillDefinition(HunterSkillId.Necromancer, "死灵法师", "死亡技 · 复活1次，恢复50%生命，3秒不被警戒", 4, 1, 1, 1, HunterSkillLifetime.Death,
                effectKind: HunterSkillEffectKind.Mechanic),
            new HunterSkillDefinition(HunterSkillId.Pitcher, "投球好手", "道具 · 飞刀与炸药束的投掷距离增加50%", 2, 1, 1, 1,
                category: HunterSkillCategory.Item, throwRange: 1.5f),
            new HunterSkillDefinition(HunterSkillId.EmergencyBandage, "应急包扎", "一次性 · G 恢复50生命，用后移除；满血不消耗", 2, 1, 1, 1,
                HunterSkillLifetime.Once, effectKind: HunterSkillEffectKind.Mechanic)
        });
        public static IList<HunterSkillDefinition> Catalogue { get { return catalogue; } }
        public static HunterSkillDefinition Find(HunterSkillId id)
        {
            foreach (var skill in catalogue) if (skill.Id == id) return skill;
            return null;
        }
    }

    /// <summary>A deployment snapshot; never edits authored gun definitions or another hunter.</summary>
    public sealed class HunterSkillEffects
    {
        public float FireIntervalMultiplier { get; private set; }
        public float ReloadMultiplier { get; private set; }
        public float MeleeStaminaMultiplier { get; private set; }
        public float ThrowRangeMultiplier { get; private set; }
        public HunterSkillEffects(IEnumerable<HunterSkillId> skills)
        {
            FireIntervalMultiplier = ReloadMultiplier = MeleeStaminaMultiplier = ThrowRangeMultiplier = 1;
            if (skills == null) throw new ArgumentException("猎人技能列表缺失。");
            var seen = new HashSet<HunterSkillId>();
            foreach (var id in skills)
            {
                var skill = HunterSkills.Find(id);
                if (skill == null || !seen.Add(id)) throw new ArgumentException("未知或重复的猎人技能。");
                FireIntervalMultiplier *= skill.FireIntervalMultiplier;
                ReloadMultiplier *= skill.ReloadMultiplier;
                MeleeStaminaMultiplier *= skill.MeleeStaminaMultiplier;
                ThrowRangeMultiplier *= skill.ThrowRangeMultiplier;
            }
        }
        public HunterGunDefinition Apply(HunterGunDefinition source)
        {
            if (source == null) throw new ArgumentException("枪械配置缺失。");
            var gun = source.Snapshot();
            gun.FireInterval *= FireIntervalMultiplier;
            gun.ReloadSeconds *= ReloadMultiplier;
            return gun;
        }
        public HunterItemDefinition Apply(HunterItemDefinition source)
        {
            if (source == null) throw new ArgumentException("道具配置缺失。");
            return source.WithThrowRangeMultiplier(ThrowRangeMultiplier);
        }
    }
}

using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    // Save identities are append-only. Balance values are provisional until the design tables are finalized.
    public enum HunterItemId { Knife = 1, FirstAid = 2, ThrowingKnife = 3, HealthShot = 4, Dynamite = 5 }
    public enum HunterItemKind { Melee, Medical, Throwable }

    public sealed class HunterItemDefinition
    {
        public HunterItemId Id { get; private set; }
        public string Name { get; private set; }
        public bool Consumable { get; private set; }
        public HunterItemKind Kind { get; private set; }
        public int Cost { get; private set; }
        public int Capacity { get; private set; }
        public int RefillAmount { get; private set; }
        public float Value { get; private set; }
        public float Delay { get; private set; }
        public float StaminaCost { get; private set; }
        public float Range { get; private set; }
        public float Radius { get; private set; }
        public string Description { get; private set; }
        internal HunterItemDefinition(HunterItemId id, string name, bool consumable, HunterItemKind kind,
            int cost, int capacity, float value, float delay, float stamina, float range, float radius, string description, int refillAmount = 1)
        {
            Id = id; Name = name; Consumable = consumable; Kind = kind; Cost = cost; Capacity = capacity;
            RefillAmount = refillAmount; Value = value; Delay = delay; StaminaCost = stamina; Range = range; Radius = radius;
            Description = description;
        }
        internal HunterItemDefinition WithThrowRangeMultiplier(float multiplier)
        {
            var copy = (HunterItemDefinition)MemberwiseClone();
            if (Kind == HunterItemKind.Throwable) copy.Range *= multiplier;
            return copy;
        }
    }

    [Serializable]
    public sealed class HunterItemStack
    {
        public HunterItemId Id;
        public int Uses;
        public HunterItemStack Copy() { return new HunterItemStack { Id = Id, Uses = Uses }; }
    }

    public static class HunterItems
    {
        public const int ToolSlots = 2, ConsumableSlots = 2, WarehouseLimit = 1000;
        public const float UseMoveMultiplier = .5f;
        private static readonly IList<HunterItemDefinition> catalogue = Array.AsReadOnly(new[] {
            new HunterItemDefinition(HunterItemId.Knife, "匕首", false, HunterItemKind.Melee, 25, 1, 30, 0, 25, 0, 0,
                "常规近战 · 伤害30，消耗25体力，不限次数"),
            new HunterItemDefinition(HunterItemId.FirstAid, "急救包", false, HunterItemKind.Medical, 40, 3, 50, 2, 0, 0, 0,
                "常规医疗 · 3次，每次恢复50生命，准备2秒"),
            new HunterItemDefinition(HunterItemId.ThrowingKnife, "飞刀", false, HunterItemKind.Throwable, 30, 3, 30, .5f, 0, 8, 0,
                "常规投掷 · 3次，伤害30，准备0.5秒"),
            new HunterItemDefinition(HunterItemId.HealthShot, "生命针剂", true, HunterItemKind.Medical, 35, 1, 75, 1, 0, 0, 0,
                "消耗品 · 恢复75生命，准备1秒，用后移除"),
            new HunterItemDefinition(HunterItemId.Dynamite, "炸药束", true, HunterItemKind.Throwable, 60, 1, 60, .8f, 0, 6, 2,
                "消耗品 · 爆炸伤害60，半径2，可能伤及自己")
        });
        public static IList<HunterItemDefinition> Catalogue { get { return catalogue; } }
        public static HunterItemDefinition Find(HunterItemId id)
        { foreach (var item in catalogue) if (item.Id == id) return item; return null; }
        public static List<HunterItemStack> Copy(List<HunterItemStack> source)
        { return source == null ? null : source.ConvertAll(item => item == null ? null : item.Copy()); }
        public static void Validate(List<HunterItemStack> items, bool equipped)
        {
            if (items == null || items.Count > (equipped ? ToolSlots + ConsumableSlots : WarehouseLimit))
                throw new ArgumentException("道具栏位缺失或超出上限。");
            int tools = 0, consumables = 0; var seenTools = new HashSet<HunterItemId>();
            foreach (var stack in items)
            {
                var item = stack == null ? null : Find(stack.Id);
                if (item == null || stack.Uses < (item.Consumable || item.Kind == HunterItemKind.Melee ? 1 : 0) || stack.Uses > item.Capacity)
                    throw new ArgumentException("道具类型或使用次数不合法。");
                if (!equipped) continue;
                if (item.Consumable) consumables++;
                else { tools++; if (!seenTools.Add(item.Id)) throw new ArgumentException("常规工具不能重复装备。"); }
            }
            if (equipped && (tools > ToolSlots || consumables > ConsumableSlots)) throw new ArgumentException("道具栏位已满。");
        }
    }
}

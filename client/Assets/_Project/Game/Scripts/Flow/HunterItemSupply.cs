using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    [Serializable]
    public sealed class HunterConsumableWeight
    {
        public HunterItemId Item;
        public int Weight = 1;
    }

    /// <summary>An immutable proposed reward; the same proposal can be retried after a failed save.</summary>
    public sealed class HunterItemSupplyPlan
    {
        public string HunterId { get; private set; }
        public HunterItemId Item { get; private set; }
        public int Index { get; private set; }
        public int ExpectedUses { get; private set; }
        public int Amount { get; private set; }
        public int ExpectedConsumables { get; private set; }
        internal HunterItemSupplyPlan(string hunter, HunterItemId item, int index, int uses, int amount, int consumables = 0)
        { HunterId = hunter; Item = item; Index = index; ExpectedUses = uses; Amount = amount; ExpectedConsumables = consumables; }
    }

    public static class HunterItemSupply
    {
        public static List<int> RefillableTools(HunterRecord hunter)
        {
            var result = new List<int>();
            if (hunter == null || hunter.Items == null) return result;
            for (int i = 0; i < hunter.Items.Count; i++)
            {
                var stack = hunter.Items[i]; var item = HunterItems.Find(stack.Id);
                if (item != null && !item.Consumable && item.Kind != HunterItemKind.Melee && stack.Uses < item.Capacity) result.Add(i);
            }
            return result;
        }
        public static bool HasConsumableSlot(HunterRecord hunter)
        {
            return hunter != null && hunter.Items != null &&
                hunter.Items.FindAll(s => HunterItems.Find(s.Id).Consumable).Count < HunterItems.ConsumableSlots;
        }
        public static int ConsumableCount(HunterRecord hunter)
        { return hunter.Items.FindAll(s => HunterItems.Find(s.Id).Consumable).Count; }
        public static int TotalWeight(IList<HunterConsumableWeight> pool)
        {
            if (pool == null || pool.Count == 0 || pool.Count > 100) return 0;
            int total = 0;
            foreach (var entry in pool)
            {
                var item = entry == null ? null : HunterItems.Find(entry.Item);
                if (item == null || !item.Consumable || entry.Weight < 0 || entry.Weight > 1000000) return 0;
                total += entry.Weight;
            }
            return total;
        }
        public static HunterItemId ChooseConsumable(IList<HunterConsumableWeight> pool, Func<int, int> random)
        {
            int total = TotalWeight(pool);
            if (total <= 0 || random == null) return 0;
            int choice = random(total);
            if (choice < 0 || choice >= total) return 0;
            foreach (var entry in pool) { if (choice < entry.Weight) return entry.Item; choice -= entry.Weight; }
            return 0;
        }
        public static bool CanSupply(HunterRecord hunter, IList<HunterConsumableWeight> pool)
        { return RefillableTools(hunter).Count > 0 || (HasConsumableSlot(hunter) && TotalWeight(pool) > 0); }
        public static HunterItemSupplyPlan Plan(HunterRecord hunter, IList<HunterConsumableWeight> pool, Func<int, int> random)
        {
            if (hunter == null || random == null) return null;
            var tools = RefillableTools(hunter);
            if (tools.Count > 0)
            {
                int choice = random(tools.Count); if (choice < 0 || choice >= tools.Count) return null;
                int index = tools[choice]; var stack = hunter.Items[index]; var item = HunterItems.Find(stack.Id);
                return new HunterItemSupplyPlan(hunter.Id, stack.Id, index, stack.Uses, Math.Min(item.RefillAmount, item.Capacity - stack.Uses));
            }
            if (!HasConsumableSlot(hunter)) return null;
            var id = ChooseConsumable(pool, random); var reward = HunterItems.Find(id);
            return reward == null ? null : new HunterItemSupplyPlan(hunter.Id, id, -1, 0, reward.Capacity, ConsumableCount(hunter));
        }
        public static bool Matches(HunterRecord hunter, HunterItemSupplyPlan plan)
        {
            if (hunter == null || plan == null || hunter.Id != plan.HunterId) return false;
            var item = HunterItems.Find(plan.Item);
            if (item == null) return false;
            if (plan.Index < 0) return item.Consumable && plan.Amount == item.Capacity &&
                RefillableTools(hunter).Count == 0 && HasConsumableSlot(hunter) && ConsumableCount(hunter) == plan.ExpectedConsumables;
            if (item.Consumable || item.Kind == HunterItemKind.Melee || plan.Index >= hunter.Items.Count) return false;
            var stack = hunter.Items[plan.Index];
            return stack.Id == plan.Item && stack.Uses == plan.ExpectedUses && stack.Uses < item.Capacity &&
                plan.Amount == Math.Min(item.RefillAmount, item.Capacity - stack.Uses);
        }
    }
}

using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    // Persisted identifiers for the next career-format revision; never reorder these values.
    public enum HunterTemplateId { Recruited = 1, Relief = 2 }
    public sealed class HunterTemplateDefinition
    {
        public HunterTemplateId Id { get; private set; }
        public string Name { get; private set; }
        public int WeaponWeightLimit { get; private set; }
        public int CarriedSkillValueLimit { get; private set; }
        public int RecruitCost { get; private set; }
        public bool RequiresEmptyRoster { get; private set; }
        internal HunterTemplateDefinition(HunterTemplateId id, string name, int weight, int skills, int cost, bool emptyRoster)
        { Id = id; Name = name; WeaponWeightLimit = weight; CarriedSkillValueLimit = skills; RecruitCost = cost; RequiresEmptyRoster = emptyRoster; }
    }

    /// <summary>Two special-attribute profiles. Values are provisional balance settings; common body stats stay shared.</summary>
    public static class HunterTemplates
    {
        public const int RosterLimit = 5;
        public const int PaidRecruitCost = 100;
        private static readonly IList<HunterTemplateDefinition> catalogue = Array.AsReadOnly(new[] {
            new HunterTemplateDefinition(HunterTemplateId.Recruited, "招募猎人", 3, 14, PaidRecruitCost, false),
            new HunterTemplateDefinition(HunterTemplateId.Relief, "救济猎人", 2, 10, 0, true)
        });
        public static IList<HunterTemplateDefinition> Catalogue { get { return catalogue; } }
        public static HunterTemplateDefinition Find(HunterTemplateId id)
        { foreach (var item in catalogue) if (item.Id == id) return item; return null; }
        public static bool CanAcquire(HunterTemplateId id, int rosterCount, int credits)
        {
            var item = Find(id);
            return item != null && rosterCount >= 0 && rosterCount < RosterLimit && credits >= item.RecruitCost &&
                (!item.RequiresEmptyRoster || rosterCount == 0);
        }
        public static bool CanCarryWeapons(HunterTemplateId id, int weight)
        { var item = Find(id); return item != null && weight >= 0 && weight <= item.WeaponWeightLimit; }
        public static bool CanCarrySkills(HunterTemplateId id, int value)
        { var item = Find(id); return item != null && value >= 0 && value <= item.CarriedSkillValueLimit; }
        public static bool CanAddSkill(HunterTemplateId id, int currentValue, int cost)
        {
            var item = Find(id);
            return item != null && currentValue >= 0 && currentValue <= item.CarriedSkillValueLimit && cost > 0 &&
                cost <= item.CarriedSkillValueLimit - currentValue;
        }
    }
}

using System;
using BigWorld.Gameplay;

public static class HunterTemplateChecks
{
    public static void Run(Action<bool, string> check)
    {
        var paid = HunterTemplateId.Recruited; var relief = HunterTemplateId.Relief;
        check(HunterTemplates.Catalogue.Count == 2 && (int)paid == 1 && (int)relief == 2,
            "The catalogue has exactly two stable template identifiers");
        check(HunterTemplates.Find(paid).WeaponWeightLimit == 3 && HunterTemplates.Find(relief).WeaponWeightLimit == 2 &&
            HunterTemplates.Find(paid).CarriedSkillValueLimit == 14 && HunterTemplates.Find(relief).CarriedSkillValueLimit == 10,
            "The two templates differ in weapon and carried-skill capacities");
        bool immutable = false;
        try { HunterTemplates.Catalogue.RemoveAt(0); } catch (NotSupportedException) { immutable = true; }
        check(immutable && HunterTemplates.Catalogue.Count == 2, "Callers cannot remove or append a third template at runtime");
        check(HunterTemplates.CanAcquire(paid, 0, 100) && HunterTemplates.CanAcquire(paid, 4, 100) && !HunterTemplates.CanAcquire(paid, 4, 99),
            "Paid acquisition retains the existing price and allows the last free roster slot");
        check(HunterTemplates.CanAcquire(relief, 0, 0) && HunterTemplates.CanAcquire(relief, 0, 100) && !HunterTemplates.CanAcquire(relief, 1, 100),
            "Relief needs an empty roster and no recruitment charge even when funds exist");
        check(!HunterTemplates.CanAcquire(paid, 5, int.MaxValue) && !HunterTemplates.CanAcquire(relief, 5, int.MaxValue),
            "Neither template exceeds roster capacity");
        check(!HunterTemplates.CanAcquire(paid, -1, 100) && !HunterTemplates.CanAcquire(relief, 0, -1) &&
            !HunterTemplates.CanAcquire(paid, int.MaxValue, int.MaxValue), "Malformed account counts and negative balances cannot acquire a template");
        check(HunterTemplates.CanCarryWeapons(paid, 3) && !HunterTemplates.CanCarryWeapons(relief, 3),
            "A rifle-plus-pistol loadout fits the recruited template but exceeds relief capacity");
        check(HunterTemplates.CanCarryWeapons(relief, 2) && HunterTemplates.CanCarryWeapons(relief, 1) &&
            HunterTemplates.CanCarryWeapons(relief, 0), "Relief can hold either existing gun; an empty camp loadout remains valid");
        check(!HunterTemplates.CanCarryWeapons(paid, -1) && !HunterTemplates.CanCarryWeapons(paid, 4) &&
            !HunterTemplates.CanCarryWeapons(relief, int.MaxValue), "Negative and excessive weapon weights are rejected");
        check(HunterTemplates.CanCarrySkills(paid, 14) && !HunterTemplates.CanCarrySkills(paid, 15) &&
            HunterTemplates.CanCarrySkills(relief, 10) && !HunterTemplates.CanCarrySkills(relief, 11),
            "Learned-skill value accepts the exact template limit and rejects one point beyond it");
        check(HunterTemplates.CanCarrySkills(relief, 0) && !HunterTemplates.CanCarrySkills(relief, -1), "An empty skill set is valid but a negative cost sum is not");
        check(HunterTemplates.CanAddSkill(relief, 8, 2) && !HunterTemplates.CanAddSkill(relief, 8, 3) &&
            !HunterTemplates.CanAddSkill(paid, 14, 1), "Both camp purchase and field pickup can use the same remaining-capacity rule");
        check(!HunterTemplates.CanAddSkill(paid, 1, 0) && !HunterTemplates.CanAddSkill(paid, 1, -1) &&
            !HunterTemplates.CanAddSkill(paid, -1, 1), "Skill admission requires a positive cost and a valid current carried value");
        check(!HunterTemplates.CanAddSkill(paid, int.MaxValue, 1) && !HunterTemplates.CanAddSkill(paid, 1, int.MaxValue),
            "Overflow-sized costs cannot wrap around the carrying limit");
        check(!HunterTemplates.CanAddSkill(relief, 10, 2) && HunterTemplates.CanAddSkill(relief, 8, 2),
            "Removing or consuming a carried two-point skill makes room for exactly one replacement");
        bool invalid = true;
        foreach (var id in new[] { (HunterTemplateId)0, (HunterTemplateId)(-1), (HunterTemplateId)3, (HunterTemplateId)int.MaxValue })
            invalid &= HunterTemplates.Find(id) == null && !HunterTemplates.CanAcquire(id, 0, 100) &&
                !HunterTemplates.CanCarryWeapons(id, 0) && !HunterTemplates.CanCarrySkills(id, 0) && !HunterTemplates.CanAddSkill(id, 0, 1);
        check(invalid, "Unknown template identifiers fail every admission path instead of falling back silently");
    }
}

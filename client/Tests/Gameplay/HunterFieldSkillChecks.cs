using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterFieldSkillChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew(); string id = career.Selected.Id;
        check(!career.AcquireFieldSkill(id, HunterSkillId.SteadyAim), "Field skills cannot be acquired in camp");
        career.Recruit(); string reserve = career.Selected.Id; career.Select(id); career.BeginRun();
        check(!career.AcquireFieldSkill(reserve, HunterSkillId.EfficientMelee) && !career.AcquireFieldSkill(id, (HunterSkillId)99),
            "Field rewards reject inactive hunters and unknown skill identities");
        int money = career.Credits;
        check(career.AcquireFieldSkill(id, HunterSkillId.SteadyAim) && career.Selected.FieldSkillValue == 3 &&
            career.Selected.SkillPoints == 0 && career.Selected.Experience == 0 && career.Credits == money,
            "A level-one hunter receives a field skill without spending points or granting currency, points or experience");
        check(!career.AcquireFieldSkill(id, HunterSkillId.SteadyAim) && career.Selected.FieldSkillValue == 3 && career.Selected.Skills.Count == 1,
            "A repeated field reward neither stacks its skill nor increases its acquisition value");
        check(new HunterCareer(career.Snapshot()).Selected.HasSkill(HunterSkillId.SteadyAim), "A skill acquired without level points passes current-schema validation");
        check(!career.RemoveSkill(id, HunterSkillId.SteadyAim), "Field skills cannot be converted into points during deployment");
        career.Settle(CareerOutcome.Extracted, 0, 0);
        check(career.Selected.HasSkill(HunterSkillId.SteadyAim) && career.Selected.Level == 1, "Successful extraction retains field skills even without gaining a level");
        check(career.RemoveSkill(id, HunterSkillId.SteadyAim) && career.Selected.SkillPoints == 1 && career.Selected.FieldSkillValue == 3,
            "A field-acquired three-point skill uses the same one-point removal refund as a purchased skill");
        check(new HunterCareer(career.Snapshot()).Selected.SkillPoints == 1 && !career.RemoveSkill(id, HunterSkillId.SteadyAim),
            "Refunds from field skills survive validation while repeated removal cannot grant points twice");
        check(career.BuySkill(id, HunterSkillId.EfficientMelee) && career.Selected.SkillPoints == 0,
            "A valid field-skill refund can be spent in the existing camp skill shop");
        career.BeginRun(); career.AcquireFieldSkill(id, HunterSkillId.SteadyAim);
        check(career.Selected.FieldSkillValue == 6 && career.Selected.Skills.Count == 2,
            "A removed skill can be acquired again in a later run with a new acquisition record");
        career.Settle(CareerOutcome.Extracted, 0, 200);
        check(career.Selected.SkillPoints == 2 && career.Selected.Level == 3 && career.Selected.FieldSkillValue == 6,
            "Level rewards add only their normal points alongside field-acquired skills");
        var death = new HunterCareer(career.Snapshot()); death.BeginRun(); death.Settle(CareerOutcome.Lost, 0);
        check(death.Selected.Id == reserve && death.Selected.FieldSkillValue == 0 && death.Selected.Skills.Count == 0,
            "Death removes field skills and their value without transferring either to another hunter");
        var retirement = new HunterCareer(career.Snapshot()); retirement.BeginRun(); retirement.Settle(CareerOutcome.Extracted, 0, 900); retirement.Retire(id);
        check(retirement.Selected.Id == reserve && retirement.Selected.SkillPoints == 0 && retirement.Selected.FieldSkillValue == 0,
            "Retirement does not transfer field acquisition value or refunds");
        var invalid = career.Snapshot(); invalid.Hunters[0].FieldSkillValue = -1;
        check(Throws(() => new HunterCareer(invalid)), "Negative field acquisition value is rejected");
        invalid = career.Snapshot(); invalid.Hunters[0].FieldSkillValue = int.MaxValue;
        check(Throws(() => new HunterCareer(invalid)), "Excessive field acquisition value is rejected without overflowing validation");
        invalid = career.Snapshot(); invalid.Hunters[0].FieldSkillValue = 0;
        check(Throws(() => new HunterCareer(invalid)), "Field skills require enough recorded value to explain their point budget");
        invalid = career.Snapshot(); invalid.Hunters[0].FieldSkillValue = HunterCareer.FieldSkillValueLimit;
        var full = new HunterCareer(invalid); full.BeginRun();
        check(!full.AcquireFieldSkill(id, HunterSkillId.QuickReload) && full.Selected.FieldSkillValue == HunterCareer.FieldSkillValueLimit,
            "A saturated field-value counter rejects a pickup without partially granting it");

        var rifle = new HunterGunDefinition();
        var pistol = new HunterGunDefinition { Weight = 1, MagazineCapacity = 8, FireInterval = .35f, ReloadSeconds = 1.2f };
        var weapons = new HunterWeapons(new[] { rifle, pistol }, new[] { new HunterAmmoDefinition() }, 3);
        weapons.TryFire(); weapons.Tick(.2f); weapons.TrySwitch(1);
        var modified = weapons.WithTimingMultipliers(.8f, .75f);
        check(modified.ActiveIndex == 1 && modified.Magazine == 8 && modified.Reserve == 58 && modified.Total(HunterAmmoType.Standard) == 71,
            "A live skill update retains active weapon, independent magazines, shared reserve and ammo total");
        modified.Tick(.47f); check(!modified.CanShoot, "A live skill update preserves the remaining fraction of fire cooldown");
        modified.Tick(.02f); check(modified.TryFire() && modified.Magazine == 7,
            "Updated cooldown finishes at the modified time and still spends one round");
        check(weapons.Magazine == 8 && !weapons.CanShoot && weapons.Reserve == 58,
            "Preparing and using a modified loadout cannot mutate the old loadout");
        modified.TryReload(); modified.Tick(.3f);
        var same = modified.WithTimingMultipliers(.8f, .75f);
        check(Near(same.FireInterval, .28f) && Near(same.ReloadProgress, 1f / 3),
            "Reapplying the same skill set does not compound effects or reset reload progress");
        var plain = same.WithTimingMultipliers(1, 1);
        check(Near(plain.FireInterval, .35f) && Near(plain.ReloadProgress, 1f / 3),
            "Restoring base timings retains relative reload progress");
        plain.Tick(.79f); check(plain.IsReloading, "Changing skills cannot finish a pending reload early");
        plain.Tick(.02f);
        check(!plain.IsReloading && plain.Magazine == 8 && plain.Reserve == 57 && plain.Total(HunterAmmoType.Standard) == 70,
            "A preserved reload completes normally and draws only missing rounds from reserve");
        check(modified.IsReloading && modified.Magazine == 7 && Near(modified.ReloadProgress, 1f / 3),
            "The prepared snapshot owns independent reload and ammunition state");
        plain.TrySwitch(0); check(plain.Magazine == 5 && Near(plain.FireInterval, .8f), "Switching after a live update restores the other weapon's existing magazine");
        check(plain.Refill() == 2 && plain.Total(HunterAmmoType.Standard) == 72, "A live skill update retains supply and ammo-capacity rules");
        check(Throws(() => weapons.WithTimingMultipliers(float.NaN, 1)) && Throws(() => weapons.WithTimingMultipliers(0, 1)) &&
            Throws(() => weapons.WithTimingMultipliers(1, float.PositiveInfinity)) && Throws(() => weapons.WithTimingMultipliers(1, float.MaxValue)),
            "Invalid or overflowing timing modifiers are rejected before publishing combat state");

        string path = Path.Combine(directory, "field-skills.json");
        var store = new HunterCareerStore(path, decode, encode); store.Load(); string storedId = store.Current.Selected.Id;
        store.Change(item => item.BeginRun()); string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(item => item.AcquireFieldSkill(storedId, HunterSkillId.SteadyAim))) &&
                store.Current.Selected.Skills.Count == 0 && store.Current.Selected.FieldSkillValue == 0 && File.ReadAllText(path) == before,
                "A failed pickup save publishes neither the skill nor its acquisition value");
        store.Change(item => item.AcquireFieldSkill(storedId, HunterSkillId.SteadyAim));
        var savedRun = new HunterCareer(decode(File.ReadAllText(path)));
        check(savedRun.InRun && savedRun.Selected.HasSkill(HunterSkillId.SteadyAim) && savedRun.Selected.FieldSkillValue == 3 && savedRun.Selected.SkillPoints == 0,
            "An immediate pickup save retains the active-run marker along with its skill");
        before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(item => item.Settle(CareerOutcome.Extracted, 250, 200))) && store.Current.InRun &&
                store.Current.Selected.FieldSkillValue == 3 && store.Current.Selected.Experience == 0 && File.ReadAllText(path) == before,
                "Failed extraction keeps acquired skills but cannot award experience or close the run");
        store.Change(item => item.Settle(CareerOutcome.Extracted, 250, 200)); store.Load();
        check(store.Current.Selected.HasSkill(HunterSkillId.SteadyAim) && store.Current.Selected.FieldSkillValue == 3 && store.Current.Selected.SkillPoints == 2,
            "Successful extraction persists field skills and normal level rewards together");
        store.Change(item => item.RemoveSkill(storedId, HunterSkillId.SteadyAim)); store.Load();
        check(store.Current.Selected.SkillPoints == 3 && store.Current.Selected.Skills.Count == 0 && store.Current.Selected.FieldSkillValue == 3,
            "Field-skill removal persists its normal refund beyond the points earned by leveling");
        store.Change(item => item.BeginRun()); store.Change(item => item.AcquireFieldSkill(storedId, HunterSkillId.SteadyAim)); store.Load();
        check(store.Current.HunterCount == 0 && store.Current.LastOutcome == CareerOutcome.Interrupted,
            "Loading an interrupted run permanently removes newly acquired skills with their hunter");

        const string legacy = "{\"Version\":3,\"Credits\":425,\"Rifles\":2,\"Pistols\":3,\"SelectedId\":\"veteran\",\"ActiveId\":\"\",\"Recruits\":1,\"Extractions\":2,\"Losses\":0,\"LastOutcome\":1,\"LastReward\":250,\"LastExperience\":275,\"LastLevelsGained\":3,\"AccountExperience\":500,\"Retirements\":1,\"Hunters\":[{\"Id\":\"veteran\",\"Name\":\"Veteran\",\"Rifle\":true,\"Pistol\":true,\"Experience\":550,\"SkillPoints\":1,\"Skills\":[1,3]}]}";
        string migrationPath = Path.Combine(directory, "v3.json"); File.WriteAllText(migrationPath, legacy);
        var migration = new HunterCareerStore(migrationPath, decode, encode);
        using (var locked = new FileStream(migrationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => migration.Load()) && migration.Current == null && File.ReadAllText(migrationPath) == legacy,
                "Version-three migration cannot publish a profile before its replacement is saved");
        migration.Load();
        check(migration.Migrated && migration.Current.Snapshot().Version == HunterCareer.CurrentVersion && File.ReadAllText(migrationPath + ".bak") == legacy &&
            migration.Current.Selected.Skills.Count == 2 && migration.Current.Selected.FieldSkillValue == 0 && migration.Current.Selected.SkillPoints == 1 &&
            migration.Current.Selected.Experience == 550 && migration.Current.AccountExperience == 500 && migration.Current.Credits == 425,
            "Version-three migration preserves purchased skills, points, growth and account data without inventing field rewards");
        migration.Load(); check(!migration.Migrated && migration.Current.Selected.FieldSkillValue == 0, "Reloading a migrated profile does not grant new acquisition value");
        File.WriteAllText(migrationPath, "damaged"); File.WriteAllText(migrationPath + ".bak", legacy); migration.Load();
        check(migration.Migrated && migration.RecoveredBackup && migration.Current.Selected.HasSkill(HunterSkillId.SteadyAim),
            "A valid version-three backup restores purchased skills before migrating");
    }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

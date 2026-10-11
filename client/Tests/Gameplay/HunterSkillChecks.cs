using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Gameplay;

public static class HunterSkillChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew(); string id = career.Selected.Id;
        check(!career.BuySkill(id, HunterSkillId.EfficientMelee) && career.Selected.Skills.Count == 0,
            "A fresh hunter cannot buy skills without earned points");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 900);
        check(!career.BuySkill(id, (HunterSkillId)99) && !career.RemoveSkill(id, (HunterSkillId)99) &&
            !career.BuySkill("stale", HunterSkillId.SteadyAim), "Unknown skills and stale identities cannot spend or refund points");
        check(career.BuySkill(id, HunterSkillId.SteadyAim) && career.Selected.SkillPoints == 6,
            "Steady aim costs three earned points");
        check(!career.BuySkill(id, HunterSkillId.SteadyAim) && career.Selected.SkillPoints == 6 && career.Selected.Skills.Count == 1,
            "Duplicate purchases do not charge or stack effects");
        check(career.BuySkill(id, HunterSkillId.QuickReload) && career.BuySkill(id, HunterSkillId.EfficientMelee) && career.Selected.SkillPoints == 3,
            "Different passive skills coexist and deduct their individual prices");
        int credits = career.Credits;
        career.Recruit(); string other = career.Selected.Id;
        check(career.Selected.Skills.Count == 0 && !career.RemoveSkill(id, HunterSkillId.SteadyAim) &&
            !career.BuySkill(id, HunterSkillId.SteadyAim), "A different selected hunter cannot change the previous hunter's skills");
        career.Select(id); var snapshot = career.Snapshot(); snapshot.Hunters[0].Skills.Clear();
        var exposed = career.Selected; exposed.Skills.Clear(); var indexed = career.GetHunter(0); indexed.Skills.Clear();
        check(career.Selected.Skills.Count == 3, "Snapshots and selected or indexed hunter records deep-copy their skill lists");
        var source = career.Snapshot(); var isolated = new HunterCareer(source); source.Hunters[0].Skills.Clear();
        check(isolated.Selected.Skills.Count == 3, "Constructing a career does not retain the caller's mutable skill list");
        career.BeginRun();
        check(!career.RemoveSkill(id, HunterSkillId.EfficientMelee) && !career.BuySkill(other, HunterSkillId.EfficientMelee),
            "Deployed hunters cannot buy or remove skills");
        career.Settle(CareerOutcome.Extracted, 0, 50);
        check(career.Selected.Skills.Count == 3 && career.Selected.SkillPoints == 3,
            "Extraction retains learned skills without granting extra points at the level cap");
        check(career.RemoveSkill(id, HunterSkillId.SteadyAim) && career.Selected.SkillPoints == 4,
            "Removing a three-point skill refunds one point by rounding down");
        check(career.RemoveSkill(id, HunterSkillId.QuickReload) && career.Selected.SkillPoints == 5,
            "Removing a two-point skill refunds exactly half");
        check(career.RemoveSkill(id, HunterSkillId.EfficientMelee) && career.Selected.SkillPoints == 6,
            "Removing a one-point skill refunds its full price");
        check(!career.RemoveSkill(id, HunterSkillId.EfficientMelee) && career.Selected.SkillPoints == 6,
            "Repeated removal cannot mint skill points");
        for (int i = 0; i < 10; i++) { career.BuySkill(id, HunterSkillId.EfficientMelee); career.RemoveSkill(id, HunterSkillId.EfficientMelee); }
        check(career.Selected.SkillPoints == 6 && career.Credits == credits - HunterCareer.RecruitCost,
            "Buying and removing skills never grants currency or extra skill points");
        career.BuySkill(id, HunterSkillId.SteadyAim); career.BuySkill(id, HunterSkillId.QuickReload); career.BuySkill(id, HunterSkillId.EfficientMelee);
        check(career.Selected.SkillPoints == 0 && new HunterCareer(career.Snapshot()).Selected.Skills.Count == 3,
            "Rebuying removed skills pays full price and remains valid after loading");
        var retired = new HunterCareer(career.Snapshot()); retired.Retire(id);
        check(retired.Selected.Id == other && retired.Selected.Skills.Count == 0 && retired.Selected.SkillPoints == 0,
            "Retirement cannot transfer skills or refunds to another hunter");
        var lost = new HunterCareer(career.Snapshot()); lost.BeginRun(); lost.Settle(CareerOutcome.Lost, 0);
        check(lost.Selected.Id == other && lost.Selected.Skills.Count == 0 && lost.Selected.SkillPoints == 0,
            "Permanent death removes the active hunter's skills without inheritance");

        var invalid = career.Snapshot(); invalid.Hunters[0].Skills.Add(HunterSkillId.SteadyAim);
        check(Throws(() => new HunterCareer(invalid)), "Duplicate saved skills are rejected");
        invalid = career.Snapshot(); invalid.Hunters[0].Skills.Add((HunterSkillId)99);
        check(Throws(() => new HunterCareer(invalid)), "Unknown saved skills are rejected");
        invalid = career.Snapshot(); invalid.Hunters[0].Skills = null;
        check(Throws(() => new HunterCareer(invalid)), "Null skill lists in the current schema are rejected");
        invalid = career.Snapshot(); invalid.Hunters[0].SkillPoints = 9;
        check(Throws(() => new HunterCareer(invalid)), "Owned skills and available points cannot exceed the hunter's earned budget");

        var gun = new HunterGunDefinition();
        var effects = new HunterSkillEffects(career.Selected.Skills);
        var modified = effects.Apply(gun);
        check(Near(modified.FireInterval, .64f) && Near(modified.ReloadSeconds, 1.5f) && Near(effects.MeleeStaminaMultiplier * 25, 20),
            "The learned skill set applies the configured cadence, reload and melee stamina modifiers");
        check(Near(gun.FireInterval, .8f) && Near(gun.ReloadSeconds, 2) && !ReferenceEquals(gun, modified) &&
            modified.MagazineCapacity == gun.MagazineCapacity && modified.Damage == gun.Damage && modified.Weight == gun.Weight,
            "Modifiers leave authored guns, magazine capacity, damage and weight unchanged");
        var weapon = new HunterWeapons(new[] { modified }, new[] { new HunterAmmoDefinition() }, 3);
        weapon.TryFire(); weapon.Tick(.63f);
        check(!weapon.CanShoot, "Improved weapon still enforces its shorter fire interval");
        weapon.Tick(.02f); check(weapon.TryFire(), "Improved weapon can fire before the base interval expires");
        weapon.TryReload(); weapon.Tick(1.49f); check(weapon.IsReloading, "Improved reload does not grant ammunition early");
        weapon.Tick(.02f); check(!weapon.IsReloading && weapon.Magazine == 6 && weapon.Reserve == 64,
            "Improved reload completes at 1.5 seconds and conserves the shared ammo pool");
        var plain = new HunterSkillEffects(new HunterSkillId[0]).Apply(gun);
        check(Near(plain.FireInterval, .8f) && Near(plain.ReloadSeconds, 2), "An unskilled hunter receives base weapon values");
        check(Throws(() => new HunterSkillEffects(new[] { HunterSkillId.SteadyAim, HunterSkillId.SteadyAim })) &&
            Throws(() => new HunterSkillEffects(new[] { (HunterSkillId)99 })), "Effect snapshots reject duplicated or unknown skills");

        string path = Path.Combine(directory, "skills.json"); File.WriteAllText(path, encode(career.Snapshot()));
        var store = new HunterCareerStore(path, decode, encode); store.Load();
        check(store.Current.Selected.Skills.Count == 3 && store.Current.Selected.SkillPoints == 0,
            "Purchased skills and spent points survive a real disk roundtrip");
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(item => item.RemoveSkill(id, HunterSkillId.SteadyAim))) &&
                store.Current.Selected.HasSkill(HunterSkillId.SteadyAim) && store.Current.Selected.SkillPoints == 0 && File.ReadAllText(path) == before,
                "A failed removal write neither removes the skill nor refunds points in memory");
        store.Change(item => item.RemoveSkill(id, HunterSkillId.EfficientMelee)); store.Load();
        check(!store.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) && store.Current.Selected.SkillPoints == 1,
            "A saved removal persists both the missing skill and its exact refund");
        before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(item => item.BuySkill(id, HunterSkillId.EfficientMelee))) &&
                !store.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) && store.Current.Selected.SkillPoints == 1 && File.ReadAllText(path) == before,
                "A failed purchase write neither grants a skill nor spends points in memory");
        store.Change(item => item.BuySkill(id, HunterSkillId.EfficientMelee)); store.Load();
        check(store.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) && store.Current.Selected.SkillPoints == 0,
            "Retrying a failed purchase grants the skill and charges exactly once");
        File.WriteAllText(path, "broken"); store.Load();
        check(store.RecoveredBackup && !store.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) && store.Current.Selected.SkillPoints == 1,
            "Backup recovery restores a consistent skill list and point balance from the same transaction");

        // Actual v2 JSON deliberately omits the Skills field, for both Unity and desktop serializers.
        const string legacy = "{\"Version\":2,\"Credits\":425,\"Rifles\":2,\"Pistols\":3,\"SelectedId\":\"veteran\",\"ActiveId\":\"\",\"Recruits\":1,\"Extractions\":2,\"Losses\":0,\"LastOutcome\":1,\"LastReward\":250,\"LastExperience\":275,\"LastLevelsGained\":3,\"AccountExperience\":500,\"Retirements\":1,\"Hunters\":[{\"Id\":\"veteran\",\"Name\":\"Veteran\",\"Rifle\":true,\"Pistol\":true,\"Experience\":550,\"SkillPoints\":5}]}";
        string migrationPath = Path.Combine(directory, "v2.json"); File.WriteAllText(migrationPath, legacy);
        var migration = new HunterCareerStore(migrationPath, decode, encode);
        using (var locked = new FileStream(migrationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => migration.Load()) && migration.Current == null && File.ReadAllText(migrationPath) == legacy,
                "Version-two migration must save successfully before publishing a profile");
        migration.Load();
        check(migration.Migrated && migration.Current.Snapshot().Version == HunterCareer.CurrentVersion && File.ReadAllText(migrationPath + ".bak") == legacy &&
            migration.Current.Selected.Skills.Count == 0 && migration.Current.Selected.SkillPoints == 5 && migration.Current.Selected.Experience == 550 &&
            migration.Current.AccountExperience == 500 && migration.Current.Retirements == 1 && migration.Current.Credits == 425,
            "Version-two migration preserves growth and account data and starts with no invented skills");
        migration.Load();
        check(!migration.Migrated && migration.Current.Selected.SkillPoints == 5, "Reloading migrated skill data cannot grant points again");
        migration.Change(item => item.BuySkill("veteran", HunterSkillId.SteadyAim)); migration.Load();
        check(migration.Current.Selected.HasSkill(HunterSkillId.SteadyAim) && migration.Current.Selected.SkillPoints == 2,
            "An existing veteran can spend previously earned points immediately after migration");
        migration.Change(item => item.BeginRun()); migration.Load();
        check(migration.Current.HunterCount == 0 && migration.Current.LastOutcome == CareerOutcome.Interrupted && migration.Current.AccountExperience == 500,
            "Interrupted deployment removes saved skills together with the hunter while retaining account experience");
    }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

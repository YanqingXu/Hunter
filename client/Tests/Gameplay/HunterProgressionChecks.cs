using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterProgressionChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew();
        string first = career.Selected.Id;
        check(career.Selected.Level == 1 && career.Selected.Experience == 0 && career.Selected.SkillPoints == 0 && career.AccountExperience == 0,
            "New hunters start at level one with zero experience and skill points");
        check(!career.CanRetireSelected && !career.Retire(first), "Retirement rejects a hunter below the level cap");
        check(HunterProgression.ExtractionReward(3, 6, true) == 395 && HunterProgression.ExtractionReward(0, 0, false) == 100,
            "Experience balance includes collected clues, both guard waves, boss and extraction");
        check(Throws(() => HunterProgression.ExtractionReward(-1, 0, true)) && Throws(() => HunterProgression.ExtractionReward(0, -1, true)) &&
            Throws(() => HunterProgression.ExtractionReward(int.MaxValue, 0, true)), "Invalid or overflowing experience counters are rejected");
        career.BeginRun();
        check(!career.Settle(CareerOutcome.Extracted, 0, -1) && !career.Settle(CareerOutcome.Lost, 0, 10) && !career.Settle(CareerOutcome.Retired, 0),
            "Failed missions cannot grant experience and retirement cannot impersonate a mission settlement");
        check(career.InRun && career.Selected.Experience == 0 && career.Selected.SkillPoints == 0, "Invalid experience settlements preserve the active run");
        career.Settle(CareerOutcome.Extracted, 250, 235);
        check(career.Selected.Experience == 235 && career.Selected.Level == 3 && career.Selected.LevelExperience == 35 && career.Selected.SkillPoints == 2 &&
            career.LastExperience == 235 && career.LastLevelsGained == 2, "One extraction can cross multiple levels and award exactly one point per level");
        check(!career.Settle(CareerOutcome.Extracted, 250, 235) && career.Selected.SkillPoints == 2 && career.Selected.Experience == 235,
            "Repeated settlement cannot grant experience or skill points twice");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 64);
        check(career.Selected.Level == 3 && career.Selected.LevelExperience == 99 && career.LastLevelsGained == 0, "Sub-threshold experience is retained without awarding a level");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 1);
        check(career.Selected.Level == 4 && career.Selected.LevelExperience == 0 && career.Selected.SkillPoints == 3, "Exactly crossing a threshold awards the next level");
        var spent = career.Snapshot(); spent.Hunters[0].SkillPoints = 1;
        var afterSpending = new HunterCareer(spent); afterSpending.BeginRun(); afterSpending.Settle(CareerOutcome.Extracted, 0, 100);
        check(afterSpending.Selected.Level == 5 && afterSpending.Selected.SkillPoints == 2, "Level gains add points without replenishing previously spent points");
        career.Recruit(); string second = career.Selected.Id; career.Toggle(HunterEquipment.Pistol);
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 99);
        check(career.Selected.Level == 1 && career.Selected.Experience == 99 && career.Selected.SkillPoints == 0, "Recruited hunters maintain their own experience progress");
        career.Select(first);
        check(career.Selected.Experience == 300 && career.Selected.SkillPoints == 3, "Switching hunters does not move experience or points between them");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, int.MaxValue);
        check(career.Selected.IsMaxLevel && career.Selected.Experience == 900 && career.Selected.SkillPoints == 9 && career.LastExperience == 600 && career.LastLevelsGained == 6,
            "Experience clamps at max level without overflow and reports the actual applied gain");
        career.BeginRun();
        check(!career.CanRetireSelected && !career.Retire(first), "An active max-level hunter cannot retire during a run");
        career.Settle(CareerOutcome.Extracted, 250, 395);
        check(career.Selected.Experience == 900 && career.Selected.SkillPoints == 9 && career.LastExperience == 0 && career.LastLevelsGained == 0,
            "Max-level hunters can still extract but cannot farm extra skill points");
        int rifles = career.Rifles, pistols = career.Pistols, credits = career.Credits;
        check(!career.Retire(second), "Retirement requires the exact currently selected hunter identity");
        check(career.Retire(first) && career.AccountExperience == 500 && career.Retirements == 1 && career.Selected.Id == second && career.HunterCount == 1,
            "Retirement removes the selected max-level hunter and converts it to account experience");
        check(career.Rifles == rifles + 1 && career.Pistols == pistols + 1 && career.Credits == credits && career.Selected.Experience == 99 && career.Selected.SkillPoints == 0,
            "Retirement returns equipped weapons, preserving money and the other hunter's growth");
        check(!career.Retire(first) && !career.Select(first) && career.AccountExperience == 500 && career.LastOutcome == CareerOutcome.Retired,
            "A retired hunter cannot be selected or rewarded again");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.HunterCount == 0 && career.AccountExperience == 500 && career.Retirements == 1 && career.LastExperience == 0,
            "Death removes hunter growth but preserves account experience from earlier retirements");
        career.ClaimRelief();
        check(career.Selected.Level == 1 && career.Selected.Experience == 0 && career.Selected.SkillPoints == 0 && career.AccountExperience == 500,
            "Relief creates a fresh hunter without inheriting lost or retired skill points");

        var maxData = HunterCareer.CreateNew().Snapshot();
        maxData.Hunters[0].Experience = 900; maxData.Hunters[0].SkillPoints = 9;
        var last = new HunterCareer(maxData); string lastId = last.Selected.Id;
        check(last.Retire(lastId) && last.HunterCount == 0 && last.Selected == null && last.CanClaimRelief && last.Rifles == 2 && last.Pistols == 2,
            "Retiring the final hunter returns gear and leaves the empty roster usable");
        maxData.Rifles = 1000000;
        var fullWarehouse = new HunterCareer(maxData);
        check(!fullWarehouse.CanRetireSelected && !fullWarehouse.Retire(fullWarehouse.Selected.Id) && fullWarehouse.AccountExperience == 0,
            "A full warehouse rejects retirement without losing a hunter or overflowing stock");
        maxData.Rifles = 1; maxData.AccountExperience = 1000000000;
        var fullAccount = new HunterCareer(maxData);
        check(!fullAccount.CanRetireSelected && !fullAccount.Retire(fullAccount.Selected.Id), "Retirement cannot overflow account experience");
        var invalid = HunterCareer.CreateNew().Snapshot(); invalid.Hunters[0].Experience = -1;
        check(Throws(() => new HunterCareer(invalid)), "Negative hunter experience is rejected on load");
        invalid = HunterCareer.CreateNew().Snapshot(); invalid.Hunters[0].Experience = 901;
        check(Throws(() => new HunterCareer(invalid)), "Experience above the cap is rejected on load");
        invalid = HunterCareer.CreateNew().Snapshot(); invalid.Hunters[0].SkillPoints = 1;
        check(Throws(() => new HunterCareer(invalid)), "Unearned skill points are rejected on load");
        invalid = HunterCareer.CreateNew().Snapshot(); invalid.LastExperience = 10;
        check(Throws(() => new HunterCareer(invalid)), "Experience result cannot appear on an uncompleted mission");

        // Real version-1 JSON has none of the new growth fields. Keep this fixture schema independent.
        const string legacy = "{\"Version\":1,\"Credits\":425,\"Rifles\":2,\"Pistols\":3,\"SelectedId\":\"legacy-hunter\",\"ActiveId\":\"\",\"Recruits\":4,\"Extractions\":7,\"Losses\":3,\"LastOutcome\":1,\"LastReward\":250,\"Hunters\":[{\"Id\":\"legacy-hunter\",\"Name\":\"Legacy Hunter\",\"Rifle\":true,\"Pistol\":true}]}";
        string path = Path.Combine(directory, "migrating.json"); File.WriteAllText(path, legacy);
        var store = new HunterCareerStore(path, decode, encode);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == legacy,
                "Migration write failure blocks loading and leaves the old profile untouched");
        }
        store.Load();
        check(store.Migrated && !store.RecoveredBackup && decode(File.ReadAllText(path)).Version == HunterCareer.CurrentVersion && File.ReadAllText(path + ".bak") == legacy,
            "Version-one migration atomically saves the new schema and preserves the original backup");
        check(store.Current.Credits == 425 && store.Current.Rifles == 2 && store.Current.Pistols == 3 && store.Current.Selected.Id == "legacy-hunter" &&
            store.Current.Snapshot().Extractions == 7 && store.Current.LastReward == 250 && store.Current.Selected.Level == 1 && store.Current.AccountExperience == 0,
            "Migration preserves identity, equipment, currency and prior results without inventing historical growth");
        store.Load();
        check(!store.Migrated && store.Current.Selected.SkillPoints == 0 && File.ReadAllText(path + ".bak") == legacy, "A second load does not migrate again or replace the original backup");
        store.Change(item => item.BeginRun()); store.Change(item => item.Settle(CareerOutcome.Extracted, 250, 395));
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.Selected.Experience == 395 && reload.Current.Selected.Level == 4 && reload.Current.Selected.SkillPoints == 3 && reload.Current.LastExperience == 395,
            "Experience, level and skill points survive a disk roundtrip");
        store.Change(item => item.BeginRun()); store.Change(item => item.Settle(CareerOutcome.Extracted, 0, 900));
        string retiredId = store.Current.Selected.Id;
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(Throws(() => store.Change(item => item.Retire(retiredId))) && store.Current.HunterCount == 1 && store.Current.AccountExperience == 0 && File.ReadAllText(path) == before,
                "Failed retirement save does not remove the hunter, grant account experience or return equipment");
        }
        store.Change(item => item.Retire(retiredId)); reload.Load();
        check(reload.Current.HunterCount == 0 && reload.Current.Rifles == 3 && reload.Current.Pistols == 4 && reload.Current.AccountExperience == 500 && reload.Current.Retirements == 1,
            "Successful retirement persists returned gear and account experience exactly once");
        check(!store.Change(item => item.Retire(retiredId)) && File.ReadAllText(path) == encode(store.Current.Snapshot()), "Duplicate retirement does not rewrite the save");

        string interruptedPath = Path.Combine(directory, "interrupted-v1.json");
        string activeLegacy = legacy.Replace("\"ActiveId\":\"\"", "\"ActiveId\":\"legacy-hunter\"");
        File.WriteAllText(interruptedPath, activeLegacy);
        var interrupted = new HunterCareerStore(interruptedPath, decode, encode); interrupted.Load();
        check(interrupted.Migrated && interrupted.Current.HunterCount == 0 && interrupted.Current.LastOutcome == CareerOutcome.Interrupted &&
            interrupted.Current.AccountExperience == 0 && File.ReadAllText(interruptedPath + ".bak") == activeLegacy,
            "Migration of an interrupted run applies permanent loss without awarding growth and retains the original evidence");
        string backupPath = Path.Combine(directory, "backup-v1.json");
        File.WriteAllText(backupPath, "damaged"); File.WriteAllText(backupPath + ".bak", legacy);
        var fallback = new HunterCareerStore(backupPath, decode, encode); fallback.Load();
        check(fallback.RecoveredBackup && fallback.Migrated && fallback.Current.Selected.Level == 1 && decode(File.ReadAllText(backupPath)).Version == HunterCareer.CurrentVersion && File.ReadAllText(backupPath + ".bak") == legacy,
            "A valid old-schema backup can recover and migrate a damaged primary without destroying the backup");
    }

    private static bool Throws(Action action)
    { try { action(); return false; } catch { return true; } }
}

using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterAccountCareerChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew();
        check(career.AccountLevel == 1 && career.AccountLevelExperience == 0 && career.AccountExperienceToNextLevel == 1000 && !career.IsAccountMaxLevel,
            "New profiles derive level one without adding an account-level save field");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 250, 900);
        check(career.Selected.IsMaxLevel && career.AccountLevel == 1 && career.AccountExperience == 0,
            "Hunter extraction experience and bounty currency do not directly advance the account level");
        string first = career.Selected.Id; career.Retire(first);
        check(career.AccountLevel == 1 && career.AccountLevelExperience == 500 && career.AccountExperienceToNextLevel == 500,
            "The existing first retirement reward advances account progress without an early level");
        career.Recruit(); career.Toggle(HunterEquipment.Pistol); career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 900);
        string second = career.Selected.Id; career.Retire(second);
        check(career.AccountExperience == 1000 && career.AccountLevel == 2 && career.AccountLevelExperience == 0 && career.Retirements == 2,
            "A second real retirement crosses the account level threshold exactly once");
        check(!career.Retire(first) && !career.Retire(second) && career.AccountLevel == 2 && career.AccountExperience == 1000,
            "Stale retired identities cannot repeat an account-level reward");
        career.ClaimRelief(); string relief = career.Selected.Id;
        check(career.Selected.Level == 1 && career.Selected.SkillPoints == 0 && career.AccountLevel == 2,
            "A higher account level grants no unearned growth or skill points to a relief hunter");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected == null && career.AccountLevel == 2 && career.AccountExperience == 1000,
            "Losing the entire roster preserves permanent account level and experience");

        var legacy = HunterCareer.CreateNew().Snapshot(); legacy.Version = 2; legacy.AccountExperience = 3500; legacy.Retirements = 7;
        string legacyPath = Path.Combine(directory, "legacy-v2.json"); File.WriteAllText(legacyPath, encode(legacy));
        var oldStore = new HunterCareerStore(legacyPath, decode, encode); oldStore.Load();
        check(oldStore.Migrated && oldStore.Current.AccountExperience == 3500 && oldStore.Current.AccountLevel == 4 &&
            oldStore.Current.AccountLevelExperience == 500 && oldStore.Current.Snapshot().Version == HunterCareer.CurrentVersion,
            "An old accumulated-account profile migrates using its real experience without resetting it or inventing historical levels");
        string unchanged = File.ReadAllText(legacyPath); oldStore.Load();
        check(!oldStore.Migrated && File.ReadAllText(legacyPath) == unchanged && oldStore.Current.AccountExperienceToNextLevel == 500,
            "Reading derived account levels from a current save requires no schema bump or unnecessary rewrite");

        var data = HunterCareer.CreateNew().Snapshot(); data.AccountExperience = 500; data.Retirements = 1;
        data.Hunters[0].Experience = 900; data.Hunters[0].SkillPoints = 9;
        var prepared = new HunterCareer(data); prepared.Recruit(); string reserveId = prepared.Selected.Id;
        prepared.Select(data.SelectedId); string path = Path.Combine(directory, "threshold.json"); File.WriteAllText(path, encode(prepared.Snapshot()));
        var store = new HunterCareerStore(path, decode, encode); store.Load();
        string veteran = store.Current.Selected.Id; int credits = store.Current.Credits, rifles = store.Current.Rifles;
        string disk = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(Throws(() => store.Change(item => item.Retire(veteran))) && store.Current.AccountLevel == 1 && store.Current.AccountLevelExperience == 500 &&
                store.Current.Selected.Id == veteran && store.Current.Rifles == rifles && File.ReadAllText(path) == disk,
                "A locked threshold-crossing retirement publishes neither an account level nor returned weapons nor hunter loss");
        }
        check(store.Change(item => item.Retire(veteran)) && store.Current.AccountLevel == 2 && store.Current.Credits == credits &&
            store.Current.Selected.Id == reserveId && store.Current.Selected.Level == 1 && store.Current.Selected.SkillPoints == 0,
            "Successful retry advances the shared account while leaving the reserve hunter's combat growth and money unchanged");
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.AccountLevel == 2 && reload.Current.AccountExperience == 1000 && reload.Current.AccountLevelExperience == 0 &&
            reload.Current.AccountExperienceToNextLevel == 1000 && !reload.Current.IsAccountMaxLevel && !store.Change(item => item.Retire(veteran)),
            "A fresh disk load derives the same level and progress, and repeated retirement cannot grant another level");
        reload.Change(item => item.Toggle(HunterEquipment.Pistol)); reload.Change(item => item.BeginRun());
        var interrupted = new HunterCareerStore(path, decode, encode); interrupted.Load();
        check(interrupted.Current.LastOutcome == CareerOutcome.Interrupted && interrupted.Current.HunterCount == 0 &&
            interrupted.Current.AccountExperience == 1000 && interrupted.Current.AccountLevel == 2,
            "Interrupted deployment recovery loses the active hunter but retains shared account growth");
        string backup = Path.Combine(directory, "backup.json"); File.WriteAllText(backup, "damaged"); File.WriteAllText(backup + ".bak", encode(interrupted.Current.Snapshot()));
        var recovered = new HunterCareerStore(backup, decode, encode); recovered.Load();
        check(recovered.RecoveredBackup && recovered.Current.AccountExperience == 1000 && recovered.Current.AccountLevel == 2,
            "Backup recovery derives account level from the recovered durable experience");

        data = HunterCareer.CreateNew().Snapshot(); data.Hunters[0].Experience = 900; data.Hunters[0].SkillPoints = 9; data.AccountExperience = 98500;
        var nearCap = new HunterCareer(data); nearCap.Retire(nearCap.Selected.Id);
        check(nearCap.AccountExperience == 99000 && nearCap.AccountLevel == 100 && nearCap.IsAccountMaxLevel &&
            nearCap.AccountLevelExperience == 0 && nearCap.AccountExperienceToNextLevel == 0, "A real retirement can reach the account cap without a false next-level bar");
        data.AccountExperience = 99500; var capped = new HunterCareer(data); capped.Retire(capped.Selected.Id);
        check(capped.AccountExperience == 100000 && capped.AccountLevel == 100 && capped.IsAccountMaxLevel,
            "Retiring at the level cap still preserves every accumulated experience point and the original retirement behavior");
        data.AccountExperience = 1000000000; var full = new HunterCareer(data);
        check(full.AccountLevel == 100 && !full.CanRetireSelected && !full.Retire(full.Selected.Id) && full.HunterCount == 1,
            "The account display cap never bypasses the preexisting durable-number overflow protection");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

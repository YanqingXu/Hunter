using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterCareerChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew();
        string originalId = career.Selected.Id;
        check(career.Credits == 500 && career.HunterCount == 1 && career.Rifles == 1 && career.Pistols == 1, "New career provides a roster, funds and warehouse stock");
        check(career.CanDeploy && career.Selected.Weight == 3, "Starter hunter has the two-gun loadout");
        career.Selected.Name = "changed";
        var detached = career.Snapshot(); detached.Credits = 0; detached.Hunters.Clear();
        check(career.Credits == 500 && career.HunterCount == 1 && career.Selected.Name != "changed", "Public snapshots cannot mutate live career state");
        check(!career.Select("missing") && !career.Select(originalId), "Unknown or duplicate selection is rejected");
        check(!career.Buy((HunterEquipment)99) && !career.Toggle((HunterEquipment)99), "Unknown equipment does not spend money or mutate gear");
        check(career.Toggle(HunterEquipment.Rifle) && career.Rifles == 2 && career.Selected.Weight == 1, "Unequip returns a rifle to storage");
        check(career.Toggle(HunterEquipment.Rifle) && career.Rifles == 1 && career.Selected.Weight == 3, "Equip removes a rifle from storage");
        check(career.Recruit() && career.Credits == 400 && career.HunterCount == 2 && career.Selected.Weight == 0, "Recruit charges once and selects an independently equipped hunter");
        string secondId = career.Selected.Id;
        check(!career.BeginRun(), "An unequipped hunter cannot deploy");
        check(career.Toggle(HunterEquipment.Pistol) && career.Selected.Weight == 1 && career.Pistols == 0, "Pistol-only equipment is supported");
        check(career.Select(originalId) && career.Selected.Weight == 3 && career.Select(secondId) && career.Selected.Weight == 1, "Each hunter retains its own loadout");
        check(career.Buy(HunterEquipment.Rifle) && career.Credits == 250 && career.Rifles == 2, "Shop purchase enters storage");
        check(career.BeginRun() && career.InRun, "Deployment records the active hunter");
        check(!career.BeginRun() && !career.Recruit() && !career.Select(originalId) && !career.Toggle(HunterEquipment.Pistol) && !career.Buy(HunterEquipment.Pistol) && !career.ClaimRelief(), "Active run locks all camp commands");
        check(!career.Settle(CareerOutcome.None, 0) && !career.Settle(CareerOutcome.Lost, 1) && !career.Settle(CareerOutcome.Extracted, -1), "Invalid settlement is rejected");
        check(career.Settle(CareerOutcome.Extracted, 250) && career.Credits == 500 && !career.InRun && career.Selected.Pistol, "Extraction credits bounty and preserves the deployed gear");
        check(!career.Settle(CareerOutcome.Extracted, 250) && career.Credits == 500, "Settlement cannot duplicate a reward");
        check(career.Buy(HunterEquipment.Rifle) && career.Buy(HunterEquipment.Rifle), "Extraction earnings can fund later purchases");
        HunterCareer.Validate(career.Snapshot());
        check(career.Credits == 200 && career.LastReward == 250, "Historical reward does not constrain future spending");
        int warehouseRifles = career.Rifles;
        check(career.BeginRun() && career.Settle(CareerOutcome.Lost, 0), "A later deployment can end in permanent death");
        check(career.HunterCount == 1 && career.Selected.Id == originalId && career.Rifles == warehouseRifles && career.Pistols == 0 && career.Credits == 200, "Death removes only the deployed hunter and gear, retaining other hunters, funds and storage");
        check(!career.Select(secondId) && !career.ClaimRelief(), "Dead hunter cannot return and relief requires an empty roster");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected == null && !career.CanDeploy && career.CanClaimRelief, "Wiped roster remains a valid recoverable state");
        check(career.ClaimRelief() && career.Selected.Rifle && career.Rifles == warehouseRifles - 1 && career.Credits == 200, "Relief consumes existing warehouse gear before granting anything");
        check(!career.ClaimRelief(), "Relief cannot be claimed twice");

        var poor = HunterCareer.CreateNew().Snapshot();
        poor.Credits = 0; poor.Rifles = poor.Pistols = 0; poor.Hunters.Clear(); poor.SelectedId = "";
        var rescue = new HunterCareer(poor);
        check(!rescue.Recruit() && !rescue.Buy(HunterEquipment.Pistol), "Insufficient funds block normal purchases and recruitment");
        check(rescue.ClaimRelief() && rescue.Credits == 0 && rescue.Selected.Pistol && rescue.CanDeploy, "No-funds relief prevents a progression dead end");
        poor.Credits = 100;
        rescue = new HunterCareer(poor); rescue.ClaimRelief();
        check(rescue.Credits == 25, "Relief buys its pistol when the account can afford it");
        var full = HunterCareer.CreateNew();
        for (int i = 0; i < 4; i++) full.Recruit();
        check(full.HunterCount == 5 && !full.Recruit() && full.Credits == 100, "Roster capacity prevents extra recruitment or charges");
        var bad = full.Snapshot(); bad.Credits = -1;
        check(Throws(() => new HunterCareer(bad)), "Negative funds are rejected on load");
        bad = full.Snapshot(); bad.Hunters[1].Id = bad.Hunters[0].Id;
        check(Throws(() => new HunterCareer(bad)), "Duplicate hunter identities are rejected");
        bad = full.Snapshot(); bad.SelectedId = "missing";
        check(Throws(() => new HunterCareer(bad)), "Invalid selected hunter is rejected");
        bad = full.Snapshot(); bad.ActiveId = bad.SelectedId;
        check(Throws(() => new HunterCareer(bad)), "An unarmed active deployment is rejected");

        string path = Path.Combine(directory, "career.json");
        var store = new HunterCareerStore(path, decode, encode); store.Load();
        check(File.Exists(path) && store.Current.Credits == 500, "First load creates a persisted profile");
        string id = store.Current.Selected.Id;
        check(store.Change(item => item.Buy(HunterEquipment.Pistol)) && File.Exists(path + ".bak"), "Atomic mutation keeps a backup");
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.Credits == 425 && reload.Current.Pistols == 2 && reload.Current.Selected.Id == id, "Reload preserves money, gear and hunter identity");
        string before = File.ReadAllText(path);
        check(!store.Change(item => item.Select("missing")) && File.ReadAllText(path) == before, "Rejected command does not write a new save");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(Throws(() => store.Change(item => item.Recruit())), "A locked save file causes the transaction to fail");
            check(store.Current.Credits == 425 && store.Current.HunterCount == 1, "Failed disk transaction does not publish its in-memory changes");
        }
        check(File.ReadAllText(path) == before && Directory.GetFiles(directory, "*.tmp-*").Length == 0, "Failed atomic replace retains the old file and removes temporary output");
        store.Change(item => item.BeginRun());
        reload.Load();
        check(reload.Current.HunterCount == 0 && !reload.Current.InRun && reload.Current.LastOutcome == CareerOutcome.Interrupted && reload.Current.Credits == 425, "Restart during an active run persists permanent loss");
        var again = new HunterCareerStore(path, decode, encode); again.Load();
        check(again.Current.Snapshot().Losses == 1 && again.Current.Rifles == 1 && again.Current.Pistols == 2, "Repeated load does not apply interrupted loss twice or alter storage");
        again.Change(item => item.ClaimRelief());
        again.Change(item => item.BeginRun()); again.Change(item => item.Settle(CareerOutcome.Extracted, 250));
        reload.Load();
        check(reload.Current.Credits == 675 && reload.Current.HunterCount == 1 && reload.Current.LastOutcome == CareerOutcome.Extracted, "Successful settlement survives restart without losing the hunter");
        File.WriteAllText(path, "broken json");
        reload.Load();
        check(reload.RecoveredBackup && reload.Current.HunterCount == 0 && reload.Current.LastOutcome == CareerOutcome.Interrupted, "Corrupt primary recovers the active-run backup and settles it conservatively");
        check(Directory.GetFiles(directory, "*.corrupt-*").Length == 1, "Corrupt primary is preserved for recovery");
        File.WriteAllText(path, "{}");
        reload.Load();
        check(reload.RecoveredBackup && reload.Current.LastOutcome == CareerOutcome.Interrupted, "Missing schema is corruption and can recover from backup");
        var future = HunterCareer.CreateNew().Snapshot(); future.Version = 99;
        File.WriteAllText(path, encode(future));
        check(Throws(() => reload.Load()) && File.ReadAllText(path) == encode(future), "Newer schema is never silently rolled back or overwritten");
        File.WriteAllText(path, "bad primary"); File.WriteAllText(path + ".bak", "bad backup");
        check(Throws(() => reload.Load()) && File.ReadAllText(path) == "bad primary", "Two damaged saves block play while preserving both files");
    }

    private static bool Throws(Action action)
    { try { action(); return false; } catch { return true; } }
}

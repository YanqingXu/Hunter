using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterHealthCareerChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode, Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew(); string first = career.Selected.Id;
        check(Same(career.Selected.HealthSegments, new float[] { 25, 25, 25, 25 }), "New hunters begin with the existing four 25-health segments");
        int credits = career.Credits, points = career.Selected.SkillPoints;
        var changed = new float[] { 50, 25, 25 };
        check(career.SetHealthLayout(first, changed) && career.Credits == credits && career.Selected.SkillPoints == points,
            "Camp layout changes cost neither currency nor skill points");
        changed[0] = 25; var selected = career.Selected; selected.HealthSegments[0] = 25;
        var snapshot = career.Snapshot(); snapshot.Hunters[0].HealthSegments[0] = 25;
        check(Same(career.Selected.HealthSegments, new float[] { 50, 25, 25 }), "Command input, selected records and snapshots cannot mutate saved health segments by reference");
        check(career.Recruit() && Same(career.Selected.HealthSegments, HunterCareer.DefaultHealthSegments()), "Recruitment gives the next hunter its own default layout");
        string second = career.Selected.Id;
        check(!career.SetHealthLayout(first, new float[] { 50, 50 }) && !career.SetHealthLayout("missing", changed),
            "Editing requires a real currently selected hunter");
        check(career.SetHealthLayout(second, new float[] { 25, 50, 25 }) && Same(career.GetHunter(0).HealthSegments, new float[] { 50, 25, 25 }),
            "Two hunters retain independent ordered blood-bar configurations");
        check(!career.SetHealthLayout(second, new float[] { 25, 50, 25 }) && !career.SetHealthLayout(second, new float[] { 50, 50, 50 }) &&
            !career.SetHealthLayout(second, null) && !career.SetHealthLayout(second, new float[] { 100 }),
            "Repeated, wrong-total, missing and unsupported camp layouts are rejected");
        career.Select(first); career.BeginRun();
        check(!career.SetHealthLayout(first, new float[] { 50, 50 }), "An active deployment locks health editing at the account boundary");
        career.Settle(CareerOutcome.Extracted, 0);
        check(Same(career.Selected.HealthSegments, new float[] { 50, 25, 25 }), "Extraction retains health segmentation without converting it into current health");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.Id == second && Same(career.Selected.HealthSegments, new float[] { 25, 50, 25 }), "Permanent death removes only the deployed hunter's layout");
        career.BeginRun(); // The second hunter has no weapon: no deployment is admitted.
        career.Toggle(HunterEquipment.Pistol); career.BeginRun(); career.Settle(CareerOutcome.Lost, 0); career.ClaimRelief();
        check(Same(career.Selected.HealthSegments, HunterCareer.DefaultHealthSegments()), "Relief initializes new health segments without inheriting a dead hunter's configuration");
        bool migrated = true;
        for (int version = 1; version <= 8; version++)
        {
            var legacy = HunterCareer.CreateNew().Snapshot(); legacy.Version = version; legacy.Hunters[0].HealthSegments = null;
            var upgraded = new HunterCareer(decode(encode(legacy)));
            migrated &= upgraded.Snapshot().Version == HunterCareer.CurrentVersion && Same(upgraded.Selected.HealthSegments, HunterCareer.DefaultHealthSegments()) &&
                upgraded.Selected.Id == legacy.SelectedId && upgraded.Credits == legacy.Credits && upgraded.Selected.Rifle && upgraded.Selected.Pistol;
        }
        check(migrated, "Versions one through eight initialize the existing default health layout while retaining identity and equipment");
        bool invalid = true;
        foreach (var value in new[] { null, new float[0], new float[] { 100 }, new float[] { 25, 50 }, new float[] { float.NaN, 50 } })
        {
            var bad = HunterCareer.CreateNew().Snapshot(); bad.Hunters[0].HealthSegments = value;
            invalid &= Throws(() => new HunterCareer(bad));
        }
        check(invalid, "Version nine requires valid explicit health segments instead of silently migrating corrupt data");

        string path = Path.Combine(directory, "health-layout.json");
        var old = HunterCareer.CreateNew().Snapshot(); old.Version = 8; old.Hunters[0].HealthSegments = null;
        string original = encode(old); File.WriteAllText(path, original);
        var store = new HunterCareerStore(path, decode, encode);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == original,
                "A failed v8 health migration retains the original file and publishes no account");
        store.Load(); first = store.Current.Selected.Id;
        check(store.Migrated && Same(store.Current.Selected.HealthSegments, HunterCareer.DefaultHealthSegments()) && File.ReadAllText(path + ".bak") == original,
            "Successful v8 migration keeps the original backup and writes explicit health segments in the current format");
        check(store.Change(c => c.SetHealthLayout(first, new float[] { 50, 50 })), "A real atomic store transaction persists a changed health layout");
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(!reload.Migrated && Same(reload.Current.Selected.HealthSegments, new float[] { 50, 50 }), "A fresh disk load preserves an edited layout without rerunning migration");
        string before = File.ReadAllText(path);
        check(!store.Change(c => c.SetHealthLayout(first, new float[] { 50, 50 })) && File.ReadAllText(path) == before,
            "A duplicate save leaves the exact primary file unchanged");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.SetHealthLayout(first, new float[] { 25, 50, 25 }))) &&
                Same(store.Current.Selected.HealthSegments, new float[] { 50, 50 }) && File.ReadAllText(path) == before,
                "A locked save does not publish either a new layout or a partial file");
        check(Directory.GetFiles(directory, "*.tmp-*").Length == 0 && store.Change(c => c.SetHealthLayout(first, new float[] { 25, 50, 25 })),
            "Failed writes remove their temporary output and a later retry commits normally");
        var corrupt = store.Current.Snapshot(); corrupt.Hunters[0].HealthSegments = null; File.WriteAllText(path, encode(corrupt));
        reload.Load();
        check(reload.RecoveredBackup && Same(reload.Current.Selected.HealthSegments, new float[] { 50, 50 }) &&
            Directory.GetFiles(directory, "*.corrupt-*").Length == 1, "Invalid v9 health data recovers the known-good backup and preserves the damaged primary");
        var future = store.Current.Snapshot(); future.Version = HunterCareer.CurrentVersion + 1; File.WriteAllText(path, encode(future));
        before = File.ReadAllText(path); reload = new HunterCareerStore(path, decode, encode);
        check(Throws(() => reload.Load()) && reload.Current == null && File.ReadAllText(path) == before,
            "An unknown future format cannot silently restore an older health-layout backup");
    }
    private static bool Same(float[] left, float[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

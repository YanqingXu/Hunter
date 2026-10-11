using System;
using System.IO;
using System.Collections.Generic;
using BigWorld.Gameplay;

public static class HunterTemplateCareerChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode, Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var career = HunterCareer.CreateNew(); string first = career.Selected.Id;
        check(career.Selected.TemplateId == HunterTemplateId.Recruited && career.Selected.Weight == 3 && career.CanDeploy,
            "The starter retains both guns as a recruited template");
        int money = career.Credits;
        check(career.Recruit() && career.Credits == money - 100 && career.Selected.TemplateId == HunterTemplateId.Recruited && career.Selected.Weight == 0,
            "Paid recruitment creates the purchased template without free equipment");
        check(!career.ClaimRelief() && career.HunterCount == 2, "A surviving roster cannot claim the relief template");
        career.Select(first); career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.TemplateId == HunterTemplateId.Recruited, "Permanent death preserves the surviving hunter's template");
        career.Toggle(HunterEquipment.Pistol); career.BeginRun(); career.Settle(CareerOutcome.Lost, 0); money = career.Credits;
        check(career.ClaimRelief() && career.Selected.TemplateId == HunterTemplateId.Relief && career.Selected.Rifle && career.Credits == money,
            "An empty roster claims relief using the remaining warehouse rifle without a recruitment fee");
        int rifles = career.Rifles; career.Buy(HunterEquipment.Pistol); int pistols = career.Pistols;
        check(!career.CanToggle(HunterEquipment.Pistol) && !career.Toggle(HunterEquipment.Pistol) && career.Rifles == rifles && career.Pistols == pistols && career.Selected.Weight == 2,
            "Relief cannot add a pistol to a rifle and rejected gear changes leave the warehouse intact");
        check(career.Toggle(HunterEquipment.Rifle) && career.Toggle(HunterEquipment.Pistol) && career.CanDeploy && career.Selected.Weight == 1,
            "Relief can exchange a rifle for a pistol within its own weight capacity");
        check(!career.Toggle(HunterEquipment.Rifle), "The inverse pistol-to-dual-gun overflow is also rejected");
        var poor = Relief(); var poorData = poor.Snapshot(); poorData.Credits = 0; poorData.Rifles = poorData.Pistols = 0;
        poor = new HunterCareer(poorData); poor.BeginRun(); poor.Settle(CareerOutcome.Lost, 0);
        check(poor.ClaimRelief() && poor.Selected.Pistol && poor.Credits == 0 && poor.Selected.TemplateId == HunterTemplateId.Relief,
            "A bankrupt empty account still receives the existing fallback pistol and can deploy");

        career = FullRelief(); string id = career.Selected.Id; int points = career.Selected.SkillPoints;
        check(career.Selected.CarriedSkillValue == 10 && career.Selected.FieldSkillValue == 100 && points == 20,
            "Carried skill value sums currently held skills independently of points and acquisition history");
        string before = encode(career.Snapshot());
        check(!career.CanBuySkill(id, HunterSkillId.EfficientMelee) && !career.BuySkill(id, HunterSkillId.EfficientMelee) && encode(career.Snapshot()) == before,
            "A full relief template rejects camp purchase without spending points or changing any state");
        check(career.RemoveSkill(id, HunterSkillId.Pitcher) && career.Selected.CarriedSkillValue == 8 && career.Selected.SkillPoints == points + 1 &&
            career.BuySkill(id, HunterSkillId.Pitcher) && career.Selected.CarriedSkillValue == 10,
            "Removing a skill releases its full carrying cost while preserving the existing half-refund rule");
        career.BeginRun(); before = encode(career.Snapshot());
        check(!career.AcquireFieldSkill(id, HunterSkillId.SteadyAim) && encode(career.Snapshot()) == before,
            "A field skill that exceeds capacity does not alter history or the deployed hunter");
        check(career.ConsumeOnceSkill(id, HunterSkillId.EmergencyBandage) && career.Selected.CarriedSkillValue == 8 &&
            career.AcquireFieldSkill(id, HunterSkillId.EmergencyBandage) && career.Selected.CarriedSkillValue == 10,
            "Using a once skill releases capacity immediately and allows a new copy at the exact limit");
        check(career.ConsumeRevival(id) && career.Selected.CarriedSkillValue == 6 && career.AcquireFieldSkill(id, HunterSkillId.SteadyAim) &&
            career.Selected.CarriedSkillValue == 9, "A consumed death skill frees its carrying value for later field skills");
        career.Settle(CareerOutcome.Extracted, 0);
        check(career.Selected.TemplateId == HunterTemplateId.Relief && career.Selected.CarriedSkillValue == 9,
            "Extraction preserves the relief template and its remaining skills");
        var copy = career.Selected; copy.TemplateId = HunterTemplateId.Recruited; copy.Skills.Clear();
        check(career.Selected.TemplateId == HunterTemplateId.Relief && career.Selected.CarriedSkillValue == 9,
            "Returned records cannot mutate the persisted template or carried skill value");
        bool migrated = true;
        for (int version = 1; version <= 9; version++)
        {
            var legacy = HunterCareer.CreateNew().Snapshot(); legacy.Version = version; legacy.Hunters[0].TemplateId = 0;
            var loaded = new HunterCareer(decode(encode(legacy)));
            migrated &= loaded.Selected.TemplateId == HunterTemplateId.Recruited && loaded.Selected.Weight == 3 &&
                loaded.Selected.Id == legacy.SelectedId && loaded.Credits == legacy.Credits && loaded.Snapshot().Version == HunterCareer.CurrentVersion;
        }
        check(migrated, "All prior save versions migrate to recruited capacity without guessing acquisition history or removing equipment");
        var all = HunterCareer.CreateNew().Snapshot(); all.Version = 9; all.Hunters[0].TemplateId = 0; all.Hunters[0].FieldSkillValue = 14;
        foreach (var skill in HunterSkills.Catalogue) all.Hunters[0].Skills.Add(skill.Id);
        check(new HunterCareer(all).Selected.CarriedSkillValue == 14, "Version-nine migration retains every previously legal skill together");
        bool invalid = true;
        foreach (var template in new[] { (HunterTemplateId)0, (HunterTemplateId)999, HunterTemplateId.Relief })
        { var bad = HunterCareer.CreateNew().Snapshot(); bad.Hunters[0].TemplateId = template; invalid &= Throws(() => new HunterCareer(bad)); }
        check(invalid, "Current saves reject missing or unknown templates and a relief hunter with both guns");
        var over = FullRelief().Snapshot(); over.Hunters[0].Skills.Add(HunterSkillId.EfficientMelee);
        check(Throws(() => new HunterCareer(over)), "Current saves reject excess carried skills even when historical skill value can fund them");

        string path = Path.Combine(directory, "templates.json"); all.Hunters[0].HealthSegments = new float[] { 50, 50 };
        string legacyText = encode(all); File.WriteAllText(path, legacyText);
        var store = new HunterCareerStore(path, decode, encode);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == legacyText,
                "Failed version-nine migration preserves its exact source file and publishes no account");
        store.Load();
        check(store.Migrated && store.Current.Selected.TemplateId == HunterTemplateId.Recruited && store.Current.Selected.CarriedSkillValue == 14 &&
            store.Current.Selected.HealthSegments.Length == 2 && File.ReadAllText(path + ".bak") == legacyText,
            "Atomic migration preserves skills and custom health segments and backs up the original v9 file");
        before = File.ReadAllText(path); money = store.Current.Credits;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.Recruit())) && store.Current.Credits == money && store.Current.HunterCount == 1 && File.ReadAllText(path) == before,
                "A failed recruitment write charges nothing and publishes no new hunter");
        check(store.Change(c => c.Recruit()), "Recruitment can be retried after the save becomes writable");
        var reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(!reload.Migrated && reload.Current.Selected.TemplateId == HunterTemplateId.Recruited && reload.Current.HunterCount == 2,
            "The new persisted template survives a fresh disk load without migration");
        File.WriteAllText(path, encode(FullRelief().Snapshot())); reload = new HunterCareerStore(path, decode, encode); reload.Load();
        check(reload.Current.Selected.TemplateId == HunterTemplateId.Relief && reload.Current.Selected.CarriedSkillValue == 10,
            "A relief template at exact skill capacity round-trips through the production store");
        reload.Change(c => c.RemoveSkill(c.Selected.Id, HunterSkillId.Pitcher)); var corrupt = reload.Current.Snapshot(); corrupt.Hunters[0].TemplateId = 0;
        File.WriteAllText(path, encode(corrupt)); reload.Load();
        check(reload.RecoveredBackup && reload.Current.Selected.TemplateId == HunterTemplateId.Relief && reload.Current.Selected.CarriedSkillValue == 10,
            "An invalid current template recovers the intact relief backup instead of silently upgrading capacity");
    }
    private static HunterCareer Relief()
    { var c = HunterCareer.CreateNew(); c.BeginRun(); c.Settle(CareerOutcome.Lost, 0); c.ClaimRelief(); return c; }
    private static HunterCareer FullRelief()
    {
        var data = Relief().Snapshot(); var hunter = data.Hunters[0]; hunter.FieldSkillValue = 100; hunter.SkillPoints = 20;
        hunter.Skills = new List<HunterSkillId> { HunterSkillId.QuickReload, HunterSkillId.Necromancer, HunterSkillId.Pitcher, HunterSkillId.EmergencyBandage };
        return new HunterCareer(data);
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

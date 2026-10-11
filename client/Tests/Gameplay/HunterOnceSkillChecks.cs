using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterOnceSkillChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var skill = HunterSkillId.EmergencyBandage; var definition = HunterSkills.Find(skill);
        check(definition != null && definition.Lifetime == HunterSkillLifetime.Once && definition.Cost == 2 && definition.Refund == 1 &&
            definition.Category == HunterSkillCategory.Character && definition.EffectKind == HunterSkillEffectKind.Mechanic &&
            HunterSkills.EmergencyBandageHealing == 50, "Emergency bandage is a two-point single-use character mechanism with fifty healing");
        var effects = new HunterSkillEffects(new[] { skill, HunterSkillId.Pitcher, HunterSkillId.QuickReload, HunterSkillId.SteadyAim });
        check(effects.ThrowRangeMultiplier == 1.5f && effects.ReloadMultiplier == .75f && effects.FireIntervalMultiplier == .8f &&
            effects.MeleeStaminaMultiplier == 1, "Carrying a once skill adds no passive modifiers or changes to other skills");
        var career = HunterCareer.CreateNew(); string hunter = career.Selected.Id;
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 900);
        career.BuyItem(HunterItemId.ThrowingKnife); career.EquipItem(hunter, HunterItemId.ThrowingKnife);
        check(career.BuySkill(hunter, skill) && career.Selected.SkillPoints == 7 && !career.BuySkill(hunter, skill),
            "Buying a once skill charges two points and forbids duplicate ownership");
        check(!career.CanConsumeOnceSkill(hunter, skill) && !career.ConsumeOnceSkill(hunter, skill) && career.Selected.HasSkill(skill),
            "Camp ownership does not authorize consuming a once skill");
        check(career.RemoveSkill(hunter, skill) && career.Selected.SkillPoints == 8 && !career.Selected.HasSkill(skill),
            "Explicit camp removal refunds one point without counting as use");
        career.BuySkill(hunter, skill); career.BuySkill(hunter, HunterSkillId.Pitcher); career.BuySkill(hunter, HunterSkillId.Necromancer);
        career.Recruit(); string other = career.Selected.Id; career.Select(hunter); career.BeginRun();
        check(!career.ConsumeOnceSkill(other, skill) && !career.ConsumeOnceSkill(null, skill) &&
            !career.ConsumeOnceSkill(hunter, (HunterSkillId)999), "Once-skill consumption rejects reserve, missing and unknown identities");
        check(!career.ConsumeOnceSkill(hunter, HunterSkillId.Pitcher) && !career.ConsumeOnceSkill(hunter, HunterSkillId.Necromancer),
            "The active command cannot consume regular or death skills");
        career.ConsumeItem(hunter, 0, HunterItemId.ThrowingKnife);
        check(career.ConsumeRevival(hunter) && career.Selected.HasSkill(skill) && career.Selected.HasSkill(HunterSkillId.Pitcher),
            "Death-skill revival retains an unused once skill and regular item skills");
        int points = career.Selected.SkillPoints, credits = career.Credits; var oldRecord = career.Selected;
        check(career.CanConsumeOnceSkill(hunter, skill) && career.ConsumeOnceSkill(hunter, skill) && !career.Selected.HasSkill(skill),
            "The deployed hunter consumes exactly its owned once skill");
        check(career.InRun && career.Credits == credits && career.Selected.SkillPoints == points && career.Selected.Experience == 900 &&
            career.Selected.Items[0].Uses == 2 && career.Selected.HasSkill(HunterSkillId.Pitcher) && oldRecord.HasSkill(skill),
            "Consumption preserves points, credits, growth, partial tools, other skills and previous record snapshots");
        check(!career.CanConsumeOnceSkill(hunter, skill) && !career.ConsumeOnceSkill(hunter, skill),
            "Repeated activation cannot consume the same copy twice");
        check(career.AcquireFieldSkill(hunter, skill) && career.Selected.FieldSkillValue == 2 && career.Selected.SkillPoints == points,
            "A consumed once skill can be acquired again as a new field copy without spending points");
        check(career.ConsumeOnceSkill(hunter, skill) && career.Selected.FieldSkillValue == 2 && career.Selected.SkillPoints == points,
            "Consuming a field copy retains historical acquisition value and grants no refund");
        check(career.AcquireFieldSkill(hunter, skill) && career.Selected.FieldSkillValue == 4 && !career.AcquireFieldSkill(hunter, skill),
            "Each genuinely new copy records value once and owned copies still reject duplicate pickup");
        career.Settle(CareerOutcome.Extracted, 0);
        var reloaded = new HunterCareer(decode(encode(career.Snapshot())));
        check(reloaded.Selected.HasSkill(skill) && reloaded.Selected.FieldSkillValue == 4 && reloaded.Selected.Items[0].Uses == 2,
            "Unused once skills survive extraction and serialization with partial tools");
        reloaded.BeginRun(); reloaded.ConsumeOnceSkill(hunter, skill); reloaded.Settle(CareerOutcome.Extracted, 0);
        check(!new HunterCareer(decode(encode(reloaded.Snapshot()))).Selected.HasSkill(skill),
            "Used once skills do not reappear after extraction and serialization");
        career.BeginRun(); career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.Id == other && !career.Selected.HasSkill(skill) && !career.ConsumeOnceSkill(hunter, skill),
            "Permanent loss removes unused once skills without transferring them to another hunter");

        var legacyData = HunterCareer.CreateNew().Snapshot(); legacyData.Version = 7;
        legacyData.Hunters[0].Experience = 400; legacyData.Hunters[0].SkillPoints = 2;
        legacyData.Hunters[0].Skills.Add(HunterSkillId.Pitcher);
        legacyData.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.ThrowingKnife, Uses = 1 });
        legacyData.Items.Add(new HunterItemStack { Id = HunterItemId.FirstAid, Uses = 0 });
        string path = Path.Combine(directory, "v7-once-skill.json"), legacy = encode(legacyData); File.WriteAllText(path, legacy);
        var store = new HunterCareerStore(path, decode, encode); hunter = legacyData.SelectedId;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == legacy,
                "Failed v7 migration preserves the original file without publishing an account");
        store.Load(); legacyData.Version = HunterCareer.CurrentVersion;
        check(store.Migrated && encode(store.Current.Snapshot()) == encode(legacyData) && File.ReadAllText(path + ".bak") == legacy,
            "Version seven migrates by changing only the format version and keeps its original backup");
        store.Change(c => c.BuySkill(hunter, skill)); store.Load();
        check(!store.Migrated && store.Current.Selected.HasSkill(skill) && store.Current.Selected.SkillPoints == 0,
            "The new once-skill identifier round-trips through a real camp save");
        store.Change(c => c.BeginRun()); string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.ConsumeOnceSkill(hunter, skill))) &&
                store.Current.Selected.HasSkill(skill) && File.ReadAllText(path) == before,
                "A failed activation write retains the exact file and owned skill");
        check(store.Change(c => c.ConsumeOnceSkill(hunter, skill)) && !store.Change(c => c.ConsumeOnceSkill(hunter, skill)),
            "Retrying after releasing a disk lock consumes exactly once");
        var persisted = new HunterCareer(decode(File.ReadAllText(path)));
        check(persisted.InRun && !persisted.Selected.HasSkill(skill) && persisted.Selected.HasSkill(HunterSkillId.Pitcher) &&
            persisted.Selected.Items[0].Uses == 1 && persisted.Selected.SkillPoints == 0,
            "Successful consumption is durable without settling the mission or changing passives and inventory");
        store.Change(c => c.AcquireFieldSkill(hunter, skill)); store.Change(c => c.ConsumeOnceSkill(hunter, skill));
        store.Change(c => c.Settle(CareerOutcome.Extracted, 0)); store.Load();
        check(!store.Current.Selected.HasSkill(skill) && store.Current.Selected.FieldSkillValue == 2 && store.Current.Selected.SkillPoints == 0,
            "Field reacquisition and reuse stay consumed after successful extraction and a fresh disk load");
        store.Change(c => c.BeginRun()); store.Change(c => c.AcquireFieldSkill(hunter, skill)); store.Load();
        check(store.Current.HunterCount == 0 && store.Current.LastOutcome == CareerOutcome.Interrupted,
            "Restarting an unfinished action loses the hunter and its unused once skill instead of reviving it");
        var future = HunterCareer.CreateNew().Snapshot(); future.Version = HunterCareer.CurrentVersion + 1;
        File.WriteAllText(path, encode(future)); before = File.ReadAllText(path); store = new HunterCareerStore(path, decode, encode);
        check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == before,
            "A future skill format remains blocked instead of restoring an older backup");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

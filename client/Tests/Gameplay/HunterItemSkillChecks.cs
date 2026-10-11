using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterItemSkillChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var definition = HunterSkills.Find(HunterSkillId.Pitcher);
        check(definition != null && definition.Cost == 2 && definition.Refund == 1 && definition.Lifetime == HunterSkillLifetime.Regular &&
            definition.Category == HunterSkillCategory.Item && definition.EffectKind == HunterSkillEffectKind.Numeric,
            "Pitcher is a two-point regular numeric item skill with a one-point camp refund");
        check(HunterSkills.Find(HunterSkillId.Necromancer).Category == HunterSkillCategory.Character &&
            HunterSkills.Find(HunterSkillId.Necromancer).EffectKind == HunterSkillEffectKind.Mechanic &&
            HunterSkills.Find(HunterSkillId.QuickReload).Category == HunterSkillCategory.Gun &&
            HunterSkills.Find(HunterSkillId.EfficientMelee).Category == HunterSkillCategory.Character,
            "Existing skills retain their character, gun and mechanism classifications");
        var plain = new HunterSkillEffects(new HunterSkillId[0]);
        var skilled = new HunterSkillEffects(new[] { HunterSkillId.Pitcher });
        var knife = HunterItems.Find(HunterItemId.ThrowingKnife); var dynamite = HunterItems.Find(HunterItemId.Dynamite);
        var thrown = skilled.Apply(knife); var explosive = skilled.Apply(dynamite);
        check(plain.ThrowRangeMultiplier == 1 && skilled.ThrowRangeMultiplier == 1.5f && thrown.Range == 12 && explosive.Range == 9,
            "Pitcher extends both regular throwing tools and consumable explosives by exactly fifty percent");
        check(knife.Range == 8 && dynamite.Range == 6 && !ReferenceEquals(knife, thrown) && !ReferenceEquals(dynamite, explosive),
            "Applying an item skill creates independent definitions without mutating the global catalogue");
        check(thrown.Value == knife.Value && thrown.Delay == knife.Delay && thrown.Capacity == knife.Capacity &&
            thrown.RefillAmount == knife.RefillAmount && thrown.StaminaCost == knife.StaminaCost &&
            explosive.Radius == dynamite.Radius && explosive.Value == dynamite.Value && explosive.Delay == dynamite.Delay,
            "Throw distance modifiers preserve damage, preparation, charges, refill size, stamina and explosion radius");
        foreach (var id in new[] { HunterItemId.Knife, HunterItemId.FirstAid, HunterItemId.HealthShot })
        {
            var source = HunterItems.Find(id); var modified = skilled.Apply(source);
            check(modified.Range == source.Range && modified.Value == source.Value && modified.Delay == source.Delay &&
                modified.StaminaCost == source.StaminaCost, "Pitcher does not change non-throwable item " + id);
        }
        var gun = new HunterGunDefinition(); var unmodified = skilled.Apply(gun);
        check(unmodified.FireInterval == gun.FireInterval && unmodified.ReloadSeconds == gun.ReloadSeconds &&
            skilled.MeleeStaminaMultiplier == 1, "An item-only skill does not modify firearm timing or melee stamina");
        var combined = new HunterSkillEffects(new[] { HunterSkillId.Pitcher, HunterSkillId.SteadyAim, HunterSkillId.QuickReload });
        check(combined.Apply(knife).Range == 12 && Math.Abs(combined.FireIntervalMultiplier - .8f) < .0001f &&
            Math.Abs(combined.ReloadMultiplier - .75f) < .0001f && skilled.Apply(knife).Range == 12,
            "Rebuilding combined skill effects from base definitions does not multiply an existing throw bonus twice");
        check(plain.Apply(knife).Range == 8 && thrown.Range == 12 && explosive.Range == 9,
            "Removing a skill restores new throws to base values while old immutable throw snapshots retain their range");
        check(Throws(() => new HunterSkillEffects(new[] { HunterSkillId.Pitcher, HunterSkillId.Pitcher })) &&
            Throws(() => skilled.Apply((HunterItemDefinition)null)), "Duplicate item skills and missing item definitions are rejected");

        var career = HunterCareer.CreateNew(); string idHunter = career.Selected.Id;
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 900);
        career.BuyItem(HunterItemId.ThrowingKnife); career.EquipItem(idHunter, HunterItemId.ThrowingKnife);
        check(career.BuySkill(idHunter, HunterSkillId.Pitcher) && career.Selected.SkillPoints == 7 &&
            !career.BuySkill(idHunter, HunterSkillId.Pitcher), "Buying Pitcher deducts its own price exactly once");
        career.BuySkill(idHunter, HunterSkillId.Necromancer); career.BeginRun(); career.ConsumeItem(idHunter, 0, HunterItemId.ThrowingKnife);
        check(career.ConsumeRevival(idHunter) && career.Selected.HasSkill(HunterSkillId.Pitcher) && career.Selected.Items[0].Uses == 2 &&
            new HunterSkillEffects(career.Selected.Skills).ThrowRangeMultiplier == 1.5f,
            "Non-permanent revival preserves the regular item skill and already-spent tool charges");
        career.Settle(CareerOutcome.Extracted, 0);
        check(career.Selected.HasSkill(HunterSkillId.Pitcher) && career.Selected.Items[0].Uses == 2,
            "Extraction retains Pitcher without restoring consumed throwing-tool charges");
        check(career.RemoveSkill(idHunter, HunterSkillId.Pitcher) && career.Selected.SkillPoints == 4 &&
            career.Selected.Items[0].Uses == 2, "Camp removal refunds one point without refilling throwing tools");
        career.Recruit(); string other = career.Selected.Id; career.Select(idHunter); career.BeginRun();
        check(career.AcquireFieldSkill(idHunter, HunterSkillId.Pitcher) && career.Selected.FieldSkillValue == 2 &&
            career.Selected.SkillPoints == 4 && !career.AcquireFieldSkill(idHunter, HunterSkillId.Pitcher),
            "Field-acquired Pitcher records its value without charging points or permitting duplicates");
        check(!career.AcquireFieldSkill(other, HunterSkillId.Pitcher) && career.GetHunter(1).Skills.Count == 0,
            "A field item skill cannot be granted to a reserve hunter");
        career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.Id == other && career.Selected.Skills.Count == 0,
            "Permanent loss deletes the item skill without transferring it to the reserve");

        // The preceding format already includes partial tools and field skills. Preserve every field.
        var legacyData = HunterCareer.CreateNew().Snapshot(); legacyData.Version = 6;
        legacyData.Credits = 333; legacyData.Hunters[0].Experience = 400; legacyData.Hunters[0].SkillPoints = 3;
        legacyData.Hunters[0].Skills.Add(HunterSkillId.EfficientMelee);
        legacyData.Hunters[0].Items.Add(new HunterItemStack { Id = HunterItemId.ThrowingKnife, Uses = 1 });
        legacyData.Items.Add(new HunterItemStack { Id = HunterItemId.FirstAid, Uses = 0 });
        string path = Path.Combine(directory, "v6-item-skill.json"), legacy = encode(legacyData); File.WriteAllText(path, legacy);
        var store = new HunterCareerStore(path, decode, encode); idHunter = legacyData.SelectedId;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Load()) && store.Current == null && File.ReadAllText(path) == legacy,
                "Failed v6 migration preserves the original inventory and publishes no account");
        store.Load();
        check(store.Migrated && store.Current.Snapshot().Version == HunterCareer.CurrentVersion && File.ReadAllText(path + ".bak") == legacy &&
            store.Current.Credits == 333 && store.Current.Selected.Experience == 400 && store.Current.Selected.SkillPoints == 3 &&
            store.Current.Selected.HasSkill(HunterSkillId.EfficientMelee) && store.Current.Selected.Items[0].Uses == 1 &&
            store.Current.WarehouseItems[0].Uses == 0, "Version six migrates without replacing partial or empty tools, skills, points or growth");
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.BuySkill(idHunter, HunterSkillId.Pitcher))) &&
                !store.Current.Selected.HasSkill(HunterSkillId.Pitcher) && store.Current.Selected.SkillPoints == 3 && File.ReadAllText(path) == before,
                "Failed Pitcher purchases preserve points and persisted skill ownership");
        store.Change(c => c.BuySkill(idHunter, HunterSkillId.Pitcher)); store.Load();
        check(!store.Migrated && store.Current.Selected.HasSkill(HunterSkillId.Pitcher) && store.Current.Selected.SkillPoints == 1 &&
            store.Current.Selected.Items[0].Uses == 1, "Retrying the purchase persists the new skill ID with original item counts");
        before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.RemoveSkill(idHunter, HunterSkillId.Pitcher))) &&
                store.Current.Selected.HasSkill(HunterSkillId.Pitcher) && store.Current.Selected.SkillPoints == 1 && File.ReadAllText(path) == before,
                "Failed item-skill removal cannot refund points or disable the persisted skill");
        store.Change(c => c.RemoveSkill(idHunter, HunterSkillId.Pitcher)); store.Change(c => c.BeginRun()); before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(c => c.AcquireFieldSkill(idHunter, HunterSkillId.Pitcher))) &&
                !store.Current.Selected.HasSkill(HunterSkillId.Pitcher) && store.Current.Selected.FieldSkillValue == 0 && File.ReadAllText(path) == before,
                "Failed field item-skill writes retain the unskilled account and acquisition value");
        store.Change(c => c.AcquireFieldSkill(idHunter, HunterSkillId.Pitcher));
        var saved = new HunterCareer(decode(File.ReadAllText(path)));
        check(saved.InRun && saved.Selected.HasSkill(HunterSkillId.Pitcher) && saved.Selected.FieldSkillValue == 2 && saved.Selected.Items[0].Uses == 1,
            "Field item-skill acquisition is durable without clearing the unfinished-run marker");
        store.Change(c => c.Settle(CareerOutcome.Extracted, 0)); store.Load();
        check(store.Current.Selected.HasSkill(HunterSkillId.Pitcher) && store.Current.Selected.Items[0].Uses == 1,
            "A fresh disk load after extraction retains the field item skill and its partially used tool");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

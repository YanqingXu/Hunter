using System;
using System.IO;
using BigWorld.Gameplay;

public static class HunterRevivalChecks
{
    public static void Run(Action<bool, string> check, Func<string, HunterCareerData> decode,
        Func<HunterCareerData, string> encode, string directory)
    {
        Directory.CreateDirectory(directory);
        var skill = HunterSkills.Find(HunterSkillId.Necromancer);
        check(skill != null && skill.Cost == 4 && skill.Refund == 2 && skill.Lifetime == HunterSkillLifetime.Death,
            "Necromancer is a four-point death skill with the normal two-point camp refund");
        var career = HunterCareer.CreateNew(); string id = career.Selected.Id;
        check(!career.CanRevive(id) && !career.ConsumeRevival(id), "A camp hunter without necromancer cannot consume a revival");
        career.BeginRun(); career.Settle(CareerOutcome.Extracted, 0, 600);
        career.BuySkill(id, HunterSkillId.Necromancer); career.BuySkill(id, HunterSkillId.EfficientMelee);
        check(career.Selected.SkillPoints == 1 && career.Selected.Skills.Count == 2 && !career.CanRevive(id),
            "Purchased revival skill waits for deployment without changing base stats or activating in camp");
        career.Recruit(); string reserve = career.Selected.Id; career.Select(id); career.BeginRun();
        check(career.CanRevive(id) && !career.CanRevive(reserve) && !career.ConsumeRevival(reserve),
            "Only the active hunter can consume its own revival skill");
        int money = career.Credits, losses = career.Snapshot().Losses, extractions = career.Snapshot().Extractions;
        check(career.ConsumeRevival(id) && !career.Selected.HasSkill(HunterSkillId.Necromancer) &&
            career.Selected.HasSkill(HunterSkillId.EfficientMelee) && career.Selected.SkillPoints == 1 && career.Selected.Experience == 600,
            "Revival consumes the death skill without refunding points or removing regular skills and growth");
        check(career.InRun && career.Selected.Id == id && career.Credits == money && career.Snapshot().Losses == losses &&
            career.Snapshot().Extractions == extractions && career.Selected.Rifle && career.Selected.Pistol,
            "Revival retains the active run, hunter identity and equipment without a settlement reward or permanent loss");
        check(!career.CanRevive(id) && !career.ConsumeRevival(id) && career.Selected.SkillPoints == 1,
            "Repeated consumption cannot grant another revival or a refund");
        check(new HunterCareer(career.Snapshot()).Selected.Skills.Count == 1, "A consumed revival remains a valid spent-point save");
        career.Settle(CareerOutcome.Extracted, 250, 100);
        check(career.Selected.Level == 8 && career.Selected.SkillPoints == 2 && !career.Selected.HasSkill(HunterSkillId.Necromancer),
            "Extraction after revival grants ordinary growth without restoring the consumed skill");
        career.BeginRun(); career.AcquireFieldSkill(id, HunterSkillId.Necromancer);
        check(career.CanRevive(id) && career.Selected.FieldSkillValue == 4 && career.ConsumeRevival(id) && career.Selected.FieldSkillValue == 4 &&
            career.Selected.SkillPoints == 2, "A newly acquired copy can revive once and consumption retains its historical value without refunds");
        career.Settle(CareerOutcome.Lost, 0);
        check(career.Selected.Id == reserve && career.Selected.SkillPoints == 0 && career.Selected.Skills.Count == 0,
            "Subsequent permanent death removes the revived hunter and does not transfer skills to the reserve");

        var refund = HunterCareer.CreateNew(); string refundId = refund.Selected.Id;
        refund.BeginRun(); refund.AcquireFieldSkill(refundId, HunterSkillId.Necromancer); refund.Settle(CareerOutcome.Extracted, 0);
        check(refund.RemoveSkill(refundId, HunterSkillId.Necromancer) && refund.Selected.SkillPoints == 2 && refund.Selected.FieldSkillValue == 4,
            "Manual camp removal of an unused field revival skill still grants its normal refund");
        var abandon = HunterCareer.CreateNew(); string abandoned = abandon.Selected.Id;
        abandon.BeginRun(); abandon.AcquireFieldSkill(abandoned, HunterSkillId.Necromancer); abandon.Settle(CareerOutcome.Lost, 0);
        check(abandon.HunterCount == 0 && abandon.LastOutcome == CareerOutcome.Lost, "Abandonment bypasses revival and permanently loses a hunter carrying necromancer");

        string path = Path.Combine(directory, "revival.json");
        var store = new HunterCareerStore(path, decode, encode); store.Load(); string storedId = store.Current.Selected.Id;
        store.Change(item => item.BeginRun()); store.Change(item => item.AcquireFieldSkill(storedId, HunterSkillId.Necromancer));
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => store.Change(item => item.ConsumeRevival(storedId))) && store.Current.CanRevive(storedId) && File.ReadAllText(path) == before,
                "Failed revival persistence keeps the skill and original active-run record intact");
        check(store.Change(item => item.ConsumeRevival(storedId)) && !store.Current.CanRevive(storedId),
            "Retrying the consumption transaction removes the revival skill exactly once");
        var saved = new HunterCareer(decode(File.ReadAllText(path)));
        check(saved.InRun && saved.Selected.Id == storedId && saved.Selected.Skills.Count == 0 && saved.Selected.FieldSkillValue == 4,
            "The consumed skill is durable before revival while the interrupted-run marker remains set");
        before = File.ReadAllText(path);
        check(!store.Change(item => item.ConsumeRevival(storedId)) && File.ReadAllText(path) == before,
            "Duplicate revival commands cannot rewrite the save or consume another hunter's skill");
        store.Load(); check(store.Current.HunterCount == 0 && store.Current.LastOutcome == CareerOutcome.Interrupted,
            "Restarting after a committed revival does not rescue the unfinished expedition");
        string interruptedPath = Path.Combine(directory, "unconsumed.json");
        var interrupted = new HunterCareerStore(interruptedPath, decode, encode); interrupted.Load(); string interruptedId = interrupted.Current.Selected.Id;
        interrupted.Change(item => item.BeginRun()); interrupted.Change(item => item.AcquireFieldSkill(interruptedId, HunterSkillId.Necromancer)); interrupted.Load();
        check(interrupted.Current.HunterCount == 0 && interrupted.Current.LastOutcome == CareerOutcome.Interrupted,
            "An unconsumed revival skill cannot bypass permanent loss when loading an interrupted expedition");

        const string legacy = "{\"Version\":4,\"Credits\":425,\"Rifles\":2,\"Pistols\":3,\"SelectedId\":\"veteran\",\"ActiveId\":\"\",\"Recruits\":1,\"Extractions\":2,\"Losses\":0,\"LastOutcome\":1,\"LastReward\":250,\"LastExperience\":275,\"LastLevelsGained\":3,\"AccountExperience\":500,\"Retirements\":1,\"Hunters\":[{\"Id\":\"veteran\",\"Name\":\"Veteran\",\"Rifle\":true,\"Pistol\":true,\"Experience\":550,\"SkillPoints\":4,\"FieldSkillValue\":3,\"Skills\":[1,3]}]}";
        string migrationPath = Path.Combine(directory, "v4.json"); File.WriteAllText(migrationPath, legacy);
        var migration = new HunterCareerStore(migrationPath, decode, encode);
        using (var locked = new FileStream(migrationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(Throws(() => migration.Load()) && migration.Current == null && File.ReadAllText(migrationPath) == legacy,
                "Version-four migration remains atomic if its schema update cannot be saved");
        migration.Load();
        check(migration.Migrated && migration.Current.Snapshot().Version == HunterCareer.CurrentVersion && File.ReadAllText(migrationPath + ".bak") == legacy &&
            migration.Current.Selected.Skills.Count == 2 && migration.Current.Selected.FieldSkillValue == 3 && migration.Current.Selected.SkillPoints == 4 &&
            migration.Current.Selected.Experience == 550 && migration.Current.AccountExperience == 500,
            "Version-four migration preserves field value, purchased skills, points and existing growth");
        migration.Change(item => item.BuySkill("veteran", HunterSkillId.Necromancer)); migration.Load();
        check(migration.Current.Selected.HasSkill(HunterSkillId.Necromancer) && migration.Current.Selected.SkillPoints == 0,
            "An existing profile can buy the new revival skill and reload its stable identity");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

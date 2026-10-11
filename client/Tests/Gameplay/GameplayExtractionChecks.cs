using System.Collections;
using System.IO;
using System.Linq;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator ExtractionCamp()
    {
        ClickButton("返回营地");
        yield return Until(() => level.Phase == LevelPhase.Ready, 10, "Return to camp after a physical no-bounty departure");
        yield return null;
    }

    private IEnumerator ExtractionChecks()
    {
        var hunt = level.Hunt; var loot = level.SkillLoot;
        bool itemLoot = level.ItemLoot.enabled, skillLoot = loot.enabled, regionClues = hunt.UseRegionClues;
        var positions = loot.Positions; float chance = loot.SpawnChance, recovery = player.HealthRecoveryRate;
        bool random = loot.RandomizeSkills;
        level.TrainingMode = false; level.Elites.enabled = false; level.ItemLoot.enabled = loot.enabled = false;
        hunt.enabled = true; hunt.UseRegionClues = true; hunt.RandomizeBossRegion = false;
        isolateEliteNpcs = true; player.HealthRecoveryRate = 0;
        level.ClaimRelief(); string hunterId = level.Career.Selected.Id;
        var before = level.Career.Selected; var account = level.Career.Snapshot();
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(74, 2.05f));
        yield return new WaitForSeconds(.2f);
        Check(hunt.State.ExtractionSeconds == 10 && hunt.InExit && hunt.State.ExtractionElapsed > 0 &&
            !hunt.State.BossDefeated && hunt.State.ClueCount == 0 && !hunt.ExitMarkerVisible && !hunt.IsBossLocated &&
            hunt.Objective.Contains("无赏金撤离"), "The physical border exit starts the normal ten-second countdown without clues, boss credit or an early map marker");
        var objective = hud.GetComponentsInChildren<Text>().First(t => t.name == "Objective");
        Check(objective.text.Contains("无赏金撤离") && objective.preferredWidth <= objective.rectTransform.rect.width,
            "The actual HUD displays the no-bounty countdown within its existing objective area");
        var gate = FindObjectOfType<ExitGate2D>();
        Check(gate.Indicator.color.g > gate.Indicator.color.r, "The physical uncontested exit indicator is available before bounty pickup");
        yield return Capture("extraction-no-bounty-countdown");
        level.TogglePause(); float elapsed = hunt.State.ExtractionElapsed;
        yield return new WaitForSecondsRealtime(.2f);
        Check(hunt.State.ExtractionElapsed == elapsed && level.Phase == LevelPhase.Paused,
            "Actual pause freezes the no-bounty countdown");
        level.TogglePause(); yield return EliteGround(new Vector2(71, 2.05f)); yield return null;
        Check(hunt.State.ExtractionElapsed == 0 && !hunt.InExit && level.Phase == LevelPhase.Playing,
            "Physically leaving the exit resets the no-bounty countdown");
        yield return EliteGround(new Vector2(74, 2.05f));
        yield return Until(() => hunt.Boss, 3, "The live boss is present for a nonlethal exit-contest check");
        bossUnderTest = hunt.Boss; var bossBody = bossUnderTest.GetComponent<Rigidbody2D>();
        bossBody.position = new Vector2(72, 2.05f); bossUnderTest.transform.position = bossBody.position; Physics2D.SyncTransforms();
        yield return new WaitForSeconds(.12f);
        Check(hunt.ExitContested && hunt.State.ExtractionElapsed == 0 && hunt.Objective.Contains("撤离受阻") &&
            gate.Indicator.color.r > gate.Indicator.color.g, "A real living enemy at the exit resets no-bounty progress and changes both HUD and exit indicator");
        bossBody.position = new Vector2(68, 2.05f); bossUnderTest.transform.position = bossBody.position; Physics2D.SyncTransforms(); bossUnderTest = null;
        yield return Until(() => level.Phase == LevelPhase.Won, 13, "The actual uninterrupted no-bounty countdown completes");
        Check(!hunt.State.BossDefeated && !hunt.IsBossLocated && !hunt.ExitMarkerVisible && !hunt.State.SecondWaveStarted &&
            hunt.State.Reward == 0 && level.RunExperience == 100 && level.Career.LastExperience == Mathf.Min(100, 900 - before.Experience),
            "A zero-clue departure awards only the existing base extraction experience and invents neither boss experience nor bounty");
        Check(level.Career.Selected.Id == hunterId && level.Career.Credits == account.Credits && !level.Career.InRun &&
            level.Career.Snapshot().Extractions == account.Extractions + 1 && level.Career.LastOutcome == CareerOutcome.Extracted,
            "The real zero-bounty transaction preserves the hunter and money and records exactly one successful extraction");
        Check(hunt.Objective == "撤离成功 · 赏金 0" && level.CampMessage.Contains("赏金 0"),
            "Successful departure explicitly displays zero bounty in the objective and settlement");
        yield return Capture("extraction-no-bounty-won"); yield return ExtractionCamp();

        // A real kill, field card and partially spent tool must survive the same route and a failed disk write.
        foreach (var skill in level.Career.Selected.Skills) level.RemoveSkill(hunterId, skill);
        while (level.Career.Selected.Items.Count > 0) level.UnequipItem(hunterId, 0, level.Career.Selected.Items[0].Id);
        var custom = new float[] { 25, 50, 25 };
        Check(level.SetHealthLayout(hunterId, custom) && level.BuyItem(HunterItemId.FirstAid) && level.EquipItem(hunterId, HunterItemId.FirstAid),
            "The no-bounty survivor equips a real medical tool and custom health layout in camp");
        loot.enabled = true; loot.SpawnChance = 1; loot.RandomizeSkills = false; loot.Positions = new[] { new Vector2(6, 2.6f) };
        before = level.Career.Selected; account = level.Career.Snapshot();
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        Check(loot.Pickups[0].TryCollect(player) && level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee),
            "The hunter learns a real field skill without killing or claiming the boss");
        actor.RestoreHealth(.2f);
        Check(player.TryUseItem(), "The deployed survivor starts normal medical preparation");
        yield return Until(() => !player.IsUsingItem, 4, "The survivor spends one real medical charge");
        Check(player.Items[0].Uses == 2 && actor.Health == 70, "The spent medical charge is visible in the live loadout before extraction");
        var guard = FindObjectsOfType<PatrolEnemy2D>().First(g => g.GetComponent<SkillActor2D>().IsAlive);
        alertGuardUnderTest = guard; Freeze(guard); var guardBody = guard.GetComponent<Rigidbody2D>(); guardBody.constraints = RigidbodyConstraints2D.FreezeAll;
        guardBody.position = new Vector2(9, 2.05f); guard.transform.position = guardBody.position; Physics2D.SyncTransforms();
        var guardActor = guard.GetComponent<SkillActor2D>();
        for (int shot = 0; shot < 8 && guardActor.IsAlive; shot++) { yield return BallisticFire(); yield return BallisticFlightEnds(); }
        Check(!guardActor.IsAlive && level.DefeatedEnemies == 1 && !hunt.State.BossDefeated && level.RunExperience == 120,
            "Actual player projectiles kill one ordinary guard and earn exactly its twenty experience without boss credit");
        alertGuardUnderTest = null;
        yield return EliteGround(new Vector2(74, 2.05f)); yield return null;
        string disk = File.ReadAllText(level.CareerSavePath);
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            yield return Until(() => level.Phase == LevelPhase.Error, 13, "The physical exit reaches a locked settlement file");
            Check(level.HasCareerSaveError && level.Career.InRun && level.Career.Selected.Id == hunterId &&
                level.Career.Selected.Experience == before.Experience && level.Career.Credits == account.Credits && File.ReadAllText(level.CareerSavePath) == disk,
                "Failed no-bounty settlement freezes the run without losing the hunter, awarding experience or modifying the file");
            Check(hunt.State.Stage == HuntStage.Extracted && !hunt.State.CarriedBountyAtExit && !hunt.State.BossDefeated &&
                hunt.State.Reward == 0 && level.RunExperience == 120 && !player.TryShoot(),
                "The failed departure retains the exact zero-bounty result and real kill experience while gameplay is frozen");
            level.ReturnToTitle(); level.RetryCareerSave();
            Check(level.Phase == LevelPhase.Error && level.Career.InRun && level.Career.Snapshot().Extractions == account.Extractions,
                "Repeated locked retries and returning to camp cannot discard or duplicate pending no-bounty settlement");
        }
        yield return null; ClickButton("重试存档"); yield return null;
        Check(level.Phase == LevelPhase.Won && !level.Career.InRun && level.Career.Credits == account.Credits &&
            level.Career.Selected.Experience == Mathf.Min(900, before.Experience + 120) && level.Career.Snapshot().Extractions == account.Extractions + 1,
            "The real retry button settles one no-bounty extraction using only the captured guard and base experience");
        level.RetryCareerSave();
        Check(!level.TryComplete() && !hunt.State.TickExtraction(100, true, false) && level.Career.Snapshot().Extractions == account.Extractions + 1,
            "Repeated completion, timer updates and retries cannot duplicate the saved survivor or rewards");
        var loaded = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true)); loaded.Load();
        var survivor = loaded.Current.Selected;
        Check(survivor.Id == hunterId && survivor.Rifle == before.Rifle && survivor.Pistol == before.Pistol &&
            survivor.HasSkill(HunterSkillId.EfficientMelee) && survivor.Items.Single().Uses == 2 && survivor.HealthSegments.SequenceEqual(custom) &&
            loaded.Current.LastReward == 0 && !loaded.Current.InRun,
            "A fresh disk load preserves the no-bounty survivor's guns, learned skill, exact remaining tool charges and custom health layout");
        yield return ExtractionCamp(); level.StartRun(); yield return null; yield return EliteGround(new Vector2(68, 2.05f));
        Check(player.Items.Single().Uses == 2 && player.HealthSegments.SequenceEqual(custom) && level.Career.Selected.HasSkill(HunterSkillId.EfficientMelee) &&
            !hunt.State.BossDefeated && hunt.State.ExtractionElapsed == 0 && !hunt.ExitMarkerVisible,
            "The retained hunter redeploys with exact equipment and a completely new boss and extraction state");
        account = level.Career.Snapshot(); before = level.Career.Selected;
        yield return Until(() => hunt.Boss, 3, "The new run's real boss spawns");
        hunt.Boss.Actor.BeHit(new SkillHitData { attackValue = 10000 }); yield return null;
        Check(hunt.State.BossDefeated && hunt.State.Stage == HuntStage.BountyAvailable && !hunt.State.SecondWaveStarted && level.RunExperience == 200,
            "A real boss death without pickup contributes boss experience while leaving its token unclaimed and wave unstarted");
        yield return EliteGround(new Vector2(74, 2.05f));
        yield return Until(() => level.Phase == LevelPhase.Won, 13, "Leave a dead boss's bounty unclaimed at the physical exit");
        Check(hunt.State.BossDefeated && !hunt.State.CarriedBountyAtExit && !hunt.State.SecondWaveStarted && !hunt.ExitMarkerVisible &&
            level.Career.Credits == account.Credits && level.Career.LastReward == 0 && level.RunExperience == 200 &&
            level.Career.Selected.Experience == Mathf.Min(900, before.Experience + 200),
            "An unclaimed boss token gives no bounty funds, yet preserves true boss experience after successful extraction");
        yield return Capture("extraction-unclaimed-bounty"); yield return ExtractionCamp();
        loot.Positions = positions; loot.SpawnChance = chance; loot.RandomizeSkills = random; loot.enabled = skillLoot;
        hunt.UseRegionClues = regionClues; hunt.ResetMission();
        Check(hunt.ClueSocketCount == (regionClues ? hunt.RegionClueSockets.Length : hunt.CluePositions.Length) &&
            hunt.State.ClueCount == 0 && hunt.State.Stage == HuntStage.Tracking,
            "Restoring the prior clue configuration rebuilds matching clue objects before the next scenario deploys");
        player.HealthRecoveryRate = recovery; level.ItemLoot.enabled = itemLoot;
        isolateEliteNpcs = false; RestoreFrozen();
    }
}

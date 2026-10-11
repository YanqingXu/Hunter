using System.Collections;
using System.Linq;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private bool isolateEliteNpcs;
    private IEnumerator EliteChecks()
    {
        var director = level.Elites; var hunt = level.Hunt;
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        float recovery = player.HealthRecoveryRate;
        level.ItemLoot.enabled = false; level.SkillLoot.enabled = false; player.HealthRecoveryRate = 0; isolateEliteNpcs = true;
        hunt.enabled = true; level.ClaimRelief();
        for (int i = 0; i < 2; i++) { level.StartRun(); yield return null; yield return SkillExtraction(); }
        var id = level.Career.Selected.Id;
        Check(level.BuySkill(id, HunterSkillId.Necromancer) && level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife),
            "Elite fixture earns skill points by extraction and equips a real revival skill and throwing knives");
        director.enabled = true; level.StartRun(); yield return null;
        Check(director.Elites.Count == 0, "Unloaded elite spawn points do not create actors at the distant map origin");
        yield return EliteGround(new Vector2(32, 2.05f));
        yield return Until(() => director.Elites.Any(e => e.Kind == HunterEliteKind.Armored), 3, "The formal armored elite loads");
        var armored = director.Elites.First(e => e.Kind == HunterEliteKind.Armored);
        armored.enabled = false; armored.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        SetElitePosition(armored, director.ArmoredPosition);
        Check(armored.Wave == 1 && armored.Actor.MaxHealth == 120 && armored.Actor.IncomingDamageMultiplier == .5f && armored.CanBasicAttack,
            "The first authored armored elite has higher health, basic attacks and a separate 50 percent armor trait");
        player.SetAimInput(Vector2.right); float before = armored.Actor.Health; float shot = player.Weapons.Damage;
        Check(player.TryShoot(), "A real equipped gun fires toward the armored elite");
        yield return Until(() => armored.Actor.Health < before, 3, "The gun projectile reaches the armored elite");
        Check(Mathf.Abs(armored.Actor.Health - (before - shot * .5f)) < .001f && actor.IncomingDamageMultiplier == 1,
            "A real bullet loses exactly half its damage without changing the player's damage multiplier");
        yield return Until(() => player.CanInteract, 3, "The shot animation ends before a knife");
        before = armored.Actor.Health;
        Check(player.TrySelectItem(0) && player.TryUseItem(), "A genuine throwing knife is prepared against armor");
        yield return Until(() => armored.Actor.Health < before, 3, "The knife hits armor");
        Check(armored.Actor.Health == before - 15 && player.Items[0].Uses == 2,
            "Thrown-knife damage uses the same armor entry and consumes one charge");
        yield return EliteGround(new Vector2(34.9f, 2.05f)); player.SetAimInput(Vector2.right); before = armored.Actor.Health;
        Check(player.TryMelee(), "A genuine melee strike is accepted beside armor");
        yield return Until(() => armored.Actor.Health < before, 3, "The melee attack reaches armor");
        Check(Mathf.Abs(armored.Actor.Health - (before - player.Weapons.MeleeDamage * 2 * .5f)) < .001f,
            "The authored melee's two-times attack coefficient is applied before the target's single armor reduction; before=" + before + ", after=" + armored.Actor.Health);
        yield return Until(() => player.CanInteract, 3, "Melee recovery ends");
        before = armored.Actor.Health;
        HunterExplosion2D.Detonate(level, armored.transform.position + Vector3.up * .75f, .5f, 60, actor);
        Check(armored.Actor.Health == before - 30, "The production explosion resolver applies the same armor to blast damage");
        var barrier = new GameObject("Elite Sight Wall"); barrier.transform.position = new Vector2(35.45f, 3.3f);
        var barrierShape = barrier.AddComponent<BoxCollider2D>(); barrierShape.size = new Vector2(.12f, 2.5f); Physics2D.SyncTransforms();
        armored.enabled = true; actor.RestoreHealth(); yield return new WaitForSeconds(.15f);
        Check(!armored.DetectsPlayer && !armored.IsWinding && actor.Health == 100, "Solid terrain blocks an elite's sight and attack windup");
        barrierShape.isTrigger = true; Physics2D.SyncTransforms();
        yield return Until(() => armored.IsWinding, 2, "A trigger-only wall permits elite warning windup");
        float remaining = armored.WindupRemaining; level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(armored.WindupRemaining == remaining && actor.Health == 100, "Pause freezes the elite's visible attack windup without dealing damage");
        level.TogglePause(); yield return Until(() => actor.Health < 100, 2, "The armored elite completes its real normal attack");
        Check(actor.Health == 82, "The armored elite's normal attack deals its configured 18 damage");
        armored.enabled = false; Destroy(barrier); yield return Capture("elite-armor");
        yield return EliteGround(new Vector2(28, 2.05f));
        SetElitePosition(armored, new Vector2(23.2f, 2.05f)); armored.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeRotation;
        armored.enabled = true; yield return new WaitForSeconds(.35f);
        Check(armored.DetectsPlayer && armored.GetComponent<Rigidbody2D>().position.x < 23.4f && armored.transform.position.y < 2.1f,
            "A chasing elite stops at the authored broken bridge instead of jumping or walking into the gap");
        armored.enabled = false; armored.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll; SetElitePosition(armored, director.ArmoredPosition);

        yield return EliteGround(new Vector2(54, 2.05f));
        yield return Until(() => director.Elites.Any(e => e.Kind == HunterEliteKind.Summoner), 3, "The authored summoner loads");
        var summoner = director.Elites.First(e => e.Kind == HunterEliteKind.Summoner);
        summoner.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        Check(!summoner.CanBasicAttack && summoner.Actor.MaxHealth == 40 && summoner.DetectionDistance > armored.DetectionDistance && summoner.Actor.IncomingDamageMultiplier == 1,
            "The second elite has lower health, greater detection and no basic attack or armor");
        yield return Until(() => summoner.Summon, 3, "The real summoning windup creates a separate actor");
        var summon = summoner.Summon;
        summon.enabled = false; summon.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        Check(summon.Owner == summoner && summon.IsSummon && summon.Actor != summoner.Actor && summon.Actor.Health == 35 && summon.CanBasicAttack && summon.DetectionDistance == 8,
            "The summon owns independent health, detection, collision and normal attacks");
        yield return new WaitForSeconds(.85f);
        Check(summoner.Summon == summon && director.Elites.Count(e => e.Kind == HunterEliteKind.Summoner) == 1,
            "An active summon prevents duplicate skill casts while its owner remains alerted");
        yield return Capture("elite-summoner");
        SetElitePosition(summon, new Vector2(55.1f, 2.05f)); summon.enabled = true; actor.RestoreHealth();
        yield return Until(() => actor.Health < 100, 2, "The independent summon attacks the real player");
        Check(actor.Health == 90, "The summon's own attack deals 10 damage while the summoner has no melee attack");
        summon.enabled = false; SetElitePosition(summon, new Vector2(56.5f, 2.05f));
        yield return EliteGround(new Vector2(54, 2.05f)); player.SetAimInput(Vector2.right);
        float ownerHealth = summoner.Actor.Health; int xp = level.RunExperience;
        for (int i = 0; i < 4 && summon && summon.Actor.IsAlive; i++)
        { yield return Until(() => player.TryShoot(), 3, "A real bullet attacks the independent summon"); yield return new WaitForSeconds(.85f); }
        Check(!summoner.Summon && summoner.Cycle.State == HunterSummonState.Cooldown && summoner.Actor.Health == ownerHealth && level.RunExperience == xp,
            "Killing the summon preserves owner health, starts cooldown and grants no farmable elite-kill experience");
        float cooldown = summoner.Cycle.CooldownRemaining; level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(summoner.Cycle.CooldownRemaining == cooldown, "Pause freezes the summoning skill cooldown");
        level.TogglePause(); yield return Until(() => summoner.Summon, 8, "A cooldown completes before a fresh summon is allowed");
        var secondSummon = summoner.Summon; secondSummon.enabled = false; secondSummon.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        Check(secondSummon != summon && secondSummon.Actor.Health == 35, "The next summon is a new independent actor with fresh health");
        // Keep the target region loaded while leaving both detection zones.
        yield return EliteGround(new Vector2(42, 2.05f));
        yield return Until(() => summoner.Cycle.UnseenSeconds > .1f, 2, "Leaving both alert areas starts abandonment");
        remaining = summoner.Cycle.UnseenSeconds; director.enabled = false; yield return new WaitForSeconds(.15f);
        Check(summoner.Summon == secondSummon && summoner.Cycle.UnseenSeconds == remaining && !secondSummon.gameObject.activeInHierarchy,
            "Disabling the owning director hides both actors and freezes retained summon lifetime");
        director.enabled = true; yield return null;
        yield return EliteGround(new Vector2(54, 2.05f));
        Check(summoner.Summon == secondSummon && summoner.Cycle.UnseenSeconds == 0, "Returning to the owner's detection zone cancels abandonment");
        bool viewport = level.World.Map.UseCameraViewport; level.World.Map.UseCameraViewport = false;
        yield return Warp(new Vector2(6, 2.05f)); yield return Until(() => !summoner.gameObject.activeInHierarchy, 4, "The actual summoner region unloads");
        remaining = summoner.Cycle.UnseenSeconds; yield return new WaitForSeconds(.15f);
        Check(summoner.Summon == secondSummon && summoner.Cycle.UnseenSeconds == remaining && summoner.Actor.Health == ownerHealth,
            "Unloaded terrain freezes the owned summon and preserves its owner's health");
        level.World.Map.UseCameraViewport = viewport; yield return EliteGround(new Vector2(54, 2.05f));
        Check(summoner.Summon == secondSummon && secondSummon.gameObject.activeInHierarchy, "Reloading restores the same summon rather than spawning a duplicate");
        Destroy(secondSummon.gameObject); yield return null;
        Check(!summoner.Summon && summoner.Cycle.State == HunterSummonState.Cooldown,
            "External destruction of an owned summon clears the reference and starts cooldown instead of permanently locking the skill");
        yield return Until(() => summoner.Summon, 8, "The summoner recovers normally after external actor removal");
        secondSummon = summoner.Summon; secondSummon.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;

        // Revival is a real career transaction; all enemy classes must respect its three-second grace.
        secondSummon.enabled = true; SetElitePosition(secondSummon, new Vector2(55.1f, 2.05f));
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 200 });
        yield return Until(() => level.Phase == LevelPhase.Playing && level.IsPlayerUndetected, 5, "The real necromancer skill revives the hunter");
        yield return new WaitForFixedUpdate();
        Check(!summoner.IsAlerted && !summoner.DetectsPlayer && !secondSummon.IsAlerted && !secondSummon.DetectsPlayer && !secondSummon.IsWinding,
            "Revival grace suppresses both elite-owner and summon detection and cancels attacks");
        yield return Until(() => !level.IsPlayerUndetected && summoner.DetectsPlayer, 4, "Elite detection resumes after actual revival grace");
        if (summoner.Summon) { summoner.Summon.enabled = false; summoner.Summon.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll; }
        yield return EliteGround(new Vector2(42, 2.05f));
        yield return Until(() => !summoner.Summon && summoner.Cycle.State == HunterSummonState.Cooldown, 4, "Leaving both zones removes the summon after three seconds");
        Check(summoner.Actor.Health == ownerHealth && summoner.Cycle.CooldownRemaining > 5,
            "Abandonment removes only the summon and starts a full new cooldown");

        // Use the real boss death and bounty pickup to trigger the independent second wave.
        yield return EliteGround(new Vector2(68, 2.05f));
        yield return Until(() => hunt.Boss, 3, "The production boss is present for the wave trigger");
        hunt.Boss.Actor.BeHit(new SkillHitData { attackValue = 10000 }); yield return null;
        yield return EliteGround((Vector2)hunt.BountyPosition - Vector2.up * .5f);
        Check(player.TryInteract() && hunt.State.SecondWaveStarted, "The real bounty interaction starts elite reinforcement spawning");
        yield return EliteGround(new Vector2(36, 2.05f));
        yield return Until(() => director.Elites.Count == 4, 4, "Both second-wave elite points load once");
        foreach (var elite in director.Elites) { elite.enabled = false; elite.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll; if (elite.Summon) elite.Summon.enabled = false; }
        Check(director.Elites.Count(e => e.Wave == 1) == 2 && director.Elites.Count(e => e.Wave == 2) == 2 && armored.Actor.Health > 0 && summoner.Actor.Health > 0,
            "Second-wave elites coexist with both still-living first-wave elites");
        Check(director.Elites.Where(e => e.Wave == 2).All(e => e.DetectionDistance > (e.Kind == HunterEliteKind.Armored ? 9 : 14)),
            "The bounty wave receives wider detection for both elite archetypes");
        yield return new WaitForSeconds(.15f);
        Check(director.Elites.Count == 4, "Repeated wave updates do not create duplicate elites");
        xp = level.RunExperience; armored.Actor.BeHit(new SkillHitData { attackValue = 10000 });
        Check(director.DefeatedCount == 1 && level.RunExperience == xp + 20, "An elite death contributes one normal enemy reward to extraction experience");
        armored.Actor.BeHit(new SkillHitData { attackValue = 10000 });
        Check(director.DefeatedCount == 1 && level.RunExperience == xp + 20, "Repeated hits on a dead elite cannot duplicate experience");
        var mage2 = director.Elites.First(e => e.Wave == 2 && e.Kind == HunterEliteKind.Summoner);
        yield return EliteGround(new Vector2(55, 2.05f)); mage2.enabled = true;
        yield return Until(() => mage2.Summon, 3, "A second-wave summoner creates its owned actor");
        var owned = mage2.Summon; owned.enabled = false;
        Check(owned.DetectionDistance > 8, "Second-wave summons inherit expanded detection");
        SetElitePosition(owned, (Vector2)level.ExitPoint.position); yield return null;
        Check(director.Contests(level.ExitPoint.position), "A living summon independently contests the real extraction area");
        mage2.Actor.BeHit(new SkillHitData { attackValue = 10000 });
        Check(!mage2.Summon && !owned.gameObject.activeInHierarchy && mage2.Cycle.State == HunterSummonState.Disabled,
            "Owner death immediately removes its summon and permanently disables the skill");
        yield return null; Check(!owned, "Owned summon objects are destroyed after owner death, leaving no orphan actor");
        foreach (var elite in director.Elites) if (elite.Actor.Health > 0) elite.Actor.BeHit(new SkillHitData { attackValue = 10000 });
        yield return SkillExtraction();
        Check(director.Elites.Count == 0, "Returning to camp clears every run-owned elite and summon");
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(32, 2.05f));
        yield return Until(() => director.Elites.Any(e => e.Kind == HunterEliteKind.Armored), 3, "A fresh run creates the first elite again");
        var fresh = director.Elites.First(e => e.Kind == HunterEliteKind.Armored);
        Check(fresh != armored && fresh.Actor.Health == 120 && director.DefeatedCount == 0 && director.Elites.All(e => e.Wave == 1),
            "A new run restores elite health and wave identity without retaining previous deaths");
        yield return SkillExtraction(); director.enabled = false; isolateEliteNpcs = false; RestoreFrozen();
        level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot; player.HealthRecoveryRate = recovery;
    }
    private void SetElitePosition(HunterElite2D elite, Vector2 point)
    { elite.GetComponent<Rigidbody2D>().position = point; elite.transform.position = point; Physics2D.SyncTransforms(); }
    private IEnumerator EliteGround(Vector2 point)
    {
        yield return Warp(point);
        yield return null; IsolateEliteNpcs();
        Physics2D.SyncTransforms(); yield return Warp(point);
        yield return Until(() => player.CanInteract && player.IsGrounded, 3, "The elite fixture player stands on real terrain at " + point);
        actor.RestoreHealth();
    }
    private void IsolateEliteNpcs()
    {
        if (!player || !body) return;
        Vector2 point = body.position;
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        {
            if (guard == alertGuardUnderTest) continue;
            if (guard.enabled) Freeze(guard);
            var rigid = guard.GetComponent<Rigidbody2D>(); rigid.constraints = RigidbodyConstraints2D.FreezeAll;
            if (Mathf.Abs(rigid.position.x - point.x) < 4)
            { rigid.position = new Vector2(point.x - 7, 2.05f); guard.transform.position = rigid.position; }
        }
        foreach (var enemy in FindObjectsOfType<HuntEnemy2D>())
        {
            if (enemy == bossUnderTest || enemy == alertReinforcementUnderTest) continue;
            enemy.enabled = false; var rigid = enemy.GetComponent<Rigidbody2D>(); rigid.constraints = RigidbodyConstraints2D.FreezeAll;
            if (enemy.Actor.IsAlive && Mathf.Abs(rigid.position.x - point.x) < 2)
            { rigid.position = new Vector2(point.x + 3, 2.05f); enemy.transform.position = rigid.position; }
        }
        Physics2D.SyncTransforms();
    }
}

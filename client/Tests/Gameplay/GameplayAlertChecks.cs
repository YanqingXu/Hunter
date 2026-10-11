using System.Collections;
using System.Linq;
using BigWorld.Gameplay;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private PatrolEnemy2D alertGuardUnderTest;
    private HuntEnemy2D alertReinforcementUnderTest;
    private IEnumerator AlertChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        bool viewport = level.World.Map.UseCameraViewport; float recovery = player.HealthRecoveryRate;
        var playerConstraints = body.constraints;
        level.ItemLoot.enabled = false; level.SkillLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.Hunt.enabled = true; level.Elites.enabled = false; level.ClaimRelief(); isolateEliteNpcs = true;
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        yield return Until(() => Guard(1), 4, "The first real streamed guard loads for alert checks");
        var guard = Guard(1); var enemy = guard.GetComponent<PatrolEnemy2D>(); var rigid = guard.GetComponent<Rigidbody2D>();
        alertGuardUnderTest = enemy; SetAlertActor(enemy.transform, new Vector2(14, 2.05f));
        rigid.constraints = RigidbodyConstraints2D.FreezeRotation; enemy.enabled = true;
        float start = rigid.position.x; yield return new WaitForSeconds(.2f);
        Check(!enemy.IsAlerted && enemy.State == PatrolEnemyState.Patrol && Mathf.Abs(rigid.velocity.x) > 1.9f && Mathf.Abs(rigid.position.x - start) > .15f,
            "An unalerted streamed guard patrols physically at its configured two-unit speed");
        PlaceBossPlayer(new Vector2(10, 2.05f));
        yield return Until(() => enemy.Target && enemy.State == PatrolEnemyState.Chase, 2, "The real guard enters pursuit");
        Check(enemy.IsAlerted && guard.CombatPinned && rigid.velocity.x < -2.9f, "A visible target enables faster pursuit and pins the real streamed actor");
        PlaceBossPlayer(new Vector2(4.5f, 2.05f));
        yield return Until(() => enemy.State == PatrolEnemyState.Searching, 2, "Loss of range enters the new searching state");
        Check(enemy.IsAlerted && !enemy.Target && guard.CombatPinned && Mathf.Abs(rigid.velocity.x) < .01f && enemy.AlertUnseenSeconds < .15f,
            "A lost target retains alert and residency while stopping movement and clearing the attack target");
        float unseen = enemy.AlertUnseenSeconds; level.TogglePause(); yield return new WaitForSecondsRealtime(.2f);
        Check(enemy.IsAlerted && enemy.AlertUnseenSeconds == unseen && guard.CombatPinned, "Pause freezes the real guard's delayed return to patrol");
        level.TogglePause(); yield return new WaitForSeconds(.25f);
        Check(enemy.State == PatrolEnemyState.Searching && enemy.IsAlerted, "Brief absence does not instantly return the guard to patrol");
        PlaceBossPlayer(new Vector2(10, 2.05f)); yield return new WaitForFixedUpdate();
        Check(enemy.Target && enemy.IsAlerted && enemy.AlertUnseenSeconds == 0, "Reentering range cancels the guard's forgetting countdown");
        PlaceBossPlayer(new Vector2(4.5f, 2.05f)); yield return new WaitForSeconds(.75f);
        Check(enemy.IsAlerted && enemy.State == PatrolEnemyState.Searching, "Reacquisition starts a full new one-second memory instead of retaining old elapsed time");
        yield return Until(() => !enemy.IsAlerted, .5f, "The real guard's one-second memory expires");
        Check(enemy.State == PatrolEnemyState.Patrol && !guard.CombatPinned && !enemy.Target && Mathf.Abs(rigid.velocity.x) > 1.9f,
            "Memory expiry releases combat residency and resumes actual patrol motion");

        rigid.constraints = RigidbodyConstraints2D.FreezeAll; SetAlertActor(enemy.transform, new Vector2(14, 2.05f));
        PlaceBossPlayer(new Vector2(12.9f, 2.05f)); body.constraints = RigidbodyConstraints2D.FreezeAll; actor.RestoreHealth();
        yield return Until(() => enemy.State == PatrolEnemyState.Windup, 3, "The alert fixture starts a real guard attack");
        var wall = new GameObject("Alert Sight Cover"); wall.transform.position = new Vector2(13.45f, 3);
        var wallShape = wall.AddComponent<BoxCollider2D>(); wallShape.size = new Vector2(.12f, 2); Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Check(enemy.State == PatrolEnemyState.Searching && !enemy.Target && enemy.IsAlerted,
            "Losing sight to a solid wall immediately cancels a guard windup but retains alert memory");
        yield return new WaitForSeconds(.5f);
        Check(actor.Health == 100 && enemy.IsAlerted, "A remembered target cannot be damaged through the new wall");
        yield return Capture("enemy-alert-search");
        wallShape.isTrigger = true; Physics2D.SyncTransforms();
        yield return Until(() => enemy.Target && enemy.AlertUnseenSeconds == 0, 2, "Trigger-only cover permits genuine reacquisition");
        enemy.ForgetPlayer();
        Check(!enemy.IsAlerted && !enemy.Target && !guard.CombatPinned && enemy.State == PatrolEnemyState.Patrol,
            "Explicit revival suppression clears guard memory and residency immediately");
        Destroy(wall); enemy.enabled = false;

        // Exercise actual map recycling and rebinding instead of calling the pool callback directly.
        SetAlertActor(enemy.transform, new Vector2(14, 2.05f)); enemy.enabled = true;
        PlaceBossPlayer(new Vector2(10, 2.05f)); yield return Until(() => enemy.IsAlerted, 2, "The guard alerts before recycling");
        guard.GetComponent<SkillActor2D>().BeHit(new SkillHitData { attackValue = 5 }); float retainedHealth = guard.Health;
        level.World.Map.UseCameraViewport = false; yield return Warp(new Vector2(70, 2.05f));
        yield return Until(() => !Guard(1), 4, "The real streamed guard is recycled after losing its target");
        Check(!enemy.IsAlerted && !enemy.Target && !guard.CombatPinned, "Pool release clears all remembered target state");
        alertGuardUnderTest = null; yield return Warp(new Vector2(6, 2.05f));
        yield return Until(() => Guard(1), 4, "The guard is rebound from the real pool");
        guard = Guard(1); enemy = guard.GetComponent<PatrolEnemy2D>(); rigid = guard.GetComponent<Rigidbody2D>(); alertGuardUnderTest = enemy;
        Check(!enemy.IsAlerted && !enemy.Target && !guard.CombatPinned && guard.Health == retainedHealth,
            "A real pool rebind restores saved health without inheriting alert memory from the previous use");
        SetAlertActor(enemy.transform, new Vector2(14, 2.05f)); rigid.constraints = RigidbodyConstraints2D.FreezeAll; enemy.enabled = true;
        PlaceBossPlayer(new Vector2(10, 2.05f)); yield return Until(() => enemy.IsAlerted, 2, "The rebound guard can alert normally");
        guard.GetComponent<SkillActor2D>().BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        Check(!Guard(1) && !enemy.IsAlerted && !enemy.Target && !guard.CombatPinned, "Guard death and immediate pool release remove its retained target and combat pin");
        alertGuardUnderTest = null; level.World.Map.UseCameraViewport = viewport;

        yield return EliteGround(new Vector2(68, 2.05f)); yield return Until(() => level.Hunt.Boss, 3, "The boss loads for a genuine second-wave trigger");
        level.Hunt.Boss.Actor.BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        yield return EliteGround((Vector2)level.Hunt.BountyPosition - Vector2.up * .5f);
        Check(player.TryInteract() && level.Hunt.State.SecondWaveStarted, "A real bounty pickup starts the reinforcement patrol scenario");
        yield return Warp(new Vector2(6, 2.05f));
        yield return Until(() => FindObjectsOfType<HuntEnemy2D>().Any(e => !e.IsBoss && e.transform.position.x < 25), 4, "The authored first reinforcement point loads");
        var soldier = FindObjectsOfType<HuntEnemy2D>().First(e => !e.IsBoss && e.transform.position.x < 25);
        alertReinforcementUnderTest = soldier; rigid = soldier.GetComponent<Rigidbody2D>();
        SetAlertActor(soldier.transform, new Vector2(14, 2.05f)); rigid.constraints = RigidbodyConstraints2D.FreezeRotation;
        PlaceBossPlayer(new Vector2(1.5f, 2.05f)); soldier.enabled = true;
        // The expanded detection range is verified before moving outside it for patrol.
        yield return new WaitForFixedUpdate();
        Check(soldier.DetectionDistance == 14 && soldier.IsAlerted && soldier.DetectsPlayer && soldier.ChaseSpeed > soldier.PatrolSpeed,
            "Second-wave ordinary soldiers keep expanded detection and distinct patrol and chase speeds");
        SetAlertActor(soldier.transform, new Vector2(17, 2.05f)); soldier.ForgetPlayer();
        yield return new WaitForFixedUpdate(); start = rigid.position.x; yield return new WaitForSeconds(.2f);
        Check(!soldier.IsAlerted && Mathf.Abs(rigid.position.x - start) > .15f && Mathf.Abs(rigid.velocity.x) > 1.9f,
            "A second-wave soldier now patrols when unalerted instead of remaining permanently still");
        SetAlertActor(soldier.transform, new Vector2(14, 2.05f)); PlaceBossPlayer(new Vector2(10, 2.05f));
        yield return Until(() => soldier.DetectsPlayer && rigid.velocity.x < -2.9f, 2, "The reinforcement pursues at its faster alert speed");
        rigid.constraints = RigidbodyConstraints2D.FreezeAll; SetAlertActor(soldier.transform, new Vector2(14, 2.05f));
        PlaceBossPlayer(new Vector2(12.9f, 2.05f)); actor.RestoreHealth();
        yield return Until(() => soldier.IsWinding, 3, "A real reinforcement melee windup starts");
        wall = new GameObject("Reinforcement Sight Cover"); wall.transform.position = new Vector2(13.45f, 3);
        wallShape = wall.AddComponent<BoxCollider2D>(); wallShape.size = new Vector2(.12f, 2); Physics2D.SyncTransforms();
        yield return new WaitForSeconds(.2f);
        Check(soldier.IsAlerted && !soldier.DetectsPlayer && !soldier.IsWinding && actor.Health == 100,
            "Second-wave lost sight retains alert while canceling the actual melee windup");
        unseen = soldier.AlertUnseenSeconds; level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(soldier.AlertUnseenSeconds == unseen && soldier.IsAlerted, "Pause freezes the second-wave forgetting timer too");
        level.TogglePause(); wallShape.isTrigger = true; Physics2D.SyncTransforms(); yield return new WaitForFixedUpdate();
        Check(soldier.DetectsPlayer && soldier.IsAlerted && soldier.AlertUnseenSeconds == 0, "The reinforcement gets a fresh memory window on reacquisition");
        wallShape.isTrigger = false; Physics2D.SyncTransforms(); yield return new WaitForSeconds(1.15f);
        Check(!soldier.IsAlerted && !soldier.IsWinding && actor.Health == 100, "A reinforcement eventually leaves alert without ever striking through cover");
        Destroy(wall); soldier.enabled = false;

        // Use the real broken bridge after restoring the soldier's physical movement.
        yield return Warp(new Vector2(28, 2.05f)); SetAlertActor(soldier.transform, new Vector2(23.2f, 2.05f));
        rigid.constraints = RigidbodyConstraints2D.FreezeRotation; soldier.enabled = true; yield return new WaitForSeconds(.35f);
        Check(soldier.DetectsPlayer && rigid.position.x < 23.5f && rigid.position.y > 1.8f,
            "Faster second-wave pursuit still stops at the authored gap without jumping");
        soldier.ForgetPlayer();
        Check(!soldier.IsAlerted && !soldier.DetectsPlayer && !soldier.IsWinding && soldier.AlertUnseenSeconds == 0,
            "Explicit suppression immediately clears the reinforcement's entire remembered attack state");
        soldier.Actor.BeHit(new SkillHitData { attackValue = 1000 });
        Check(!soldier.IsAlerted && !soldier.IsWinding, "Reinforcement death clears retained alert immediately");
        alertReinforcementUnderTest = null;

        // The same memory now drives existing elite alerting without changing summon abandonment timing.
        level.Elites.enabled = true; level.Elites.BeginRun(); yield return EliteGround(new Vector2(32, 2.05f));
        yield return Until(() => level.Elites.Elites.Any(e => e.Kind == HunterEliteKind.Armored), 3, "The armored elite loads for shared alert memory checks");
        var elite = level.Elites.Elites.First(e => e.Kind == HunterEliteKind.Armored);
        foreach (var other in level.Elites.Elites) if (other != elite) other.enabled = false;
        elite.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll; SetElitePosition(elite, new Vector2(36, 2.05f));
        yield return Until(() => elite.IsAlerted, 2, "The elite acquires its target with the shared memory");
        wall = new GameObject("Elite Alert Cover"); wall.transform.position = new Vector2(34, 3); wallShape = wall.AddComponent<BoxCollider2D>(); wallShape.size = new Vector2(.1f, 2);
        Physics2D.SyncTransforms(); yield return new WaitForSeconds(.25f);
        Check(elite.IsAlerted && !elite.DetectsPlayer && elite.AlertUnseenSeconds >= .2f && !elite.IsWinding,
            "Existing elite AI uses the same delayed forgetting without attacking its hidden target");
        yield return new WaitForSeconds(.85f);
        Check(!elite.IsAlerted, "The elite's prior one-second delay is preserved by the shared rules");
        wallShape.isTrigger = true; Physics2D.SyncTransforms(); yield return Until(() => elite.IsAlerted, 2, "The elite reacquires through trigger cover");
        elite.ForgetPlayer(); Check(!elite.IsAlerted && !elite.IsWinding && elite.AlertUnseenSeconds == 0,
            "Elite explicit suppression also clears retained alert immediately");
        Destroy(wall); yield return Capture("enemy-alert-reinforcements");
        yield return SkillExtraction(); level.Elites.enabled = false; isolateEliteNpcs = false; RestoreFrozen();
        body.constraints = playerConstraints; level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot; player.HealthRecoveryRate = recovery;
    }
    private void SetAlertActor(Transform target, Vector2 point)
    { var rigid = target.GetComponent<Rigidbody2D>(); rigid.position = point; target.position = point; rigid.velocity = Vector2.zero; Physics2D.SyncTransforms(); }
}

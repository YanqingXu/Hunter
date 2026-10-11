using System.Collections;
using BigWorld.Gameplay;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private HuntEnemy2D bossUnderTest;
    private IEnumerator BossChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        float recovery = player.HealthRecoveryRate; var playerConstraints = body.constraints;
        level.ItemLoot.enabled = false; level.SkillLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.Elites.enabled = false; level.Hunt.enabled = true; level.ClaimRelief(); isolateEliteNpcs = true;
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(56, 2.05f));
        yield return Until(() => level.Hunt.Boss, 3, "The formal hunt boss is loaded for ability checks");
        bossUnderTest = level.Hunt.Boss; var boss = bossUnderTest; var combat = boss.BossCombat; var rigid = boss.GetComponent<Rigidbody2D>();
        rigid.constraints = RigidbodyConstraints2D.FreezeRotation; boss.enabled = true;
        Vector2 idle = rigid.position; yield return new WaitForSeconds(.25f);
        Check(boss.Actor.Health == 150 && !boss.IsAlerted && !combat.Clock.IsWinding && Mathf.Abs(rigid.position.x - idle.x) < .01f,
            "The formal 150-health boss stays still before entering its alert range");

        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(58.6f, 2.05f));
        yield return Until(() => combat.Clock.Active == HunterBossAbility.Slash, 3, "The alert skill group selects a close slash");
        Check(combat.Warning.enabled && combat.Facing == -1 && !boss.IsEnraged, "A close slash has a visible warning and locks its facing");
        float windup = combat.Clock.WindupRemaining; level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(combat.Clock.WindupRemaining == windup && actor.Health == 100, "Pause freezes the boss slash before any damage");
        level.TogglePause(); yield return Until(() => actor.Health < 100, 2, "The real slash completes its windup");
        Check(actor.Health == 82, "The alert slash deals exactly 18 damage");
        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(58.6f, 2.05f));
        yield return Until(() => combat.Clock.Active == HunterBossAbility.Slash, 3, "The next slash begins after recovery");
        PlaceBossPlayer(new Vector2(61.4f, 2.05f)); yield return new WaitForSeconds(.6f);
        Check(actor.Health == 100 && !combat.Clock.IsWinding, "Crossing behind the snapshotted slash direction avoids its damage");

        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(56, 2.05f));
        yield return Until(() => combat.Clock.Active == HunterBossAbility.Bolt, 3, "Distance selects the alert group's ranged bolt");
        Vector2 aim = combat.AimDirection; PlaceBossPlayer(new Vector2(61.4f, 2.05f));
        yield return Until(() => combat.ActiveProjectiles == 1, 2, "The ranged warning creates one independent energy bolt");
        var bolt = FindObjectOfType<HunterBossBolt2D>();
        Check(bolt && Vector2.Distance(bolt.Direction, aim) < .001f && bolt.Direction.x < 0,
            "A boss bolt retains the aim from the start of windup after the player crosses behind");
        yield return new WaitForSeconds(.3f);
        Check(actor.Health == 100, "The fixed-direction bolt does not home back toward the dodging player");
        boss.ForgetPlayer(); yield return null;
        Check(combat.ActiveProjectiles == 0 && !bolt, "Forgetting the target removes already-launched boss projectiles");

        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(56, 2.05f));
        yield return Until(() => combat.ActiveProjectiles == 1, 3, "A real ranged bolt launches toward the stationary player");
        bolt = FindObjectOfType<HunterBossBolt2D>(); Vector2 boltPosition = bolt.transform.position;
        level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(bolt && Vector2.Distance(bolt.transform.position, boltPosition) < .001f && actor.Health == 100,
            "Pause freezes an in-flight boss projectile without collision damage");
        level.TogglePause();
        var duplicate = player.gameObject.AddComponent<BoxCollider2D>(); duplicate.size = new Vector2(.4f, 1); duplicate.offset = Vector2.up * .75f;
        yield return Until(() => actor.Health < 100, 2, "The boss bolt sweeps into the player's actual collider");
        Check(actor.Health == 84 && combat.ActiveProjectiles == 0, "A boss energy bolt deals 16 damage once even with overlapping target colliders");
        Destroy(duplicate);
        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(56, 2.05f));
        // A solid prop outside the terrain sight mask still has to stop the actual projectile sweep.
        var cover = new GameObject("Boss Bolt Cover"); cover.layer = 8; cover.transform.position = new Vector2(58, 2.8f);
        var coverShape = cover.AddComponent<BoxCollider2D>(); coverShape.size = new Vector2(.15f, 2); Physics2D.SyncTransforms();
        yield return Until(() => combat.ActiveProjectiles == 1, 3, "A bolt launches toward a solid prop");
        yield return Until(() => combat.ActiveProjectiles == 0, 2, "The bolt collides with solid cover");
        Check(actor.Health == 100, "Swept boss projectiles cannot pass through solid cover");
        coverShape.isTrigger = true; Physics2D.SyncTransforms();
        yield return Until(() => actor.Health < 100, 4, "Trigger-only cover permits the next real boss bolt");
        Check(actor.Health == 84, "Trigger volumes do not absorb energy bolts");
        Destroy(cover);

        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(58.2f, 2.05f));
        boss.Actor.BeHit(new SkillHitData { attackValue = 50 });
        Check(boss.Actor.Health == 100 && boss.IsEnraged && combat.Clock.Active == HunterBossAbility.RagePulse && combat.ActiveProjectiles == 0,
            "Crossing 100 health immediately preempts the alert group with a rage pulse windup");
        float rage = combat.RageRemaining; windup = combat.Clock.WindupRemaining;
        boss.Actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(combat.RageRemaining == rage && combat.Clock.WindupRemaining == windup, "Further hits above the next threshold cannot restart rage or its pulse");
        yield return new WaitForFixedUpdate(); yield return Capture("boss-rage-warning");
        level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(combat.Clock.Active == HunterBossAbility.RagePulse && actor.Health == 100, "Pause also suspends the rage area warning");
        level.TogglePause(); yield return Until(() => actor.Health < 100, 2, "The rage pulse resolves against the real player");
        Check(actor.Health == 75 && boss.Actor.Health == 99, "The rage pulse deals 25 once and cannot damage its own boss");
        boss.enabled = false; PlaceBossPlayer(new Vector2(56.5f, 2.05f)); actor.RestoreHealth();
        boss.enabled = true;
        yield return Until(() => combat.Clock.Active == HunterBossAbility.Rush, 2, "Rage selects the exclusive rush skill at distance");
        rigid.constraints = RigidbodyConstraints2D.FreezeRotation;
        yield return Until(() => combat.IsRushing, 2, "The rush finishes its visible lane warning");
        float rushStart = rigid.position.x; yield return Until(() => actor.Health < 100, 1, "The rushing body hits the real player");
        Check(actor.Health == 78 && rigid.position.x < rushStart - .5f, "The rage rush moves physically and hits for 22 damage");
        yield return new WaitForSeconds(.15f);
        Check(actor.Health == 78, "Continued rush overlap cannot repeatedly damage the same player");
        yield return Until(() => !boss.IsEnraged, 2, "The first rage period expires");
        Check(!combat.IsRushing && !HunterBossAbilityClock.IsRageAbility(combat.Clock.Active), "Rage expiry cancels remaining rage motion and forbids rage-group windups");
        yield return Capture("boss-skills");

        // The second real health threshold is used beside the authored broken bridge.
        yield return BossPlace(new Vector2(23.2f, 2.05f), new Vector2(28, 2.05f), true);
        boss.Actor.BeHit(new SkillHitData { attackValue = 49 });
        Check(boss.Actor.Health == 50 && combat.Clock.Active == HunterBossAbility.RagePulse, "The second health threshold independently triggers a new rage pulse");
        yield return Until(() => combat.Clock.Active == HunterBossAbility.Rush, 3, "The boss warns of a rush toward the player across the real gap");
        yield return Until(() => !combat.Clock.IsWinding, 2, "The warned rush attempts movement at the broken bridge");
        Check(rigid.position.x < 23.5f && rigid.position.y > 1.8f && actor.Health == 100,
            "A rage rush cannot jump or charge into the authored floor gap");
        yield return Until(() => !boss.IsEnraged, 2, "The second rage group ends");

        // A separate instance of the same production actor isolates a solid prop from sight-mask cancellation.
        boss.enabled = false; yield return EliteGround(new Vector2(56.5f, 2.05f)); body.constraints = RigidbodyConstraints2D.FreezeAll;
        var wallBoss = new GameObject("Boss Rush Wall Probe").AddComponent<HuntEnemy2D>(); wallBoss.transform.position = new Vector2(60, 2.05f);
        wallBoss.Configure(true, level.Hunt.EnemySprite, level.Hunt.EnemyMaterial); bossUnderTest = wallBoss;
        var wallBody = wallBoss.GetComponent<Rigidbody2D>(); wallBody.constraints = RigidbodyConstraints2D.FreezeAll;
        wallBoss.Actor.BeHit(new SkillHitData { attackValue = 50 });
        yield return Until(() => wallBoss.BossCombat.Clock.Active == HunterBossAbility.Rush, 3, "A production boss instance prepares a rush at solid cover");
        cover = new GameObject("Boss Rush Solid Cover"); cover.layer = 8; cover.transform.position = new Vector2(58.5f, 2.8f);
        coverShape = cover.AddComponent<BoxCollider2D>(); coverShape.size = new Vector2(.15f, 2); Physics2D.SyncTransforms();
        wallBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        yield return Until(() => wallBoss.BossCombat.IsRushing, 2, "The wall fixture rush starts");
        Vector2 pausedRush = wallBody.position; rage = wallBoss.BossCombat.RageRemaining;
        level.TogglePause(); yield return new WaitForSecondsRealtime(.15f);
        Check(wallBody.position == pausedRush && wallBoss.BossCombat.RageRemaining == rage,
            "Pause freezes actual rush displacement and the rage duration together");
        level.TogglePause(); yield return Until(() => !wallBoss.BossCombat.IsRushing, 1, "The rush stops at the solid prop");
        Check(wallBody.position.x > 59 && actor.Health == 100, "Swept boss body motion stops before solid cover without charging through it");
        wallBoss.gameObject.SetActive(false); Destroy(wallBoss.gameObject); Destroy(cover); bossUnderTest = boss;

        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(56, 2.05f));
        yield return Until(() => combat.ActiveProjectiles == 1, 4, "A projectile exists before owner component disable");
        boss.enabled = false; yield return null;
        Check(combat.ActiveProjectiles == 0 && !combat.Clock.IsWinding && !combat.Warning.enabled && !FindObjectOfType<HunterBossBolt2D>(),
            "Disabling the boss cancels warnings and destroys all owned projectiles");
        boss.enabled = true; yield return Until(() => combat.Clock.IsWinding, 3, "The boss can acquire and warn again after enable");
        PlaceBossPlayer(new Vector2(72, 2.05f)); yield return new WaitForFixedUpdate();
        Check(!boss.IsAlerted && !combat.Clock.IsWinding && combat.ActiveProjectiles == 0, "Leaving detection cancels an unfinished ability without a delayed hit");
        bool viewport = level.World.Map.UseCameraViewport; level.World.Map.UseCameraViewport = false;
        yield return Warp(new Vector2(6, 2.05f)); yield return Until(() => !boss.gameObject.activeInHierarchy, 4, "The actual boss region unloads");
        Check(boss.Actor.Health == 50 && !combat.Clock.IsWinding && combat.ActiveProjectiles == 0,
            "Region unload retains boss health but removes all attack effects");
        level.World.Map.UseCameraViewport = viewport;
        yield return BossPlace(new Vector2(60, 2.05f), new Vector2(56, 2.05f));
        yield return Until(() => combat.ActiveProjectiles == 1, 4, "The same boss resumes normal-group combat after reload");
        boss.Actor.BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        Check(!boss.Actor.IsAlive && combat.ActiveProjectiles == 0 && !combat.Warning.enabled && !FindObjectOfType<HunterBossBolt2D>() &&
            level.Hunt.State.Stage == HuntStage.BountyAvailable && !level.Hunt.State.SecondWaveStarted,
            "Boss death removes all in-flight effects and retains the real bounty-before-second-wave flow");
        yield return SkillExtraction();
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(56, 2.05f));
        yield return Until(() => level.Hunt.Boss, 3, "The next run creates its formal boss");
        Check(level.Hunt.Boss != boss && level.Hunt.Boss.Actor.Health == 150 && !level.Hunt.Boss.IsEnraged && level.Hunt.Boss.BossCombat.ActiveProjectiles == 0,
            "A new run resets boss health, thresholds and owned attack objects");
        yield return SkillExtraction(); bossUnderTest = null; isolateEliteNpcs = false; RestoreFrozen();
        body.constraints = playerConstraints; level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot; player.HealthRecoveryRate = recovery;
    }
    private IEnumerator BossPlace(Vector2 bossPoint, Vector2 playerPoint, bool move = false)
    {
        bossUnderTest.enabled = false; var rigid = bossUnderTest.GetComponent<Rigidbody2D>();
        rigid.position = bossPoint; bossUnderTest.transform.position = bossPoint; rigid.velocity = Vector2.zero;
        rigid.constraints = move ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.FreezeAll;
        yield return EliteGround(playerPoint); body.constraints = RigidbodyConstraints2D.FreezeAll;
        bossUnderTest.enabled = true; yield return new WaitForFixedUpdate();
    }
    private void PlaceBossPlayer(Vector2 point)
    { body.position = point; player.transform.position = point; body.velocity = Vector2.zero; Physics2D.SyncTransforms(); }
}

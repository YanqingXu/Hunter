using System.Collections;
using System.Linq;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator BarrelChecks()
    {
        var manager = level.SceneInteractions;
        bool huntEnabled = level.Hunt.enabled, lootEnabled = level.ItemLoot.enabled, skillEnabled = level.SkillLoot.enabled;
        float recovery = player.HealthRecoveryRate, delay = manager.BarrelDelay;
        level.Hunt.enabled = false; level.Hunt.ResetMission(); level.ItemLoot.enabled = false; level.SkillLoot.enabled = false;
        player.HealthRecoveryRate = 0; level.ClaimRelief();
        var id = level.Career.Selected.Id;
        while (level.Career.Selected.Items.Count > 0)
        {
            var item = level.Career.Selected.Items[0];
            Check(level.UnequipItem(id, 0, item.Id), "Barrel fixture clears previous equipment through a camp transaction");
        }
        Check(level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.Dynamite) && level.EquipItem(id, HunterItemId.Dynamite) &&
            level.BuyItem(HunterItemId.Dynamite) && level.EquipItem(id, HunterItemId.Dynamite),
            "Barrel fixture buys and equips real knives and two independent explosive charges");
        level.StartRun(); yield return null;
        var barrels = manager.Barrels.ToArray();
        Check(barrels.Length == 3 && System.Array.TrueForAll(barrels, b => !b.IsArmed && !b.HasExploded && b.Detonations == 0),
            "A formal run creates three fresh explosive barrels in the authored extraction region");
        Check(!barrels[0].Available && !barrels[0].GetComponent<Collider2D>().enabled,
            "Unloaded barrels expose neither collision nor a remotely ticking fuse");
        yield return BarrelGround(new Vector2(66.5f, 2.05f));
        Check(System.Array.TrueForAll(barrels, b => b.Available && b.GetComponent<Collider2D>().enabled && b.SkillTeamId != actor.SkillTeamId),
            "The full blast terrain and neutral-team barrel colliders load on the real attack layer");
        barrels[0].BeHit(new SkillHitData { attackValue = 100, soure = actor });
        Check(!barrels[0].IsArmed, "A non-projectile damage callback cannot masquerade as a bullet");
        yield return BarrelGround(new Vector2(68.9f, 2.05f));
        player.SetAimInput(Vector2.right);
        Check(player.TryMelee(), "A real melee attack can be performed beside a barrel");
        yield return Until(() => player.CanInteract, 3, "Barrel melee fixture ends");
        Check(!barrels[0].IsArmed, "The actual melee collision does not ignite a bullet-only fuse");
        yield return BarrelGround(new Vector2(66.5f, 2.05f));
        player.SetAimInput(Vector2.right); Check(player.TrySelectItem(0) && player.TryUseItem(), "A real knife is thrown toward a solid barrel");
        yield return Until(() => !player.IsUsingItem && !level.GetComponentInChildren<HunterThrowable2D>(), 3, "The knife finishes its physical flight");
        Check(player.Items[0].Uses == 2 && !barrels[0].IsArmed, "A thrown knife spends one charge without igniting the barrel");
        var above = ThrowTarget((Vector2)barrels[0].transform.position + Vector2.up * 2.65f);
        var extra = new GameObject("Extra Barrel Target Collider"); extra.transform.SetParent(above.transform, false);
        extra.AddComponent<BoxCollider2D>().size = new Vector2(.25f, .25f);
        int firstHits = 0; above.Damaged += hit => firstHits++;
        var cover = new GameObject("Barrel Chain Cover"); cover.transform.position = new Vector2(71.25f, 4);
        var wall = cover.AddComponent<BoxCollider2D>(); wall.size = new Vector2(.12f, 5);
        Physics2D.SyncTransforms();
        int magazine = player.Weapons.Magazine;
        Check(player.TryShoot() && player.Weapons.Magazine == magazine - 1, "The equipped gun spends a real bullet toward the barrel");
        yield return Until(() => barrels[0].IsArmed, 2, "A real SkillProjectile collision arms the barrel");
        Check(!barrels[0].HasExploded && above.Health == 100, "The first bullet starts a delay without immediate blast damage");
        level.TogglePause(); float progress = barrels[0].Progress;
        yield return new WaitForSecondsRealtime(.15f);
        Check(barrels[0].Progress == progress && !player.TryShoot() && !barrels[0].HasExploded,
            "Pausing freezes the burning fuse and rejects gameplay fire input");
        level.TogglePause();
        yield return Capture("barrel-burning");
        yield return Until(() => player.TryShoot(), 1.2f, "A second real bullet hits before the barrel deadline");
        yield return new WaitForSeconds(.12f);
        Check(barrels[0].IsArmed && barrels[0].Progress > progress + .1f,
            "A second projectile cannot restart or extend the already-burning fuse");
        yield return Until(() => barrels[0].HasExploded, 2, "The delayed barrel detonates");
        Check(barrels[0].Detonations == 1 && above.Health == 40 && firstHits == 1 && !barrels[0].GetComponent<Collider2D>().enabled,
            "One delayed blast damages a multi-collider actor exactly once and removes the barrel obstacle");
        Check(!barrels[1].IsArmed && !barrels[1].HasExploded && !barrels[2].HasExploded,
            "A solid wall interrupts the actual three-barrel chain");
        float health = actor.Health; yield return new WaitForSeconds(.15f);
        Check(firstHits == 1 && barrels[0].Detonations == 1 && actor.Health == health && health == 100,
            "The spent barrel cannot repeat its damage and a distant shooter remains unharmed");
        Destroy(above.gameObject); wall.isTrigger = true; Physics2D.SyncTransforms();
        var chainTarget = ThrowTarget((Vector2)barrels[1].transform.position + Vector2.up * 2.65f);
        int chainedHits = 0; chainTarget.Damaged += hit => chainedHits++;
        yield return BarrelGround(new Vector2(69, 2.05f)); player.SetAimInput(Vector2.right);
        Check(player.TrySelectItem(1) && player.TryUseItem(), "A real dynamite charge is aimed past the spent barrel");
        yield return Until(() => !player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), 3, "Dynamite is committed into flight");
        Check(player.Items.Count == 2 && player.Items[1].Id == HunterItemId.Dynamite,
            "Launching dynamite consumes only its own persisted consumable slot");
        yield return Until(() => barrels[2].HasExploded, 3, "The actual thrown explosive reaches the extraction barrels");
        Check(barrels[1].HasExploded && barrels[1].Detonations == 1 && barrels[2].Detonations == 1 && barrels[0].Detonations == 1,
            "Explosion impact immediately claims adjacent barrels once, including the return edge of the chain");
        Check(chainTarget.Health == 40 && chainedHits == 1,
            "A target outside the thrown explosive radius is damaged once by the propagated barrel blast");
        yield return Capture("barrel-chain-spent");
        Destroy(chainTarget.gameObject); Destroy(cover);
        bool viewport = level.World.Map.UseCameraViewport; level.World.Map.UseCameraViewport = false;
        yield return Warp(new Vector2(6, 2.05f)); yield return Until(() => !barrels[0].Available, 4, "Spent barrel terrain unloads");
        yield return BarrelGround(new Vector2(66.5f, 2.05f));
        Check(System.Array.TrueForAll(barrels, b => b.HasExploded && b.Detonations == 1 && !b.GetComponent<Collider2D>().enabled),
            "Unloading and revisiting the region preserves all three spent states");
        level.World.Map.UseCameraViewport = viewport; RestoreFrozen(); yield return SkillExtraction();

        manager.BarrelDelay = 4; level.StartRun(); yield return null;
        var fresh = manager.Barrels.ToArray();
        Check(fresh[0] != barrels[0] && System.Array.TrueForAll(fresh, b => !b.IsArmed && !b.HasExploded && b.Detonations == 0),
            "Redeploying creates new barrels instead of inheriting a previous run's explosions");
        yield return BarrelGround(new Vector2(66.5f, 2.05f)); player.SetAimInput(Vector2.right);
        Check(player.TryShoot(), "The lifecycle fixture uses another real bullet");
        yield return Until(() => fresh[0].IsArmed, 2, "Fresh barrel is armed before lifecycle checks");
        progress = fresh[0].Progress; fresh[0].enabled = false;
        yield return new WaitForSeconds(.12f);
        Check(fresh[0].Progress == progress && !fresh[0].GetComponent<Collider2D>().enabled,
            "Disabling a burning barrel freezes its fuse and removes its collision");
        fresh[0].enabled = true; yield return null;
        manager.enabled = false; progress = fresh[0].Progress; yield return new WaitForSeconds(.12f);
        Check(fresh[0].IsArmed && fresh[0].Progress == progress && !fresh[0].GetComponent<Collider2D>().enabled,
            "Disabling the scene manager preserves the burning state without invisible damage");
        manager.enabled = true; level.World.Map.UseCameraViewport = false;
        yield return Warp(new Vector2(6, 2.05f)); yield return Until(() => !fresh[0].Available, 4, "Burning barrel region actually unloads");
        progress = fresh[0].Progress; yield return new WaitForSeconds(.2f);
        Check(fresh[0].IsArmed && fresh[0].Progress == progress && !fresh[0].GetComponent<Collider2D>().enabled,
            "An unloaded burning barrel freezes rather than blasting through absent terrain or resetting");
        yield return BarrelGround(new Vector2(66.5f, 2.05f)); level.World.Map.UseCameraViewport = viewport;
        Check(fresh[0].IsArmed && fresh[0].Progress >= progress && fresh[0].Progress < .8f,
            "Reloading resumes the retained fuse with enough time to test explosive interruption");
        var farTarget = ThrowTarget((Vector2)fresh[2].transform.position + Vector2.up * 2.65f);
        Check(player.TrySelectItem(1) && player.SetAimInput(Vector2.right) && player.TryUseItem(),
            "The second genuine dynamite charge starts toward an already-burning chain");
        float releaseDeadline = Time.realtimeSinceStartup + 3;
        while (Time.realtimeSinceStartup < releaseDeadline && (player.IsUsingItem || !level.GetComponentInChildren<HunterThrowable2D>())) yield return null;
        Check(!player.IsUsingItem && level.GetComponentInChildren<HunterThrowable2D>(), "The last dynamite charge enters flight; phase=" + level.Phase +
            ", status=" + player.ItemStatus + ", health=" + actor.Health + ", position=" + body.position + ", progress=" + fresh[0].Progress +
            ", items=" + player.Items.Count + ", using=" + player.IsUsingItem + ", control=" + player.ControlEnabled);
        Check(player.Items.Count == 1 && player.Items[0].Id == HunterItemId.ThrowingKnife,
            "The final explosive is consumed exactly once before its collision");
        yield return Warp(new Vector2(68.6f, 2.05f)); body.constraints = RigidbodyConstraints2D.FreezeAll; actor.RestoreHealth(.1f);
        yield return Until(() => level.Phase == LevelPhase.Dead, 3, "Lethal self-damage settles the real hunter");
        Check(System.Array.TrueForAll(fresh, b => b.HasExploded && b.Detonations == 1) && farTarget.Health == 40,
            "The entire explosion chain and distant enemy damage resolve before lethal player damage pauses the session");
        Check(actor.Health == 0 && level.Career.Selected == null && Time.timeScale == 0,
            "Barrel self-damage uses normal permanent-death and settlement rules");
        Destroy(farTarget.gameObject); body.constraints = RigidbodyConstraints2D.FreezeRotation;
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 5, "Barrel checks return to camp after death");
        manager.BarrelDelay = delay; level.ClaimRelief(); level.StartRun(); yield return null;
        Check(manager.Barrels[0] != fresh[0] && !manager.Barrels[0].HasExploded && !manager.Barrels[0].IsArmed,
            "A new hunter's run cannot inherit a dead hunter's armed or spent barrel");
        yield return SkillExtraction(); RestoreFrozen();
        level.Hunt.enabled = huntEnabled; level.ItemLoot.enabled = lootEnabled; level.SkillLoot.enabled = skillEnabled; player.HealthRecoveryRate = recovery;
    }
    private IEnumerator BarrelGround(Vector2 position)
    {
        yield return Warp(position);
        yield return Until(() => Guard(3), 4, "The asynchronous extraction guard loads before fixture isolation");
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        {
            if (guard.enabled) Freeze(guard);
            var guardBody = guard.GetComponent<Rigidbody2D>();
            if (guardBody.position.x > 63) { guardBody.position = new Vector2(62, 2.05f); guard.transform.position = guardBody.position; }
        }
        Physics2D.SyncTransforms(); yield return Warp(position);
        yield return Until(() => player.CanInteract && player.IsGrounded && level.SceneInteractions.Barrels[0].Available, 3, "Player stands beside the loaded barrel region");
        actor.RestoreHealth();
    }
}

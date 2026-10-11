using System;
using System.Collections;
using System.Linq;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private SkillProjectile[] PlayerBullets()
    { return SkillProjectile.Active.Where(p => p && p.Alive && ReferenceEquals(p.Context.Caster, actor)).ToArray(); }
    private IEnumerator BallisticFire()
    {
        if (player.Weapons.Magazine == 0)
        {
            yield return Until(() => player.TryReload(), 3, "Reload an empty ballistic fixture magazine");
            yield return Until(() => player.CanInteract, 3, "Complete the real ballistic fixture reload");
        }
        player.SetAimInput(Vector2.right);
        yield return Until(() => player.TryShoot(), 3, "A real cartridge shot is accepted");
    }
    private IEnumerator BallisticFlightEnds()
    { yield return new WaitForSeconds(.12f); yield return Until(() => player.CanInteract && PlayerBullets().Length == 0, 3, "The real cast and its projectiles finish"); }
    private HunterPenetrableProp2D BallisticCover(Vector2 position, int kind)
    {
        var root = new GameObject("Ballistic fixture cover"); root.transform.position = position;
        root.AddComponent<BoxCollider2D>().size = new Vector2(.4f, 1.6f);
        var prop = root.AddComponent<HunterPenetrableProp2D>(); prop.Kind = kind; Physics2D.SyncTransforms(); return prop;
    }
    private IEnumerator BallisticChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        var rifleRound = player.Guns[0].DefaultRound; var pistolRound = player.Guns[1].DefaultRound;
        var pistolType = player.Guns[1].AmmoType; var ammunition = player.Ammunition;
        level.ItemLoot.enabled = level.SkillLoot.enabled = false; level.Elites.enabled = false; isolateEliteNpcs = true;
        level.ClaimRelief(); yield return RecruitDualGunFixture();
        if (!level.Career.Selected.Rifle) { if (level.Career.Rifles == 0) level.BuyEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Rifle); }
        if (!level.Career.Selected.Pistol) { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
        var manager = level.SceneInteractions; manager.BallisticCoverEnabled = true;
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(29, 2.05f)); yield return null;
        Check(manager.Covers.Count == 2 && manager.Covers.All(c => c.GetComponent<Collider2D>().enabled) &&
            player.Weapons.Round.PenetrationPower == 2 && player.Weapons.Damage == 25,
            "Formal deployment loads two visible solid wooden covers and the authored penetrating rifle cartridge");
        var covers = manager.Covers.ToArray(); var target = ItemTarget(new Vector2(35, 2.05f));
        int before = player.Weapons.Magazine;
        yield return BallisticFire(); yield return Until(() => target.Health < 100, 3, "Rifle traverses both production covers");
        Check(Mathf.Abs(target.Health - 85.9375f) < .001f && player.Weapons.Magazine == before - 1,
            "One real rifle cartridge passes two designated props and deals 25 x .75 x .75 damage to the target behind them");
        yield return BallisticFlightEnds(); target.RestoreHealth();
        var extra = new GameObject("Second collider on the same logical prop"); extra.transform.SetParent(covers[0].transform, false);
        extra.transform.localPosition = Vector3.right * .05f; extra.AddComponent<BoxCollider2D>().size = new Vector2(.4f, 1.6f); Physics2D.SyncTransforms();
        yield return BallisticFire(); yield return Until(() => target.Health < 100, 3, "Compound cover shot reaches the target");
        Check(Mathf.Abs(target.Health - 85.9375f) < .001f,
            "Overlapping colliders on one physical cover do not consume another penetration or reduce damage twice");
        yield return BallisticFlightEnds(); target.RestoreHealth();
        var third = BallisticCover(new Vector2(33.5f, 2.8f), 1);
        yield return BallisticFire(); yield return BallisticFlightEnds();
        Check(target.Health == 100, "A third distinct wood prop stops a real rifle projectile before the target");
        Destroy(third.gameObject); yield return null; covers[0].Kind = 2; Physics2D.SyncTransforms();
        yield return BallisticFire(); yield return BallisticFlightEnds();
        Check(target.Health == 100, "An explicitly marked but unlisted prop kind blocks the bullet even with penetration power available");
        covers[0].Kind = 1;
        var wall = new GameObject("Ordinary solid ballistic wall"); wall.transform.position = new Vector2(31.5f, 2.8f);
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.3f, 1.6f); Physics2D.SyncTransforms();
        yield return BallisticFire(); yield return BallisticFlightEnds();
        Check(target.Health == 100, "An unmarked solid wall remains blocking between otherwise penetrable covers");
        Destroy(wall); yield return null;
        Check(player.TrySwitchWeapon(1) && player.Weapons.Round.PenetrationPower == 0, "The actual switch action equips the pistol's separate nonpenetrating cartridge");
        yield return BallisticFire(); yield return BallisticFlightEnds();
        Check(target.Health == 100, "A pistol bullet cannot pass the same production wood that admitted the rifle");
        Check(player.TrySwitchWeapon(0), "The player can switch back to its original rifle after the blocked pistol shot");
        var front = ItemTarget(new Vector2(34, 2.05f));
        // Refill the magazine normally; the preceding checks spent the six-shot rifle magazine.
        if (player.Weapons.Magazine == 0) { yield return Until(() => player.TryReload(), 3, "Reload penetration fixture"); yield return Until(() => player.CanInteract, 3, "Reload finishes"); }
        yield return BallisticFire(); yield return Until(() => front.Health < 100, 3, "Bullet reaches the first actor behind the covers");
        Check(Mathf.Abs(front.Health - 85.9375f) < .001f && target.Health == 100,
            "Prop penetration does not grant additional actor piercing: the first actor still stops the bullet");
        yield return BallisticFlightEnds(); yield return Capture("ballistics-wood-penetration");
        Destroy(front.gameObject); Destroy(target.gameObject); Destroy(extra);
        yield return null;
        // Create the target first so contact creation order cannot substitute for a wall-priority rule.
        target = ItemTarget(new Vector2(body.position.x + .65f, 2.05f));
        var overlap = BallisticCover(new Vector2(body.position.x + .65f, 2.8f), 1);
        yield return BallisticFire(); yield return Until(() => target.Health < 100, 3, "A spawn-overlap shot resolves cover and target in order");
        Check(Mathf.Abs(target.Health - 81.25f) < .001f,
            "When a projectile starts inside both wood and a target, solid-cover traversal precedes damage regardless of object creation order");
        yield return BallisticFlightEnds(); Destroy(overlap.gameObject); Destroy(target.gameObject);
        yield return EliteGround(new Vector2(28.8f, 2.05f));
        player.SetSprint(true); player.SetInput(1, true, true);
        yield return Until(() => body.position.x > 33.3f, 3, "The player jumps across the two production wooden covers");
        player.SetInput(0, false, false); player.SetSprint(false);
        yield return Until(() => player.IsGrounded, 3, "The player lands beyond the cover pair");
        Check(body.position.x > 33.3f && actor.IsAlive, "The new solid cover pair leaves a traversable route using normal jump and sprint input");
        string identity = covers[0].LogicalId; bool viewport = level.World.Map.UseCameraViewport; level.World.Map.UseCameraViewport = false;
        yield return EliteGround(new Vector2(77, 2.05f));
        yield return Until(() => !covers[0].GetComponent<Collider2D>().enabled, 4, "Unloaded wood loses its physical collider");
        Check(covers[0].GetComponentsInChildren<SpriteRenderer>().All(s => !s.enabled), "Wood outside loaded terrain has no invisible blocking collider or leftover visuals");
        yield return EliteGround(new Vector2(29, 2.05f)); yield return null;
        Check(covers[0].GetComponent<Collider2D>().enabled && covers[0].LogicalId == identity,
            "Loading the same terrain restores its cover with the same logical identity");
        level.World.Map.UseCameraViewport = viewport;
        yield return SkillExtraction(); yield return null;
        Check(covers.All(c => !c.GetComponent<Collider2D>().enabled), "Returning to camp hides and disables formal cover collisions");

        player.Guns[0].DefaultRound = new HunterRoundDefinition { Pellets = 3, Damage = 8, SpreadAngle = 20, Distance = 8,
            PenetrationPower = 2, DamageRetainedPerProp = .5f, PenetrablePropKinds = new[] { 1 } };
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        Check(manager.Covers[0].LogicalId != identity, "A new deployment creates fresh logical prop identities");
        var a = BallisticCover(new Vector2(8, 2.8f), 1); var b = BallisticCover(new Vector2(9, 2.8f), 1);
        target = ItemTarget(new Vector2(11, 2.05f)); target.GetComponent<BoxCollider2D>().size = new Vector2(.65f, 4);
        int hits = 0; float totalDamage = 0; target.Damaged += hit => { hits++; totalDamage += hit.attackValue; }; Physics2D.SyncTransforms();
        before = player.Weapons.Magazine; yield return BallisticFire();
        player.SetAimInput(Vector2.up); player.Guns[0].DefaultRound.Pellets = 64; player.Guns[0].DefaultRound.Damage = 999;
        yield return Until(() => PlayerBullets().Length == 3, 2, "Three individual pellets launch from the accepted shot");
        var bullets = PlayerBullets(); var angles = bullets.Select(p => { var d = SkillFacing2D.Forward(p.Motion.Pose.Rotation, SkillSpace.TwoD); return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; }).OrderBy(x => x).ToArray();
        Check(Mathf.Abs(angles[0] + 10) < .1f && Mathf.Abs(angles[1]) < .1f && Mathf.Abs(angles[2] - 10) < .1f && player.Weapons.Magazine == before - 1,
            "One spent cartridge launches three independently directed pellets across the configured 20-degree fan despite later aiming and authored-setting changes");
        level.TogglePause(); var ages = bullets.Select(p => p.Motion.Age).ToArray(); var damage = bullets.Select(p => p.CurrentDamage).ToArray();
        yield return new WaitForSecondsRealtime(.15f);
        Check(bullets.Select((p, i) => p.Alive && p.Motion.Age == ages[i] && p.CurrentDamage == damage[i]).All(v => v) && target.Health == 100,
            "Pause freezes every pellet's motion, penetration state and future damage");
        level.TogglePause(); yield return Capture("ballistics-pellet-fan");
        yield return Until(() => hits == 3, 3, "All three independently attenuated pellets hit");
        Check(Mathf.Abs(target.Health - 94) < .001f && Mathf.Abs(totalDamage - 6) < .001f,
            "Each pellet has its own prop ledger and damage calculation: three pellets each deal 8 x .5 x .5 damage");
        yield return BallisticFlightEnds(); yield return Capture("ballistics-pellet-damage");
        Destroy(a.gameObject); Destroy(b.gameObject); Destroy(target.gameObject); yield return SkillExtraction();

        player.Guns[0].DefaultRound = new HunterRoundDefinition { Damage = 11, Distance = 12 };
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        target = ItemTarget(new Vector2(6, 13)); player.SetAimInput(Vector2.up);
        yield return Until(() => player.TryShoot(), 3, "The snapshot fixture fires into an unobstructed vertical corridor");
        yield return Until(() => player.TrySwitchWeapon(1), 2, "Switch weapons after the accepted shot's animation ends");
        Check(PlayerBullets().Length == 1 && player.Weapons.Damage == 15, "A real switch can occur while the previous cartridge is still in flight");
        yield return Until(() => target.Health < 100, 2, "The older shot reaches the distant target after switching");
        Check(target.Health == 89, "Switching to the pistol cannot change the earlier projectile's 11-damage cartridge snapshot");
        Destroy(target.gameObject); yield return BallisticFlightEnds(); yield return SkillExtraction();

        player.Guns[0].DefaultRound = new HunterRoundDefinition { Damage = 11, Distance = 4 };
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        target = ItemTarget(new Vector2(6, 11)); player.SetAimInput(Vector2.up);
        yield return Until(() => player.TryShoot(), 3, "Fire a short-range cartridge toward a distant vertical target"); yield return BallisticFlightEnds();
        Check(target.Health == 100, "A cartridge's four-unit range ends before a farther target even though the shared skill asset has a longer authored range");
        target.transform.position = new Vector2(6, 6); Physics2D.SyncTransforms();
        yield return Until(() => player.TryShoot(), 3, "Fire the short-range cartridge toward a reachable vertical target");
        yield return Until(() => target.Health < 100, 2, "The same cartridge reaches an in-range target");
        Check(target.Health == 89, "The shorter cartridge still deals its own damage to a reachable target");
        Destroy(target.gameObject); yield return BallisticFlightEnds(); yield return SkillExtraction();
        player.Guns[0].DefaultRound = rifleRound; player.Guns[1].DefaultRound = pistolRound;
        player.Guns[1].AmmoType = HunterAmmoType.Shotgun;
        player.Ammunition = new[] { new HunterAmmoDefinition { MaximumTotal = 8, RefillAmount = 1 },
            new HunterAmmoDefinition { Type = HunterAmmoType.Shotgun, MaximumTotal = 10, RefillAmount = 2 } };
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        for (int i = 0; i < 2; i++) { yield return BallisticFire(); yield return BallisticFlightEnds(); }
        Check(player.TrySwitchWeapon(1), "The two-caliber fixture switches to the second actual weapon");
        for (int i = 0; i < 3; i++) { yield return BallisticFire(); yield return BallisticFlightEnds(); }
        Check(player.Weapons.Total(HunterAmmoType.Standard) == 6 && player.Weapons.Total(HunterAmmoType.Shotgun) == 7,
            "Actual shots debit only their own ammo type, counting loaded and reserve cartridges together");
        var supply = FindObjectsOfType<AmmoSupply2D>().First(s => s.gameObject.activeInHierarchy && !s.Used && s.transform.position.x < 20);
        yield return EliteGround(new Vector2(supply.transform.position.x, 2.05f));
        Check(player.TryUseNearbySupply() && supply.Used && player.Weapons.Total(HunterAmmoType.Standard) == 7 && player.Weapons.Total(HunterAmmoType.Shotgun) == 9,
            "A real supply box replenishes both carried ammo types with each type's own configured amount");
        Check(!player.TryUseNearbySupply() && player.Weapons.Total(HunterAmmoType.Standard) == 7 && player.Weapons.Total(HunterAmmoType.Shotgun) == 9,
            "The consumed supply cannot repeat either caliber's award");
        yield return SkillExtraction(); player.Guns[1].AmmoType = pistolType; player.Ammunition = ammunition;
        manager.BallisticCoverEnabled = false; level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot;
        isolateEliteNpcs = false; RestoreFrozen();
    }
}

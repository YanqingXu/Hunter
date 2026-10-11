using System;
using System.Collections;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator LadderChecks()
    {
        bool huntEnabled = level.Hunt.enabled, lootEnabled = level.ItemLoot.enabled, skillEnabled = level.SkillLoot.enabled;
        float recovery = player.HealthRecoveryRate;
        level.Hunt.enabled = false; level.ItemLoot.enabled = false; level.SkillLoot.enabled = false; player.HealthRecoveryRate = 0;
        level.Hunt.ResetMission(); level.ClaimRelief();
        yield return RecruitDualGunFixture();
        Check(level.Career.Selected != null && level.BuyItem(HunterItemId.FirstAid) && level.EquipItem(level.Career.Selected.Id, HunterItemId.FirstAid),
            "Ladder fixture obtains a real hunter and medical equipment through camp transactions");
        if (!level.Career.Selected.Rifle) { level.BuyEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Rifle); }
        Check(level.Career.Selected.Rifle && level.Career.Selected.Pistol, "Ladder fixture carries two real guns for exclusive-action checks");
        level.StartRun(); yield return null;
        var ladder = level.SceneInteractions.Ladder;
        Check(ladder && !player.TryAttachLadder(ladder), "A production ladder exists but cannot pull a remote player across the map");
        yield return LadderGround(ladder.Bottom);
        Check(ladder.Available && player.NearbyLadder == ladder && player.NearbyLootHint().Contains("梯子"),
            "The authored raised route exposes a loaded unobstructed ladder and its interaction prompt");
        Check(player.TrySetProne(true) && !player.TryAttachLadder(ladder) && player.TrySetProne(false),
            "Climbing requires a standing posture and cannot expand a prone body inside a ladder");
        Check(player.TryShoot(), "Ladder fixture creates a partially spent magazine using a real shot");
        yield return Until(() => player.CanInteract, 3, "Ladder setup shot finishes");
        Check(player.TryReload() && !player.TryAttachLadder(ladder), "An active reload rejects attachment without cancelling the reload");
        yield return Until(() => player.CanInteract, 3, "Ladder setup reload finishes");
        Check(player.TryShoot(), "Ladder fixture restores a reloadable magazine for blocked-action assertions");
        yield return Until(() => player.CanInteract, 3, "Second ladder setup shot finishes");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 }); yield return LadderGround(ladder.Bottom);
        float originalGravity = body.gravityScale; body.gravityScale = 4.25f;
        float gravity = body.gravityScale, staminaBefore = player.Stamina, health = actor.Health;
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve, uses = player.Items[0].Uses;
        Vector2 aim = player.AimDirection;
        Check(player.TryInteract() && player.Ladder == ladder && body.gravityScale == 0 && body.velocity == Vector2.zero && !player.CanInteract,
            "The normal interaction attaches to the physical ladder, clears motion and suspends gravity");
        Func<bool>[] blocked = { () => player.TryShoot(), () => player.TryMelee(), () => player.TryReload(), () => player.TrySwitchWeapon(1),
            () => player.TrySelectItem(0), () => player.TryUseItem(), () => player.TryUseOnceSkill(), () => player.TryUseNearbySupply(),
            () => player.TryInteract(), () => player.TrySetProne(true), () => player.SetAimInput(Vector2.down) };
        string[] names = { "shoot", "melee", "reload", "switch gun", "select item", "use medical item", "once skill", "ammo supply", "repeat interact", "prone", "aim" };
        for (int i = 0; i < blocked.Length; i++) Check(!blocked[i]() && player.IsOnLadder, "Ladder ownership ignores " + names[i] + " without detaching");
        player.SetSprint(true); player.SetInput(0, true, true); level.Hunt.SetTouchInvestigation(true, Vector2.right);
        yield return new WaitForSeconds(.1f);
        Check(player.IsOnLadder && !player.IsSprinting && !level.Hunt.IsSpecialView && !player.IsUsingItem && !player.Weapons.IsReloading &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve && player.Items[0].Uses == uses && actor.Health == health &&
            player.Stamina == staminaBefore && player.AimDirection == aim && Mathf.Abs(body.position.y - ladder.Bottom.y) < .01f,
            "Blocked actions consume no resources, cannot jump or investigate, and do not change aim");
        player.SetDirectionalInput(Vector2.up, true, true);
        yield return Until(() => body.position.y >= 3, 2, "The real body climbs above the lower endpoint"); player.CancelInput();
        Vector2 middle = body.position;
        Check(player.IsOnLadder && Mathf.Abs(middle.x - ladder.Bottom.x) < .001f && middle.y < ladder.Top.y,
            "Vertical input climbs the fixed rail without horizontal movement or a jump impulse");
        yield return new WaitForSeconds(.15f);
        Check(body.position == middle && body.gravityScale == 0, "Releasing direction holds the player on the ladder");
        level.TogglePause(); player.SetDirectionalInput(Vector2.down, true, true);
        yield return new WaitForSecondsRealtime(.15f);
        Check(level.Phase == LevelPhase.Paused && player.IsOnLadder && body.position == middle && !player.TryShoot(),
            "Pause preserves attachment and discards climbing and attack input");
        level.TogglePause(); yield return new WaitForSeconds(.1f);
        Check(body.position == middle, "Resuming requires fresh input rather than replaying the paused direction");
        player.SetDirectionalInput(Vector2.up, false, false); player.SendMessage("OnApplicationFocus", false);
        yield return new WaitForSeconds(.1f);
        Check(player.IsOnLadder && body.position == middle, "Losing focus clears climbing input while keeping a safe stationary attachment");
        player.SendMessage("OnApplicationFocus", true);
        player.SetDirectionalInput(new Vector2(float.NaN, 1), false, false); yield return new WaitForSeconds(.1f);
        Check(body.position == middle && player.IsOnLadder, "Invalid directional input cannot move or corrupt the attached body");
        player.SetDirectionalInput(Vector2.right, false, false); yield return new WaitForSeconds(.1f); player.CancelInput();
        Check(player.IsOnLadder && body.position == middle && !string.IsNullOrEmpty(player.LadderStatus),
            "The solid raised platform blocks a sideways exit through its wall");
        var wall = new GameObject("Ladder Side Sweep Wall"); wall.transform.position = new Vector3(ladder.Bottom.x - .55f, middle.y + .75f, 0);
        var wallCollider = wall.AddComponent<BoxCollider2D>(); wallCollider.size = new Vector2(.1f, 1.5f); Physics2D.SyncTransforms();
        player.SetDirectionalInput(Vector2.left, false, false); yield return new WaitForSeconds(.1f); player.CancelInput();
        Check(player.IsOnLadder && body.position == middle, "A thin wall blocks the swept exit path even when the destination body fits beyond it");
        wallCollider.isTrigger = true; Physics2D.SyncTransforms();
        player.SetDirectionalInput(Vector2.left, false, false); yield return new WaitForFixedUpdate(); yield return null;
        Debug.Log("LADDER SIDE EXIT: " + LadderSnapshot());
        yield return Until(() => !player.IsOnLadder, 1, "Trigger-only side exit: " + LadderSnapshot()); player.CancelInput();
        Check(!player.IsOnLadder && Mathf.Abs(body.gravityScale - gravity) < .001f && body.position.x < middle.x - 1,
            "A trigger permits directional dismount and restores the exact original gravity");
        Destroy(wall); yield return LadderGround(ladder.Bottom);
        Check(player.TryInteract(), "The reusable ladder accepts a second visit after safe dismount");
        var ceiling = new GameObject("Ladder Ceiling"); ceiling.transform.position = new Vector3(ladder.Bottom.x, 4.6f, 0);
        ceiling.AddComponent<BoxCollider2D>().size = new Vector2(.8f, .1f); Physics2D.SyncTransforms();
        player.SetDirectionalInput(Vector2.up, false, false); yield return new WaitForSeconds(.65f); player.CancelInput();
        Check(player.IsOnLadder && body.position.y < 3.2f && player.LadderStatus == "攀爬方向受阻",
            "The standing body stops below a ceiling instead of climbing through it");
        Destroy(ceiling); yield return null;
        var exitWall = new GameObject("Ladder Upper Exit Wall"); exitWall.transform.position = ladder.TopExit + Vector2.up * .8f;
        var exitCollider = exitWall.AddComponent<BoxCollider2D>(); exitCollider.size = new Vector2(.7f, 1.6f); Physics2D.SyncTransforms();
        player.SetDirectionalInput(Vector2.up, false, false); yield return new WaitForSeconds(.7f); player.CancelInput();
        Check(player.IsOnLadder && Mathf.Abs(body.position.y - ladder.Top.y) < .01f && body.gravityScale == 0 && player.LadderStatus.Contains("出口受阻"),
            "A blocked upper exit retains the player at the top without entering the obstacle or restoring gravity");
        yield return Capture("ladder-blocked-exit");
        exitCollider.isTrigger = true; Physics2D.SyncTransforms();
        player.SetDirectionalInput(Vector2.up, false, false);
        yield return Until(() => !player.IsOnLadder, 1, "Trigger-only upper exit: " + LadderSnapshot()); player.CancelInput();
        Check(!player.IsOnLadder && Mathf.Abs(body.position.x - ladder.TopExit.x) < .01f && body.position.y > 3.95f && body.position.y < 4.1f && body.gravityScale == gravity,
            "Clearing the upper exit lets the real player step onto the authored platform");
        Destroy(exitWall); yield return Until(() => player.IsGrounded, 2, "Player lands on the authored raised route");
        Check(player.NearbyLadder == ladder && player.TryInteract(), "The upper landing also provides a reachable mount for descent");
        player.SetDirectionalInput(Vector2.down, false, false); yield return Until(() => !player.IsOnLadder, 2, "Player descends to the bottom"); player.CancelInput();
        Check(Mathf.Abs(body.position.x - ladder.Bottom.x) < .01f && body.position.y < 2.1f && body.gravityScale == gravity,
            "Downward endpoint exit returns to the lower floor and restores gravity");
        yield return LadderGround(ladder.Bottom);
        Check(player.TryInteract(), "Disable checks start with a real attachment"); ladder.enabled = false;
        Check(!player.IsOnLadder && body.gravityScale == gravity && !player.TryAttachLadder(ladder), "Disabling the ladder restores gravity and rejects remounts");
        ladder.enabled = true; yield return LadderGround(ladder.Bottom); Check(player.TryInteract(), "Re-enabled ladder can be reused");
        level.SceneInteractions.enabled = false;
        Check(!player.IsOnLadder && body.gravityScale == gravity && !player.NearbyLadder, "Disabling the interaction manager also releases its ladder owner");
        level.SceneInteractions.enabled = true; yield return LadderGround(ladder.Bottom); Check(player.TryInteract(), "Player-disable fixture attaches");
        player.enabled = false; Check(!player.IsOnLadder && body.gravityScale == gravity, "Disabling the player restores gravity before simulation continues");
        player.enabled = true; yield return LadderGround(ladder.Bottom); Check(player.TryInteract(), "Damage fixture attaches");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(!player.IsOnLadder && body.gravityScale == gravity && player.IsHurt && body.velocity.y > 0,
            "Real damage detaches before applying the original hit impulse");
        yield return LadderGround(ladder.Bottom);
        hud.ForceTouchControls = true; yield return null; yield return null;
        var touch = hud.TouchControls;
        Check(touch.FindControl("Sprint").Label.text == "交互", "The touch sprint button becomes interaction near the ladder");
        Tap(touch.FindControl("Sprint")); yield return null;
        Check(player.IsOnLadder && touch.FindControl("Sprint").Label.text == "攀爬中", "The contextual touch button mounts the physical ladder");
        magazine = player.Weapons.Magazine;
        TouchDown(touch.Stick, 810, Vector2.up); TouchDown(touch.FindControl("Shoot"), 811); TouchDown(touch.FindControl("Investigate"), 812);
        yield return new WaitForSeconds(.24f); TouchUp(touch.Stick, 810); TouchUp(touch.FindControl("Shoot"), 811); TouchUp(touch.FindControl("Investigate"), 812);
        Check(player.IsOnLadder && body.position.y > ladder.Bottom.y + .3f && player.Weapons.Magazine == magazine && !level.Hunt.IsSpecialView,
            "Simultaneous touch gestures climb while shooting and investigation remain blocked");
        yield return Capture("ladder-touch");
        var hint = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Weapon Hint");
        Check(hint && hint.text.Contains("攀爬"), "The visible HUD explains directional climbing rather than advertising weapon actions");
        middle = body.position; TouchDown(touch.Stick, 813, Vector2.up); touch.SendMessage("OnApplicationPause", true);
        yield return new WaitForSeconds(.1f);
        Check(player.IsOnLadder && body.position == middle && !touch.Stick.Held, "Application suspension drops touch gestures and preserves the stationary ladder attachment");
        touch.SendMessage("OnApplicationPause", false); yield return null;
        Tap(touch.FindControl("Pause")); yield return null;
        Check(level.Phase == LevelPhase.Paused && player.IsOnLadder, "Touch pause remains available during exclusive climbing");
        level.TogglePause(); yield return null;
        TouchDown(touch.Stick, 814, Vector2.left); yield return new WaitForSeconds(.1f); TouchUp(touch.Stick, 814);
        Check(!player.IsOnLadder && body.gravityScale == gravity, "A directional stick gesture exits sideways through clear space");
        hud.ForceTouchControls = false; yield return null;
        yield return LadderGround(ladder.Bottom); Check(player.TryInteract(), "Reset fixture mounts the ladder");
        // The camera's padding intentionally retains this central chunk even from the map's edge.
        // Isolate target-based streaming to exercise actual unload/reload without changing production bounds.
        bool viewport = level.World.Map.UseCameraViewport; level.World.Map.UseCameraViewport = false;
        yield return Warp(new Vector2(78.5f, 2.05f));
        Check(!player.IsOnLadder && body.gravityScale == gravity, "A real position reset releases climbing and restores physics");
        yield return Until(() => !ladder.Available, 4, "Moving away unloads the fixed ladder's map region");
        Check(!player.TryAttachLadder(ladder), "An unloaded ladder cannot be entered through a direct API call");
        level.World.Map.UseCameraViewport = viewport;
        yield return LadderGround(ladder.Bottom); Check(player.TryInteract(), "Reloading the streamed region restores the same reusable ladder");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && !player.IsOnLadder && body.gravityScale == gravity,
            "Permanent death on a ladder releases exclusive control and restores gravity");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 5, "Ladder checks return to camp");
        level.ClaimRelief(); level.StartRun(); yield return null;
        Check(level.SceneInteractions.Ladder != ladder && !player.IsOnLadder && body.gravityScale == gravity,
            "A new run owns a new ladder and cannot inherit an old attachment");
        yield return SkillExtraction();
        level.Hunt.enabled = huntEnabled; level.ItemLoot.enabled = lootEnabled; level.SkillLoot.enabled = skillEnabled;
        player.HealthRecoveryRate = recovery; body.gravityScale = originalGravity; RestoreFrozen();
    }
    private IEnumerator LadderGround(Vector2 position)
    {
        yield return Warp(position);
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        {
            if (guard.enabled) Freeze(guard);
            // An alive frozen guard is still a valid solid obstacle. Move the fixture aside, not through it.
            var guardBody = guard.GetComponent<Rigidbody2D>(); float x = level.SceneInteractions.Ladder.Bottom.x;
            if (Mathf.Abs(guardBody.position.x - x) < 4)
            {
                guardBody.position = new Vector2(x - 4, 2.05f); guard.transform.position = guardBody.position;
            }
        }
        Physics2D.SyncTransforms();
        yield return Warp(position);
        yield return Until(() => player.CanInteract && player.IsGrounded, 3, "Standing beside the real ladder");
        yield return new WaitForSeconds(.1f);
    }
    private string LadderSnapshot()
    {
        var result = new System.Text.StringBuilder("attached=" + player.IsOnLadder + ", pos=" + body.position.ToString("F4") +
            ", gravity=" + body.gravityScale + ", status=" + player.LadderStatus + ", keyboard=" + player.ReadKeyboard);
        foreach (var hit in Physics2D.BoxCastAll(body.position + Vector2.up * .75f, new Vector2(.63f, 1.43f), 0, Vector2.left, 1.1f))
            if (hit.collider) result.Append(" | ").Append(hit.collider.name).Append(" trigger=").Append(hit.collider.isTrigger)
                .Append(" distance=").Append(hit.distance).Append(" position=").Append(hit.collider.transform.position.ToString("F3"));
        return result.ToString();
    }
}

using System;
using System.Collections;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator SceneInteractionChecks()
    {
        Check(level.SceneInteractions && level.Phase == LevelPhase.Ready, "Existing levels expose the new scene interaction manager in camp");
        bool huntEnabled = level.Hunt.enabled, lootEnabled = level.ItemLoot.enabled, skillEnabled = level.SkillLoot.enabled;
        float recovery = player.HealthRecoveryRate;
        level.Hunt.enabled = false; level.ItemLoot.enabled = false; level.SkillLoot.enabled = false; player.HealthRecoveryRate = 0;
        // Isolate interaction timing from enemy AI and random cards; use the real career and streamed map.
        level.Hunt.ResetMission(); level.ClaimRelief();
        Check(level.Career.Selected != null && level.BuyItem(HunterItemId.FirstAid) && level.EquipItem(level.Career.Selected.Id, HunterItemId.FirstAid),
            "Scene fixture equips a real relief hunter and first aid through camp transactions");
        level.StartRun(); yield return null;
        var manager = level.SceneInteractions;
        Check(manager.Points.Count == 2, "A production deployment creates both configured cabinets on the actual map");
        var instant = manager.Points[0]; var delayed = manager.Points[1];
        Check(instant.DelaySeconds == 0 && delayed.DelaySeconds == 3 && !instant.Used && !delayed.Used &&
            !instant.Supply.gameObject.activeSelf && !delayed.Supply.gameObject.activeSelf,
            "Immediate and three-second cabinets begin closed with their real rewards unavailable");
        yield return SceneGround(instant);
        Check(instant.TerrainReady() && instant.InReach(player) && player.NearbySceneInteraction == instant,
            "The immediate cabinet is reachable on solid ground after the broken bridge");
        Check(player.TryShoot(), "The actual weapon spends a round before opening a cabinet");
        yield return Until(() => player.CanInteract, 3, "Cabinet setup shot finishes");
        int magazine = player.Weapons.Magazine, reserve = player.Weapons.Reserve;
        Check(player.TryInteract() && instant.Used && !player.IsSceneInteracting && instant.Supply.gameObject.activeSelf &&
            player.Weapons.Magazine == magazine && player.Weapons.Reserve == reserve,
            "One interaction opens the immediate cabinet in the same frame without granting ammunition itself");
        Check(!player.TryBeginSceneInteraction(instant) && player.TryInteract() && instant.Supply.Used && player.Weapons.Reserve > reserve,
            "The exposed cabinet reward refills through the original one-use ammo supply transaction");
        reserve = player.Weapons.Reserve;
        Check(!player.TryInteract() && player.Weapons.Reserve == reserve, "Repeated cabinet interaction cannot duplicate its ammo reward");
        yield return Warp(new Vector2(74, 2.05f));
        yield return Until(() => !instant.TerrainReady(), 4, "Leaving the cabinet unloads its terrain");
        yield return null;
        Check(instant.Used && instant.Supply.Used && !instant.Supply.gameObject.activeSelf, "Unloaded cabinet state survives while its supply is hidden and unregistered");
        yield return SceneGround(instant); yield return null;
        Check(instant.Used && instant.Supply.Used && instant.Supply.gameObject.activeSelf,
            "Returning to a streamed cabinet preserves its opened and already-consumed states");

        yield return SceneGround(delayed, -1);
        Check(delayed.InReach(player), "The wall fixture begins within actual cabinet range before adding any obstacle");
        var wall = new GameObject("Interaction Acceptance Wall"); wall.transform.position = delayed.transform.position + Vector3.left * .5f;
        wall.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 2); Physics2D.SyncTransforms();
        Debug.Log("CABINET WALL: body=" + body.position.ToString("F4") + ", render=" + player.transform.position.ToString("F4") +
            ", wall=" + wall.transform.position.ToString("F4") + ", cabinet=" + delayed.transform.position + ", reach=" + delayed.InReach(player) +
            ", nearby=" + (player.NearbySceneInteraction ? player.NearbySceneInteraction.name : "none"));
        Check(!delayed.InReach(player) && player.NearbySceneInteraction == null && !player.TryBeginSceneInteraction(delayed),
            "Solid terrain between player and cabinet blocks interaction and contextual targeting");
        wall.GetComponent<BoxCollider2D>().isTrigger = true; Physics2D.SyncTransforms();
        Check(delayed.InReach(player), "A trigger-only marker does not block the interaction ray");
        Destroy(wall); yield return null; yield return SceneGround(delayed);
        player.SetDirectionalInput(Vector2.right * .5f, false, false);
        Check(!player.TryBeginSceneInteraction(delayed), "A held direction prevents starting a stationary countdown");
        player.CancelInput();
        Check(player.TryInteract() && player.SceneInteraction == delayed && !player.CanInteract,
            "Interaction with the closed delayed cabinet starts its exclusive countdown");
        float x = body.position.x;
        player.SetDirectionalInput(new Vector2(.1f, .1f), false, false);
        player.SetAimInput(new Vector2(float.NaN, 0));
        yield return new WaitForSeconds(.2f);
        Check(player.IsSceneInteracting && delayed.Progress > 0 && delayed.Progress < .2f && Mathf.Abs(body.position.x - x) < .01f,
            "Neutral drift and invalid aim cannot cancel the countdown, and the player remains horizontally still");
        float progress = delayed.Progress;
        Check(!player.TryBeginSceneInteraction(delayed) && delayed.Progress == progress,
            "An adapter cannot replace a running interaction by directly starting it again");
        var hint = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Weapon Hint");
        Check(hint && hint.text.Contains("开柜") && hint.text.Contains("%"), "The visible HUD reports countdown progress and interruption guidance");
        yield return Capture("scene-interaction-progress");
        level.TogglePause(); progress = delayed.Progress;
        player.SetDirectionalInput(Vector2.right, true, true);
        Check(!player.TryShoot() && !player.TryUseItem() && !player.TryInteract(), "Paused action attempts cannot escape the scene interaction gate");
        yield return new WaitForSecondsRealtime(.15f);
        Check(player.IsSceneInteracting && delayed.Progress == progress && !delayed.Used,
            "Pause freezes the unfinished interaction and discards paused gameplay input");
        level.TogglePause();
        player.SetDirectionalInput(Vector2.up, false, false);
        Check(!player.IsSceneInteracting && delayed.Progress == 0 && !delayed.Used,
            "Vertical aiming intent cancels the countdown even though it does not move the character");
        player.CancelInput();

        Action[] actions = {
            () => player.TryShoot(), () => player.TryMelee(), () => player.TryReload(),
            () => player.TrySwitchWeapon(0), () => player.TrySelectItem(0), () => player.TryUseItem(),
            () => player.TryUseOnceSkill(), () => player.TryUseNearbySupply(), () => player.TrySetProne(!player.IsProne),
            () => player.SetSprint(true), () => player.SetAimInput(Vector2.left),
            () => player.SetInput(0, true, true)
        };
        string[] names = { "shoot", "melee", "reload", "weapon switch", "item selection", "item use", "once skill", "direct ammo supply", "posture", "sprint", "aim", "jump" };
        for (int i = 0; i < actions.Length; i++)
        {
            player.CancelInput(); actor.RestoreHealth();
            yield return Until(() => player.CanInteract && player.IsGrounded, 4, "Prepare countdown cancellation by " + names[i]);
            Check(player.TryBeginSceneInteraction(delayed), "A fresh countdown begins before " + names[i]);
            actions[i]();
            Check(!player.IsSceneInteracting && !delayed.Used && delayed.Progress == 0,
                "The " + names[i] + " request cancels the whole delay, even when that requested action has nothing to consume");
            player.CancelInput();
        }
        yield return Until(() => player.CanInteract && player.IsGrounded, 4, "Jump cancellation lands");
        if (player.IsProne) player.TrySetProne(false);
        yield return SceneGround(delayed);
        Check(player.TryBeginSceneInteraction(delayed), "Range cancellation starts with a valid close interaction");
        body.position += Vector2.left * 4; Physics2D.SyncTransforms();
        Check(!delayed.InReach(player), "Range validation reads the new physics position without waiting for render interpolation");
        yield return null; yield return null;
        Check(!player.IsSceneInteracting && !delayed.Used && delayed.Progress == 0,
            "External movement out of range cancels the attempt without opening the cabinet");
        yield return SceneGround(delayed);
        Check(player.TryBeginSceneInteraction(delayed), "Damage cancellation starts a new attempt");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(!player.IsSceneInteracting && !delayed.Used, "Real damage immediately cancels scene interaction before hit stun");
        yield return SceneGround(delayed);
        Check(player.TryBeginSceneInteraction(delayed), "Object-disable cancellation starts a new attempt");
        delayed.enabled = false;
        Check(!player.IsSceneInteracting && !player.TryBeginSceneInteraction(delayed), "Disabling a scene object cancels its owner and rejects further starts");
        delayed.enabled = true; yield return null;
        Check(player.TryBeginSceneInteraction(delayed), "The same unused cabinet can be retried after re-enable");
        manager.enabled = false;
        Check(!player.IsSceneInteracting && player.NearbySceneInteraction == null && !player.TryBeginSceneInteraction(delayed),
            "Disabling the scene manager cancels its pending interactions without consuming them");
        manager.enabled = true; yield return null;

        hud.ForceTouchControls = true; yield return null; yield return null;
        var touch = hud.TouchControls;
        Check(touch.FindControl("Sprint").Label.text == "交互", "Near a usable scene object the touch sprint button changes to interaction");
        Tap(touch.FindControl("Sprint")); yield return null;
        Check(player.IsSceneInteracting && !touch.SprintSelected && touch.FindControl("Sprint").Label.text == "取消",
            "The contextual touch button starts the real cabinet countdown rather than toggling sprint");
        TouchDown(touch.Stick, 700, Vector2.right); yield return new WaitForSeconds(.12f); TouchUp(touch.Stick, 700);
        Check(!player.IsSceneInteracting && body.velocity.x > 0 && !delayed.Used, "A real stick gesture cancels the delay and then moves the player");
        yield return new WaitForSeconds(.15f);
        Tap(touch.FindControl("Sprint")); yield return null; Tap(touch.FindControl("Sprint")); yield return null;
        Check(!player.IsSceneInteracting && !delayed.Used, "The contextual cancel button abandons a fresh attempt without reward");
        Tap(touch.FindControl("Interact")); yield return null;
        TouchDown(touch.FindControl("Investigate"), 701); yield return null;
        Check(!player.IsSceneInteracting, "Holding touch investigation interrupts scene interaction before entering that view");
        TouchUp(touch.FindControl("Investigate"), 701); yield return null;
        Tap(touch.FindControl("Interact")); yield return null;
        Check(player.IsSceneInteracting, "Touch focus handling begins with an active countdown");
        touch.SendMessage("OnApplicationFocus", false);
        Check(!player.IsSceneInteracting && !delayed.Used, "Losing touch focus discards an incomplete scene interaction");
        touch.SendMessage("OnApplicationFocus", true); yield return null;
        Tap(touch.FindControl("Interact")); yield return null;
        Check(player.IsSceneInteracting, "A fresh touch interaction starts after focus returns");
        Tap(touch.FindControl("Pause")); yield return null;
        Check(player.IsSceneInteracting && level.Phase == LevelPhase.Paused, "The touch pause command preserves the countdown while clearing gestures");
        level.TogglePause(); yield return null;
        hud.ForceTouchControls = false; yield return null;
        Check(!player.IsSceneInteracting && !delayed.Used, "Switching input modes cancels an unfinished touch interaction");

        yield return SceneGround(delayed);
        Check(player.TryInteract(), "The delayed cabinet can be opened after all interruption attempts");
        yield return Until(() => delayed.Used, 4, "The uninterrupted configured three-second delay finishes");
        Check(!player.IsSceneInteracting && delayed.Progress == 1 && delayed.Supply.gameObject.activeSelf &&
            !delayed.Supply.Used && !player.TryBeginSceneInteraction(delayed), "Completion opens the delayed cabinet once and exposes its separate unused ammo reward");
        yield return Capture("scene-interaction-opened");
        RestoreFrozen(); yield return SkillExtraction();
        Check(instant.Used && delayed.Used && !instant.Supply.gameObject.activeInHierarchy && !delayed.Supply.gameObject.activeInHierarchy,
            "Returning to camp hides opened cabinets without accidentally issuing another reward");
        level.StartRun(); yield return null;
        var next = manager.Points[1];
        Check(next != delayed && !manager.Points[0].Used && !next.Used && !next.Supply.Used && !next.Supply.gameObject.activeSelf,
            "A new production run creates fresh closed cabinets and fresh one-use supplies");
        yield return SceneGround(next); Check(player.TryBeginSceneInteraction(next), "Death cancellation begins a real delayed interaction");
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && !player.IsSceneInteracting && !next.Used && !next.Supply.gameObject.activeInHierarchy,
            "Permanent death cancels the countdown and hides its unclaimed reward");
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 5, "Scene interaction checks return to camp");
        level.Hunt.enabled = huntEnabled; level.ItemLoot.enabled = lootEnabled; level.SkillLoot.enabled = skillEnabled;
        player.HealthRecoveryRate = recovery; RestoreFrozen();
    }
    private IEnumerator SceneGround(SceneInteraction2D point, float offset = 0)
    {
        yield return Warp(new Vector2(point.transform.position.x + offset, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>())
        {
            if (guard.enabled) Freeze(guard);
            var guardBody = guard.GetComponent<Rigidbody2D>();
            if (Mathf.Abs(guardBody.position.x - point.transform.position.x) < 5)
            { guardBody.position = new Vector2(point.transform.position.x - 6, 2.05f); guard.transform.position = guardBody.position; }
        }
        // Reset any displacement received during the first streamed frame, after the NPC corridor is clear.
        yield return Warp(new Vector2(point.transform.position.x + offset, 2.05f));
        yield return Until(() => player.CanInteract && player.IsGrounded, 3, "Stable ground near the real cabinet");
        yield return new WaitForSeconds(.1f);
    }
}

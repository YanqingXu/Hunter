using System;
using System.Collections;
using System.Collections.Generic;
using BigWorld.Gameplay;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator TouchChecks()
    {
        Check(hud.TouchControls && !hud.TouchControls.TouchMode, "Desktop retains its original controls until touch mode is selected");
        if (level.Career.Selected.SkillPoints < 2)
        { level.StartRun(); yield return null; yield return SkillExtraction(); }
        string id = level.Career.Selected.Id;
        Check(level.UnequipItem(id, 0, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.ThrowingKnife) && level.EquipItem(id, HunterItemId.ThrowingKnife) &&
            level.BuyItem(HunterItemId.FirstAid) && level.EquipItem(id, HunterItemId.FirstAid) &&
            level.BuyItem(HunterItemId.HealthShot) && level.EquipItem(id, HunterItemId.HealthShot) &&
            level.BuySkill(id, HunterSkillId.EmergencyBandage), "Touch fixture purchases real tools, medicine and a once skill through camp transactions");
        level.BuyEquipment(HunterEquipment.Rifle); level.ToggleEquipment(HunterEquipment.Rifle);
        Check(level.Career.Selected.Rifle && level.Career.Selected.Pistol, "Touch fixture equips a second gun for the switch button");
        float recovery = player.HealthRecoveryRate, chance = level.SkillLoot.SpawnChance;
        player.HealthRecoveryRate = 0; level.SkillLoot.SpawnChance = 0;
        player.ReadKeyboard = true; level.ReadKeyboard = true;
        hud.ForceTouchControls = true; yield return null;
        var touch = hud.TouchControls;
        Check(touch.TouchMode && !player.ReadKeyboard && !level.ReadKeyboard && !touch.Stick.gameObject.activeInHierarchy,
            "Touch mode owns both keyboard readers and hides gameplay controls in camp");
        level.StartRun(); yield return null; yield return Warp(new Vector2(6, 2.05f));
        foreach (var guard in FindObjectsOfType<PatrolEnemy2D>()) Freeze(guard);
        yield return Until(() => player.CanInteract && player.IsGrounded, 3, "Touch deployment becomes controllable");
        Canvas.ForceUpdateCanvases();
        foreach (var control in touch.GetComponentsInChildren<HunterTouchControl>())
        {
            var data = TouchData(control, 100, Vector2.zero);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Check(hits.Count > 0 && hits[0].gameObject == control.gameObject, "GraphicRaycaster reaches touch control " + control.name);
        }
        var stick = touch.Stick; var shoot = touch.FindControl("Shoot");
        var objective = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Objective");
        var toast = Array.Find(hud.GetComponentsInChildren<Text>(), t => t.name == "Checkpoint Toast");
        Check(objective && objective.text.Contains("按住调查") && !objective.text.Contains(" V ") &&
            toast && toast.text.Contains("点交互") && !toast.text.Contains(" V "), "Mobile mission and deployment hints name visible touch actions");
        yield return Capture("touch-basic");
        var drag = TouchData(stick, 11, Vector2.zero); drag.useDragThreshold = true;
        ExecuteEvents.Execute(stick.gameObject, drag, ExecuteEvents.initializePotentialDrag);
        Check(!drag.useDragThreshold, "The stick disables the EventSystem drag threshold for responsive analog input");
        TouchDown(stick, 11, new Vector2(.6f, .8f));
        TouchDown(stick, 12, -Vector2.one); TouchUp(stick, 12);
        TouchDrag(stick, 12, Vector2.left);
        yield return new WaitForSeconds(.15f);
        Check(stick.Held && Vector2.Distance(stick.Value, new Vector2(.6f, .8f)) < .002f && body.velocity.x > 1.5f &&
            Vector2.Distance(player.AimDirection, new Vector2(.6f, .8f)) < .002f, "A second finger cannot steal or release the moving diagonal stick");
        int magazine = player.Weapons.Magazine;
        TouchDown(shoot, 21); TouchDown(shoot, 22); TouchUp(shoot, 22);
        yield return new WaitForSeconds(.15f);
        Check(shoot.Held && stick.Held && player.Weapons.Magazine == magazine - 1,
            "Holding a second control fires one real shot while the first finger continues moving and aiming");
        yield return new WaitForSeconds(player.Weapons.FireInterval + .15f);
        Check(player.Weapons.Magazine <= magazine - 2, "Holding the fire pointer repeats through the real gun cooldown");
        TouchUp(shoot, 21); TouchUp(stick, 11); yield return new WaitForSeconds(.2f);
        magazine = player.Weapons.Magazine; yield return new WaitForSeconds(.4f);
        Check(!shoot.Held && !stick.Held && player.Weapons.Magazine == magazine && Mathf.Abs(body.velocity.x) < .1f,
            "Matching pointer releases stop movement and repeated fire without leaving keyboard overwrite");
        yield return Until(() => player.CanInteract, 2, "Touch fire animation finishes");
        Tap(touch.FindControl("Reload")); yield return null;
        Check(player.Weapons.IsReloading, "The reload button starts the real weapon reload");
        yield return Until(() => !player.Weapons.IsReloading, 3, "Touch reload finishes");
        int weapon = player.Weapons.ActiveIndex; Tap(touch.FindControl("Weapon")); yield return null;
        Check(player.Weapons.ActiveIndex != weapon, "The switch button changes the equipped gun");
        TouchDown(stick, 31, Vector2.right); TouchDrag(stick, 31, Vector2.right * 3); yield return new WaitForSeconds(.15f);
        Check(Vector2.Distance(stick.Value, Vector2.right) < .002f && Mathf.Abs(body.velocity.x - player.WalkSpeed) < .15f,
            "Dragging outside the stick circle clamps movement to walking speed");
        Tap(touch.FindControl("Sprint")); yield return new WaitForSeconds(.15f);
        Check(touch.SprintSelected && player.IsSprinting && Mathf.Abs(body.velocity.x - player.MoveSpeed) < .15f,
            "The sprint toggle changes actual movement speed independently of the stick");
        TouchUp(stick, 31); Tap(touch.FindControl("Sprint")); yield return null;
        Check(!touch.SprintSelected, "A second sprint tap restores walking mode");
        var resource = (HunterStamina)typeof(PlayerController2D).GetField("stamina", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(player);
        resource.TrySpend(resource.Current - 1); Tap(touch.FindControl("Sprint")); yield return null;
        Check(!touch.SprintSelected && player.Stamina == 1, "Sprint cannot start with exactly one stamina and the rejected toggle spends nothing");
        resource.Reset();
        yield return Warp(new Vector2(6, 2.05f)); yield return new WaitForSeconds(.1f);
        Tap(touch.FindControl("Prone")); yield return null;
        Check(player.IsProne && touch.FindControl("Prone").Label.text == "起身", "The prone button changes collision posture and its caption");
        TouchDown(stick, 32, new Vector2(1, -1)); Tap(touch.FindControl("Sprint")); yield return new WaitForSeconds(.15f);
        Check(player.AimDirection == Vector2.right && !touch.SprintSelected && body.velocity.x > .8f && body.velocity.x < 1.2f,
            "Touch prone movement uses reduced speed, projects downward aim and rejects sprint");
        TouchUp(stick, 32); Tap(touch.FindControl("Prone")); yield return null;
        TouchDown(touch.FindControl("Jump"), 33); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(body.velocity.y > 1 && !player.IsProne, "A jump pointer creates a buffered real jump after standing");
        TouchUp(touch.FindControl("Jump"), 99); Check(touch.FindControl("Jump").Held, "An unrelated finger cannot release the jump hold");
        TouchUp(touch.FindControl("Jump"), 33);
        yield return Until(() => player.IsGrounded && body.velocity.y <= .1f, 3, "Touch jump lands");
        float stamina = player.Stamina; Tap(touch.FindControl("Melee")); yield return null;
        Check(player.Stamina == stamina - player.EffectiveMeleeStaminaCost && actor.Player.IsPlaying, "The melee button starts the real skill and consumes stamina once");
        yield return Until(() => player.CanInteract, 3, "Touch melee finishes");

        // Investigation consumes the stick but keeps the player still and blocks attacks.
        yield return Warp(new Vector2(6, 2.05f));
        TouchDown(touch.FindControl("Investigate"), 40); TouchDown(stick, 41, new Vector2(.6f, .8f));
        magazine = player.Weapons.Magazine; Vector2 before = body.position;
        TouchDown(shoot, 42); yield return new WaitForSeconds(.3f);
        Check(level.Hunt.IsSpecialView && player.Weapons.Magazine == magazine && Mathf.Abs(body.position.x - before.x) < .1f &&
            level.FollowCamera.Target != player.transform && level.FollowCamera.Target.position.y > player.transform.position.y + .3f,
            "Touch investigation pans the real camera with the stick while blocking movement and fire");
        TouchUp(shoot, 42); TouchUp(stick, 41); TouchUp(touch.FindControl("Investigate"), 40); yield return null;
        Check(!level.Hunt.IsSpecialView && level.FollowCamera.Target == player.transform, "Releasing investigation returns camera ownership to the player");
        yield return Warp(level.Hunt.CluePositions[0]);
        Tap(touch.FindControl("Interact")); yield return null;
        Check(level.Hunt.State.HasClue("0"), "The touch interaction button investigates a real nearby clue");
        yield return Warp(new Vector2(6, 2.05f));

        actor.RestoreHealth(.2f);
        Tap(touch.FindControl("NextItem")); yield return null;
        Check(player.ItemIndex == 1, "The item selector moves from the knife to equipped first aid");
        int uses = player.Items[1].Uses; Tap(touch.FindControl("UseItem")); yield return null;
        Check(player.IsUsingItem && player.Items[1].Uses == uses, "Touch use begins preparation without prematurely spending its charge");
        TouchDown(stick, 50, Vector2.right); yield return new WaitForSeconds(.15f);
        Check(body.velocity.x > 1.3f && body.velocity.x < 1.7f, "Moving during touch medical preparation applies the same movement penalty");
        TouchUp(stick, 50);
        Tap(touch.FindControl("Pause")); yield return null;
        Check(level.Phase == LevelPhase.Paused && !stick.Held && !shoot.Held && !touch.SprintSelected && !stick.gameObject.activeInHierarchy,
            "The pause button hides and cancels every gameplay pointer");
        float preparation = player.ItemUseProgress; yield return new WaitForSecondsRealtime(.1f);
        Check(player.ItemUseProgress == preparation && player.Items[1].Uses == uses, "Paused touch preparation freezes time and preserves charges");
        level.TogglePause(); yield return null;
        yield return Until(() => !player.IsUsingItem, 4, "Resumed touch medical preparation completes");
        Check(actor.Health > 20 && player.Items[1].Uses == uses - 1, "The medical button heals and persists exactly one charge after resuming");
        actor.RestoreHealth(.2f); Tap(touch.FindControl("OnceSkill")); yield return null;
        Check(actor.Health == 70 && !level.Career.Selected.HasSkill(HunterSkillId.EmergencyBandage), "Touch once-skill activation heals fifty and removes the skill transactionally");
        Tap(touch.FindControl("OnceSkill")); yield return null;
        Check(actor.Health == 70, "Repeated touch activation cannot reuse the consumed once skill");
        Tap(touch.FindControl("NextItem")); yield return null; Tap(touch.FindControl("NextItem")); yield return null;
        Check(player.ItemIndex == 0, "The next-item button wraps across the equipped consumable to the knife");
        TouchDown(stick, 51, Vector2.up); yield return null;
        var target = ThrowTarget((Vector2)player.transform.position + Vector2.up * 3.8f);
        Tap(touch.FindControl("UseItem")); yield return null; TouchUp(stick, 51);
        Check(player.IsUsingItem, "Touch throwing starts from the vertical direction selected by the stick");
        yield return Until(() => target.Health < 100, 3, "Touch knife reaches the real vertical target");
        Check(target.Health == 70 && player.Items[0].Uses == 2, "Touch throw uses the actual projectile, direction and saved charge");
        Destroy(target.gameObject);

        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 1 });
        Check(player.IsHurt && !touch.CanReceiveInput, "Damage locks gameplay touch actions while the session remains active");
        Tap(touch.FindControl("Pause"));
        Check(level.Phase == LevelPhase.Paused && !stick.Held && !stick.gameObject.activeInHierarchy,
            "The session pause button still works immediately during hit stun");
        level.TogglePause(); yield return Until(() => player.CanInteract, 3, "Hit stun ends after resuming the touch session");

        TouchDown(stick, 61, Vector2.right); TouchDown(shoot, 62); Tap(touch.FindControl("Sprint")); yield return null;
        touch.SendMessage("OnApplicationFocus", false); yield return null;
        magazine = player.Weapons.Magazine;
        Check(!stick.Held && !shoot.Held && !touch.SprintSelected && !touch.CanReceiveInput, "Losing application focus clears movement, fire and sprint ownership");
        touch.SendMessage("OnApplicationFocus", true); yield return new WaitForSeconds(.2f);
        Check(player.Weapons.Magazine == magazine && Mathf.Abs(body.velocity.x) < .1f, "Regaining focus requires a fresh press and never resumes old held actions");
        TouchDown(stick, 63, Vector2.left); touch.SendMessage("OnApplicationPause", true); yield return null;
        Check(!stick.Held && !touch.CanReceiveInput, "Mobile application suspension clears the owned pointer");
        touch.SendMessage("OnApplicationPause", false); yield return null;
        TouchDown(stick, 64, Vector2.right); touch.enabled = false;
        Check(!stick.Held && player.ReadKeyboard && level.ReadKeyboard, "Disabling the adapter restores both original keyboard flags");
        touch.enabled = true; yield return null;
        Check(!player.ReadKeyboard && !level.ReadKeyboard && !stick.Held, "Re-enabling the adapter reacquires input without resurrecting a finger");
        TouchDown(stick, 65, Vector2.right); yield return null;
        touch.SetViewportPreview(new Vector2(1920, 1080), new Rect(80, 30, 1760, 1020));
        Check(!stick.Held && !touch.SprintSelected, "A changed viewport or safe area cancels the prior gesture before layout moves");
        touch.SetViewportPreview(Vector2.zero, default(Rect));
        yield return Capture("touch-landscape", 1280, 720, new Rect(50, 20, 1180, 680));
        yield return Capture("touch-portrait", 1080, 1920, new Rect(40, 80, 1000, 1760));
        yield return Capture("touch-wide", 1920, 840, new Rect(96, 24, 1744, 792));
        TouchDown(stick, 66, Vector2.right); TouchDown(shoot, 67); yield return null;
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = actor.MaxHealth + 1 }); yield return null;
        Check(level.Phase == LevelPhase.Dead && !stick.Held && !shoot.Held && !touch.CanReceiveInput && !stick.gameObject.activeInHierarchy,
            "Real death removes the gameplay controls and discards held pointers");
        hud.ForceTouchControls = false; yield return null;
        Check(player.ReadKeyboard && level.ReadKeyboard && !touch.TouchMode &&
            hud.transform.Find("Game HUD Content/Vitals") && hud.transform.Find("Game HUD Content/Controls").gameObject.activeSelf,
            "Leaving touch mode restores the desktop layout and original keyboard readers");
        player.ReadKeyboard = false; level.ReadKeyboard = false;
        ClickButton("返回营地"); yield return Until(() => level.Phase == LevelPhase.Ready, 5, "Touch checks return to camp");
        player.HealthRecoveryRate = recovery; level.SkillLoot.SpawnChance = chance; RestoreFrozen();
    }

    private PointerEventData TouchData(HunterTouchControl control, int id, Vector2 offset)
    {
        var rect = (RectTransform)control.transform;
        var canvas = control.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center + offset * rect.rect.width * .5f));
        return new PointerEventData(EventSystem.current) { pointerId = id, position = position, button = PointerEventData.InputButton.Left };
    }
    private void TouchDown(HunterTouchControl control, int id, Vector2 offset = default(Vector2))
    { ExecuteEvents.Execute(control.gameObject, TouchData(control, id, offset), ExecuteEvents.pointerDownHandler); }
    private void TouchUp(HunterTouchControl control, int id)
    { ExecuteEvents.Execute(control.gameObject, TouchData(control, id, Vector2.zero), ExecuteEvents.pointerUpHandler); }
    private void TouchDrag(HunterTouchControl control, int id, Vector2 offset)
    { ExecuteEvents.Execute(control.gameObject, TouchData(control, id, offset), ExecuteEvents.dragHandler); }
    private void Tap(HunterTouchControl control) { TouchDown(control, 500); TouchUp(control, 500); }
    private void CheckTouchLayout(Camera camera, Rect safe, string name)
    {
        var controls = hud.TouchControls.GetComponentsInChildren<HunterTouchControl>();
        var bounds = new List<Rect>(); bool contained = true, overlap = false, readable = true;
        foreach (var control in controls)
        {
            var corners = new Vector3[4]; ((RectTransform)control.transform).GetWorldCorners(corners);
            Vector2 min = camera.WorldToScreenPoint(corners[0]), max = camera.WorldToScreenPoint(corners[2]);
            var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            contained &= rect.xMin >= safe.xMin - 1 && rect.yMin >= safe.yMin - 1 && rect.xMax <= safe.xMax + 1 && rect.yMax <= safe.yMax + 1;
            readable &= rect.width >= 60 && rect.height >= 44;
            foreach (var previous in bounds) overlap |= previous.Overlaps(rect);
            bounds.Add(rect);
        }
        Check(controls.Length == 14 && contained && !overlap && readable, name + " keeps fourteen distinct touch targets inside the simulated device safe area");
        var mapRect = TouchScreenRect((RectTransform)hud.RegionMap.transform, camera);
        bool mapClear = hud.RegionMap.gameObject.activeInHierarchy && mapRect.xMin >= safe.xMin - 1 && mapRect.yMin >= safe.yMin - 1 &&
            mapRect.xMax <= safe.xMax + 1 && mapRect.yMax <= safe.yMax + 1;
        foreach (var target in bounds) mapClear &= !target.Overlaps(mapRect);
        foreach (string panelName in new[] { "Vitals", "Run Skills", "Mission", "Weapon", "Run Items" })
        {
            var panel = hud.TouchControls.SafeContent.Find(panelName) as RectTransform;
            mapClear &= panel && !TouchScreenRect(panel, camera).Overlaps(mapRect);
        }
        Check(mapClear, name + " retains the map inside the safe area without overlapping HUD panels or touch controls");
    }
    private static Rect TouchScreenRect(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = camera.WorldToScreenPoint(corners[0]), max = camera.WorldToScreenPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}

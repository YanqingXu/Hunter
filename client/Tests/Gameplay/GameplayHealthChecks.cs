using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BigWorld.Gameplay;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator HealthChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled;
        float delay = player.HealthRecoveryDelay, rate = player.HealthRecoveryRate;
        level.ItemLoot.enabled = level.SkillLoot.enabled = false; level.Elites.enabled = false; isolateEliteNpcs = true;
        level.ClaimRelief(); string first = level.Career.Selected.Id;
        level.SetHealthLayout(first, HunterCareer.DefaultHealthSegments());
        string disk = File.ReadAllText(level.CareerSavePath);
        ClickButton("血段设置"); yield return null;
        Check(hud.IsHealthLayoutOpen && !FindButton("保存血段").interactable && !FindButton("开始行动").interactable,
            "The real camp editor opens for the selected hunter, rejects a no-op save and blocks background deployment");
        ClickButton("合并第 1、2 段"); yield return null;
        Check(FindButton("拆分第 1 段").interactable && FindButton("保存血段").interactable &&
            level.Career.Selected.HealthSegments.SequenceEqual(HunterCareer.DefaultHealthSegments()) && File.ReadAllText(level.CareerSavePath) == disk,
            "Merging through the actual UI changes only the editable draft, not the account or disk");
        ClickButton("取消并返回"); yield return null;
        Check(!hud.IsHealthLayoutOpen && File.ReadAllText(level.CareerSavePath) == disk && FindButton("开始行动").interactable,
            "Cancel discards the draft and restores actionable camp buttons without writing");
        ClickButton("血段设置"); yield return null; ClickButton("合并第 2、3 段"); yield return null;
        var save = FindButton("保存血段");
        using (var locked = new FileStream(level.CareerSavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ClickButton("保存血段"); yield return null;
            Check(hud.IsHealthLayoutOpen && save.interactable && level.CampMessage.StartsWith("保存失败") &&
                level.Career.Selected.HealthSegments.SequenceEqual(HunterCareer.DefaultHealthSegments()) && File.ReadAllText(level.CareerSavePath) == disk,
                "A real locked-file save keeps the health draft open and leaves both account and disk unchanged");
        }
        yield return Capture("health-layout-save-retry");
        ClickButton("保存血段"); yield return null;
        var custom = new float[] { 25, 50, 25 };
        Check(!hud.IsHealthLayoutOpen && level.Career.Selected.HealthSegments.SequenceEqual(custom),
            "Retrying the same save commits the ordered health layout and returns to camp");
        var reload = new HunterCareerStore(level.CareerSavePath, json => JsonUtility.FromJson<HunterCareerData>(json), data => JsonUtility.ToJson(data, true));
        reload.Load(); disk = File.ReadAllText(level.CareerSavePath);
        Check(reload.Current.Selected.HealthSegments.SequenceEqual(custom) && reload.Current.Snapshot().Version == HunterCareer.CurrentVersion,
            "Unity serialization and a new career store recover the committed current-format health layout");
        Check(!level.SetHealthLayout(first, custom) && File.ReadAllText(level.CareerSavePath) == disk,
            "A repeated scene command does not rewrite an unchanged health layout");
        hud.ForceTouchControls = true; yield return null; ClickButton("血段设置"); yield return null;
        var split = FindButton("拆分第 2 段");
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, split.transform.position) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && (hits[0].gameObject == split.gameObject || hits[0].gameObject.transform.IsChildOf(split.transform)),
            "The touch editor's split button is reachable by the actual GraphicRaycaster");
        ExecuteEvents.Execute(split.gameObject, pointer, ExecuteEvents.pointerClickHandler); yield return null;
        Check(!FindButton("拆分第 2 段").interactable && FindButton("合并第 1、2 段").interactable && level.Career.Selected.HealthSegments.SequenceEqual(custom),
            "A touch click splits the draft segment while preserving the saved configuration");
        yield return Capture("health-layout-touch-landscape", 1280, 720, new Rect(40, 20, 1200, 680));
        yield return Capture("health-layout-touch-portrait", 720, 1280, new Rect(20, 40, 680, 1200));
        ClickButton("取消并返回"); hud.ForceTouchControls = false; yield return null; yield return null;
        Check(level.Career.Selected.HealthSegments.SequenceEqual(custom) && File.ReadAllText(level.CareerSavePath) == disk,
            "Canceling a touch-edited draft preserves the same durable hunter layout");

        if (level.Career.HunterCount == 1)
        {
            if (!level.Career.CanRecruit) { level.StartRun(); yield return null; yield return SkillExtraction(); }
            level.RecruitHunter(); yield return null;
        }
        string second = Enumerable.Range(0, level.Career.HunterCount).Select(i => level.Career.GetHunter(i).Id).First(id => id != first);
        level.SelectHunter(second); level.SetHealthLayout(second, HunterCareer.DefaultHealthSegments());
        if (level.Career.Selected.Weight == 0)
        { if (level.Career.Pistols == 0) level.BuyEquipment(HunterEquipment.Pistol); level.ToggleEquipment(HunterEquipment.Pistol); }
        level.SelectHunter(first); ClickButton("血段设置"); yield return null;
        level.SelectHunter(second); yield return null;
        Check(!hud.IsHealthLayoutOpen && level.Career.Selected.Id == second && !level.SetHealthLayout(first, new float[] { 50, 50 }),
            "Changing hunters closes the old draft and stale health commands cannot edit a different hunter");
        level.SelectHunter(first); level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f)); yield return null;
        Check(player.HealthSegments.SequenceEqual(custom) && actor.MaxHealth == HunterCareer.MaximumHealth && actor.Health == 100,
            "Deployment applies the selected hunter's saved segments without changing maximum or initial health");
        var dividers = hud.GetComponentsInChildren<RectTransform>().Where(t => t.name.StartsWith("Health Segment ")).ToArray();
        Check(dividers.Length == 2 && dividers.Any(t => Mathf.Abs(t.anchorMin.x - .25f) < .001f) && dividers.Any(t => Mathf.Abs(t.anchorMin.x - .75f) < .001f),
            "The live HUD draws separators at the saved 25 and 75 health boundaries");
        disk = File.ReadAllText(level.CareerSavePath); save.onClick.Invoke();
        Check(!hud.IsHealthLayoutOpen && !level.SetHealthLayout(first, new float[] { 50, 50 }) && File.ReadAllText(level.CareerSavePath) == disk && player.HealthSegments.SequenceEqual(custom),
            "Active gameplay rejects both stale UI callbacks and direct blood-layout changes");
        player.HealthRecoveryDelay = .25f; player.HealthRecoveryRate = 100;
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 70 });
        Check(actor.Health == 30 && player.HealthRecoveryCeiling == 75, "Real damage uses the custom middle 50 segment to choose 75 as the recovery ceiling");
        level.TogglePause(); yield return new WaitForSecondsRealtime(.3f);
        Check(actor.Health == 30, "Pause prevents health regeneration across an edited threshold");
        level.TogglePause(); yield return Until(() => actor.Health >= 75, 3, "Actual segmented regeneration reaches the saved threshold");
        yield return new WaitForSeconds(.2f);
        Check(actor.Health == 75 && actor.MaxHealth == 100, "Automatic recovery stops at the edited boundary rather than filling the fixed maximum");
        yield return Capture("health-layout-regeneration"); yield return SkillExtraction();
        Check(level.Career.Selected.HealthSegments.SequenceEqual(custom), "The edited layout survives a real extraction and return to camp");
        level.SelectHunter(second); level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        actor.RestoreHealth(); actor.BeHit(new SkillHitData { attackValue = 70 });
        Check(player.HealthSegments.SequenceEqual(HunterCareer.DefaultHealthSegments()) && player.HealthRecoveryCeiling == 50,
            "Deploying the other hunter replaces the previous layout and uses its independent 50-health recovery boundary");
        yield return Until(() => actor.Health >= 50, 3, "The second hunter recovers to its own threshold");
        yield return new WaitForSeconds(.2f); Check(actor.Health == 50, "The second hunter cannot inherit the first hunter's higher recovery ceiling");
        yield return SkillExtraction(); level.SelectHunter(first); level.SetHealthLayout(first, HunterCareer.DefaultHealthSegments());
        player.HealthRecoveryDelay = delay; player.HealthRecoveryRate = rate; isolateEliteNpcs = false; RestoreFrozen();
        level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot;
    }
    private void CheckHealthTouchLayout(Camera camera, Rect safe, string name)
    {
        var panel = hud.GetComponentsInChildren<RectTransform>().First(t => t.name == "Hunter Health Layout");
        var frame = TouchScreenRect(panel, camera); bool clear = frame.xMin >= safe.xMin - 1 && frame.yMin >= safe.yMin - 1 && frame.xMax <= safe.xMax + 1 && frame.yMax <= safe.yMax + 1;
        var bounds = new List<Rect>();
        foreach (var button in panel.GetComponentsInChildren<Button>())
        {
            var rect = TouchScreenRect(button.GetComponent<RectTransform>(), camera);
            clear &= rect.width >= 90 && rect.height >= 44 && frame.Contains(rect.min) && frame.Contains(rect.max);
            foreach (var other in bounds) clear &= !rect.Overlaps(other);
            bounds.Add(rect);
        }
        Check(clear && bounds.Count == 10, name + " keeps all ten health-editor buttons large, distinct and inside the device safe area");
    }
}

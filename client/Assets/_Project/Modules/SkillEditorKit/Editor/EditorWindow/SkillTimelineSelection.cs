// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Selection survives view rebuilds through event IDs; legacy assets use addresses until migrated.</summary>
public sealed class SkillTimelineSelection
{
    private readonly SkillEditorWindow window;
    private readonly HashSet<string> keys = new HashSet<string>();
    private string anchor;
    private VisualElement capture;
    private readonly Dictionary<VisualElement, Vector3> dragPositions = new Dictionary<VisualElement, Vector3>();
    private List<SkillEventEntry> dragEntries;
    private Vector2 startMouse;
    private float startScroll;
    private int dragDelta;
    private string dragTrackId;
    private bool moved;
    private bool marquee;
    private bool finishing;
    private TrackItemBase clicked;
    private bool trimming, trimLeft;
    private SkillClipTrim trimPlan;
    private StyleLength trimWidth;
    private VisualElement box;
    private VisualElement snapGuide;
    private Label snapGuideLabel;
    internal int? SnapFrame { get; private set; }
    private HashSet<string> initialKeys;
    private HashSet<string> marqueeBaseKeys;
    public bool IsDragging => capture != null;
    public bool SnapEnabled = true;

    public SkillTimelineSelection(SkillEditorWindow window) { this.window = window; }
    public List<TrackItemBase> Items => window.TrackItems.Where(i => i?.Data != null && i.Element != null).ToList();
    public List<SkillEventEntry> Entries => SkillTimelineData.Read(window.SkillConfig).Where(e => keys.Contains(e.Key)).ToList();
    public List<TrackItemBase> SelectedItems
    {
        get
        {
            var data = Entries.Select(e => e.Data).ToList();
            return Items.Where(i => data.Contains(i.Data)).ToList();
        }
    }

    private string Key(TrackItemBase item) => SkillTimelineData.Read(window.SkillConfig).FirstOrDefault(e => ReferenceEquals(e.Data, item.Data))?.Key;

    public void Clear()
    {
        Cancel();
        keys.Clear(); anchor = null;
        Rebind();
    }

    public void SelectOnly(TrackItemBase item)
    {
        keys.Clear();
        if (item.ParentTrack.Model != null) window.SetActiveTrack(item.ParentTrack.Model.TrackId);
        string key = Key(item);
        if (key != null) { keys.Add(key); anchor = key; }
        Rebind();
        window.FocusTimeline();
    }

    public void SelectData(IEnumerable<SkillFrameEventBase> data)
    {
        var selected = new HashSet<SkillFrameEventBase>(data);
        keys.Clear();
        foreach (var entry in SkillTimelineData.Read(window.SkillConfig))
            if (selected.Contains(entry.Data)) keys.Add(entry.Key);
        anchor = keys.FirstOrDefault();
        Rebind();
    }

    public void SelectAll()
    {
        foreach (var entry in SkillTimelineData.Read(window.SkillConfig)) keys.Add(entry.Key);
        Rebind();
    }

    public void Rebind()
    {
        var selected = SelectedItems;
        foreach (var item in Items)
        {
            if (selected.Contains(item)) item.OnSelect();
            else item.OnUnSelect();
        }
        var primary = selected.LastOrDefault(i => Key(i) == anchor) ?? selected.LastOrDefault();
        SkillEditorInspector.DisplaySelection(primary, primary?.ParentTrack);
    }

    public void BeginItemDrag(TrackItemBase item, VisualElement area, MouseDownEvent evt)
    {
        if (evt.button != 0) return;
        Cancel();
        if (item.ParentTrack.Model != null) window.SetActiveTrack(item.ParentTrack.Model.TrackId);
        string key = Key(item);
        if (key == null) return;
        if (evt.shiftKey && anchor != null)
        {
            var ordered = Items.Where(i => ReferenceEquals(i.ParentTrack, item.ParentTrack)).OrderBy(i => i.FrameIndex).ToList();
            int from = ordered.FindIndex(i => Key(i) == anchor), to = ordered.IndexOf(item);
            if (!evt.ctrlKey && !evt.commandKey) keys.Clear();
            if (from < 0) keys.Add(key);
            else for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) keys.Add(Key(ordered[i]));
        }
        else if (evt.ctrlKey || evt.commandKey)
        {
            if (!keys.Add(key)) keys.Remove(key);
            anchor = key;
        }
        else if (!keys.Contains(key))
        {
            keys.Clear(); keys.Add(key); anchor = key;
        }
        Rebind();
        window.FocusTimeline();
        evt.StopPropagation();
        if (evt.ctrlKey || evt.commandKey || evt.shiftKey || !keys.Contains(key)) return;
        window.IsPlaying = false;
        clicked = item;
        dragEntries = Entries;
        if (dragEntries.Any(e => SkillTimelineData.IsLocked(window.SkillConfig, e))) return;
        foreach (var selected in SelectedItems) dragPositions[selected.Element] = selected.Element.transform.position;
        StartCapture(area, evt.mousePosition, false);
    }

    public void BindMarquee(VisualElement content)
    {
        content.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.clickCount > 1) return;
            for (var target = evt.target as VisualElement; target != null && target != content; target = target.parent)
                if (target.userData is TrackItemBase || target is BaseField<string>) return;
            Cancel();
            initialKeys = new HashSet<string>(keys);
            marqueeBaseKeys = evt.ctrlKey || evt.commandKey || evt.shiftKey ? new HashSet<string>(keys) : new HashSet<string>();
            keys.Clear();
            foreach (var key in marqueeBaseKeys) keys.Add(key);
            box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.position = Position.Absolute;
            box.style.backgroundColor = new Color(0.25f, 0.65f, 1, 0.18f);
            box.style.borderLeftWidth = box.style.borderRightWidth = box.style.borderTopWidth = box.style.borderBottomWidth = 1;
            box.style.borderLeftColor = box.style.borderRightColor = box.style.borderTopColor = box.style.borderBottomColor = new Color(0.3f, 0.7f, 1);
            window.rootVisualElement.Add(box);
            StartCapture(content, evt.mousePosition, true);
            window.FocusTimeline();
            Rebind();
            evt.StopPropagation();
        });
    }

    public void BeginTrim(TrackItemBase item, VisualElement handle, MouseDownEvent evt, bool left)
    {
        if (evt.button != 0) return;
        evt.StopPropagation();
        Cancel();
        if (!SkillTimelineData.IsEditable(window.SkillConfig, out _) || item.ParentTrack.Model?.Locked == true || !SkillClipTrim.Supports(item.Data)) return;
        // Edge dragging edits one clip, never silently trims other selected clips.
        SelectOnly(item);
        window.IsPlaying = false;
        clicked = item; dragEntries = Entries;
        trimWidth = item.Element.style.width;
        dragPositions[item.Element] = item.Element.transform.position;
        trimming = true; trimLeft = left; trimPlan = null;
        StartCapture(handle, evt.mousePosition, false);
    }

    private void StartCapture(VisualElement element, Vector2 position, bool isMarquee)
    {
        capture = element; startMouse = position; startScroll = window.TimelineScrollX;
        marquee = isMarquee; moved = false; dragDelta = 0; dragTrackId = null;
        capture.RegisterCallback<MouseMoveEvent>(Move);
        capture.RegisterCallback<MouseUpEvent>(End);
        capture.RegisterCallback<MouseCaptureOutEvent>(LostCapture);
        capture.CaptureMouse();
    }

    private void Move(MouseMoveEvent evt)
    {
        if (capture == null) return;
        moved |= Vector2.Distance(startMouse, evt.mousePosition) > 3;
        if (!moved) return;
        if (marquee)
        {
            var rect = Rect.MinMaxRect(Math.Min(startMouse.x, evt.mousePosition.x), Math.Min(startMouse.y, evt.mousePosition.y),
                Math.Max(startMouse.x, evt.mousePosition.x), Math.Max(startMouse.y, evt.mousePosition.y));
            Vector2 local = window.rootVisualElement.WorldToLocal(rect.position);
            box.style.left = local.x; box.style.top = local.y; box.style.width = rect.width; box.style.height = rect.height;
            keys.Clear();
            foreach (var key in marqueeBaseKeys) keys.Add(key);
            foreach (var item in Items)
            {
                if (!rect.Overlaps(item.Element.worldBound)) continue;
                var key = Key(item);
                if (key != null) keys.Add(key);
            }
            Rebind();
        }
        else if (trimming)
        {
            window.ScrollTimelineEdge(evt.mousePosition);
            float rawDelta = (evt.mousePosition.x - startMouse.x + window.TimelineScrollX - startScroll) / window.FrameUnitWidth;
            var entry = dragEntries[0];
            int? snappedFrame = null;
            trimPlan = null;
            if (SnapEnabled && !evt.altKey)
            {
                int edge = trimLeft ? entry.Frame : entry.Frame + entry.Duration(window.SkillConfig.FrameRote);
                foreach (var candidate in GetSnapCandidates(rawDelta, new[] { edge }, null))
                {
                    if (!SkillClipTrim.Plan(window.SkillConfig, entry, trimLeft, candidate.Delta, out var plan, out _)) continue;
                    int actualEdge = trimLeft ? plan.Proposed.Frame : plan.Proposed.Frame + plan.Proposed.Duration(window.SkillConfig.FrameRote);
                    if (actualEdge != candidate.Frame) continue; // Source-range clamping must not show a false snap.
                    trimPlan = plan; snappedFrame = candidate.Frame;
                    break;
                }
            }
            string reason = null;
            if (trimPlan != null || SkillClipTrim.Plan(window.SkillConfig, entry, trimLeft, Mathf.RoundToInt(rawDelta), out trimPlan, out reason))
            {
                var proposed = trimPlan.Proposed;
                clicked.Element.transform.position = dragPositions[clicked.Element] + Vector3.right * ((proposed.Frame - entry.Frame) * window.FrameUnitWidth);
                clicked.Element.style.width = proposed.Duration(window.SkillConfig.FrameRote) * window.FrameUnitWidth;
                capture.tooltip = $"起点 {proposed.Frame}；长度 {proposed.Duration(window.SkillConfig.FrameRote)} 帧；源起点 {SkillMediaTiming.ClipIn(proposed.Data):0.###} 秒";
                PreviewAnimationBlends(new[] { entry }, new[] { proposed });
                ShowSnapGuide(snappedFrame);
            }
            else
            {
                clicked.Element.transform.position = dragPositions[clicked.Element];
                clicked.Element.style.width = trimWidth;
                capture.tooltip = reason;
                RestoreAnimationBlends();
                ShowSnapGuide(null);
            }
        }
        else
        {
            window.ScrollTimelineEdge(evt.mousePosition);
            float pixels = evt.mousePosition.x - startMouse.x + window.TimelineScrollX - startScroll;
            float rawDelta = pixels / window.FrameUnitWidth;
            string targetTrackId = null;
            SkillTrackBase targetView = null;
            if (window.SkillConfig.UseTrackModel && dragEntries.Select(e => e.TrackId).Distinct().Count() == 1)
            {
                targetView = window.TrackViews.FirstOrDefault(t => t.Lane != null && t.Lane.contentRoot.worldBound.Contains(evt.mousePosition));
                if (targetView != null) targetTrackId = targetView.Model.TrackId;
            }
            List<SkillEventEntry> proposed = null;
            int? snappedFrame = null;
            if (SnapEnabled && !evt.altKey)
            {
                foreach (var candidate in GetSnapCandidates(rawDelta, dragEntries.SelectMany(Edges), targetTrackId))
                {
                    if (!SkillTimelineData.PlanMove(window.SkillConfig, dragEntries, candidate.Delta, out var plan, out _, targetTrackId)) continue;
                    if (plan[0].Frame - dragEntries[0].Frame != candidate.Delta) continue;
                    proposed = plan; snappedFrame = candidate.Frame;
                    break;
                }
            }
            string reason = null;
            if (proposed != null || SkillTimelineData.PlanMove(window.SkillConfig, dragEntries, Mathf.RoundToInt(rawDelta), out proposed, out reason, targetTrackId))
            {
                dragDelta = proposed[0].Frame - dragEntries[0].Frame;
                dragTrackId = targetTrackId;
                capture.tooltip = snappedFrame.HasValue ? $"吸附到第 {snappedFrame.Value} 帧（Alt 临时关闭）" : $"整体移动 {dragDelta:+0;-0} 帧";
                foreach (var pair in dragPositions)
                    pair.Key.transform.position = pair.Value + new Vector3(dragDelta * window.FrameUnitWidth,
                        targetView == null ? 0 : targetView.Lane.contentRoot.worldBound.y - clicked.ParentTrack.Lane.contentRoot.worldBound.y, 0);
                PreviewAnimationBlends(dragEntries, proposed);
                ShowSnapGuide(snappedFrame);
            }
            else
            {
                dragDelta = 0; dragTrackId = null;
                capture.tooltip = reason;
                foreach (var pair in dragPositions) pair.Key.transform.position = pair.Value;
                RestoreAnimationBlends();
                ShowSnapGuide(null);
            }
        }
        evt.StopPropagation();
    }

    private IEnumerable<int> Edges(SkillEventEntry entry)
    {
        yield return entry.Frame;
        if (entry.Kind != SkillEventKind.Custom) yield return entry.Frame + entry.Duration(window.SkillConfig.FrameRote);
    }

    private void PreviewAnimationBlends(IList<SkillEventEntry> original, IList<SkillEventEntry> proposed)
    {
        var entries = SkillTimelineData.Read(window.SkillConfig).Where(e => !original.Any(o => ReferenceEquals(o.Data, e.Data)))
            .Concat(proposed).ToList();
        foreach (var item in Items.OfType<AnimationTrackItem>())
        {
            int index = -1;
            for (int i = 0; i < original.Count; i++) if (ReferenceEquals(original[i].Data, item.Data)) { index = i; break; }
            var current = index >= 0 ? proposed[index] : entries.FirstOrDefault(e => ReferenceEquals(e.Data, item.Data));
            if (current != null) item.PreviewBlendRange(current, entries);
        }
    }

    private void RestoreAnimationBlends()
    {
        foreach (var item in Items.OfType<AnimationTrackItem>()) item.RefreshBlendRange();
    }

    private IEnumerable<SkillTimelineSnap.Candidate> GetSnapCandidates(float rawDelta, IEnumerable<int> movingEdges, string targetTrackId)
    {
        var targets = new List<SkillTimelineSnap.Target>();
        foreach (var entry in SkillTimelineData.Read(window.SkillConfig))
        {
            if (dragEntries.Any(d => ReferenceEquals(d.Data, entry.Data))) continue;
            bool sameLane = dragEntries.Any(d => d.Kind == entry.Kind &&
                (!window.SkillConfig.UseTrackModel || entry.TrackId == (targetTrackId ?? d.TrackId)));
            foreach (int edge in Edges(entry)) targets.Add(new SkillTimelineSnap.Target(edge, sameLane ? 0 : 1));
        }
        // Nearby clip edges take precedence over a stationary playhead, which often
        // still sits on the dragged clip's old start and would otherwise pull it back.
        targets.Add(new SkillTimelineSnap.Target(window.CurrentSelectFrameIndex, 2));
        targets.Add(new SkillTimelineSnap.Target(0, 2));
        return SkillTimelineSnap.Candidates(rawDelta, movingEdges, targets, window.FrameUnitWidth);
    }

    private void ShowSnapGuide(int? frame)
    {
        SnapFrame = frame;
        if (!frame.HasValue)
        {
            snapGuide?.RemoveFromHierarchy(); snapGuide = null; snapGuideLabel = null;
            return;
        }
        var content = window.rootVisualElement.Q<VisualElement>("ContentListView");
        if (content == null) return;
        if (snapGuide == null)
        {
            var color = new Color(1f, .75f, .2f);
            snapGuide = new VisualElement { name = "SnapGuide", pickingMode = PickingMode.Ignore };
            snapGuide.style.position = Position.Absolute;
            snapGuide.style.top = 0; snapGuide.style.bottom = 0; snapGuide.style.width = 2;
            snapGuide.style.backgroundColor = color;
            snapGuideLabel = new Label { name = "SnapGuideLabel", pickingMode = PickingMode.Ignore };
            snapGuideLabel.style.position = Position.Absolute;
            snapGuideLabel.style.top = 0; snapGuideLabel.style.left = 4;
            snapGuideLabel.style.fontSize = 12; snapGuideLabel.style.whiteSpace = WhiteSpace.NoWrap;
            snapGuideLabel.style.color = color; snapGuideLabel.style.backgroundColor = new Color(.12f, .12f, .12f, .95f);
            snapGuideLabel.style.paddingLeft = snapGuideLabel.style.paddingRight = 4;
            snapGuide.Add(snapGuideLabel);
            content.Add(snapGuide);
        }
        snapGuide.style.left = frame.Value * window.FrameUnitWidth;
        snapGuideLabel.text = $"对齐：{frame.Value} 帧";
        snapGuide.BringToFront();
    }

    public static int SnapDelta(int delta, IEnumerable<int> movingEdges, IEnumerable<int> targetEdges, float threshold)
    {
        var targets = targetEdges.Distinct().OrderBy(v => v).ToArray();
        long best = long.MaxValue;
        int result = delta;
        foreach (int edge in movingEdges)
            foreach (int target in targets)
            {
                long adjustment = target - ((long)edge + delta);
                if (Math.Abs(adjustment) > threshold || Math.Abs(adjustment) >= best) continue;
                long snapped = (long)delta + adjustment;
                if (snapped < int.MinValue || snapped > int.MaxValue) continue;
                best = Math.Abs(adjustment); result = (int)snapped;
            }
        return result;
    }

    private void End(MouseUpEvent evt)
    {
        if (evt.button != 0) return;
        bool wasMarquee = marquee, didMove = moved;
        int delta = dragDelta;
        string targetTrackId = dragTrackId;
        TrackItemBase item = clicked;
        bool wasTrim = trimming;
        var plannedTrim = trimPlan;
        FinishCapture();
        if (wasTrim) { if (didMove) plannedTrim?.Commit(); }
        else if (!wasMarquee && didMove && (delta != 0 || targetTrackId != null)) SkillEditorClipboard.MoveSelected(delta, targetTrackId);
        else if (!wasMarquee && !didMove && item != null) SelectOnly(item);
        evt.StopPropagation();
    }

    private void LostCapture(MouseCaptureOutEvent evt) { if (!finishing) Cancel(); }

    public void Cancel()
    {
        ShowSnapGuide(null);
        if (capture == null) return;
        if (marquee && initialKeys != null)
        {
            keys.Clear();
            foreach (var key in initialKeys) keys.Add(key);
        }
        FinishCapture();
    }

    private void FinishCapture()
    {
        finishing = true;
        ShowSnapGuide(null);
        if (trimming && clicked?.Element != null) clicked.Element.style.width = trimWidth;
        foreach (var pair in dragPositions) if (pair.Key != null) pair.Key.transform.position = pair.Value;
        dragPositions.Clear();
        RestoreAnimationBlends();
        if (capture != null)
        {
            capture.UnregisterCallback<MouseMoveEvent>(Move);
            capture.UnregisterCallback<MouseUpEvent>(End);
            capture.UnregisterCallback<MouseCaptureOutEvent>(LostCapture);
            capture.tooltip = "";
            if (capture.HasMouseCapture()) capture.ReleaseMouse();
        }
        box?.RemoveFromHierarchy(); box = null; capture = null; dragEntries = null; clicked = null; initialKeys = null; marqueeBaseKeys = null;
        trimming = false; trimPlan = null;
        finishing = false;
    }
}

}

// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEditor;
using UnityEngine.UIElements;

[CustomEditor(typeof(SkillEditorWindow))]
public class SkillEditorInspector : Editor
{
    public static SkillEditorInspector Instance;
    public static TrackItemBase currentTrackItem { get; private set; }
    private static SkillTrackBase currentTrack;
    public static void SetTrackItem(TrackItemBase trackItem, SkillTrackBase track)
    {
        var window = SkillEditorWindow.Instance;
        if (window == null) { DisplaySelection(trackItem, track); return; }
        if (trackItem == null) window.TimelineSelection.Clear();
        else window.TimelineSelection.SelectOnly(trackItem);
    }

    public static void DisplaySelection(TrackItemBase trackItem, SkillTrackBase track)
    {
        currentTrackItem = trackItem;
        currentTrack = track;
        if (Instance != null) Instance.Show();
        SkillEditorWindow.Instance?.OnWorkflowSelectionChanged();
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    private VisualElement root;
    public override VisualElement CreateInspectorGUI()
    {
        Instance = this;
        root = new VisualElement();
        Show();
        return root;
    }

    private SkillEventDataInspectorBase eventDataInspector;

    public void Show()
    {
        Clean();
        if (root == null) return;
        eventDataInspector = DrawSelection(root, SkillEditorWindow.Instance);
        if (currentTrackItem != null) trackItemFrameIndex = currentTrackItem.FrameIndex;
    }

    public static SkillEventDataInspectorBase DrawSelection(VisualElement root, SkillEditorWindow window)
    {
        bool editable = SkillTimelineData.IsEditable(window?.SkillConfig, out string reason);
        if (editable && window.TimelineSelection.Entries.Exists(entry => SkillTimelineData.IsLocked(window.SkillConfig, entry)))
        { editable = false; reason = "选中片段所属轨道已锁定"; }
        root.SetEnabled(editable); root.tooltip = editable ? "" : reason;
        var selected = window?.TimelineSelection.Entries;
        if (selected != null && selected.Count > 1) { SkillMultiEventInspector.Draw(root, selected); return null; }
        if (currentTrackItem == null || currentTrack == null) return null;
        SkillEventDataInspectorBase inspector = null;
        if (currentTrackItem is AnimationTrackItem) inspector = new SkillAnimationEventInspector();
        else if (currentTrackItem is AudioTrackItem) inspector = new SkillAudioEventInspector();
        else if (currentTrackItem is EffectTrackItem) inspector = new SkillEffectEventInspector();
        else if (currentTrackItem is AttackDetectionTrackItem) inspector = new SkillAttackDetectionEventInspector();
        else if (currentTrackItem is ProjectileTrackItem) inspector = new SkillProjectileEventInspector();
        else if (currentTrackItem is EventTrackItem) inspector = new SkillCustomEventInspector();
        inspector?.Draw(root, currentTrackItem, currentTrack);
        return inspector;
    }

    private void Clean()
    {
        if (root != null)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                root.RemoveAt(i);
            }
        }
    }
    private int trackItemFrameIndex;
    public void SetTrackItemFrameIndex(int trackItemFrameIndex)
    {
        this.trackItemFrameIndex = trackItemFrameIndex;
        eventDataInspector?.SetFrameIndex(trackItemFrameIndex);
    }
}

}

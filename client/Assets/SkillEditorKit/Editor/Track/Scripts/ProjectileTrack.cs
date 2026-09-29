// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ProjectileTrack : SkillTrackBase
{
    private readonly List<ProjectileTrackItem> items = new List<ProjectileTrackItem>();
    public override IEnumerable<TrackItemBase> Items => items;
    public override void Init(VisualElement menu, VisualElement content, float width)
    { base.Init(menu, content, width); InitLane(menu, content); ResetView(); }
    public override void ResetView(float width)
    {
        base.ResetView(width);
        foreach (var item in items) { item.Cleanup(); item.Element.RemoveFromHierarchy(); }
        items.Clear();
        foreach (var clip in Model.Clips)
        {
            var item = new ProjectileTrackItem(this, (SkillProjectileEvent)clip.Data, width);
            items.Add(item); Lane.contentRoot.Add(item.Element);
        }
        Lane.LayoutItems(Items);
    }
    public override TrackItemBase FindItemByData(object data) => items.FirstOrDefault(i => ReferenceEquals(i.Data, data));
    public override void TickView(int frame) { foreach (var item in items) item.TickPreview(); }
    public override void OnSceneGUI() { foreach (var item in items) item.DrawScene(); }
    public void Cleanup() { foreach (var item in items) item.Cleanup(); }
    public override void Destory() { Cleanup(); Lane?.Destory(); }
}

public sealed class ProjectileTrackItem : TrackItemBase<ProjectileTrack>
{
    public SkillProjectileEvent Event { get; }
    public override SkillFrameEventBase Data => Event;
    private readonly List<GameObject> visuals = new List<GameObject>();
    private GameObject visualPrefab;
    internal readonly List<SkillAttackShape.Pose> PreviewPoses = new List<SkillAttackShape.Pose>();
    internal readonly List<SkillAttackShape.Pose> LaunchPoses = new List<SkillAttackShape.Pose>();
    internal string PreviewError { get; private set; }
    internal bool IsSelected => SkillEditorWindow.Instance?.TimelineSelection.Entries.Exists(e => ReferenceEquals(e.Data, Event)) == true;
    private double Age => Math.Max(0, (SkillEditorWindow.Instance.CurrentSelectFrameIndex - FrameIndex) / (double)Math.Max(1, SkillEditorWindow.Instance.SkillConfig.FrameRote));
    internal bool IsActive
    {
        get
        {
            var window = SkillEditorWindow.Instance;
            return window?.PreviewSession != null && track.Model.Enabled && FrameIndex <= window.SkillConfig.FrameCount &&
                window.CurrentSelectFrameIndex >= FrameIndex && Age < Event.LifeSeconds(window.SkillConfig.FrameRote) &&
                (Event.OnSkillEnd == SkillProjectileEndPolicy.ContinueFlying || window.CurrentSelectFrameIndex < window.SkillConfig.FrameCount);
        }
    }
    internal string PreviewState => !track.Model.Enabled ? "投射物已禁用｜参考路径" :
        IsActive ? "投射物飞行中｜预测路径不含碰撞" : "投射物未生效｜起点 / 终点参考";

    private sealed class Style : SkillTrackItemStyleBase
    {
        private readonly Label title;
        public Style()
        {
            root = new VisualElement { name = "ProjectileClip" };
            root.style.position = Position.Absolute; root.style.height = 30;
            root.style.borderLeftWidth = 3; root.style.borderLeftColor = new Color(1, .7f, .3f);
            title = new Label { name = "ProjectileLaunchMarker", pickingMode = PickingMode.Ignore };
            title.style.whiteSpace = WhiteSpace.NoWrap; title.style.overflow = Overflow.Hidden; root.Add(title);
        }
        public void Title(string text) => title.text = text;
    }

    public ProjectileTrackItem(ProjectileTrack owner, SkillProjectileEvent data, float width)
    {
        track = owner; Event = data; frameIndex = data.FrameIndex;
        normalColor = new Color(.55f, .3f, .7f, .5f); selectColor = new Color(.65f, .35f, .85f, .9f);
        itemStyle = new Style(); BindInteraction(Element); itemStyle.SetBGColor(normalColor); ResetView(width);
    }
    public override void ResetView(float width)
    {
        base.ResetView(width);
        var window = SkillEditorWindow.Instance;
        int frames = Event.PreviewFrames(window.SkillConfig.FrameRote);
        itemStyle.SetPosition(FrameIndex * width); itemStyle.SetWidth(frames * width);
        ((Style)itemStyle).Title("▲ 发射  ·  预计 " + frames + " 帧");
        Element.tooltip = "左端为发射点，色条为预计飞行区间；不会延长技能动作。直线定时模式可拖动右端调整飞行时长。";
        if (track.Items.Any(i => ReferenceEquals(i, this))) track.Lane?.LayoutItems(track.Items);
    }

    internal bool SamplePreview()
    {
        PreviewError = null; PreviewPoses.Clear(); LaunchPoses.Clear();
        var window = SkillEditorWindow.Instance;
        if (window?.PreviewCharacterObj == null) { PreviewError = "未选择预览角色"; return false; }
        if (!SkillProjectileEvent.Validate(Event, window.SkillConfig.FrameRote, out string error)) { PreviewError = error; return false; }
        if (Event.Flight == SkillProjectileFlight.Homing && window.PreviewTarget == null)
        { PreviewError = "追踪预览缺少目标：请设置顶部的预览目标。"; return false; }
        var player = window.PreviewCharacterObj.GetComponent<SkillPlayer>();
        var actor = player?.ModelTransform != null ? player.ModelTransform : window.PreviewCharacterObj.transform;
        try
        {
            if (window.PreviewSession != null) window.PreviewSession.Evaluate(window.SkillConfig, Math.Min(FrameIndex, window.SkillConfig.FrameCount));
            for (int i = 0; i < Event.Count; i++)
            {
                if (!SkillProjectileMotion.TryLaunch(Event, actor, window.PreviewTarget,
                    window.PreviewAimPoint == null ? (Vector3?)null : window.PreviewAimPoint.position, i, out var launch, out error, window.SkillConfig.Space))
                { PreviewError = error; return false; }
                LaunchPoses.Add(launch);
            }
        }
        finally
        {
            if (window.PreviewSession != null) window.PreviewSession.Evaluate(window.SkillConfig, Math.Min(window.CurrentSelectFrameIndex, window.SkillConfig.FrameCount));
        }
        foreach (var launch in LaunchPoses)
        {
            var motion = new SkillProjectileMotion(Event, window.SkillConfig.FrameRote, launch);
            motion.Advance(Age, window.PreviewTarget == null ? (Vector3?)null : window.PreviewTarget.position);
            PreviewPoses.Add(motion.Pose);
        }
        return true;
    }

    public void TickPreview()
    {
        if (!IsActive || !SamplePreview() || Event.Prefab == null) { Cleanup(); return; }
        if (visualPrefab != Event.Prefab || visuals.Count != Event.Count) Cleanup();
        visualPrefab = Event.Prefab;
        try
        {
            while (visuals.Count < Event.Count)
            {
                var visual = SkillPreviewObjects.CreateEffect(Event.Prefab, null, Event.EventId + ":" + visuals.Count);
                visuals.Add(visual); visual.SetActive(true);
            }
            for (int i = 0; i < visuals.Count; i++)
            {
                visuals[i].transform.SetPositionAndRotation(PreviewPoses[i].Position, PreviewPoses[i].Rotation);
                SkillPreviewObjects.Simulate(visuals[i], (float)Age);
            }
        }
        catch { Cleanup(); throw; }
    }

    internal void DrawScene()
    {
        if ((!IsSelected && !IsActive) || !SamplePreview()) return;
        var window = SkillEditorWindow.Instance;
        using (new Handles.DrawingScope(!track.Model.Enabled ? Color.gray : IsActive ? Color.cyan : new Color(1, .75f, .3f)))
        {
            foreach (var pose in PreviewPoses)
                foreach (var axis in (pose.Space == SkillSpace.TwoD ? new[] { Vector3.forward } : new[] { Vector3.up, Vector3.right, Vector3.forward })) Handles.DrawWireDisc(pose.Position, axis, Event.Radius);
            if (!IsSelected) return;
            // Show the complete prediction for each projectile; preview targets are stationary snapshots.
            foreach (var launch in LaunchPoses)
            {
                var motion = new SkillProjectileMotion(Event, window.SkillConfig.FrameRote, launch);
                var points = new Vector3[41]; points[0] = launch.Position;
                for (int i = 1; i < points.Length; i++)
                {
                    motion.Advance(Event.LifeSeconds(window.SkillConfig.FrameRote) / 40, window.PreviewTarget == null ? (Vector3?)null : window.PreviewTarget.position);
                    points[i] = motion.Pose.Position;
                }
                Handles.DrawAAPolyLine(2, points);
                Handles.Label(launch.Position, "▲ 发射");
            }
            Handles.Label(PreviewPoses[0].Position + Vector3.up * .5f, PreviewState +
                (Event.Flight == SkillProjectileFlight.Homing ? "\n追踪按静止目标重放；实际命中时间受目标运动影响" : ""));
        }
    }
    public void Cleanup()
    {
        foreach (var visual in visuals) if (visual != null) UnityEngine.Object.DestroyImmediate(visual);
        visuals.Clear(); visualPrefab = null;
    }
}

}

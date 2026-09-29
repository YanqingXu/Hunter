// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AttackDetectionTrackItem : TrackItemBase<AttackDetectionTrack>
{
    public override SkillFrameEventBase Data => skillAttackDetectionEvent;
    private SkillMultilineTrackStyle.ChildTrack childTrackStyle;
    private SkillAttackDetectionTrackItemStyle trackItemStyle;

    private SkillAttackDetectionEvent skillAttackDetectionEvent;
    public SkillAttackDetectionEvent SkillAttackDetectionEvent { get => skillAttackDetectionEvent; }

    public void Init(AttackDetectionTrack track, float frameUnitWidth, SkillAttackDetectionEvent skillAttackDetectionEvent, SkillMultilineTrackStyle.ChildTrack childTrack)
    {
        this.track = track;
        this.frameIndex = skillAttackDetectionEvent.FrameIndex;
        this.childTrackStyle = childTrack;
        this.skillAttackDetectionEvent = skillAttackDetectionEvent;
        normalColor = new Color(0.388f, 0.850f, 0.905f, 0.5f);
        selectColor = new Color(0.388f, 0.850f, 0.905f, 1f);
        trackItemStyle = new SkillAttackDetectionTrackItemStyle();
        itemStyle = trackItemStyle;
        ResetView(frameUnitWidth);
    }

    public override void ResetView(float frameUnitWidth)
    {
        base.ResetView(frameUnitWidth);
        if (!trackItemStyle.isInit)
        {
            trackItemStyle.Init(frameUnitWidth, skillAttackDetectionEvent, childTrackStyle);
            // 绑定事件
            BindInteraction(trackItemStyle.mainDragArea);
        }
        trackItemStyle.ResetView(frameUnitWidth, skillAttackDetectionEvent);
        track.Lane?.LayoutItems(track.Items);
    }

    public void Destroy()
    {
        childTrackStyle.Destroy();
    }

    public void SetTrackName(string name)
    {
        childTrackStyle.SetTrackName(name);
    }

    public void CheckFrameCount()
    {
        int frameCount = Mathf.Max(1, skillAttackDetectionEvent.DurationFrame);
        // 如果超过右侧边界，拓展边界
        if (frameIndex + frameCount > SkillEditorWindow.Instance.SkillConfig.FrameCount)
        {
            // 保存配置导致对象无效，重新引用
            SkillEditorWindow.Instance.SkillConfig.FrameCount = Mathf.Clamp(frameIndex + frameCount, 0, SkillTimelineData.MaxFrame);
            SkillEditorWindow.Instance.CurrentFrameCount = SkillEditorWindow.Instance.SkillConfig.FrameCount;
        }
    }


    internal SkillAttackShape.Pose PreviewPose { get; private set; }
    internal SkillAttackShape.Origin PreviewOrigin { get; private set; }
    internal string PreviewError { get; private set; }
    internal bool IsSelected => SkillEditorWindow.Instance?.TimelineSelection.Entries.Exists(e => ReferenceEquals(e.Data, Data)) == true;
    internal bool IsActive => track.Model?.Enabled != false && SkillEditorWindow.Instance?.PreviewSession != null &&
        SkillEditorWindow.Instance.CurrentSelectFrameIndex >= FrameIndex &&
        SkillEditorWindow.Instance.CurrentSelectFrameIndex <= SkillEditorWindow.Instance.SkillConfig.FrameCount &&
        (long)SkillEditorWindow.Instance.CurrentSelectFrameIndex <= (long)FrameIndex + Math.Max(0, skillAttackDetectionEvent.DurationFrame);
    internal string PreviewState => track.Model?.Enabled == false ? "已禁用｜灰色参考轮廓，不造成伤害" :
        IsActive ? "攻击范围生效" : "当前未生效｜片段外参考显示";
    internal float PreviewSize
    {
        get
        {
            var shape = skillAttackDetectionEvent.AttackDetectionData;
            if (shape is AttackBoxDetectionData box) return Mathf.Max(box.Scale.x, box.Scale.y, box.Scale.z) + 1;
            if (shape is AttackSphereDetectionData sphere) return sphere.Radius * 2 + 1;
            if (shape is AttackFanDetectionData fan) return Mathf.Max(fan.Radius * 2, fan.Height) + 1;
            return 3;
        }
    }

    public void TickView(int frame) => TryPreview(false);

    internal bool TryPreview(bool reference)
    {
        PreviewError = null;
        var window = SkillEditorWindow.Instance;
        if (window?.PreviewCharacterObj == null) { PreviewError = "未选择预览角色"; return false; }
        if (skillAttackDetectionEvent?.AttackDetectionData is AttackWeaponDetectionData weaponData)
        {
            var player = window.PreviewCharacterObj.GetComponent<SkillPlayer>();
            if (player?.WeaponDic == null || !player.WeaponDic.TryGetValue(weaponData.weaponName ?? "", out var weapon) ||
                weapon == null || (window.SkillConfig.Space == SkillSpace.TwoD ? weapon.GetComponent<Collider2D>() == null : weapon.GetComponent<Collider>() == null))
            { PreviewError = "缺少武器或碰撞体：" + weaponData.weaponName; return false; }
            PreviewPose = new SkillAttackShape.Pose { Position = weapon.transform.position, Rotation = weapon.transform.rotation };
            return true;
        }
        var shape = skillAttackDetectionEvent?.AttackDetectionData as AttackShapeDetectionDataBase;
        if (shape == null) { PreviewError = "当前攻击未配置伤害范围"; return false; }
        if (!SkillAttackShape.Validate(shape, out string validation, window.SkillConfig.Space)) { PreviewError = validation; return false; }
        if (!reference && !IsActive) return false;
        int displayed = window.CurrentSelectFrameIndex, start = FrameIndex;
        int sample = (int)Math.Max(start, Math.Min((long)displayed, (long)start + Mathf.Max(0, skillAttackDetectionEvent.DurationFrame)));
        var playerComponent = window.PreviewCharacterObj.GetComponent<SkillPlayer>();
        var model = playerComponent?.ModelTransform != null ? playerComponent.ModelTransform : window.PreviewCharacterObj.transform;
        bool resample = window.PreviewSession != null && (SkillAttackShape.IsFrozen(shape) || displayed != sample);
        try
        {
            if (resample) window.PreviewSession.Evaluate(window.SkillConfig, SkillAttackShape.IsFrozen(shape) ? start : sample);
            if (!SkillAttackShape.TryCapture(shape, model, window.PreviewTarget, window.PreviewAimPoint == null ?
                (Vector3?)null : window.PreviewAimPoint.position, out var origin, out string error, window.SkillConfig.Space))
            { PreviewError = error; return false; }
            PreviewOrigin = origin;
            PreviewPose = SkillAttackShape.Resolve(shape, origin, SkillAttackShape.Progress(sample, start, skillAttackDetectionEvent.DurationFrame));
            return true;
        }
        catch (Exception ex) { PreviewError = "范围预览失败：" + ex.Message; return false; }
        finally { if (resample) window.PreviewSession.Evaluate(window.SkillConfig, Math.Min(displayed, window.SkillConfig.FrameCount)); }
    }

    public void DrawGizmos() { } // Scene overlay owns editor range rendering.

    public void OnSceneGUI()
    {
        var window = SkillEditorWindow.Instance;
        if (window == null || (!IsActive && !IsSelected) || !TryPreview(IsSelected)) return;
        Color color = track.Model?.Enabled == false ? Color.gray :
            IsActive ? new Color(.2f, 1, .4f) : new Color(1, .75f, .25f);
        if (skillAttackDetectionEvent.AttackDetectionData is AttackShapeDetectionDataBase shape)
        {
            SkillSceneRangeRenderer.Draw(shape, PreviewPose, color);
            if (!IsSelected) return;
            using (new Handles.DrawingScope(color))
            {
                Handles.Label(PreviewPose.Position + Vector3.up * (PreviewSize * .5f), PreviewState + "\n" + Dimensions(shape));
                if (!shape.UseFootOrigin) return;
                var from = SkillAttackShape.Resolve(shape, PreviewOrigin, 0);
                Handles.DrawDottedLine(PreviewOrigin.Position, from.Position, 4);
                Handles.Label(PreviewOrigin.Position, shape.Anchor == SkillRangeAnchor.CharacterFeet ? "脚下起点" : "范围起点");
                if (SkillAttackShape.IsFlight(shape))
                {
                    var to = SkillAttackShape.Resolve(shape, PreviewOrigin, 1);
                    Handles.DrawDottedLine(from.Position, to.Position, 4);
                    Handles.Label(from.Position, "起始 " + shape.StartDistance.ToString("0.##") + " 米");
                    Handles.Label(to.Position, "结束 " + shape.EndDistance.ToString("0.##") + " 米");
                }
            }
        }
        else if (IsSelected)
        {
            var player = window.PreviewCharacterObj.GetComponent<SkillPlayer>();
            var data = (AttackWeaponDetectionData)skillAttackDetectionEvent.AttackDetectionData;
            var weapon = player.WeaponDic[data.weaponName];
            if (window.SkillConfig.Space == SkillSpace.TwoD)
            {
                var c=weapon.GetComponent<Collider2D>();
                using(new Handles.DrawingScope(color)) Handles.DrawWireCube(c.bounds.center,c.bounds.size);
                Handles.Label(PreviewPose.Position,PreviewState); return;
            }
            var collider = weapon.GetComponent<Collider>();
            using (new Handles.DrawingScope(color, collider.transform.localToWorldMatrix))
            {
                if (collider is BoxCollider box) Handles.DrawWireCube(box.center, box.size);
                else if (collider is SphereCollider sphere)
                    foreach (var axis in new[] { Vector3.up, Vector3.forward, Vector3.right }) Handles.DrawWireDisc(sphere.center, axis, sphere.radius);
                else using (new Handles.DrawingScope(Matrix4x4.identity)) Handles.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }
            Handles.Label(PreviewPose.Position, PreviewState);
        }
    }

    private static string Dimensions(AttackShapeDetectionDataBase shape)
    {
        if (SkillEditorWindow.Instance?.SkillConfig?.Space == SkillSpace.TwoD)
        {
            if (shape is AttackBoxDetectionData b) return $"2D 矩形 宽 {b.Scale.x:0.##} × 高 {b.Scale.y:0.##} 米";
            if (shape is AttackSphereDetectionData c) return $"2D 圆形 半径 {c.Radius:0.##} 米";
            if (shape is AttackFanDetectionData f) return $"2D 扇形 半径 {f.Radius:0.##} 米｜角度 {f.Angle:0.#}°";
        }
        if (shape is AttackBoxDetectionData box) return $"长 {box.Scale.z:0.##} × 宽 {box.Scale.x:0.##} × 高 {box.Scale.y:0.##} 米";
        if (shape is AttackSphereDetectionData sphere) return $"球体半径 {sphere.Radius:0.##} 米";
        if (shape is AttackFanDetectionData fan) return $"半径 {fan.Radius:0.##} 米｜高 {fan.Height:0.##} 米｜角度 {fan.Angle:0.#}°";
        return "";
    }
}

}

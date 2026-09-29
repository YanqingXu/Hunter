// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class EffectTrackItem : TrackItemBase<EffectTrack>
{
    public override SkillFrameEventBase Data => skillEffectEvent;
    private SkillMultilineTrackStyle.ChildTrack childTrackStyle;
    private SkillEffectTrackItemStyle trackItemStyle;

    private SkillEffectEvent skillEffectEvent;
    public SkillEffectEvent SkillEffectEvent { get => skillEffectEvent; }
    public void Init(EffectTrack track, float frameUnitWidth, SkillEffectEvent skillEffectEvent, SkillMultilineTrackStyle.ChildTrack childTrack)
    {
        this.track = track;
        this.frameIndex = skillEffectEvent.FrameIndex;
        this.childTrackStyle = childTrack;
        this.skillEffectEvent = skillEffectEvent;
        normalColor = new Color(0.388f, 0.850f, 0.905f, 0.5f);
        selectColor = new Color(0.388f, 0.850f, 0.905f, 1f);
        trackItemStyle = new SkillEffectTrackItemStyle();
        itemStyle = trackItemStyle;

        if (track.Model == null)
        {
        childTrackStyle.trackRoot.RegisterCallback<DragUpdatedEvent>(OnDragUpdate);
        childTrackStyle.trackRoot.RegisterCallback<DragPerformEvent>(DragPerform);
        }
        ResetView(frameUnitWidth);
    }

    public override void ResetView(float frameUnitWidth)
    {
        base.ResetView(frameUnitWidth);
        if (!trackItemStyle.isInit)
        {
            trackItemStyle.Init(frameUnitWidth, skillEffectEvent, childTrackStyle);
            BindInteraction(trackItemStyle.mainDragArea);
        }
        trackItemStyle.ResetView(frameUnitWidth, skillEffectEvent);
        track.Lane?.LayoutItems(track.Items);

        // 强行重新生成预览
        CleanEffectPreviewObj();
        TickView(SkillEditorWindow.Instance.CurrentSelectFrameIndex);
    }

    public void Destroy()
    {
        CleanEffectPreviewObj();
        childTrackStyle.Destroy();
    }

    public void CleanEffectPreviewObj()
    {
        if (effectPreviewObj != null)
        {
            GameObject.DestroyImmediate(effectPreviewObj);
            effectPreviewObj = null;
        }
    }

    public void SetTrackName(string name)
    {
        childTrackStyle.SetTrackName(name);
    }

    private void CheckFrameCount()
    {
        SkillEditorWindow window = SkillEditorWindow.Instance;
        if (window == null || window.SkillConfig == null) return;
        int endFrame = frameIndex + Mathf.Max(1, skillEffectEvent.Duration);
        if (endFrame > window.SkillConfig.FrameCount)
        {
            window.SkillConfig.FrameCount = Mathf.Clamp(endFrame, 0, SkillTimelineData.MaxFrame);
            window.CurrentFrameCount = window.SkillConfig.FrameCount;
        }
    }


    #region 拖拽资源
    private void OnDragUpdate(DragUpdatedEvent evt)
    {
        // 监听用户拖拽的是否是动画
        UnityEngine.Object[] objs = DragAndDrop.objectReferences;
        if (objs == null || objs.Length == 0) return;
        GameObject prefab = objs[0] as GameObject;
        if (prefab != null)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }
    private void DragPerform(DragPerformEvent evt)
    {
        var window = SkillEditorWindow.Instance;
        if (window.SkillConfig == null || DragAndDrop.objectReferences.Length == 0 ||
            !(DragAndDrop.objectReferences[0] is GameObject prefab) || !EditorUtility.IsPersistent(prefab)) return;
        int frame = window.GetFrameIndexByPos(evt.localMousePosition.x);
        float seconds = 0;
        foreach (var particle in prefab.GetComponentsInChildren<ParticleSystem>(true))
            seconds = Mathf.Max(seconds, particle.main.duration);
        int duration = Mathf.Max(1, Mathf.CeilToInt(seconds * Mathf.Max(1, window.SkillConfig.FrameRote)));
        if (frame < 0 || (long)frame + duration > SkillTimelineData.MaxFrame) return;
        var selected = new System.Collections.Generic.List<SkillEventEntry> { new SkillEventEntry(SkillEventKind.Effect, skillEffectEvent, frame, 0) };
        if (SkillEditorClipboard.Commit("设置特效事件", () =>
        {
            skillEffectEvent.FrameIndex = frame;
            skillEffectEvent.Prefab = prefab;
            skillEffectEvent.Duration = duration;
            SkillTimelineData.AssignNewIdIfMissing(skillEffectEvent);
            window.SkillConfig.FrameCount = Mathf.Max(window.SkillConfig.FrameCount, frame + duration);
        }, selected)) DragAndDrop.AcceptDrag();
        evt.StopPropagation();
    }
    #endregion


    #region 预览
    private GameObject effectPreviewObj;
    public void TickView(int frameIndex)
    {
        var window = SkillEditorWindow.Instance;
        int start = track.StartFrame(skillEffectEvent, skillEffectEvent.FrameIndex);
        if (track.Model?.Enabled == false || skillEffectEvent.Prefab == null || window == null || window.PreviewCharacterObj == null || window.PreviewSession == null ||
            frameIndex < start || (long)frameIndex >= (long)start + skillEffectEvent.Duration)
        {
            CleanEffectPreviewObj();
            return;
        }
        if (effectPreviewObj == null)
        {
            effectPreviewObj = SkillPreviewObjects.CreateEffect(skillEffectEvent.Prefab, EffectTrack.EffectParent,
                skillEffectEvent.EventId ?? ("legacy:" + skillEffectEvent.FrameIndex));
            effectPreviewObj.name = skillEffectEvent.Prefab.name;
        }
        var anchor = window.PreviewSession.GetEffectAnchor(window.SkillConfig, start, frameIndex, out var rotation);
        effectPreviewObj.transform.SetPositionAndRotation(anchor.MultiplyPoint3x4(skillEffectEvent.Position),
            rotation * Quaternion.Euler(skillEffectEvent.Rotation));
        effectPreviewObj.transform.localScale = skillEffectEvent.Scale;
        SkillPreviewObjects.Simulate(effectPreviewObj,
            (frameIndex - start) / (float)Mathf.Max(1, window.SkillConfig.FrameRote));
    }

    public void ApplyModelTransformData()
    {
        var window = SkillEditorWindow.Instance;
        if (effectPreviewObj == null || window?.PreviewSession == null) return;
        // Capture the handle result before re-evaluating the current preview.
        var position = effectPreviewObj.transform.position;
        var rotation = effectPreviewObj.transform.rotation;
        var scale = effectPreviewObj.transform.localScale;
        var matrix = window.PreviewSession.GetEffectAnchor(window.SkillConfig, track.StartFrame(skillEffectEvent, skillEffectEvent.FrameIndex),
            window.CurrentSelectFrameIndex, out var originRotation);
        skillEffectEvent.Position = matrix.inverse.MultiplyPoint3x4(position);
        skillEffectEvent.Rotation = (Quaternion.Inverse(originRotation) * rotation).eulerAngles;
        skillEffectEvent.Scale = scale;
    }
    #endregion
}

}

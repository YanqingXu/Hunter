// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AudioTrackItem : TrackItemBase<AudioTrack>
{
    public override SkillFrameEventBase Data => skillAudioEvent;
    private SkillMultilineTrackStyle.ChildTrack childTrackStyle;
    private SkillAudioTrackItemStyle trackItemStyle;

    private SkillAudioEvent skillAudioEvent;
    public SkillAudioEvent SkillAudioEvent { get => skillAudioEvent; }
    public void Init(AudioTrack track, float frameUnitWidth, SkillAudioEvent skillAudioEvent, SkillMultilineTrackStyle.ChildTrack childTrack)
    {
        this.track = track;
        this.frameIndex = skillAudioEvent.FrameIndex;
        this.childTrackStyle = childTrack;
        this.skillAudioEvent = skillAudioEvent;
        normalColor = new Color(0.388f, 0.850f, 0.905f, 0.5f);
        selectColor = new Color(0.388f, 0.850f, 0.905f, 1f);
        trackItemStyle = new SkillAudioTrackItemStyle();
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
            trackItemStyle.Init(frameUnitWidth, skillAudioEvent, childTrackStyle);
            BindInteraction(trackItemStyle.mainDragArea);
        }
        trackItemStyle.ResetView(frameUnitWidth, skillAudioEvent);
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
        int frameCount = SkillMediaTiming.AudioFrames(skillAudioEvent, SkillEditorWindow.Instance.SkillConfig.FrameRote);
        // 如果超过右侧边界，拓展边界
        if (frameIndex + frameCount > SkillEditorWindow.Instance.SkillConfig.FrameCount)
        {
            // 保存配置导致对象无效，重新引用
            SkillEditorWindow.Instance.SkillConfig.FrameCount = Mathf.Clamp(frameIndex + frameCount, 0, SkillTimelineData.MaxFrame);
            SkillEditorWindow.Instance.CurrentFrameCount = SkillEditorWindow.Instance.SkillConfig.FrameCount;
        }
    }


    #region 拖拽资源
    private void OnDragUpdate(DragUpdatedEvent evt)
    {
        // 监听用户拖拽的是否是动画
        UnityEngine.Object[] objs = DragAndDrop.objectReferences;
        if (objs == null || objs.Length == 0) return;
        AudioClip clip = objs[0] as AudioClip;
        if (clip != null)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }
    private void DragPerform(DragPerformEvent evt)
    {
        var window = SkillEditorWindow.Instance;
        if (window.SkillConfig == null || DragAndDrop.objectReferences.Length == 0 ||
            !(DragAndDrop.objectReferences[0] is AudioClip clip)) return;
        int frame = window.GetFrameIndexByPos(evt.localMousePosition.x);
        int duration = Mathf.Max(1, Mathf.CeilToInt(clip.length * Mathf.Max(1, window.SkillConfig.FrameRote)));
        if (frame < 0 || (long)frame + duration > SkillTimelineData.MaxFrame) return;
        var selected = new System.Collections.Generic.List<SkillEventEntry> { new SkillEventEntry(SkillEventKind.Audio, skillAudioEvent, frame, 0) };
        if (SkillEditorClipboard.Commit("设置音效事件", () =>
        {
            skillAudioEvent.AudioClip = clip;
            skillAudioEvent.UseClipRange = false;
            skillAudioEvent.ClipIn = 0;
            skillAudioEvent.DurationFrame = 0;
            skillAudioEvent.FrameIndex = frame;
            SkillTimelineData.AssignNewIdIfMissing(skillAudioEvent);
            window.SkillConfig.FrameCount = Mathf.Max(window.SkillConfig.FrameCount, frame + duration);
        }, selected)) DragAndDrop.AcceptDrag();
        evt.StopPropagation();
    }
    #endregion
}

}

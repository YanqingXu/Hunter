// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;

public class AnimationTrackItem : TrackItemBase<AnimationTrack>
{
    public override SkillFrameEventBase Data => animationEvent;
    private SkillAnimationEvent animationEvent;
    public SkillAnimationEvent AnimationEvent { get => animationEvent; }

    private SkillAnimationTrackItemStyle trackItemStyle;
    public void Init(AnimationTrack animationTrack, SkillTrackStyleBase parentTrackStyle, int startFrameIndex, float frameUnitWidth, SkillAnimationEvent animationEvent)
    {
        this.frameUnitWidth = frameUnitWidth;
        this.frameIndex = startFrameIndex;
        track = animationTrack;
        this.animationEvent = animationEvent;

        trackItemStyle = new SkillAnimationTrackItemStyle();
        itemStyle = trackItemStyle;
        trackItemStyle.Init(parentTrackStyle, startFrameIndex, frameUnitWidth);

        normalColor = new Color(0.388f, 0.850f, 0.905f, 0.5f);
        selectColor = new Color(0.388f, 0.850f, 0.905f, 1f);
        OnUnSelect();
        // 绑定事件
        BindInteraction(trackItemStyle.mainDragArea);
        ResetView(frameUnitWidth);
    }

    public override void ResetView(float frameUnitWidth)
    {
        this.frameUnitWidth = frameUnitWidth;
        trackItemStyle.SetTitle(animationEvent.AnimationClip == null ? "未设置动画" : animationEvent.AnimationClip.name);
        // 位置计算
        trackItemStyle.SetPosition(frameIndex * frameUnitWidth);
        trackItemStyle.SetWidth(Mathf.Max(1, animationEvent.DurationFrame) * frameUnitWidth);
        RefreshBlendRange();

        int animationClipFrameCount = animationEvent.AnimationClip == null
            ? 0
            : Mathf.CeilToInt((animationEvent.AnimationClip.length - SkillMediaTiming.ClipIn(animationEvent)) * Mathf.Max(1, SkillEditorWindow.Instance.SkillConfig.FrameRote));
        // 计算动画结束线的位置
        if (animationClipFrameCount > animationEvent.DurationFrame)
        {
            trackItemStyle.animationOverLine.style.display = DisplayStyle.None;
        }
        else
        {
            trackItemStyle.animationOverLine.style.display = DisplayStyle.Flex;
            Vector3 overLinePos = trackItemStyle.animationOverLine.transform.position;
            overLinePos.x = animationClipFrameCount * frameUnitWidth - 1;// 线条自身宽度为2
            trackItemStyle.animationOverLine.transform.position = overLinePos;
        }
    }

    public void RefreshBlendRange()
    {
        var entries = SkillTimelineData.Read(SkillEditorWindow.Instance.SkillConfig);
        var current = entries.FirstOrDefault(e => ReferenceEquals(e.Data, Data));
        if (current != null) PreviewBlendRange(current, entries);
    }

    public void PreviewBlendRange(SkillEventEntry current, IEnumerable<SkillEventEntry> entries)
    {
        var animations = entries.Where(e => e.Kind == SkillEventKind.Animation && e.TrackId == current.TrackId &&
            ((SkillAnimationEvent)e.Data).AnimationClip != null).OrderBy(e => e.Frame).ToList();
        var previous = animations.LastOrDefault(e => e.Frame < current.Frame);
        var next = animations.FirstOrDefault(e => e.Frame > current.Frame);
        var data = (SkillAnimationEvent)current.Data;
        int incoming = previous == null ? 0 : SkillAnimationTiming.OverlapFrames(previous.Frame, (SkillAnimationEvent)previous.Data, current.Frame, data);
        int outgoing = next == null ? 0 : SkillAnimationTiming.OverlapFrames(current.Frame, data, next.Frame, (SkillAnimationEvent)next.Data);
        trackItemStyle.SetBlendRanges(incoming, outgoing, frameUnitWidth, SkillEditorWindow.Instance.SkillConfig.FrameRote);
    }

    public override void OnSelect()
    {
        base.OnSelect();
        // Selecting a clip exposes its edge handles even when the neighbour overlaps it.
        Element?.BringToFront();
    }

    public void CheckFrameCount()
    {
        // 如果超过右侧边界，拓展边界
        if (frameIndex + animationEvent.DurationFrame > SkillEditorWindow.Instance.SkillConfig.FrameCount)
        {
            // 保存配置导致对象无效，重新引用
            SkillEditorWindow.Instance.SkillConfig.FrameCount = Mathf.Clamp(frameIndex + animationEvent.DurationFrame, 0, SkillTimelineData.MaxFrame);
            SkillEditorWindow.Instance.CurrentFrameCount = SkillEditorWindow.Instance.SkillConfig.FrameCount;
        }
    }


    public override void OnConfigChanged()
    {
        animationEvent = track.AnimationData.FrameData[frameIndex];
    }
}

}

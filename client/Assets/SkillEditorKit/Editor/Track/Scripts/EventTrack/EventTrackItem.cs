// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEngine;
using UnityEngine.UIElements;

public class EventTrackItem : TrackItemBase<EventTrack>
{
    public override SkillFrameEventBase Data => customEvent;
    private SkillCustomEvent customEvent;
    public SkillCustomEvent CustomEvent { get => customEvent; }

    private SkillCustomEventTrackItemStyle trackItemStyle;
    public static EventTrackItem currentSelectItem;
    public void Init(EventTrack eventTrack, SkillTrackStyleBase parentTrackStyle, int startFrameIndex, float frameUnitWidth, SkillCustomEvent customEvent)
    {
        this.frameUnitWidth = frameUnitWidth;
        this.frameIndex = startFrameIndex;
        track = eventTrack;
        this.customEvent = customEvent;

        trackItemStyle = new SkillCustomEventTrackItemStyle();
        itemStyle = trackItemStyle;
        trackItemStyle.Init(parentTrackStyle);

        normalColor = new Color(0.388f, 0.850f, 0.905f, 0.5f);
        selectColor = new Color(0.388f, 0.850f, 0.905f, 1f);
        OnUnSelect();
        BindInteraction(trackItemStyle.root);
        ResetView(frameUnitWidth);
    }

    public override void ResetView(float frameUnitWidth)
    {
        this.frameUnitWidth = frameUnitWidth;
        // 位置计算
        trackItemStyle.SetPosition(frameIndex * frameUnitWidth - frameUnitWidth / 2);
        trackItemStyle.SetWidth(frameUnitWidth);
    }

    public void ChangeFrameIndex(int newIndex)
    {
        int oldIndex = frameIndex;
        if (oldIndex == newIndex) return;
        if (SkillEditorChangeUtility.Apply("移动技能事件", () => track.SetFrameIndex(oldIndex, newIndex)))
        {
            frameIndex = newIndex;
            SkillEditorInspector.Instance?.SetTrackItemFrameIndex(frameIndex);
            ResetView();
        }
    }

}

}

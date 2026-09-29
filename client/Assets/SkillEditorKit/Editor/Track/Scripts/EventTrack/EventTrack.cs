// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

public class EventTrack : SkillTrackBase
{
    private SkillTrackStyleBase trackStyle;
    private Dictionary<int, EventTrackItem> trackItemDic = new Dictionary<int, EventTrackItem>();
    public SkillCustomEventData CustomEventData { get => Model != null ? new SkillCustomEventData { FrameData = Model.Clips.ToDictionary(c => c.Frame, c => (SkillCustomEvent)c.Data) } : SkillEditorWindow.Instance == null || SkillEditorWindow.Instance.SkillConfig == null ? null : SkillEditorWindow.Instance.SkillConfig.skillCustomEventData; }
    public override void Init(VisualElement menuParent, VisualElement trackParent, float frameWdith)
    {
        base.Init(menuParent, trackParent, frameWdith);
        if (Model != null) { InitLane(menuParent, trackParent); trackStyle = Lane; }
        else
        {
            var legacy = new SkillSingleLineTrackStyle();
            legacy.Init(menuParent, trackParent, "事件配置");
            trackStyle = legacy;
        }
        if (Model == null) trackStyle.contentRoot.RegisterCallback<MouseDownEvent>(ContentRootMouseDown);
        ResetView();
    }

    private void ContentRootMouseDown(MouseDownEvent evt)
    {
        if (evt.button != 0 || evt.clickCount != 2) return;
        int frameIndex = SkillEditorWindow.Instance.GetFrameIndexByMousePos(evt.localMousePosition.x);
        if (CustomEventData == null || CustomEventData.FrameData == null || CustomEventData.FrameData.ContainsKey(frameIndex)) return;
        var window = SkillEditorWindow.Instance;
        var entry = new SkillEventEntry(SkillEventKind.Custom, new SkillCustomEvent(), frameIndex, frameIndex, Model?.TrackId);
        var proposed = new List<SkillEventEntry> { entry };
        if (SkillTimelineData.ValidatePlacement(window.SkillConfig, proposed, null, out string reason))
            SkillEditorClipboard.Commit("新增技能事件", () => SkillTimelineData.Add(window.SkillConfig, entry), proposed);
        evt.StopPropagation();
    }

    public override void ResetView(float frameWidth)
    {
        base.ResetView(frameWidth);
        // 销毁当前已有
        foreach (var item in trackItemDic)
        {
            trackStyle.DeleteItem(item.Value.itemStyle.root);
        }
        trackItemDic.Clear();
        if (CustomEventData == null || CustomEventData.FrameData == null) return;

        // 根据数据绘制TrackItem
        foreach (var item in CustomEventData.FrameData)
        {
            if (item.Value == null) continue;
            CreateItem(item.Key, item.Value);
        }
        Lane?.LayoutItems(Items);
    }

    private void CreateItem(int frameIndex, SkillCustomEvent skillCustomEvent)
    {
        EventTrackItem trackItem = new EventTrackItem();
        trackItem.Init(this, trackStyle, frameIndex, frameWidth, skillCustomEvent);
        trackItemDic.Add(frameIndex, trackItem);
    }


    /// <summary>
    /// 将oldIndex的数据变为newIndex
    /// </summary>
    public void SetFrameIndex(int oldIndex, int newIndex)
    {
        if (CustomEventData == null || CustomEventData.FrameData == null) return;
        if (CustomEventData.FrameData.Remove(oldIndex, out SkillCustomEvent customEvent))
        {
            CustomEventData.FrameData.Add(newIndex, customEvent);
            trackItemDic.Remove(oldIndex, out EventTrackItem eventTrackItem);
            trackItemDic.Add(newIndex, eventTrackItem);
        }
    }

    public override void DeleteTrackItem(int frameIndex)
    {
        if (CustomEventData == null || CustomEventData.FrameData == null) return;
        CustomEventData.FrameData.Remove(frameIndex);
        if (trackItemDic.Remove(frameIndex, out EventTrackItem item))
        {
            trackStyle.DeleteItem(item.itemStyle.root);
        }
    }


    public override void Destory()
    {
        trackStyle?.Destory();
    }

    public override System.Collections.Generic.IEnumerable<TrackItemBase> Items => trackItemDic.Values;

    public override TrackItemBase FindItemByData(object data)
    {
        foreach (EventTrackItem item in trackItemDic.Values)
        {
            if (ReferenceEquals(item.CustomEvent, data)) return item;
        }
        return null;
    }
}

}

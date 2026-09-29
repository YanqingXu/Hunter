// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using UnityEngine.UIElements;
using System.Linq;
using UnityEngine;

public class EffectTrack : SkillTrackBase
{
    private SkillMultilineTrackStyle trackStyle;
    public SkillEffectData EffectData { get => Model != null ? new SkillEffectData { FrameData = Model.Clips.Select(c => (SkillEffectEvent)c.Data).ToList() } : SkillEditorWindow.Instance == null || SkillEditorWindow.Instance.SkillConfig == null ? null : SkillEditorWindow.Instance.SkillConfig.SkillEffectData; }
    private List<EffectTrackItem> trackItemList = new List<EffectTrackItem>();

    public static Transform EffectParent { get; private set; }
    private static int previewRootUsers;

    public override void Init(VisualElement menuParent, VisualElement trackParent, float frameWdith)
    {
        base.Init(menuParent, trackParent, frameWdith);
        if (Model != null) InitLane(menuParent, trackParent);
        else
        {
        trackStyle = new SkillMultilineTrackStyle();
        trackStyle.Init(menuParent, trackParent, "特效配置", AddChildTrack, CheckDeleteChildTrack, SwapChildTrack, UpdateChildTrackName);
        }

        // Own only our temporary objects. A scene's existing "Effects" hierarchy
        // may contain user-authored data and must never be cleared by this tool.
        if (EffectParent == null)
            EffectParent = new GameObject("[SkillEditor] Effect Preview") { hideFlags = HideFlags.HideAndDontSave }.transform;
        previewRootUsers++;

        ResetView();
    }

    public override void ResetView(float frameWidth)
    {
        base.ResetView(frameWidth);

        // 销毁已有的
        foreach (EffectTrackItem item in trackItemList)
        {
            item.Destroy();
        }
        trackItemList.Clear();

        // 根据数据绘制TrackItem
        if (EffectData == null || EffectData.FrameData == null) return;
        foreach (SkillEffectEvent item in EffectData.FrameData)
        {
            if (item == null) continue;
            CreateItem(item);
        }
        Lane?.LayoutItems(Items);
    }

    private void CreateItem(SkillEffectEvent effectEvent)
    {
        EffectTrackItem item = new EffectTrackItem();
        item.Init(this, frameWidth, effectEvent, Model == null ? trackStyle.AddChildTrack() : SkillMultilineTrackStyle.ChildTrack.Shared(Lane.contentRoot));
        item.SetTrackName(effectEvent.TrackName);
        trackItemList.Add(item);
    }

    private void UpdateChildTrackName(SkillMultilineTrackStyle.ChildTrack childTrack, string newName)
    {
        int index = childTrack.GetIndex();
        if (index < 0 || index >= trackItemList.Count) return;
        var data = trackItemList[index].SkillEffectEvent;
        SkillEditorChangeUtility.Apply("重命名特效轨道", () => data.TrackName = newName);
    }

    private void AddChildTrack()
    {
        var data = new SkillEffectEvent();
        SkillTimelineData.AssignNewIdIfMissing(data);
        if (SkillEditorChangeUtility.Apply("新增特效轨道", () =>
            SkillTimelineData.Add(SkillEditorWindow.Instance.SkillConfig, new SkillEventEntry(SkillEventKind.Effect, data, 0, 0))))
        {
            CreateItem(data);
            SkillEditorWindow.Instance.TimelineSelection.SelectData(new[] { data });
        }
    }

    private bool CheckDeleteChildTrack(int index)
    {
        if (index < 0 || index >= trackItemList.Count) return false;
        var data = trackItemList[index].SkillEffectEvent;
        if (!SkillEditorChangeUtility.Apply("删除特效轨道", () => EffectData.FrameData.Remove(data))) return false;
        trackItemList[index].CleanEffectPreviewObj();
        // The style owns removal of its row. Do not destroy the same row twice.
        trackItemList.RemoveAt(index);
        SkillEditorWindow.Instance.TimelineSelection.Rebind();
        return true;
    }

    private void SwapChildTrack(int index1, int index2)
    {
        if (index1 < 0 || index2 < 0 || index1 >= trackItemList.Count || index2 >= trackItemList.Count) return;
        var selected = SkillEditorWindow.Instance.TimelineSelection.Entries.ConvertAll(e => e.Data);
        int row1 = EffectData.FrameData.IndexOf(trackItemList[index1].SkillEffectEvent);
        int row2 = EffectData.FrameData.IndexOf(trackItemList[index2].SkillEffectEvent);
        if (row1 < 0 || row2 < 0) return;
        if (!SkillEditorChangeUtility.Apply("调整特效轨道顺序", () =>
        {
            var first = EffectData.FrameData[row1];
            EffectData.FrameData[row1] = EffectData.FrameData[row2];
            EffectData.FrameData[row2] = first;
        })) return;
        var item = trackItemList[index1];
        trackItemList[index1] = trackItemList[index2];
        trackItemList[index2] = item;
        SkillEditorWindow.Instance.TimelineSelection.SelectData(selected);
    }

    public override void Destory()
    {
        trackStyle?.Destory();
        Lane?.Destory();
        for (int i = 0; i < trackItemList.Count; i++)
        {
            trackItemList[i].CleanEffectPreviewObj();
        }
        previewRootUsers = Mathf.Max(0, previewRootUsers - 1);
        if (previewRootUsers == 0)
        {
            if (EffectParent != null) GameObject.DestroyImmediate(EffectParent.gameObject);
            EffectParent = null;
        }
    }

    public override void TickView(int frameIndex)
    {
        for (int i = 0; i < trackItemList.Count; i++)
        {
            trackItemList[i].TickView(frameIndex);
        }
    }

    public override System.Collections.Generic.IEnumerable<TrackItemBase> Items => trackItemList;

    public override TrackItemBase FindItemByData(object data)
    {
        return trackItemList.Find(item => ReferenceEquals(item.SkillEffectEvent, data));
    }
}

}

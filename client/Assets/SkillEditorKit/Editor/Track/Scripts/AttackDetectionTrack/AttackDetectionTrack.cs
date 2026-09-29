// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

public class AttackDetectionTrack : SkillTrackBase
{
    private SkillMultilineTrackStyle trackStyle;
    public SkillAttackDetectionData SkillAttackDetectionData { get => Model != null ? new SkillAttackDetectionData { FrameData = Model.Clips.Select(c => (SkillAttackDetectionEvent)c.Data).ToList() } : SkillEditorWindow.Instance == null || SkillEditorWindow.Instance.SkillConfig == null ? null : SkillEditorWindow.Instance.SkillConfig.SkillAttackDetectionData; }
    private List<AttackDetectionTrackItem> trackItemList = new List<AttackDetectionTrackItem>();
    public override void Init(VisualElement menuParent, VisualElement trackParent, float frameWdith)
    {
        base.Init(menuParent, trackParent, frameWdith);
        if (Model != null) InitLane(menuParent, trackParent);
        else
        {
        trackStyle = new SkillMultilineTrackStyle();
        trackStyle.Init(menuParent, trackParent, "攻击伤害监测", AddChildTrack, CheckDeleteChildTrack, SwapChildTrack, UpdateChildTrackName);
        }
        ResetView();
    }

    public override void ResetView(float frameWidth)
    {
        base.ResetView(frameWidth);

        // 销毁已有的
        foreach (AttackDetectionTrackItem item in trackItemList)
        {
            item.Destroy();
        }
        trackItemList.Clear();
        if (SkillAttackDetectionData == null || SkillAttackDetectionData.FrameData == null) return;

        // 根据数据绘制TrackItem
        foreach (SkillAttackDetectionEvent item in SkillAttackDetectionData.FrameData)
        {
            if (item == null) continue;
            CreateItem(item);
        }
        Lane?.LayoutItems(Items);
    }

    private void CreateItem(SkillAttackDetectionEvent skillAttackDetectionEvent)
    {
        AttackDetectionTrackItem item = new AttackDetectionTrackItem();
        item.Init(this, frameWidth, skillAttackDetectionEvent, Model == null ? trackStyle.AddChildTrack() : SkillMultilineTrackStyle.ChildTrack.Shared(Lane.contentRoot));
        item.SetTrackName(skillAttackDetectionEvent.TrackName);
        trackItemList.Add(item);
    }

    private void UpdateChildTrackName(SkillMultilineTrackStyle.ChildTrack childTrack, string newName)
    {
        int index = childTrack.GetIndex();
        if (index < 0 || index >= trackItemList.Count) return;
        var data = trackItemList[index].SkillAttackDetectionEvent;
        SkillEditorChangeUtility.Apply("重命名攻击检测轨道", () => data.TrackName = newName);
    }

    private void AddChildTrack()
    {
        var data = new SkillAttackDetectionEvent();
        SkillTimelineData.AssignNewIdIfMissing(data);
        if (SkillEditorChangeUtility.Apply("新增攻击检测轨道", () =>
            SkillTimelineData.Add(SkillEditorWindow.Instance.SkillConfig, new SkillEventEntry(SkillEventKind.Attack, data, 0, 0))))
        {
            CreateItem(data);
            SkillEditorWindow.Instance.TimelineSelection.SelectData(new[] { data });
        }
    }

    private bool CheckDeleteChildTrack(int index)
    {
        if (index < 0 || index >= trackItemList.Count) return false;
        var data = trackItemList[index].SkillAttackDetectionEvent;
        if (!SkillEditorChangeUtility.Apply("删除攻击检测轨道", () => SkillAttackDetectionData.FrameData.Remove(data))) return false;
        
        // The style owns removal of its row. Do not destroy the same row twice.
        trackItemList.RemoveAt(index);
        SkillEditorWindow.Instance.TimelineSelection.Rebind();
        return true;
    }

    private void SwapChildTrack(int index1, int index2)
    {
        if (index1 < 0 || index2 < 0 || index1 >= trackItemList.Count || index2 >= trackItemList.Count) return;
        var selected = SkillEditorWindow.Instance.TimelineSelection.Entries.ConvertAll(e => e.Data);
        int row1 = SkillAttackDetectionData.FrameData.IndexOf(trackItemList[index1].SkillAttackDetectionEvent);
        int row2 = SkillAttackDetectionData.FrameData.IndexOf(trackItemList[index2].SkillAttackDetectionEvent);
        if (row1 < 0 || row2 < 0) return;
        if (!SkillEditorChangeUtility.Apply("调整攻击检测轨道顺序", () =>
        {
            var first = SkillAttackDetectionData.FrameData[row1];
            SkillAttackDetectionData.FrameData[row1] = SkillAttackDetectionData.FrameData[row2];
            SkillAttackDetectionData.FrameData[row2] = first;
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
    }

    public override void DrawGizmos()
    {
        for (int i = 0; i < trackItemList.Count; i++)
        {
            int currFrameIndex = SkillEditorWindow.Instance.CurrentSelectFrameIndex;
            SkillAttackDetectionEvent detectionEvent = trackItemList[i].SkillAttackDetectionEvent;
            if (detectionEvent == null) continue;
            if (currFrameIndex < StartFrame(detectionEvent, detectionEvent.FrameIndex) || currFrameIndex > StartFrame(detectionEvent, detectionEvent.FrameIndex) + Mathf.Max(0, detectionEvent.DurationFrame))
            {
                continue;
            }
            trackItemList[i].DrawGizmos();
        }
    }

    public override void TickView(int frameIndex)
    {
        foreach (var item in trackItemList) item.TickView(frameIndex);
    }

    public override void OnSceneGUI()
    {
        foreach (var item in trackItemList) item.OnSceneGUI();
    }

    public override System.Collections.Generic.IEnumerable<TrackItemBase> Items => trackItemList;

    public override TrackItemBase FindItemByData(object data)
    {
        return trackItemList.Find(item => ReferenceEquals(item.SkillAttackDetectionEvent, data));
    }
}

}

// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AnimationTrack : SkillTrackBase
{
    private SkillTrackStyleBase trackStyle;
    private Dictionary<int, AnimationTrackItem> trackItemDic = new Dictionary<int, AnimationTrackItem>();
    public SkillAnimationData AnimationData { get => Model != null ? new SkillAnimationData { FrameData = Model.Clips.ToDictionary(c => c.Frame, c => (SkillAnimationEvent)c.Data) } : SkillEditorWindow.Instance == null || SkillEditorWindow.Instance.SkillConfig == null ? null : SkillEditorWindow.Instance.SkillConfig.SkillAnimationData; }
    public override void Init(VisualElement menuParent, VisualElement trackParent, float frameWdith)
    {
        base.Init(menuParent, trackParent, frameWdith);
        if (Model != null) { InitLane(menuParent, trackParent); trackStyle = Lane; }
        else
        {
            var legacy = new SkillSingleLineTrackStyle();
            legacy.Init(menuParent, trackParent, "动画配置");
            trackStyle = legacy;
        }
        if (Model == null)
        {
            trackStyle.contentRoot.RegisterCallback<DragUpdatedEvent>(OnDragUpdate);
            trackStyle.contentRoot.RegisterCallback<DragPerformEvent>(DragPerform);
        }
        ResetView();
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
        if (AnimationData == null || AnimationData.FrameData == null) return;

        // 根据数据绘制TrackItem
        foreach (var item in AnimationData.FrameData.OrderBy(p => p.Key))
        {
            if (item.Value == null)
            {
                Debug.LogWarning($"动画事件@{item.Key} 为空，已跳过绘制。", SkillEditorWindow.Instance.SkillConfig);
                continue;
            }
            CreateItem(item.Key, item.Value);
        }
        Lane?.LayoutItems(Items);
    }

    private void CreateItem(int frameIndex, SkillAnimationEvent skillAnimationEvent)
    {
        AnimationTrackItem trackItem = new AnimationTrackItem();
        trackItem.Init(this, trackStyle, frameIndex, frameWidth, skillAnimationEvent);
        trackItemDic.Add(frameIndex, trackItem);
    }

    #region 拖拽资源
    private void OnDragUpdate(DragUpdatedEvent evt)
    {
        // 监听用户拖拽的是否是动画
        UnityEngine.Object[] objs = DragAndDrop.objectReferences;
        if (objs == null || objs.Length == 0) return;
        AnimationClip clip = objs[0] as AnimationClip;
        if (clip != null)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }
    private void DragPerform(DragPerformEvent evt)
    {
        var window = SkillEditorWindow.Instance;
        if (window.SkillConfig == null || DragAndDrop.objectReferences.Length == 0 ||
            !(DragAndDrop.objectReferences[0] is AnimationClip clip)) return;
        int frame = window.GetFrameIndexByPos(evt.localMousePosition.x);
        var data = new SkillAnimationEvent
        {
            AnimationClip = clip,
            DurationFrame = Mathf.Max(1, Mathf.CeilToInt(clip.length * Mathf.Max(1, window.SkillConfig.FrameRote))),
            TransitionTime = 0.25f
        };
        var entry = new SkillEventEntry(SkillEventKind.Animation, data, frame, frame, Model?.TrackId);
        var proposed = new List<SkillEventEntry> { entry };
        if (!SkillTimelineData.ValidatePlacement(window.SkillConfig, proposed, null, out string reason))
        { Debug.LogWarning(reason); return; }
        if (SkillEditorClipboard.Commit("新增动画事件", () => SkillTimelineData.Add(window.SkillConfig, entry), proposed))
            DragAndDrop.AcceptDrag();
        evt.StopPropagation();
    }
    #endregion

    public bool CheckFrameIndexOnDrag(int targetIndex, int selfIndex, bool isLeft)
    {
        if (AnimationData == null || AnimationData.FrameData == null) return false;
        var config = SkillEditorWindow.Instance.SkillConfig;
        var original = SkillTimelineData.Read(config).FirstOrDefault(e => e.Kind == SkillEventKind.Animation && e.Frame == selfIndex);
        if (original == null) return false;
        var proposed = SkillTimelineData.Clone(original).At(isLeft ? targetIndex : selfIndex);
        ((SkillAnimationEvent)proposed.Data).DurationFrame = isLeft
            ? selfIndex + original.Duration(config.FrameRote) - targetIndex : targetIndex - selfIndex;
        return ((SkillAnimationEvent)proposed.Data).DurationFrame > 0 &&
            SkillTimelineData.ValidatePlacement(config, new[] { proposed }, new[] { original.Data }, out _);
    }

    /// <summary>
    /// 将oldIndex的数据变为newIndex
    /// </summary>
    public void SetFrameIndex(int oldIndex, int newIndex)
    {
        if (AnimationData == null || AnimationData.FrameData == null) return;
        if (AnimationData.FrameData.Remove(oldIndex, out SkillAnimationEvent animationEvent))
        {
            AnimationData.FrameData.Add(newIndex, animationEvent);
            trackItemDic.Remove(oldIndex, out AnimationTrackItem animationTrackItem);
            trackItemDic.Add(newIndex, animationTrackItem);
        }
    }

    public override void DeleteTrackItem(int frameIndex)
    {
        if (AnimationData == null || AnimationData.FrameData == null) return;
        AnimationData.FrameData.Remove(frameIndex);
        if (trackItemDic.Remove(frameIndex, out AnimationTrackItem item))
        {
            trackStyle.DeleteItem(item.itemStyle.root);
        }
    }

    public override void OnConfigChanged()
    {
        foreach (var item in trackItemDic.Values)
        {
            item.OnConfigChanged();
        }
    }


    public Vector3 GetPostionForRootMotion(int frameIndex, bool recove = false)
    {
        var window = SkillEditorWindow.Instance;
        return window.PreviewSession == null ? Vector3.zero : window.PreviewSession.GetRootPosition(window.SkillConfig, frameIndex);
    }

    public override System.Collections.Generic.IEnumerable<TrackItemBase> Items => trackItemDic.Values;
    public override TrackItemBase FindItemByData(object data) =>
        trackItemDic.Values.FirstOrDefault(item => ReferenceEquals(item.AnimationEvent, data));

    public override void TickView(int frameIndex)
    {
        var window = SkillEditorWindow.Instance;
        if (Model == null) window.PreviewSession?.Evaluate(window.SkillConfig, frameIndex);
    }

    public override void Destory() => trackStyle?.Destory();
}

}

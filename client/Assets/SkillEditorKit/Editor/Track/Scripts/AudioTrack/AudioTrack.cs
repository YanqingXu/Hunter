// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

public class AudioTrack : SkillTrackBase
{
    private SkillMultilineTrackStyle trackStyle;
    public SkillAudioData AudioData { get => Model != null ? new SkillAudioData { FrameData = Model.Clips.Select(c => (SkillAudioEvent)c.Data).ToList() } : SkillEditorWindow.Instance == null || SkillEditorWindow.Instance.SkillConfig == null ? null : SkillEditorWindow.Instance.SkillConfig.SkillAudioData; }
    private List<AudioTrackItem> trackItemList = new List<AudioTrackItem>();
    public override void Init(VisualElement menuParent, VisualElement trackParent, float frameWdith)
    {
        base.Init(menuParent, trackParent, frameWdith);
        if (Model != null) InitLane(menuParent, trackParent);
        else
        {
        trackStyle = new SkillMultilineTrackStyle();
        trackStyle.Init(menuParent, trackParent, "音效配置", AddChildTrack, CheckDeleteChildTrack, SwapChildTrack, UpdateChildTrackName);
        }
        ResetView();
    }

    public override void ResetView(float frameWidth)
    {
        base.ResetView(frameWidth);

        // 销毁已有的
        foreach (AudioTrackItem item in trackItemList)
        {
            item.Destroy();
        }
        trackItemList.Clear();
        if (AudioData == null || AudioData.FrameData == null) return;

        // 根据数据绘制TrackItem
        foreach (SkillAudioEvent item in AudioData.FrameData)
        {
            if (item == null) continue;
            CreateItem(item);
        }
        Lane?.LayoutItems(Items);
    }

    private void CreateItem(SkillAudioEvent audioEvent)
    {
        AudioTrackItem item = new AudioTrackItem();
        item.Init(this, frameWidth, audioEvent, Model == null ? trackStyle.AddChildTrack() : SkillMultilineTrackStyle.ChildTrack.Shared(Lane.contentRoot));
        item.SetTrackName(audioEvent.TrackName);
        trackItemList.Add(item);
    }

    private void UpdateChildTrackName(SkillMultilineTrackStyle.ChildTrack childTrack, string newName)
    {
        int index = childTrack.GetIndex();
        if (index < 0 || index >= trackItemList.Count) return;
        var data = trackItemList[index].SkillAudioEvent;
        SkillEditorChangeUtility.Apply("重命名音效轨道", () => data.TrackName = newName);
    }

    private void AddChildTrack()
    {
        var data = new SkillAudioEvent();
        SkillTimelineData.AssignNewIdIfMissing(data);
        if (SkillEditorChangeUtility.Apply("新增音效轨道", () =>
            SkillTimelineData.Add(SkillEditorWindow.Instance.SkillConfig, new SkillEventEntry(SkillEventKind.Audio, data, 0, 0))))
        {
            CreateItem(data);
            SkillEditorWindow.Instance.TimelineSelection.SelectData(new[] { data });
        }
    }

    private bool CheckDeleteChildTrack(int index)
    {
        if (index < 0 || index >= trackItemList.Count) return false;
        var data = trackItemList[index].SkillAudioEvent;
        if (!SkillEditorChangeUtility.Apply("删除音效轨道", () => AudioData.FrameData.Remove(data))) return false;
        
        // The style owns removal of its row. Do not destroy the same row twice.
        trackItemList.RemoveAt(index);
        SkillEditorWindow.Instance.TimelineSelection.Rebind();
        return true;
    }

    private void SwapChildTrack(int index1, int index2)
    {
        if (index1 < 0 || index2 < 0 || index1 >= trackItemList.Count || index2 >= trackItemList.Count) return;
        var selected = SkillEditorWindow.Instance.TimelineSelection.Entries.ConvertAll(e => e.Data);
        int row1 = AudioData.FrameData.IndexOf(trackItemList[index1].SkillAudioEvent);
        int row2 = AudioData.FrameData.IndexOf(trackItemList[index2].SkillAudioEvent);
        if (row1 < 0 || row2 < 0) return;
        if (!SkillEditorChangeUtility.Apply("调整音效轨道顺序", () =>
        {
            var first = AudioData.FrameData[row1];
            AudioData.FrameData[row1] = AudioData.FrameData[row2];
            AudioData.FrameData[row2] = first;
        })) return;
        var item = trackItemList[index1];
        trackItemList[index1] = trackItemList[index2];
        trackItemList[index2] = item;
        SkillEditorWindow.Instance.TimelineSelection.SelectData(selected);
    }

    public override void Destory()
    {
        EditorAudioUnility.StopAllAudios();
        trackStyle?.Destory();
        Lane?.Destory();
    }

    private int lastPreviewFrame = -1;
    private void PlayAt(SkillAudioEvent data, int frame)
    {
        if (data == null || data.AudioClip == null || data.AudioClip.length <= 0) return;
        int rate = Mathf.Max(1, SkillEditorWindow.Instance.SkillConfig.FrameRote);
        int start = StartFrame(data, data.FrameIndex);
        if (!SkillMediaTiming.Validate(data, rate, out _) || frame < start ||
            (long)frame >= (long)start + SkillMediaTiming.AudioFrames(data, rate)) return;
        float elapsed = (frame - start) / (float)rate;
        float sourceTime = SkillMediaTiming.ClipIn(data) + elapsed;
        if (sourceTime >= data.AudioClip.length) return;
        EditorAudioUnility.PlayAudio(data.AudioClip, sourceTime / data.AudioClip.length, data.Voluem,
            Mathf.Max(0, SkillMediaTiming.DurationSeconds(data, rate) - elapsed), SkillEditorWindow.Instance.PlaybackSpeed);
    }

    public override void OnPlay(int startFrameIndex)
    {
        if (Model == null) EditorAudioUnility.StopAllAudios();
        lastPreviewFrame = startFrameIndex;
        if (AudioData?.FrameData == null) return;
        foreach (var data in AudioData.FrameData) PlayAt(data, startFrameIndex);
    }

    public override void TickView(int frameIndex)
    {
        if (AudioData?.FrameData != null && SkillEditorWindow.Instance.IsPlaying)
        {
            if (frameIndex < lastPreviewFrame) { OnPlay(frameIndex); return; }
            foreach (var data in AudioData.FrameData)
                if (data != null && StartFrame(data, data.FrameIndex) > lastPreviewFrame && StartFrame(data, data.FrameIndex) <= frameIndex) PlayAt(data, frameIndex);
            lastPreviewFrame = frameIndex;
        }
    }

    public override void OnStop()
    {
        EditorAudioUnility.StopAllAudios();
        lastPreviewFrame = -1;
    }

    public override System.Collections.Generic.IEnumerable<TrackItemBase> Items => trackItemList;

    public override TrackItemBase FindItemByData(object data)
    {
        return trackItemList.Find(item => ReferenceEquals(item.SkillAudioEvent, data));
    }

}

}

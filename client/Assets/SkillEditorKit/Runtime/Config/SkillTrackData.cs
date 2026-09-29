// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using System.Linq;

public enum SkillEventKind { Custom, Animation, Audio, Effect, Attack, Projectile }

[Serializable]
public sealed class SkillTrackData
{
    public string TrackId = Guid.NewGuid().ToString("N");
    public string Name;
    public SkillEventKind Kind;
    public bool Enabled = true;
    public bool Locked;
    public List<SkillTrackClip> Clips = new List<SkillTrackClip>();
}

[Serializable]
public sealed class SkillTrackClip
{
    public int Frame;
    public SkillFrameEventBase Data;
}

/// <summary>Runtime/editor shared read path; never mutates the source asset.</summary>
public static class SkillTrackModel
{
    public sealed class Entry
    {
        public SkillEventKind Kind;
        public SkillFrameEventBase Data;
        public int Frame, Row;
        public string TrackId;
    }

    public static IEnumerable<Entry> Read(SkillClip clip, bool enabledOnly = false)
    {
        if (clip == null) yield break;
        if (clip.UseTrackModel)
        {
            if (clip.Tracks == null) yield break;
            int row = 0;
            foreach (var track in clip.Tracks)
            {
                if (track == null || track.Clips == null) continue;
                foreach (var item in track.Clips)
                {
                    int index = row++;
                    if (item?.Data == null || (enabledOnly && !track.Enabled)) continue;
                    yield return new Entry { Kind = track.Kind, Data = item.Data, Frame = item.Frame, Row = index, TrackId = track.TrackId };
                }
            }
            yield break;
        }
        if (clip.skillCustomEventData?.FrameData != null)
            foreach (var p in clip.skillCustomEventData.FrameData.OrderBy(p => p.Key))
                if (p.Value != null) yield return new Entry { Kind = SkillEventKind.Custom, Data = p.Value, Frame = p.Key, Row = p.Key };
        if (clip.SkillAnimationData?.FrameData != null)
            foreach (var p in clip.SkillAnimationData.FrameData.OrderBy(p => p.Key))
                if (p.Value != null) yield return new Entry { Kind = SkillEventKind.Animation, Data = p.Value, Frame = p.Key, Row = p.Key };
        if (clip.SkillAudioData?.FrameData != null)
            for (int i = 0; i < clip.SkillAudioData.FrameData.Count; i++)
            { var d = clip.SkillAudioData.FrameData[i]; if (d != null) yield return new Entry { Kind = SkillEventKind.Audio, Data = d, Frame = d.FrameIndex, Row = i }; }
        if (clip.SkillEffectData?.FrameData != null)
            for (int i = 0; i < clip.SkillEffectData.FrameData.Count; i++)
            { var d = clip.SkillEffectData.FrameData[i]; if (d != null) yield return new Entry { Kind = SkillEventKind.Effect, Data = d, Frame = d.FrameIndex, Row = i }; }
        if (clip.SkillAttackDetectionData?.FrameData != null)
            for (int i = 0; i < clip.SkillAttackDetectionData.FrameData.Count; i++)
            { var d = clip.SkillAttackDetectionData.FrameData[i]; if (d != null) yield return new Entry { Kind = SkillEventKind.Attack, Data = d, Frame = d.FrameIndex, Row = i }; }
    }

    public static bool Matches(SkillEventKind kind, SkillFrameEventBase data)
    {
        switch (kind)
        {
            case SkillEventKind.Custom: return data is SkillCustomEvent;
            case SkillEventKind.Animation: return data is SkillAnimationEvent;
            case SkillEventKind.Audio: return data is SkillAudioEvent;
            case SkillEventKind.Effect: return data is SkillEffectEvent;
            case SkillEventKind.Attack: return data is SkillAttackDetectionEvent;
            case SkillEventKind.Projectile: return data is SkillProjectileEvent;
            default: return false;
        }
    }

    public static bool ValidateStructure(SkillClip clip, out string reason)
    {
        reason = null;
        if (!clip.UseTrackModel) return true;
        if (clip.Tracks == null) { reason = "轨道列表缺失"; return false; }
        if (clip.skillCustomEventData?.FrameData?.Count > 0 || clip.SkillAnimationData?.FrameData?.Count > 0 ||
            clip.SkillAudioData?.FrameData?.Count > 0 || clip.SkillEffectData?.FrameData?.Count > 0 || clip.SkillAttackDetectionData?.FrameData?.Count > 0)
        { reason = "新轨道模型与旧容器同时包含数据；请检查写入来源，禁止静默忽略旧数据"; return false; }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var objects = new HashSet<SkillFrameEventBase>();
        int animationCount = 0;
        foreach (var track in clip.Tracks)
        {
            if (track == null || track.Clips == null) { reason = "存在空轨道或空片段列表"; return false; }
            if (string.IsNullOrEmpty(track.TrackId) || !ids.Add(track.TrackId)) { reason = "轨道 ID 缺失或重复"; return false; }
            if (!Enum.IsDefined(typeof(SkillEventKind), track.Kind)) { reason = "未知轨道类型"; return false; }
            if (track.Kind == SkillEventKind.Animation && ++animationCount > 1) { reason = "目前只支持一条主动画轨道"; return false; }
            var frames = new HashSet<int>();
            foreach (var item in track.Clips)
            {
                if (item?.Data == null || !Matches(track.Kind, item.Data)) { reason = "轨道内包含空片段或不匹配的片段类型"; return false; }
                if (!objects.Add(item.Data)) { reason = "多个片段共享同一个事件对象"; return false; }
                if ((track.Kind == SkillEventKind.Custom || track.Kind == SkillEventKind.Animation) && !frames.Add(item.Frame))
                { reason = "同一事件/动画轨道存在重复起始帧"; return false; }
            }
        }
        return true;
    }

    // Frame on SkillTrackClip is authoritative. Mirrors keep legacy event APIs compatible.
    public static void SetFrame(SkillFrameEventBase data, int frame)
    {
        if (data is SkillAudioEvent audio) audio.FrameIndex = frame;
        else if (data is SkillEffectEvent effect) effect.FrameIndex = frame;
        else if (data is SkillAttackDetectionEvent attack) attack.FrameIndex = frame;
        else if (data is SkillProjectileEvent projectile) projectile.FrameIndex = frame;
    }

    public static Dictionary<int, SkillAnimationEvent> Animation(SkillClip clip)
        => Read(clip, true).Where(e => e.Kind == SkillEventKind.Animation).ToDictionary(e => e.Frame, e => (SkillAnimationEvent)e.Data);
}

}

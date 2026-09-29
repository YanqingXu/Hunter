// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Linq;
using UnityEngine;

/// <summary>Pure trim planning. Only Commit writes data, once per mouse gesture.</summary>
public sealed class SkillClipTrim
{
    public SkillEventEntry Original { get; private set; }
    public SkillEventEntry Proposed { get; private set; }
    private SkillClip clip;
    private int rate, oldDuration;
    private float oldIn, sourceLength;
    private bool oldRange;
    private UnityEngine.Object source;
    public static bool Supports(SkillFrameEventBase data) => data is SkillAnimationEvent || data is SkillAudioEvent ||
        data is SkillEffectEvent || data is SkillAttackDetectionEvent || (data is SkillProjectileEvent projectile &&
            projectile.Flight == SkillProjectileFlight.Straight && projectile.Timing == SkillProjectileTiming.DistanceAndTime);

    public static bool Plan(SkillClip clip, SkillEventEntry entry, bool left, int delta, out SkillClipTrim plan, out string reason)
    {
        plan = null;
        if (!SkillTimelineData.IsEditable(clip, out reason) || entry == null || !Supports(entry.Data)) return false;
        if (left && entry.Data is SkillProjectileEvent) { reason = "投射物发射点请整体移动，右端拖动用于调整飞行时长。"; return false; }
        if (clip.FrameRote <= 0) { reason = "请先设置有效技能帧率"; return false; }
        int duration = entry.Duration(clip.FrameRote);
        bool media = entry.Data is SkillAnimationEvent || entry.Data is SkillAudioEvent;
        float clipIn = SkillMediaTiming.ClipIn(entry.Data), length = SkillMediaTiming.SourceLength(entry.Data);
        if (media && (length <= 0 || float.IsNaN(clipIn) || float.IsInfinity(clipIn) || clipIn < 0))
        { reason = "请先指定有效动画或音频资源"; return false; }
        long min = left ? -(long)entry.Frame : 1L - duration;
        long max = left ? duration - 1L : SkillTimelineData.MaxFrame - (long)entry.Frame - duration;
        if (entry.Data is SkillProjectileEvent flight) max = Math.Min(max, (long)Math.Floor(flight.MaxLifetime * clip.FrameRote) - duration);
        if (media)
        {
            if (left) min = Math.Max(min, -(long)Math.Floor((double)clipIn * clip.FrameRote + 0.00001));
            else max = Math.Min(max, (long)Math.Ceiling((double)(length - clipIn) * clip.FrameRote - 0.00001) - duration);
        }
        if (min > max) { reason = "没有可用裁剪范围"; return false; }
        int amount = (int)Math.Max(min, Math.Min(max, delta));
        int frame = left ? entry.Frame + amount : entry.Frame;
        int newDuration = left ? duration - amount : duration + amount;
        var copy = SkillTimelineData.Clone(entry).At(frame);
        copy.Data.EventId = entry.Data.EventId;
        if (media) SkillMediaTiming.SetRange(copy.Data, Mathf.Max(0, clipIn + (left ? amount / (float)clip.FrameRote : 0)), newDuration);
        else SetDuration(copy.Data, newDuration);
        if (!SkillTimelineData.ValidatePlacement(clip, new[] { copy }, new[] { entry.Data }, out reason)) return false;
        plan = new SkillClipTrim { clip = clip, Original = entry, Proposed = copy, rate = clip.FrameRote,
            oldDuration = duration, oldIn = clipIn, sourceLength = length, source = SkillMediaTiming.Source(entry.Data), oldRange = SkillMediaTiming.HasRange(entry.Data) };
        return true;
    }

    public bool HasChanges => Original.Frame != Proposed.Frame || oldDuration != Proposed.Duration(rate) ||
        Math.Abs(oldIn - SkillMediaTiming.ClipIn(Proposed.Data)) > 0.000001f;

    public bool IsCurrent() => clip != null && clip.FrameRote == rate &&
        SkillTimelineData.Read(clip).Any(e => ReferenceEquals(e.Data, Original.Data) && e.Frame == Original.Frame && e.TrackId == Original.TrackId) &&
        Original.Duration(rate) == oldDuration && SkillMediaTiming.ClipIn(Original.Data) == oldIn &&
        SkillMediaTiming.SourceLength(Original.Data) == sourceLength && SkillMediaTiming.Source(Original.Data) == source && SkillMediaTiming.HasRange(Original.Data) == oldRange;

    public void Apply()
    {
        if (!IsCurrent() || !SkillTimelineData.ValidatePlacement(clip, new[] { Proposed }, new[] { Original.Data }, out string reason))
            throw new InvalidOperationException("裁剪目标已变化，请重新拖动。");
        if (SkillMediaTiming.HasRange(Proposed.Data))
            SkillMediaTiming.SetRange(Original.Data, SkillMediaTiming.ClipIn(Proposed.Data), Proposed.Duration(rate));
        else SetDuration(Original.Data, Proposed.Duration(rate));
        SkillTimelineData.ApplyMove(clip, new[] { Original }, new[] { Original.At(Proposed.Frame) });
    }

    public bool Commit()
    {
        if (!HasChanges || !IsCurrent()) return false;
        return SkillEditorClipboard.Commit("裁剪技能片段", Apply, new[] { Original.At(Proposed.Frame) });
    }

    private static void SetDuration(SkillFrameEventBase data, int duration)
    {
        if (data is SkillEffectEvent e) e.Duration = duration;
        else if (data is SkillAttackDetectionEvent a) a.DurationFrame = duration;
        else if (data is SkillProjectileEvent p) p.FlightFrames = duration;
    }
}

}

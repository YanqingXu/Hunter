// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

/// <summary>Shared by editor and player; ClipIn is source seconds, duration is skill frames.</summary>
public static class SkillMediaTiming
{
    public static UnityEngine.Object Source(SkillFrameEventBase data) =>
        data is SkillAnimationEvent a ? (UnityEngine.Object)a.AnimationClip : data is SkillAudioEvent b ? b.AudioClip : null;
    public static double AnimationRangeTime(AnimationClip clip, double time) => Math.Max(0,
        Math.Min(time, clip.isLooping ? Math.Max(0, clip.length - 0.00001) : clip.length));
    public static bool HasRange(SkillFrameEventBase data) =>
        data is SkillAnimationEvent a ? a.UseClipRange : data is SkillAudioEvent b && b.UseClipRange;
    public static float ClipIn(SkillFrameEventBase data) => !HasRange(data) ? 0 :
        data is SkillAnimationEvent a ? a.ClipIn : ((SkillAudioEvent)data).ClipIn;
    public static float SourceLength(SkillFrameEventBase data) =>
        data is SkillAnimationEvent a ? (a.AnimationClip == null ? 0 : a.AnimationClip.length) :
        data is SkillAudioEvent b && b.AudioClip != null ? b.AudioClip.length : 0;
    public static int AudioFrames(SkillAudioEvent data, int rate) => data.UseClipRange
        ? Math.Max(1, data.DurationFrame) : Math.Max(1, Mathf.CeilToInt(SourceLength(data) * Math.Max(1, rate)));
    public static float DurationSeconds(SkillFrameEventBase data, int rate) =>
        HasRange(data) ? (data is SkillAnimationEvent a ? a.DurationFrame : ((SkillAudioEvent)data).DurationFrame) / (float)Math.Max(1, rate)
        : SourceLength(data);
    public static void SetRange(SkillFrameEventBase data, float clipIn, int duration)
    {
        if (data is SkillAnimationEvent a) { a.UseClipRange = true; a.ClipIn = clipIn; a.DurationFrame = duration; }
        else if (data is SkillAudioEvent b) { b.UseClipRange = true; b.ClipIn = clipIn; b.DurationFrame = duration; }
    }
    public static bool Validate(SkillFrameEventBase data, int rate, out string reason)
    {
        reason = null;
        if (data is SkillProjectileEvent projectile) return SkillProjectileEvent.Validate(projectile, rate, out reason);
        if (!HasRange(data)) return true;
        float start = ClipIn(data), length = SourceLength(data);
        int frames = data is SkillAnimationEvent a ? a.DurationFrame : ((SkillAudioEvent)data).DurationFrame;
        // At most the last partial frame may extend beyond the source. No loop extension in trim mode.
        if (rate <= 0 || float.IsNaN(start) || float.IsInfinity(start) || start < 0 || length <= 0 ||
            start >= length || frames <= 0 || frames > Math.Ceiling((double)(length - start) * Math.Max(1, rate) - 0.00001))
            reason = "裁剪范围无效：需要有效资源，源起点不得为负，终点不能超出资源末帧。";
        return reason == null;
    }
}

}

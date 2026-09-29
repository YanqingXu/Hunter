// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>One timing contract for the editor and the skill-clock animation player.</summary>
public static class SkillAnimationTiming
{
    public static double Duration(SkillAnimationEvent data, int rate) =>
        Math.Max(1, data.DurationFrame) / (double)Math.Max(1, rate);

    public static long EndFrame(int start, SkillAnimationEvent data) => (long)start + Math.Max(1, data.DurationFrame);

    public static int OverlapFrames(int previousStart, SkillAnimationEvent previous, int start, SkillAnimationEvent current)
    {
        if (previous?.AnimationClip == null || current?.AnimationClip == null || previousStart >= start) return 0;
        return (int)Math.Max(0, Math.Min(EndFrame(previousStart, previous), EndFrame(start, current)) - start);
    }

    public static float BlendWeight(double elapsed, double overlapSeconds) => overlapSeconds <= 0 ? 1 :
        Mathf.Clamp01((float)(Math.Max(0, elapsed) / overlapSeconds));

    public static KeyValuePair<int, SkillAnimationEvent> Previous(IEnumerable<KeyValuePair<int, SkillAnimationEvent>> entries, int start) =>
        entries.Where(p => p.Key < start && p.Value?.AnimationClip != null).OrderBy(p => p.Key).LastOrDefault();

    /// <summary>Two-clip crossfades only. Ordered ends prevent ambiguous contained clips or a return to an older clip.</summary>
    public static bool ValidateSequence(IEnumerable<KeyValuePair<int, SkillAnimationEvent>> entries, out string reason)
    {
        var ordered = entries.Where(p => p.Value != null).OrderBy(p => p.Key).ToList();
        reason = null;
        for (int i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1]; var current = ordered[i];
            if (current.Key == previous.Key)
                reason = $"第 {current.Key} 帧已有动画；两个动画不能同帧开始";
            else if (EndFrame(current.Key, current.Value) <= EndFrame(previous.Key, previous.Value))
                reason = $"动画@{current.Key} 被前一片段完全包含；请让后一个片段的末尾超出前一个片段";
            else if (i >= 2 && current.Key < EndFrame(ordered[i - 2].Key, ordered[i - 2].Value))
                reason = $"第 {current.Key} 帧出现三个动画同时重叠；目前只支持两段交叠融合";
            if (reason != null) return false;
        }
        return true;
    }

    public static SkillAnimationEvent Snapshot(SkillAnimationEvent data) => data == null ? null : new SkillAnimationEvent
    { AnimationClip = data.AnimationClip, UseClipRange = data.UseClipRange, ClipIn = data.ClipIn, DurationFrame = data.DurationFrame,
        ApplyRootMotion = data.ApplyRootMotion, MainWeaponOnLeftHand = data.MainWeaponOnLeftHand };

    public static double SourceTime(SkillAnimationEvent data, int rate, double elapsed)
    {
        var clip = data.AnimationClip;
        if (clip == null || clip.length <= 0) return 0;
        double time = Math.Max(0, Math.Min(Duration(data, rate), elapsed));
        if (data.UseClipRange) return SkillMediaTiming.AnimationRangeTime(clip, data.ClipIn + time);
        // Playable time stays unwrapped for looping sources so root motion and events
        // can cross a loop boundary. The preview sampler wraps only when sampling a clip.
        return clip.isLooping ? time : Math.Min(time, clip.length);
    }

    public static float PreviewSourceTime(SkillAnimationEvent data, int rate, double elapsed)
    {
        double time = SourceTime(data, rate, elapsed);
        if (!data.UseClipRange && data.AnimationClip != null && data.AnimationClip.isLooping && data.AnimationClip.length > 0)
            time %= data.AnimationClip.length;
        return (float)time;
    }
}

}

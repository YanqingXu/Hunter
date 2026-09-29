// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;



/// <summary>A read-only address of an event in the current asset, not a saved copy.</summary>
public sealed class SkillEventEntry
{
    public readonly SkillEventKind Kind;
    public readonly SkillFrameEventBase Data;
    public readonly int Frame;
    public readonly int Row;
    public readonly string TrackId;
    internal bool UseLegacyKey;
    public string Key => UseLegacyKey || string.IsNullOrEmpty(Data.EventId) ? $"legacy:{Kind}:{TrackId}:{Row}" : Data.EventId;

    public SkillEventEntry(SkillEventKind kind, SkillFrameEventBase data, int frame, int row, string trackId = null)
    {
        Kind = kind; Data = data; Frame = frame; Row = row; TrackId = trackId;
    }

    public int Duration(int rate)
    {
        if (Data is SkillAnimationEvent animation) return Math.Max(1, animation.DurationFrame);
        if (Data is SkillEffectEvent effect) return Math.Max(1, effect.Duration);
        if (Data is SkillAttackDetectionEvent attack) return Math.Max(1, attack.DurationFrame);
        if (Data is SkillAudioEvent audio) return SkillMediaTiming.AudioFrames(audio, rate);
        if (Data is SkillProjectileEvent projectile) return projectile.PreviewFrames(rate);
        return 1;
    }

    public SkillEventEntry At(int frame) => new SkillEventEntry(Kind, Data, frame, Row, TrackId);
    public SkillEventEntry OnTrack(string trackId) => new SkillEventEntry(Kind, Data, Frame, Row, trackId);
}

/// <summary>Data operations shared by keyboard, clipboard, Inspector and dragging.</summary>
public static class SkillTimelineData
{
    public const int MaxFrame = 1000000;

    public static List<SkillEventEntry> Read(SkillClip clip)
    {
        var result = SkillTrackModel.Read(clip).Select(e => new SkillEventEntry(e.Kind, e.Data, e.Frame, e.Row, e.TrackId)).ToList();
        // Malformed duplicate IDs must not select multiple unrelated events together.
        foreach (var group in result.Where(e => !string.IsNullOrEmpty(e.Data.EventId)).GroupBy(e => e.Data.EventId, StringComparer.OrdinalIgnoreCase))
            if (group.Count() > 1) foreach (var entry in group) entry.UseLegacyKey = true;
        return result;
    }

    public static bool IsEditable(SkillClip clip, out string reason)
    {
        reason = null;
        if (clip == null) reason = "没有加载技能配置";
        else if (clip.DataVersion > SkillClip.CurrentDataVersion) reason = "此配置由更新版本的编辑器保存，当前仅可查看";
        else if (clip.UseTrackModel && !SkillTrackModel.ValidateStructure(clip, out reason)) return false;
        else if (!clip.UseTrackModel && (clip.skillCustomEventData?.FrameData == null || clip.SkillAnimationData?.FrameData == null ||
                 clip.SkillAudioData?.FrameData == null || clip.SkillEffectData?.FrameData == null ||
                 clip.SkillAttackDetectionData?.FrameData == null)) reason = "轨道数据缺失，请先校验配置";
        if (reason == null)
        {
            var entries = Read(clip);
            if (entries.Select(e => e.Data).Distinct().Count() != entries.Count)
                reason = "多个轨道项引用同一个事件对象，请先拆分共享数据";
        }
        return reason == null;
    }

    public static bool ValidatePlacement(SkillClip clip, IList<SkillEventEntry> proposed,
        ICollection<SkillFrameEventBase> removed, out string reason)
    {
        if (!IsEditable(clip, out reason)) return false;
        var all = Read(clip);
        if (removed != null && all.Any(e => removed.Contains(e.Data) && IsLocked(clip, e)))
        { reason = "选中片段所属轨道已锁定"; return false; }
        var existing = all.Where(e => removed == null || !removed.Contains(e.Data)).ToList();
        for (int i = 0; i < proposed.Count; i++)
        {
            var entry = proposed[i];
            if (clip.UseTrackModel)
            {
                var owner = FindTrack(clip, entry.TrackId);
                if (owner == null || owner.Kind != entry.Kind || !SkillTrackModel.Matches(entry.Kind, entry.Data))
                { reason = "请先选择匹配类型的目标轨道"; return false; }
                if (owner.Locked) { reason = "目标轨道已锁定"; return false; }
            }
            if (!SkillMediaTiming.Validate(entry.Data, clip.FrameRote, out reason)) return false;
            // A malformed null-valued dictionary slot still occupies its key.
            if (!clip.UseTrackModel && ((entry.Kind == SkillEventKind.Custom && clip.skillCustomEventData.FrameData.TryGetValue(entry.Frame, out var custom) && custom == null) ||
                (entry.Kind == SkillEventKind.Animation && clip.SkillAnimationData.FrameData.TryGetValue(entry.Frame, out var animation) && animation == null)))
            { reason = $"第 {entry.Frame} 帧有空数据，请先校验并修复该条目"; return false; }
            if (entry.Frame < 0 || (long)entry.Frame + entry.Duration(clip.FrameRote) > MaxFrame)
            {
                reason = $"事件超出可编辑范围 0～{MaxFrame} 帧"; return false;
            }
            foreach (var other in existing.Concat(proposed.Take(i)))
            {
                if (entry.Kind != other.Kind || (clip.UseTrackModel && entry.TrackId != other.TrackId)) continue;
                if (entry.Kind == SkillEventKind.Custom && entry.Frame == other.Frame)
                { reason = $"第 {entry.Frame} 帧已有自定义事件"; return false; }
            }
        }
        foreach (var lane in existing.Concat(proposed).Where(e => e.Kind == SkillEventKind.Animation).GroupBy(e => e.TrackId))
            if (!SkillAnimationTiming.ValidateSequence(lane.Select(e => new KeyValuePair<int, SkillAnimationEvent>(e.Frame, (SkillAnimationEvent)e.Data)), out reason))
                return false;
        return true;
    }

    public static bool PlanMove(SkillClip clip, IList<SkillEventEntry> selected, int delta,
        out List<SkillEventEntry> proposed, out string reason, string targetTrackId = null)
    {
        proposed = new List<SkillEventEntry>();
        reason = null;
        if (selected.Count == 0) return false;
        var current = Read(clip);
        if (selected.Any(e => !current.Any(c => ReferenceEquals(c.Data, e.Data) && c.Frame == e.Frame && c.TrackId == e.TrackId)))
        { reason = "选中事件已变化，请重新选择"; return false; }
        long clamped = Math.Max((long)delta, -(long)selected.Min(e => e.Frame));
        foreach (var entry in selected)
        {
            long frame = entry.Frame + clamped;
            if (frame < 0 || frame > MaxFrame) { reason = "移动超过时间轴范围"; return false; }
            proposed.Add(targetTrackId == null ? entry.At((int)frame) : entry.At((int)frame).OnTrack(targetTrackId));
        }
        return ValidatePlacement(clip, proposed, selected.Select(e => e.Data).ToList(), out reason);
    }

    public static void ApplyMove(SkillClip clip, IList<SkillEventEntry> selected, IList<SkillEventEntry> proposed)
    {
        var current = Read(clip);
        if (selected.Count != proposed.Count || selected.Select(e => e.Data).Distinct().Count() != selected.Count ||
            selected.Any(e => !current.Any(c => ReferenceEquals(c.Data, e.Data) && c.Frame == e.Frame && c.TrackId == e.TrackId)) ||
            proposed.Any(e => !selected.Any(s => ReferenceEquals(s.Data, e.Data))))
            throw new InvalidOperationException("移动目标已变化，请重新选择");
        if (!ValidatePlacement(clip, proposed, selected.Select(e => e.Data).ToList(), out string reason))
            throw new InvalidOperationException(reason);
        if (clip.UseTrackModel)
        {
            foreach (var entry in proposed)
            {
                var original = selected.First(e => ReferenceEquals(e.Data, entry.Data));
                var source = FindTrack(clip, original.TrackId);
                var item = source.Clips.First(c => ReferenceEquals(c.Data, entry.Data));
                if (original.TrackId != entry.TrackId)
                {
                    source.Clips.Remove(item);
                    FindTrack(clip, entry.TrackId).Clips.Add(item);
                }
                item.Frame = entry.Frame;
                SkillTrackModel.SetFrame(item.Data, entry.Frame);
                AssignNewIdIfMissing(item.Data);
                Extend(clip, entry);
            }
            return;
        }
        // Remove every dictionary key first, so a group can move into its own old positions.
        foreach (var entry in selected)
        {
            if (entry.Kind == SkillEventKind.Custom) clip.skillCustomEventData.FrameData.Remove(entry.Frame);
            if (entry.Kind == SkillEventKind.Animation) clip.SkillAnimationData.FrameData.Remove(entry.Frame);
        }
        foreach (var entry in proposed)
        {
            AssignNewIdIfMissing(entry.Data);
            switch (entry.Kind)
            {
                case SkillEventKind.Custom: clip.skillCustomEventData.FrameData.Add(entry.Frame, (SkillCustomEvent)entry.Data); break;
                case SkillEventKind.Animation: clip.SkillAnimationData.FrameData.Add(entry.Frame, (SkillAnimationEvent)entry.Data); break;
                case SkillEventKind.Audio: ((SkillAudioEvent)entry.Data).FrameIndex = entry.Frame; break;
                case SkillEventKind.Effect: ((SkillEffectEvent)entry.Data).FrameIndex = entry.Frame; break;
                case SkillEventKind.Attack: ((SkillAttackDetectionEvent)entry.Data).FrameIndex = entry.Frame; break;
            }
            Extend(clip, entry);
        }
    }

    public static SkillTrackData FindTrack(SkillClip clip, string id) => clip?.Tracks?.FirstOrDefault(t => t != null && t.TrackId == id);
    public static bool IsLocked(SkillClip clip, SkillEventEntry entry) => clip.UseTrackModel && FindTrack(clip, entry.TrackId)?.Locked == true;

    public static void Add(SkillClip clip, SkillEventEntry entry)
    {
        if (!clip.UseTrackModel && entry.Kind == SkillEventKind.Projectile)
            throw new InvalidOperationException("投射物需要独立轨道；请先升级轨道模型。");
        AssignNewIdIfMissing(entry.Data);
        if (clip.UseTrackModel)
        {
            var track = FindTrack(clip, entry.TrackId);
            if (track == null || track.Locked || track.Kind != entry.Kind) throw new InvalidOperationException("无效或已锁定的目标轨道");
            SkillTrackModel.SetFrame(entry.Data, entry.Frame);
            track.Clips.Add(new SkillTrackClip { Frame = entry.Frame, Data = entry.Data });
            Extend(clip, entry);
            return;
        }
        switch (entry.Kind)
        {
            case SkillEventKind.Custom: clip.skillCustomEventData.FrameData.Add(entry.Frame, (SkillCustomEvent)entry.Data); break;
            case SkillEventKind.Animation: clip.SkillAnimationData.FrameData.Add(entry.Frame, (SkillAnimationEvent)entry.Data); break;
            case SkillEventKind.Audio:
                ((SkillAudioEvent)entry.Data).FrameIndex = entry.Frame;
                clip.SkillAudioData.FrameData.Add((SkillAudioEvent)entry.Data); break;
            case SkillEventKind.Effect:
                ((SkillEffectEvent)entry.Data).FrameIndex = entry.Frame;
                clip.SkillEffectData.FrameData.Add((SkillEffectEvent)entry.Data); break;
            case SkillEventKind.Attack:
                ((SkillAttackDetectionEvent)entry.Data).FrameIndex = entry.Frame;
                clip.SkillAttackDetectionData.FrameData.Add((SkillAttackDetectionEvent)entry.Data); break;
        }
        Extend(clip, entry);
    }

    public static void Remove(SkillClip clip, SkillEventEntry entry)
    {
        if (clip.UseTrackModel)
        {
            var track = FindTrack(clip, entry.TrackId);
            if (track == null || track.Locked) throw new InvalidOperationException("目标轨道不存在或已锁定");
            track.Clips.RemoveAll(c => ReferenceEquals(c.Data, entry.Data));
            return;
        }
        switch (entry.Kind)
        {
            case SkillEventKind.Custom: clip.skillCustomEventData.FrameData.Remove(entry.Frame); break;
            case SkillEventKind.Animation: clip.SkillAnimationData.FrameData.Remove(entry.Frame); break;
            case SkillEventKind.Audio: clip.SkillAudioData.FrameData.Remove((SkillAudioEvent)entry.Data); break;
            case SkillEventKind.Effect: clip.SkillEffectData.FrameData.Remove((SkillEffectEvent)entry.Data); break;
            case SkillEventKind.Attack: clip.SkillAttackDetectionData.FrameData.Remove((SkillAttackDetectionEvent)entry.Data); break;
        }
    }

    private static void Extend(SkillClip clip, SkillEventEntry entry)
    {
        if (SkillMediaTiming.HasRange(entry.Data)) clip.DataVersion = Math.Max(clip.DataVersion, SkillClip.MediaRangeDataVersion);
        if (entry.Kind == SkillEventKind.Projectile) clip.DataVersion = Math.Max(clip.DataVersion, SkillClip.ProjectileDataVersion);
        int end = entry.Kind == SkillEventKind.Custom || entry.Kind == SkillEventKind.Projectile ? entry.Frame : checked(entry.Frame + entry.Duration(clip.FrameRote));
        clip.FrameCount = Math.Max(clip.FrameCount, end);
    }

    public static void AssignNewIdIfMissing(SkillFrameEventBase data)
    {
        if (string.IsNullOrEmpty(data.EventId)) data.EventId = Guid.NewGuid().ToString("N");
    }

    public static SkillEventEntry Clone(SkillEventEntry entry)
    {
        SkillFrameEventBase copy;
        if (entry.Data is SkillCustomEvent c) copy = new SkillCustomEvent
        { EventType = c.EventType, CustomEventName = c.CustomEventName, IntArg = c.IntArg, FloatArg = c.FloatArg, StringArg = c.StringArg, ObjectArg = c.ObjectArg };
        else if (entry.Data is SkillAnimationEvent a) copy = new SkillAnimationEvent
        { AnimationClip = a.AnimationClip, ApplyRootMotion = a.ApplyRootMotion, MainWeaponOnLeftHand = a.MainWeaponOnLeftHand, TransitionTime = a.TransitionTime, DurationFrame = a.DurationFrame, UseClipRange = a.UseClipRange, ClipIn = a.ClipIn };
        else if (entry.Data is SkillAudioEvent audio) copy = new SkillAudioEvent
        { TrackName = audio.TrackName, FrameIndex = entry.Frame, AudioClip = audio.AudioClip, Voluem = audio.Voluem, UseClipRange = audio.UseClipRange, ClipIn = audio.ClipIn, DurationFrame = audio.DurationFrame };
        else if (entry.Data is SkillEffectEvent effect) copy = new SkillEffectEvent
        { TrackName = effect.TrackName, FrameIndex = entry.Frame, Prefab = effect.Prefab, Position = effect.Position, Rotation = effect.Rotation, Scale = effect.Scale, Duration = effect.Duration, AutoDestruct = effect.AutoDestruct };
        else if (entry.Data is SkillAttackDetectionEvent attack)
        {
            var hit = attack.AttackHitConfig;
            copy = new SkillAttackDetectionEvent
            {
                TrackName = attack.TrackName, FrameIndex = entry.Frame, DurationFrame = attack.DurationFrame,
                AttackDetectionData = CloneShape(attack.AttackDetectionData),
                HitRules = attack.HitRules?.Copy(),
                AttackHitConfig = hit == null ? null : new AttackHitConfig
                { AttackMultiply = hit.AttackMultiply, RepelStrength = hit.RepelStrength, RepelTime = hit.RepelTime, HitEffectPrefab = hit.HitEffectPrefab, HitAudioClip = hit.HitAudioClip }
            };
        }
        else if (entry.Data is SkillProjectileEvent projectile) copy = projectile.Copy();
        else throw new NotSupportedException("不支持的事件类型：" + entry.Data.GetType().Name);
        copy.EventId = Guid.NewGuid().ToString("N");
        return new SkillEventEntry(entry.Kind, copy, entry.Frame, entry.Row, entry.TrackId);
    }

    private static AttackDetectionDataBase CloneShape(AttackDetectionDataBase data)
    {
        if (data == null) return null;
        if (data is AttackWeaponDetectionData w) return new AttackWeaponDetectionData { weaponName = w.weaponName };
        AttackShapeDetectionDataBase copy;
        if (data is AttackBoxDetectionData b) copy = new AttackBoxDetectionData { Rotation = b.Rotation, Scale = b.Scale };
        else if (data is AttackSphereDetectionData s) copy = new AttackSphereDetectionData { Radius = s.Radius };
        else if (data is AttackFanDetectionData f) copy = new AttackFanDetectionData
        { Rotation = f.Rotation, Radius = f.Radius, InsideRadius = f.InsideRadius, Height = f.Height, Angle = f.Angle, GroundCircle = f.GroundCircle };
        else throw new NotSupportedException("不支持的攻击形状：" + data.GetType().Name);
        var shape = (AttackShapeDetectionDataBase)data;
        copy.Position = shape.Position; copy.UseFootOrigin = shape.UseFootOrigin; copy.Motion = shape.Motion;
        copy.StartDistance = shape.StartDistance; copy.EndDistance = shape.EndDistance; copy.HeightOffset = shape.HeightOffset;
        copy.Anchor = shape.Anchor; copy.Facing = shape.Facing; copy.SocketPath = shape.SocketPath;
        return copy;
    }
}

}

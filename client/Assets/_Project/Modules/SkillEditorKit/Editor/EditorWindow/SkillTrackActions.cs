// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SkillTrackActions
{
    public static string Label(SkillEventKind kind)
    {
        switch (kind)
        {
            case SkillEventKind.Custom: return "事件";
            case SkillEventKind.Animation: return "主动画";
            case SkillEventKind.Audio: return "音效";
            case SkillEventKind.Effect: return "特效";
            case SkillEventKind.Attack: return "攻击检测";
            case SkillEventKind.Projectile: return "投射物";
            default: return kind.ToString();
        }
    }

    public static void ShowAddMenu()
    {
        var window = SkillEditorWindow.Instance;
        if (window?.SkillConfig == null) return;
        if (!SkillTimelineData.IsEditable(window.SkillConfig, out string reason)) { Debug.LogWarning(reason); return; }
        if (!window.SkillConfig.UseTrackModel && SkillTimelineData.Read(window.SkillConfig).Count > 0)
        { SkillClipMigrationWindow.Open(); return; }
        var menu = new GenericMenu();
        foreach (SkillEventKind kind in Enum.GetValues(typeof(SkillEventKind)))
        {
            var captured = kind;
            if (kind == SkillEventKind.Animation && window.SkillConfig.Tracks.Any(t => t.Kind == kind))
                menu.AddDisabledItem(new GUIContent("主动画（已有）"));
            else menu.AddItem(new GUIContent(Label(kind) + "轨道"), false, () => AddTrack(captured));
        }
        menu.ShowAsContext();
    }

    public static bool AddTrack(SkillEventKind kind)
    {
        var window = SkillEditorWindow.Instance;
        var clip = window?.SkillConfig;
        if (clip == null || !SkillTimelineData.IsEditable(clip, out _)) return false;
        if (!clip.UseTrackModel && SkillTimelineData.Read(clip).Count != 0) return false;
        if (clip.UseTrackModel && kind == SkillEventKind.Animation && clip.Tracks.Any(t => t.Kind == kind)) return false;
        var track = new SkillTrackData { Kind = kind, Name = Label(kind) + "轨道" };
        window.ActiveTrackId = track.TrackId;
        return SkillEditorClipboard.Commit("新增" + Label(kind) + "轨道", () =>
        {
            if (!clip.UseTrackModel) SkillClipMigrationPlan.Build(clip).Apply();
            clip.Tracks.Add(track);
            if (kind == SkillEventKind.Projectile) clip.DataVersion = Math.Max(clip.DataVersion, SkillClip.ProjectileDataVersion);
        }, new List<SkillEventEntry>());
    }

    public static bool Change(SkillTrackData track, string name, Action action, bool allowLocked = false)
    {
        var window = SkillEditorWindow.Instance;
        if (track == null || window?.SkillConfig == null || !window.SkillConfig.Tracks.Contains(track) || (track.Locked && !allowLocked)) return false;
        window.EndPreview();
        return SkillEditorClipboard.Commit(name, action, window.TimelineSelection.Entries);
    }

    public static bool Reorder(SkillTrackData track, int delta)
    {
        var tracks = SkillEditorWindow.Instance.SkillConfig.Tracks;
        int from = tracks.IndexOf(track), to = Mathf.Clamp(from + delta, 0, tracks.Count - 1);
        if (from < 0 || to == from) return false;
        return Change(track, "调整轨道顺序", () => { tracks.RemoveAt(from); tracks.Insert(to, track); });
    }

    public static bool Duplicate(SkillTrackData track)
    {
        if (track.Kind == SkillEventKind.Animation) return false;
        var clip = SkillEditorWindow.Instance.SkillConfig;
        var copy = new SkillTrackData { Kind = track.Kind, Name = track.Name + " 副本", Enabled = track.Enabled };
        foreach (var item in track.Clips)
            copy.Clips.Add(new SkillTrackClip { Frame = item.Frame, Data = SkillTimelineData.Clone(new SkillEventEntry(track.Kind, item.Data, item.Frame, 0, track.TrackId)).Data });
        return Change(track, "复制整条轨道", () => clip.Tracks.Insert(clip.Tracks.IndexOf(track) + 1, copy), true);
    }

    public static bool Delete(SkillTrackData track, bool confirm = true)
    {
        var window = SkillEditorWindow.Instance;
        if (track == null || track.Locked || window?.SkillConfig == null || !window.SkillConfig.Tracks.Contains(track)) return false;
        if (confirm && track.Clips.Count > 0 && !EditorUtility.DisplayDialog("删除轨道", $"删除“{track.Name}”及其 {track.Clips.Count} 个片段？可撤销。", "删除", "取消")) return false;
        return Change(track, "删除轨道及片段", () => SkillEditorWindow.Instance.SkillConfig.Tracks.Remove(track));
    }

    public static bool AddClip(SkillTrackData track, int frame, UnityEngine.Object resource = null)
    {
        var clip = SkillEditorWindow.Instance.SkillConfig;
        SkillFrameEventBase data;
        switch (track.Kind)
        {
            case SkillEventKind.Custom: data = new SkillCustomEvent(); break;
            case SkillEventKind.Animation:
                var animation = resource as AnimationClip;
                data = new SkillAnimationEvent { AnimationClip = animation, DurationFrame = animation == null ? Math.Max(1, clip.FrameRote) : Math.Max(1, Mathf.CeilToInt(animation.length * clip.FrameRote)), TransitionTime = 0.25f }; break;
            case SkillEventKind.Audio: data = new SkillAudioEvent { AudioClip = resource as AudioClip }; break;
            case SkillEventKind.Effect: data = new SkillEffectEvent { Prefab = resource as GameObject, Duration = Math.Max(1, clip.FrameRote), Scale = Vector3.one }; break;
            case SkillEventKind.Attack: data = new SkillAttackDetectionEvent { DurationFrame = Math.Max(1, clip.FrameRote) }; break;
            case SkillEventKind.Projectile: data = new SkillProjectileEvent { Prefab = resource as GameObject, FlightFrames = Math.Max(1, clip.FrameRote) }; break;
            default: return false;
        }
        var entries = new List<SkillEventEntry> { new SkillEventEntry(track.Kind, data, frame, track.Clips.Count, track.TrackId) };
        if (!SkillTimelineData.ValidatePlacement(clip, entries, null, out string reason)) { Debug.LogWarning(reason); return false; }
        SkillEditorWindow.Instance.ActiveTrackId = track.TrackId;
        return SkillEditorClipboard.Commit("新增" + Label(track.Kind) + "片段", () => SkillTimelineData.Add(clip, entries[0]), entries);
    }
}

}

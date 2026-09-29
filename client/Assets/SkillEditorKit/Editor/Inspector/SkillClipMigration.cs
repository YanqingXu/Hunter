// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SkillClipMigrationPlan
{
    public sealed class Change
    {
        public SkillFrameEventBase Data;
        public string OldId;
        public string NewId;
        public string Label;
    }

    public SkillClip Clip { get; private set; }
    public int OriginalVersion { get; private set; }
    public readonly List<Change> Changes = new List<Change>();
    public string Error { get; private set; }
    public bool HasChanges => Error == null && (OriginalVersion != SkillClip.CurrentDataVersion || !originalTrackModel || Changes.Count != 0);
    private List<SkillEventEntry> originalEntries;
    private List<string> originalIds;
    private bool originalTrackModel;
    private string[] originalNames;
    private static string LegacyName(SkillFrameEventBase data) => data is SkillAudioEvent a ? a.TrackName : data is SkillEffectEvent e ? e.TrackName : data is SkillAttackDetectionEvent d ? d.TrackName : null;
    public List<SkillTrackData> MigratedTracks { get; private set; }
    public bool ConvertsTracks => !originalTrackModel;

    public static SkillClipMigrationPlan Build(SkillClip clip)
    {
        var plan = new SkillClipMigrationPlan { Clip = clip };
        if (clip == null) { plan.Error = "配置为空"; return plan; }
        plan.OriginalVersion = clip.DataVersion;
        plan.originalTrackModel = clip.UseTrackModel;
        if (!SkillTimelineData.IsEditable(clip, out var error)) { plan.Error = error; return plan; }
        plan.originalEntries = SkillTimelineData.Read(clip);
        if (!clip.UseTrackModel)
        {
            int slots = clip.skillCustomEventData.FrameData.Count + clip.SkillAnimationData.FrameData.Count +
                clip.SkillAudioData.FrameData.Count + clip.SkillEffectData.FrameData.Count + clip.SkillAttackDetectionData.FrameData.Count;
            if (slots != plan.originalEntries.Count) { plan.Error = "旧配置存在空事件，先修复后再迁移，避免丢失条目"; return plan; }
            if (clip.Tracks != null && clip.Tracks.Count > 0) { plan.Error = "旧配置同时包含轨道数据，请先检查数据来源"; return plan; }
            plan.MigratedTracks = new List<SkillTrackData>();
            foreach (var entry in plan.originalEntries)
            {
                SkillTrackData track = null;
                if (entry.Kind == SkillEventKind.Custom || entry.Kind == SkillEventKind.Animation)
                    track = plan.MigratedTracks.FirstOrDefault(t => t.Kind == entry.Kind);
                if (track == null)
                {
                    string name = entry.Data is SkillAudioEvent audio ? audio.TrackName :
                        entry.Data is SkillEffectEvent effect ? effect.TrackName :
                        entry.Data is SkillAttackDetectionEvent attack ? attack.TrackName : null;
                    track = new SkillTrackData { Kind = entry.Kind, Name = string.IsNullOrWhiteSpace(name) ? SkillTrackActions.Label(entry.Kind) + "轨道" : name };
                    plan.MigratedTracks.Add(track);
                }
                track.Clips.Add(new SkillTrackClip { Frame = entry.Frame, Data = entry.Data });
            }
        }
        plan.originalIds = plan.originalEntries.Select(e => e.Data.EventId).ToList();
        plan.originalNames = plan.originalEntries.Select(e => LegacyName(e.Data)).ToArray();
        var objects = new HashSet<SkillFrameEventBase>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Reserve every existing valid ID before allocating replacements.
        var reserved = new HashSet<string>(plan.originalEntries.Select(e => e.Data.EventId ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.originalEntries)
        {
            if (!objects.Add(entry.Data))
            { plan.Error = "多个轨道项引用同一个事件对象，需要先拆分该共享对象"; return plan; }
            string id = entry.Data.EventId;
            if (Guid.TryParseExact(id, "N", out _) && used.Add(id)) continue;
            string replacement;
            do { replacement = Guid.NewGuid().ToString("N"); } while (!reserved.Add(replacement));
            used.Add(replacement);
            plan.Changes.Add(new Change { Data = entry.Data, OldId = id, NewId = replacement, Label = $"{entry.Kind}@{entry.Frame}" });
        }
        return plan;
    }

    public bool IsCurrent()
    {
        if (Clip == null || Clip.DataVersion != OriginalVersion || Clip.UseTrackModel != originalTrackModel || Error != null) return false;
        if (!SkillTimelineData.IsEditable(Clip, out _)) return false;
        var current = SkillTimelineData.Read(Clip);
        if (!originalTrackModel && (Clip.skillCustomEventData.FrameData.Count + Clip.SkillAnimationData.FrameData.Count + Clip.SkillAudioData.FrameData.Count + Clip.SkillEffectData.FrameData.Count + Clip.SkillAttackDetectionData.FrameData.Count != current.Count || Clip.Tracks.Count != 0)) return false;
        return current.Count == originalEntries.Count &&
               current.Zip(originalEntries, (a, b) => ReferenceEquals(a.Data, b.Data) && a.Frame == b.Frame && a.Row == b.Row && a.TrackId == b.TrackId).All(v => v) &&
               current.Select(e => e.Data.EventId).SequenceEqual(originalIds) && current.Select(e => LegacyName(e.Data)).SequenceEqual(originalNames);
    }

    // Call inside an Undo transaction. Does not change timings or resource references.
    public void Apply()
    {
        if (!IsCurrent()) throw new InvalidOperationException("配置已变化，请刷新迁移预览");
        foreach (var change in Changes) change.Data.EventId = change.NewId;
        if (!originalTrackModel)
        {
            Clip.Tracks = MigratedTracks;
            foreach (var track in Clip.Tracks)
                foreach (var item in track.Clips) SkillTrackModel.SetFrame(item.Data, item.Frame);
            Clip.UseTrackModel = true;
            // One authoritative graph; old fields remain only for pre-v3 compatibility.
            Clip.skillCustomEventData.FrameData.Clear();
            Clip.SkillAnimationData.FrameData.Clear();
            Clip.SkillAudioData.FrameData.Clear();
            Clip.SkillEffectData.FrameData.Clear();
            Clip.SkillAttackDetectionData.FrameData.Clear();
        }
        Clip.DataVersion = SkillClip.CurrentDataVersion;
    }
}

public sealed class SkillClipMigrationWindow : EditorWindow
{
    private readonly List<SkillClipMigrationPlan> plans = new List<SkillClipMigrationPlan>();
    private Vector2 scroll;
    private bool allAssets;
    private string lastBackup;

    [MenuItem("SkillEditorKit/数据版本迁移预览")]
    public static void Open()
    {
        var window = GetWindow<SkillClipMigrationWindow>("技能数据迁移");
        window.RefreshPlans();
    }

    private void RefreshPlans()
    {
        plans.Clear();
        if (allAssets)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SkillClip").OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                plans.Add(SkillClipMigrationPlan.Build(AssetDatabase.LoadAssetAtPath<SkillClip>(AssetDatabase.GUIDToAssetPath(guid))));
        }
        else
        {
            var clip = SkillEditorWindow.Instance == null ? Selection.activeObject as SkillClip : SkillEditorWindow.Instance.SkillConfig;
            if (clip != null) plans.Add(SkillClipMigrationPlan.Build(clip));
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("版本 3 将旧分类升级为真正轨道：动画/事件各保留一条，旧音效/特效/攻击的每行分别保留为一条轨道。原名称、顺序、帧和资源不变，不自动启用裁剪。旧容器迁移后清空，先备份并支持 Undo。", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        allAssets = EditorGUILayout.Toggle("全部技能", allAssets);
        if (EditorGUI.EndChangeCheck() || GUILayout.Button("刷新迁移预览")) RefreshPlans();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var plan in plans)
        {
            EditorGUILayout.ObjectField(plan.Clip, typeof(SkillClip), false);
            if (plan.Error != null) EditorGUILayout.HelpBox(plan.Error, MessageType.Error);
            else
            {
                EditorGUILayout.LabelField($"版本 {plan.OriginalVersion} → {SkillClip.CurrentDataVersion}；补充/修复 {plan.Changes.Count} 个 ID");
                if (plan.ConvertsTracks) EditorGUILayout.LabelField($"升级后 {plan.MigratedTracks.Count} 条轨道，{plan.MigratedTracks.Sum(t => t.Clips.Count)} 个片段");
                foreach (var change in plan.Changes)
                    EditorGUILayout.LabelField(change.Label, $"{(string.IsNullOrEmpty(change.OldId) ? "(空)" : change.OldId)} → {change.NewId}");
            }
        }
        EditorGUILayout.EndScrollView();
        if (plans.Count == 0) EditorGUILayout.LabelField("请选择 SkillClip，或勾选全部技能。");
        using (new EditorGUI.DisabledScope(plans.Count == 0 || plans.Any(p => p.Error != null) || !plans.Any(p => p.HasChanges)))
        {
            if (GUILayout.Button("备份并执行上方迁移")) ExecutePlans();
        }
        if (!string.IsNullOrEmpty(lastBackup))
        {
            EditorGUILayout.LabelField("备份目录", lastBackup);
            if (GUILayout.Button("打开备份目录")) EditorUtility.RevealInFinder(lastBackup);
        }
    }

    private void ExecutePlans()
    {
        if (plans.Any(p => !p.IsCurrent()))
        {
            ShowNotification(new GUIContent("配置已变化，已刷新预览；请检查后再执行"));
            RefreshPlans();
            return;
        }
        var changes = plans.Where(p => p.HasChanges).ToList();
        string backup = Path.GetFullPath(Path.Combine("SkillEditorBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        // Keep paths below legacy Windows/Mono limits; reject before copying or modifying assets.
        foreach (var plan in changes)
            if (Path.Combine(backup, AssetDatabase.GetAssetPath(plan.Clip) + ".memory.json").Length >= 260)
                throw new InvalidOperationException("备份路径超过 Windows/Mono 长度限制，请将项目放到较短路径后重试；技能尚未修改。");
        // Back up all targets before touching any asset. Include in-memory state
        // so unsaved Inspector changes are recoverable as well as the disk file.
        foreach (var plan in changes)
        {
            string path = AssetDatabase.GetAssetPath(plan.Clip);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("只能迁移 Assets 中的技能资源");
            string target = Path.Combine(backup, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(path, target, false);
            if (File.Exists(path + ".meta")) File.Copy(path + ".meta", target + ".meta", false);
            ((ISerializationCallbackReceiver)plan.Clip).OnBeforeSerialize();
            File.WriteAllText(target + ".memory.json", EditorJsonUtility.ToJson(plan.Clip, true));
        }
        lastBackup = backup;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("迁移技能事件 ID");
        try
        {
            foreach (var plan in changes)
                SkillEditorChangeUtility.ApplyTo(plan.Clip, "迁移技能事件 ID", plan.Apply);
            Undo.CollapseUndoOperations(group);
            SkillEditorChangeUtility.SaveNow();
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
        finally { Undo.IncrementCurrentGroup(); }
        SkillEditorWindow.Instance?.RefreshAfterUndo();
        Debug.Log($"已迁移 {changes.Count} 个技能；备份目录：{backup}");
        RefreshPlans();
    }
}

}

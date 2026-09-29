// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Session clipboard; all group operations validate before touching the asset.</summary>
public static class SkillEditorClipboard
{
    private static List<SkillEventEntry> payload = new List<SkillEventEntry>();
    private static SkillEditorWindow Window => SkillEditorWindow.Instance;

    [MenuItem("SkillEditorKit/快捷键说明")]
    private static void ShowShortcutHelp()
    {
        EditorUtility.DisplayDialog("技能编辑器快捷键",
            "Ctrl/Shift+单击：增减/范围多选；空白拖拽：框选\nCtrl+A：全选；Escape：取消拖拽/清空选择\nCtrl+C/V/D：整组复制/粘贴到播放头/复制一组\nDelete：整组删除；←/→：移动 1 帧；Shift：10 帧\n拖拽自动吸附；按住 Alt 临时关闭吸附\nCtrl+S：保存；Ctrl+Z/Y：撤销/重做\nSpace：播放/暂停；Home/End：首帧/末帧\n自定义轨道空白处双击：新增事件", "确定");
    }

    public static bool CopySelected()
    {
        var entries = Window?.TimelineSelection.Entries;
        if (entries == null || entries.Count == 0) return false;
        try { payload = entries.Select(SkillTimelineData.Clone).ToList(); return true; }
        catch (Exception ex) { Debug.LogWarning("复制取消：" + ex.Message); return false; }
    }

    // Pure planning is also used by regression tests. Each paste gets independent nested data and IDs.
    public static bool PlanPaste(SkillClip clip, IList<SkillEventEntry> source, int target,
        out List<SkillEventEntry> proposed, out string reason, string targetTrackId = null)
    {
        proposed = new List<SkillEventEntry>();
        reason = null;
        if (source == null || source.Count == 0) { reason = "剪贴板为空"; return false; }
        int first = source.Min(e => e.Frame);
        try
        {
            foreach (var entry in source)
            {
                long frame = (long)target + entry.Frame - first;
                if (frame < 0 || frame > SkillTimelineData.MaxFrame) { reason = "粘贴超出时间轴范围"; return false; }
                var copy = SkillTimelineData.Clone(entry).At((int)frame);
                if (clip != null && clip.UseTrackModel)
                {
                    // A single source lane may be redirected; groups keep distinct ownership.
                    bool singleLane = source.Select(e => e.Kind).Distinct().Count() == 1 && source.Select(e => e.TrackId).Distinct().Count() == 1;
                    var targetTrack = SkillTimelineData.FindTrack(clip, targetTrackId);
                    if (singleLane && targetTrack != null)
                    {
                        if (targetTrack.Kind != entry.Kind) { reason = "选中的目标轨道类型不匹配"; return false; }
                        copy = copy.OnTrack(targetTrack.TrackId);
                    }
                    else if (SkillTimelineData.FindTrack(clip, copy.TrackId) == null)
                    { reason = "原轨道不存在；请先选择匹配的目标轨道（跨配置请逐轨粘贴）"; return false; }
                }
                proposed.Add(copy);
            }
        }
        catch (Exception ex) { reason = ex.Message; return false; }
        return SkillTimelineData.ValidatePlacement(clip, proposed, null, out reason);
    }

    public static bool PasteAt(int targetFrame)
    {
        if (Window == null) return false;
        var clip = Window.SkillConfig;
        if (!PlanPaste(clip, payload, targetFrame, out var proposed, out string reason, Window.ActiveTrackId)) return Reject(reason);
        return Commit("粘贴技能事件组", () =>
        {
            foreach (var entry in proposed) SkillTimelineData.Add(clip, entry);
        }, proposed);
    }

    public static bool DuplicateSelected()
    {
        var entries = Window?.TimelineSelection.Entries;
        if (entries == null || entries.Count == 0 || !CopySelected()) return false;
        long end = entries.Max(e => (long)e.Frame + e.Duration(Window.SkillConfig.FrameRote));
        if (end > SkillTimelineData.MaxFrame) return Reject("复制超出时间轴范围");
        var active = Window.ActiveTrackId;
        Window.ActiveTrackId = null;
        try { return PasteAt((int)end); }
        finally { Window.ActiveTrackId = active; }
    }

    public static bool MoveSelected(int delta, string targetTrackId = null)
    {
        if (Window == null) return false;
        var selected = Window.TimelineSelection.Entries;
        var clip = Window.SkillConfig;
        if (!SkillTimelineData.PlanMove(clip, selected, delta, out var proposed, out string reason, targetTrackId)) return Reject(reason);
        if (proposed[0].Frame == selected[0].Frame && proposed.Zip(selected, (a, b) => a.TrackId == b.TrackId).All(v => v)) return false;
        return Commit("移动技能事件组", () => SkillTimelineData.ApplyMove(clip, selected, proposed), proposed);
    }

    public static bool DeleteSelected()
    {
        var selected = Window?.TimelineSelection.Entries;
        if (selected == null || selected.Count == 0) return false;
        var clip = Window.SkillConfig;
        if (selected.Any(e => SkillTimelineData.IsLocked(clip, e))) return Reject("选中片段所属轨道已锁定");
        return Commit("删除技能事件组", () =>
        {
            foreach (var entry in selected) SkillTimelineData.Remove(clip, entry);
        }, new List<SkillEventEntry>());
    }

    public static bool Commit(string name, Action mutation, IList<SkillEventEntry> selected)
    {
        if (Window == null || !SkillTimelineData.IsEditable(Window.SkillConfig, out string reason)) return false;
        Window.IsPlaying = false;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        try
        {
            if (!SkillEditorChangeUtility.Apply(name, mutation)) return false;
            Undo.CollapseUndoOperations(group);
        }
        catch (Exception ex)
        {
            Undo.RevertAllDownToGroup(group);
            Window.RefreshAfterUndo();
            return Reject("操作已回滚：" + ex.Message);
        }
        finally { Undo.IncrementCurrentGroup(); }
        Window.RefreshAfterDataChange(selected.Count == 0 ? Window.CurrentSelectFrameIndex : selected.Min(e => e.Frame),
            selected.Select(e => e.Data).ToList());
        return true;
    }

    private static bool Reject(string reason)
    {
        if (!string.IsNullOrEmpty(reason)) Debug.LogWarning(reason);
        return false;
    }
}

}

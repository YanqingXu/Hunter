// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SkillEditorChangeUtility
{
    private const double SaveDelaySeconds = 0.5d;
    private static readonly HashSet<SkillClip> pending = new HashSet<SkillClip>();
    private static double nextSaveTime;
    private static bool refreshScheduled;
    private static readonly Dictionary<SkillClip, int> revisions = new Dictionary<SkillClip, int>();
    private static readonly Dictionary<SkillClip, string> saveErrors = new Dictionary<SkillClip, string>();
    public static int Revision(SkillClip clip) => clip != null && revisions.TryGetValue(clip, out int value) ? value : 0;
    public static string SaveError(SkillClip clip) => clip != null && saveErrors.TryGetValue(clip, out string value) ? value : null;

    static SkillEditorChangeUtility()
    {
        EditorApplication.update += SavePendingConfig;
        EditorApplication.quitting += SaveNow;
        AssemblyReloadEvents.beforeAssemblyReload += SaveNow;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    public static SkillClip CurrentConfig => SkillEditorWindow.Instance == null ? null : SkillEditorWindow.Instance.SkillConfig;
    public static bool Record(string undoName) => Record(CurrentConfig, undoName);

    private static bool Record(SkillClip config, string undoName)
    {
        if (!SkillTimelineData.IsEditable(config, out string reason))
        {
            if (config != null) Debug.LogWarning(reason, config);
            return false;
        }
        ((ISerializationCallbackReceiver)config).OnBeforeSerialize();
        Undo.RegisterCompleteObjectUndo(config, undoName);
        return true;
    }

    public static bool Apply(string undoName, Action mutation) => ApplyTo(CurrentConfig, undoName, mutation);

    public static bool ApplyTo(SkillClip config, string undoName, Action mutation)
    {
        if (mutation == null || !Record(config, undoName)) return false;
        mutation();
        MarkChanged(config);
        return true;
    }

    public static void MarkChanged() => MarkChanged(CurrentConfig);
    public static void MarkChanged(SkillClip config)
    {
        if (config == null) return;
        revisions[config] = Revision(config) + 1;
        ((ISerializationCallbackReceiver)config).OnBeforeSerialize();
        EditorUtility.SetDirty(config);
        pending.Add(config);
        nextSaveTime = EditorApplication.timeSinceStartup + SaveDelaySeconds;
    }

    public static void SaveNow()
    {
        foreach (var config in pending.ToArray()) SaveNow(config);
        pending.RemoveWhere(config => config == null);
    }

    public static void SaveNow(SkillClip config)
    {
        if (config == null) { pending.Remove(config); return; }
        try
        {
            if (EditorUtility.IsDirty(config)) AssetDatabase.SaveAssetIfDirty(config);
            if (EditorUtility.IsPersistent(config) && EditorUtility.IsDirty(config)) throw new InvalidOperationException("资源仍未保存，请检查文件是否只读。");
            saveErrors.Remove(config); pending.Remove(config);
        }
        catch (Exception ex)
        {
            saveErrors[config] = ex.Message; pending.Add(config);
            nextSaveTime = EditorApplication.timeSinceStartup + 2;
        }
    }

    private static void SavePendingConfig()
    {
        if (pending.Count != 0 && EditorApplication.timeSinceStartup >= nextSaveTime) SaveNow();
    }

    private static void OnUndoRedoPerformed()
    {
        // Avoid serializing a stale managed Odin graph over restored nodes.
        if (refreshScheduled) return;
        refreshScheduled = true;
        EditorApplication.delayCall += () =>
        {
            refreshScheduled = false;
            foreach (var config in Resources.FindObjectsOfTypeAll<SkillClip>())
            {
                if (EditorUtility.IsPersistent(config) && EditorUtility.IsDirty(config)) pending.Add(config);
                if (config != null) revisions[config] = Revision(config) + 1;
            }
            nextSaveTime = EditorApplication.timeSinceStartup + SaveDelaySeconds;
            SkillEditorWindow.Instance?.RefreshAfterUndo();
        };
    }
}

}

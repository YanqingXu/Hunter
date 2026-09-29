// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Asset entry points and local recent history; never rewrites an existing skill.</summary>
public static class SkillEditorDocuments
{
    [Serializable] private sealed class History { public List<string> guids = new List<string>(); }
    internal static string PreferenceKey => "SkillEditorKit.Documents." + Hash128.Compute(Application.dataPath);
    public static List<SkillClip> Recent => ReadHistory().guids.Select(AssetDatabase.GUIDToAssetPath)
        .Select(AssetDatabase.LoadAssetAtPath<SkillClip>).Where(clip => clip != null).ToList();

    private static History ReadHistory()
    {
        try
        {
            var value = JsonUtility.FromJson<History>(EditorPrefs.GetString(PreferenceKey, "")) ?? new History();
            if (value.guids == null) value.guids = new List<string>();
            return value;
        }
        catch { return new History(); }
    }

    public static void Remember(SkillClip clip)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(path)) return;
        string guid = AssetDatabase.AssetPathToGUID(path);
        var history = ReadHistory(); history.guids.RemoveAll(value => value == guid);
        history.guids.Insert(0, guid);
        if (history.guids.Count > 8) history.guids.RemoveRange(8, history.guids.Count - 8);
        EditorPrefs.SetString(PreferenceKey, JsonUtility.ToJson(history));
    }

    public static SkillClip Create(string path, string skillName, int frameRate, int frameCount, string starter)
    {
        path = (path ?? "").Replace('\\', '/');
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
            path.Split('/').Any(part => part == ".." || part == ".") || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\', '/')))
            throw new ArgumentException("请将技能保存为 Assets 文件夹内的 .asset 文件。");
        if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            throw new ArgumentException("该文件已存在，请使用新文件名；不会覆盖原技能。");
        if (string.IsNullOrWhiteSpace(skillName) || frameRate < 1 || frameRate > 240 || frameCount < 0 || frameCount > SkillTimelineData.MaxFrame)
            throw new ArgumentException("请填写技能名称、有效帧率和结束帧。");
        if (starter != "空白" && starter != "近战" && starter != "投射物") throw new ArgumentException("未知起始轨道类型。");
        var clip = ScriptableObject.CreateInstance<SkillClip>();
        clip.SkillName = skillName.Trim(); clip.FrameRote = frameRate; clip.FrameCount = frameCount;
        clip.DataVersion = SkillClip.CurrentDataVersion; clip.UseTrackModel = true;
        if (starter != "空白")
            foreach (var kind in new[] { SkillEventKind.Animation, starter == "投射物" ? SkillEventKind.Projectile : SkillEventKind.Attack, SkillEventKind.Effect, SkillEventKind.Audio })
                clip.Tracks.Add(new SkillTrackData { Kind = kind, Name = SkillTrackActions.Label(kind) + "轨道" });
        try
        {
            AssetDatabase.CreateAsset(clip, path);
            SkillEditorChangeUtility.MarkChanged(clip); SkillEditorChangeUtility.SaveNow(clip);
            Remember(clip); return clip;
        }
        catch { if (!EditorUtility.IsPersistent(clip)) UnityEngine.Object.DestroyImmediate(clip); throw; }
    }

    public static void ShowOpen(SkillEditorWindow window)
    {
        string path = EditorUtility.OpenFilePanel("打开技能配置", Application.dataPath, "asset");
        if (string.IsNullOrEmpty(path)) return;
        path = FileUtil.GetProjectRelativePath(path);
        var clip = AssetDatabase.LoadAssetAtPath<SkillClip>(path);
        if (clip == null) { EditorUtility.DisplayDialog("打开技能", "请选择当前工程内的 SkillClip 技能资源。", "确定"); return; }
        window.OpenDocument(clip);
    }

    public static void ShowRecent(SkillEditorWindow window)
    {
        var menu = new GenericMenu(); var clips = Recent;
        if (clips.Count == 0) menu.AddDisabledItem(new GUIContent("暂无最近使用的技能"));
        foreach (var clip in clips)
        {
            var selected = clip;
            menu.AddItem(new GUIContent((string.IsNullOrEmpty(clip.SkillName) ? clip.name : clip.SkillName).Replace('/', '／') + "  ·  " + clip.name.Replace('/', '／')),
                window.SkillConfig == clip, () => window.OpenDocument(selected));
        }
        menu.ShowAsContext();
    }

    [OnOpenAsset]
    private static bool OpenAsset(int instanceId, int line)
    {
        var clip = EditorUtility.InstanceIDToObject(instanceId) as SkillClip;
        if (clip == null) return false;
        var window = EditorWindow.GetWindow<SkillEditorWindow>(); window.titleContent = new GUIContent("技能编辑器·独立版"); window.Show();
        if (window.rootVisualElement.Q<ObjectField>("SkillConfigObjectField") == null) window.CreateGUI();
        window.OpenDocument(clip); return true;
    }
}

}

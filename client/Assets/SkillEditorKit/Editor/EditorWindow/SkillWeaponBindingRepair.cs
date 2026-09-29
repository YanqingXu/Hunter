// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

/// <summary>Repairs only missing prefab hand references, never custom non-null bindings.</summary>
[InitializeOnLoad]
public static class SkillWeaponBindingRepair
{
    internal const string EditorScenePath = "Assets/SkillEditorKit/Samples/SkillEditorScene.unity";
    private const string ActorPrefabPath = "Assets/SkillEditorKit/Samples/PreviewActor.prefab";

    static SkillWeaponBindingRepair()
    {
        // A loaded scene can still contain the old null overrides after scripts reload.
        // Repair that in-memory instance as well; do not save or reload the user's scene.
        EditorApplication.delayCall += RepairLoadedEditorScene;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == EditorScenePath) EditorApplication.delayCall += RepairLoadedEditorScene;
    }

    [MenuItem("技能编辑器（独立版）/修复预览场景武器挂点")]
    public static void RepairLoadedEditorScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetSceneByPath(EditorScenePath);
        if (!scene.IsValid() || !scene.isLoaded) return;
        var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SkillPlayer>(true))
            .Where(player => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(player)) == ActorPrefabPath &&
                Replacements(player).Count > 0).ToArray();
        if (actors.Length == 0) return;
        var windows = Resources.FindObjectsOfTypeAll<SkillEditorWindow>()
            .Where(window => actors.Any(actor => window.PreviewCharacterObj == actor.gameObject)).ToArray();
        foreach (var window in windows) window.EndPreview();
        if (AnimationMode.InAnimationMode())
        {
            Debug.LogWarning("请先停止其他窗口的动画预览，再使用“技能编辑器（独立版）/修复预览场景武器挂点”。");
            return;
        }
        int repaired = 0;
        foreach (var actor in actors) repaired += RepairMissingSources(actor);
        foreach (var window in windows) window.TickSkill();
        if (repaired == 0) return;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        Debug.Log("技能预览：已恢复 " + repaired + " 个武器挂点引用。支持撤销，未自动保存场景。");
    }

    internal static int RepairMissingSources(SkillPlayer player)
    {
        var replacements = Replacements(player);
        if (replacements.Count == 0) return 0;
        var constraint = player.MainWeaponParentConstraint;
        Undo.RecordObject(constraint, "修复武器挂点引用");
        foreach (var pair in replacements)
        {
            var source = constraint.GetSource(pair.Key);
            source.sourceTransform = pair.Value;
            constraint.SetSource(pair.Key, source);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(constraint);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        return replacements.Count;
    }

    private static Dictionary<int, Transform> Replacements(SkillPlayer player)
    {
        var result = new Dictionary<int, Transform>();
        if (player == null || EditorUtility.IsPersistent(player) || !player.gameObject.scene.IsValid()) return result;
        var constraint = player.MainWeaponParentConstraint;
        var prefabPlayer = PrefabUtility.GetCorrespondingObjectFromSource(player);
        if (constraint == null || prefabPlayer == null || prefabPlayer.MainWeaponParentConstraint == null) return result;
        var prefabConstraint = prefabPlayer.MainWeaponParentConstraint;
        if (PrefabUtility.GetCorrespondingObjectFromSource(constraint) != prefabConstraint || constraint.sourceCount != prefabConstraint.sourceCount) return result;
        for (int i = 0; i < constraint.sourceCount; i++)
        {
            if (constraint.GetSource(i).sourceTransform != null) continue;
            var expected = prefabConstraint.GetSource(i).sourceTransform;
            if (expected == null || !expected.IsChildOf(prefabPlayer.transform)) continue;
            string path = AnimationUtility.CalculateTransformPath(expected, prefabPlayer.transform);
            var source = FindExact(player.transform, path);
            if (source != null) result.Add(i, source);
        }
        return result;
    }

    private static Transform FindExact(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        foreach (string part in path.Split('/'))
        {
            Transform match = null;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name == part)
                {
                    if (match != null) return null;
                    match = root.GetChild(i);
                }
            if (match == null) return null;
            root = match;
        }
        return root;
    }
}

}

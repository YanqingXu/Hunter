using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YouYou;

namespace BigWorld.YouYou2D.Editor
{
    /// <summary>Adds the components required by the original UI lifecycle, preserving prefab contents.</summary>
    public static class NativeHudPrefabRepair
    {
        [MenuItem("Tools/YouYou Full Migration/Repair 2D HUD Prefabs")]
        public static void RepairHudPrefabs()
        {
            foreach (string path in new[]
            {
                "Assets/_Project/Game/Prefabs/GameHud.prefab",
                "Assets/_Project/Examples/Framework2D/Prefabs/FrameworkHud2D.prefab"
            })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!root.GetComponent<UIFormBase>() || !(root.transform is RectTransform rect))
                        throw new InvalidOperationException("HUD prefab must contain original YouYou.UIFormBase and RectTransform: " + path);
                    var canvas = root.GetComponent<Canvas>();
                    if (!canvas) canvas = root.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.overrideSorting = true;
                    if (!root.GetComponent<GraphicRaycaster>()) root.AddComponent<GraphicRaycaster>();
                    root.layer = 5;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("Original UI Canvas repaired: " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
    }
}

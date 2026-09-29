using UnityEditor;
using UnityEngine;

namespace YouYou.Framework.Editor
{
    public static class FrameworkMenu
    {
        [MenuItem("GameObject/YouYou/Framework Root", false, 10)]
        private static void CreateRoot()
        {
            var existing = UnityEngine.Object.FindObjectOfType<FrameworkEntry>();
            if (existing) { Selection.activeGameObject = existing.gameObject; return; }
            var root = new GameObject("YouYou Framework");
            Undo.RegisterCreatedObjectUndo(root, "Create YouYou Framework");
            Undo.AddComponent<FrameworkEntry>(root);
            Selection.activeGameObject = root;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Maintains map identities outside serialization callbacks, including template layouts.</summary>
    [InitializeOnLoad]
    public static class GridMapAssetIdentity
    {
        private static readonly HashSet<string> pendingPaths = new HashSet<string>(StringComparer.Ordinal);
        private static bool scanAll = true;
        private static bool queued;

        static GridMapAssetIdentity()
        {
            Queue();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) Queue();
            };
        }

        /// <summary>
        /// Returns whether a persistent map needed maintenance. Transient maps are untouched.
        /// Call explicitly after AssetDatabase.CopyAsset when immediate batch validation is needed.
        /// </summary>
        public static bool EnsureIdentity(GridMapAsset map, bool save = true)
        {
            if (map == null || !EditorUtility.IsPersistent(map) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(map, out string guid, out long localId) ||
                string.IsNullOrEmpty(guid)) return false;
            string path = AssetDatabase.GetAssetPath(map);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string identity = guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            if (!map.SynchronizeEditorIdentity(identity)) return false;
            EditorUtility.SetDirty(map);
            if (save) AssetDatabase.SaveAssetIfDirty(map);
            return true;
        }

        /// <summary>Repairs all project maps and template layout subassets; returns the number changed.</summary>
        public static int SynchronizeAll()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            CollectPaths("t:GridMapAsset", paths);
            // Explicitly include template assets because a layout is not their main asset.
            CollectPaths("t:MapStructureTemplate", paths);
            int changed = 0;
            foreach (string path in paths) changed += SynchronizePath(path);
            return changed;
        }

        private static void CollectPaths(string filter, HashSet<string> paths)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static int SynchronizePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return 0;
            int changed = 0;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is GridMapAsset map && EnsureIdentity(map)) changed++;
            return changed;
        }

        internal static void QueuePaths(string[] paths)
        {
            foreach (string path in paths)
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) pendingPaths.Add(path);
            if (pendingPaths.Count > 0) Queue();
        }

        private static void Queue()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += ProcessPending;
        }

        private static void ProcessPending()
        {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Queue(); return; }
            bool fullScan = scanAll;
            scanAll = false;
            var paths = new List<string>(pendingPaths);
            pendingPaths.Clear();
            if (fullScan) SynchronizeAll();
            foreach (string path in paths) SynchronizePath(path);
        }
    }

    internal sealed class GridMapAssetIdentityPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            GridMapAssetIdentity.QueuePaths(imported);
            GridMapAssetIdentity.QueuePaths(moved);
        }
    }

    internal sealed class GridMapAssetIdentityBuildPreparation : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) { GridMapAssetIdentity.SynchronizeAll(); }
    }
}

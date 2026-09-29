using System;
using BigWorld.YouYou2D;
using BigWorld.YouYou2D.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Framework2DValidation
{
    public static void Run()
    {
        try
        {
            Framework2DDemoBuilder.Create();
            NativeFramework2DBuild.PrepareContent(EditorUserBuildSettings.activeBuildTarget);
            var catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(Framework2DDemoBuilder.CatalogPath);
            catalog.ValidateEntries();
            string playerPath = catalog.GetAssetPath("actor.player");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(playerPath) || AssetDatabase.LoadAssetAtPath<AudioClip>(playerPath))
                throw new Exception("Native resource authoring path/type checks failed.");
            bool missingRejected = false;
            try { catalog.GetAssetPath("missing"); }
            catch (System.Collections.Generic.KeyNotFoundException) { missingRejected = true; }
            if (!missingRejected) throw new Exception("Missing resource keys were not rejected.");
            bool rejected = false;
            var duplicateCatalog = ScriptableObject.CreateInstance<GameAssetCatalog>();
            try
            {
                duplicateCatalog.Entries = new[] { catalog.Entries[0], catalog.Entries[0] };
                duplicateCatalog.ValidateEntries();
            }
            catch (ArgumentException) { rejected = true; }
            finally { UnityEngine.Object.DestroyImmediate(duplicateCatalog); }
            if (!rejected) throw new Exception("Duplicate resource keys were not rejected.");
            EditorSceneManager.OpenScene(Framework2DDemoBuilder.ScenePath);
            new GameObject("Existing Project Pool Driver").AddComponent<BigWorld.Pooling.Unity.PoolDriver>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/__Framework2DChecks/WithExistingDriver.unity");
            EditorSceneManager.OpenScene(Framework2DDemoBuilder.ScenePath);
            new GameObject("Framework2D Integration Checks").AddComponent<Framework2DPlayChecks>();
            // Save a test-only scene so entering Play after domain reload retains the probe.
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/__Framework2DChecks/Validation.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}

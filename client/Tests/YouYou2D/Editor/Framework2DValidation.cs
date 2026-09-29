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
            var catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(Framework2DDemoBuilder.CatalogPath);
            var provider = catalog.CreateProvider();
            if (!provider.Load<GameObject>("actor.player") || provider.Load<AudioClip>("actor.player") || provider.Load<GameObject>("missing"))
                throw new Exception("Asset catalog lookup/type checks failed.");
            bool rejected = false;
            try { new CatalogAssetProvider(new[] { catalog.Entries[0], catalog.Entries[0] }); }
            catch (ArgumentException) { rejected = true; }
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

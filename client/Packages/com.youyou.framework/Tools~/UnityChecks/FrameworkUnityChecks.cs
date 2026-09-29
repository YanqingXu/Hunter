using System;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using YouYou.Framework;
using YouYou.Framework.Validation;

public static class FrameworkUnityChecks
{
    private sealed class FakeAssets : IAssetProvider
    {
        public GameObject Prefab;
        public int Loads, Releases;
        public T Load<T>(string path) where T : UnityEngine.Object { Loads++; return Prefab as T; }
        public IEnumerator LoadAsync<T>(string path, Action<T> complete) where T : UnityEngine.Object
        { complete(Load<T>(path)); yield break; }
        public void Release(UnityEngine.Object value) { Releases++; }
    }

    public static void Run()
    {
        try
        {
            Debug.Log(CoreChecks.Run());
            CheckUI(); CheckObjectPool(); CheckResources();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Framework validation").AddComponent<FrameworkPlayProbe>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/FrameworkValidation.unity");
            Debug.Log("Editor checks passed. Starting Play Mode lifecycle checks.");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void CheckUI()
    {
        var prefab = new GameObject("Form", typeof(RectTransform), typeof(FrameworkTestForm));
        var root = new GameObject("Canvas", typeof(RectTransform));
        var provider = new FakeAssets { Prefab = prefab };
        var ui = new UIManager(provider, root.transform);
        try
        {
            ui.Register("Test", "Test");
            var first = (FrameworkTestForm)ui.OpenUIForm("Test", "payload");
            CoreChecks.Assert(first.Opened == 1 && (string)first.Payload == "payload");
            CoreChecks.Assert(ui.OpenUIForm("Test") == first && provider.Loads == 1);
            ui.CloseUIForm("Test"); CoreChecks.Assert(!first && provider.Releases == 1);
            var second = ui.OpenUIForm("Test"); ui.Dispose();
            CoreChecks.Assert(!second && provider.Releases == 2);
            UnityEngine.Object.DestroyImmediate(prefab.GetComponent<FrameworkTestForm>());
            ui = new UIManager(provider, root.transform); ui.Register("Broken", "Broken");
            bool failed = false;
            try { ui.OpenUIForm("Broken"); } catch (InvalidOperationException) { failed = true; }
            CoreChecks.Assert(failed && provider.Releases == 3 && root.transform.childCount == 0);
        }
        finally { ui.Dispose(); UnityEngine.Object.DestroyImmediate(prefab); UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckObjectPool()
    {
        var prefab = new GameObject("Pooled"); var root = new GameObject("PoolRoot");
        using (var pool = new GameObjectPool(prefab, root.transform, 1))
        {
            var first = pool.Rent(); pool.Return(first); CoreChecks.Assert(!first.activeSelf);
            bool failed = false; try { pool.Return(first); } catch (InvalidOperationException) { failed = true; }
            CoreChecks.Assert(failed); CoreChecks.Assert(pool.Rent() == first);
            var second = pool.Rent(); pool.Return(first); pool.Return(second); CoreChecks.Assert(!second);
        }
        UnityEngine.Object.DestroyImmediate(prefab); UnityEngine.Object.DestroyImmediate(root);
    }

    private static void CheckResources()
    {
        var assets = new ResourcesAssetProvider();
        var text = assets.Load<TextAsset>("framework-validation");
        CoreChecks.Assert(text && text.text.Trim() == "standalone-resource-ok");
        assets.Release(text);
    }
}

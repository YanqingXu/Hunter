using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YouYou;

/// <summary>Explicit offline validation entry; never opens or runs the original account-server launch scene.</summary>
public static class YouYouFullValidation
{
    public const string ScenePath = "Assets/YouYouFullValidation/YouYouFullValidation.unity";
    public const string ParamsPath = "Assets/YouYouFramework/YouYouAssets/ParamsSettings.asset";

    public static void RunCore() { Run(YouYouValidationStage.Core); }
    public static void RunContent() { Run(YouYouValidationStage.Content); }
    public static void RunBundles() { Run(YouYouValidationStage.Bundles); }
    public static void RunScenes() { Run(YouYouValidationStage.Scenes); }
    public static void RunNetwork() { Run(YouYouValidationStage.Network); }

    // Run this in a separate -quit invocation so the validation starts in a newly compiled domain.
    public static void PrepareDirectMode() { PrepareMode(true); }
    public static void PrepareBundleMode() { PrepareMode(false); }

    private static void PrepareMode(bool direct)
    {
        const string symbol = "DISABLE_ASSETBUNDLE";
        var symbols = new List<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Split(';'));
        symbols.RemoveAll(value => value == symbol || string.IsNullOrEmpty(value));
        if (direct) symbols.Add(symbol);
        string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
        string target = string.Join(";", symbols);
        if (current != target) PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, target);
        AssetDatabase.SaveAssets();
        Debug.Log("YOUYOU_FULL_VALIDATION_MODE_PREPARED: " + (direct ? "Direct" : "Bundles"));
    }

    [MenuItem("Tools/YouYou Full Migration/Create Offline Validation Scene")]
    public static void CreateScene() { Prepare(YouYouValidationStage.Core); }

    private static void Run(YouYouValidationStage stage)
    {
        try
        {
            Prepare(stage);
            Debug.Log("YOUYOU_FULL_VALIDATION_START: " + stage);
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    private static void Prepare(YouYouValidationStage stage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before validation.");
        Directory.CreateDirectory("Assets/YouYouFullValidation");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Original Framework Validation").AddComponent<YouYouFullValidationProbe>();
        probe.Stage = stage;
        if (stage == YouYouValidationStage.Bundles) BuildBundleFixtures(probe);
        var root = new GameObject("Original YouYou GameEntry");
        root.SetActive(false);
        var entry = root.AddComponent<GameEntry>();
        var settings = AssetDatabase.LoadAssetAtPath<ParamsSettings>(ParamsPath);
        if (!settings) throw new FileNotFoundException("Original ParamsSettings is required", ParamsPath);
        entry.Configure(settings, ParamsSettings.DeviceGrade.High, YouYouLanguage.Chinese, false);
        entry.PoolParent = Child("Original Pool Root", root.transform, false);
        entry.GameObjectPoolGroups = new[] { new GameObjectPoolEntity { PoolId = 1, PoolName = "Role" } };
        entry.LockedAssetBundle = Array.Empty<string>();
        entry.StandardWidth = 1280; entry.StandardHeight = 720;

        var camera = Child("Validation Camera", root.transform, false).gameObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .08f, .13f);
        camera.gameObject.AddComponent<AudioListener>();
        if (stage == YouYouValidationStage.Content) camera.gameObject.AddComponent<FMODUnity.StudioListener>();
        entry.UICamera = camera;
        var ui = Child("Original UI Root", root.transform, true).gameObject;
        entry.UIRootCanvas = ui.AddComponent<Canvas>();
        entry.UIRootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        entry.UIRootRectTransform = (RectTransform)ui.transform;
        entry.UIRootCanvasScaler = ui.AddComponent<CanvasScaler>();
        entry.UIRootCanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        entry.UIRootCanvasScaler.referenceResolution = new Vector2(1280, 720);
        ui.AddComponent<GraphicRaycaster>();
        entry.UIGroups = new UIGroup[4];
        ushort[] sorting = { 100, 1000, 6000, 0 };
        for (byte i = 0; i < 4; i++) entry.UIGroups[i] = new UIGroup
        { Id = (byte)(i + 1), BaseOrder = sorting[i], Group = Child("Original UI Group " + (i + 1), ui.transform, true) };
        new GameObject("Validation Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        root.SetActive(true);
        probe.Entry = entry;
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save validation scene.");
        if (stage == YouYouValidationStage.Scenes)
        {
            PrepareSceneFixtures(probe, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        AssetDatabase.SaveAssets();
    }

    private static Transform Child(string name, Transform parent, bool rect)
    {
        var obj = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
        obj.transform.SetParent(parent, false);
        if (obj.transform is RectTransform transform)
        { transform.anchorMin = Vector2.zero; transform.anchorMax = Vector2.one; transform.offsetMin = transform.offsetMax = Vector2.zero; }
        return obj.transform;
    }

    private static void BuildBundleFixtures(YouYouFullValidationProbe probe)
    {
#if DISABLE_ASSETBUNDLE
        throw new InvalidOperationException("Remove DISABLE_ASSETBUNDLE for RunBundles: this stage must execute the original AssetBundle branch.");
#else
        string id = Guid.NewGuid().ToString("N");
        probe.BundleFixtureDirectory = Path.GetFullPath("TestResults/YouYouFull/Bundles-" + id);
        probe.BundleRelativePath = "youyou-validation/" + id + "/probe.assetbundle";
        Directory.CreateDirectory("Assets/YouYouFullValidation/Fixtures");
        foreach (string version in new[] { "first", "replacement" })
        {
            File.WriteAllText(YouYouFullValidationProbe.PayloadPath, "original-youyou-" + version);
            AssetDatabase.ImportAsset(YouYouFullValidationProbe.PayloadPath, ImportAssetOptions.ForceSynchronousImport);
            string directory = Path.Combine(probe.BundleFixtureDirectory, version);
            Directory.CreateDirectory(directory);
            var manifest = BuildPipeline.BuildAssetBundles(directory, new[] { new AssetBundleBuild
            { assetBundleName = "probe.assetbundle", assetNames = new[] { YouYouFullValidationProbe.PayloadPath } } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            if (!manifest) throw new InvalidOperationException("Could not build real validation AssetBundle.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "probe.assetbundle"));
            string md5;
            using (var hash = MD5.Create()) md5 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            using (var stream = new MMO_MemoryStream())
            {
                stream.WriteInt(2); stream.WriteUTF8String(version);
                stream.WriteUTF8String(probe.BundleRelativePath); stream.WriteUTF8String(md5);
                stream.WriteULong((ulong)bytes.Length); stream.WriteByte(1); stream.WriteByte(0);
                File.WriteAllBytes(Path.Combine(directory, "version.bytes"), ZlibHelper.CompressBytes(stream.ToArray()));
            }
            using (var stream = new MMO_MemoryStream())
            {
                stream.WriteInt(1); stream.WriteByte((byte)AssetCategory.DataTable);
                stream.WriteUTF8String(YouYouFullValidationProbe.PayloadPath);
                stream.WriteUTF8String(probe.BundleRelativePath); stream.WriteInt(0);
                File.WriteAllBytes(Path.Combine(directory, "assetinfo.bytes"), ZlibHelper.CompressBytes(stream.ToArray()));
            }
        }
#endif
    }

    private static void PrepareSceneFixtures(YouYouFullValidationProbe probe, Scene hostScene)
    {
#if !DISABLE_ASSETBUNDLE
        throw new InvalidOperationException("RunScenes uses the original DISABLE_ASSETBUNDLE scene-name branch.");
#else
        Directory.CreateDirectory("Assets/YouYouFullValidation/Fixtures");
        var original = EditorBuildSettings.scenes;
        probe.OriginalBuildPaths = new string[original.Length];
        probe.OriginalBuildEnabled = new bool[original.Length];
        var scenes = new List<EditorBuildSettingsScene>();
        for (int i = 0; i < original.Length; i++)
        { probe.OriginalBuildPaths[i] = original[i].path; probe.OriginalBuildEnabled[i] = original[i].enabled; scenes.Add(original[i]); }
        foreach (string name in new[] { YouYouSceneValidation.FirstName, YouYouSceneValidation.SecondName })
        {
            string path = "Assets/YouYouFullValidation/Fixtures/" + name + ".unity";
            var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);
            new GameObject(name + "-Actual-Scene-Content");
            if (!EditorSceneManager.SaveScene(fixture, path)) throw new IOException("Could not save " + path);
            SceneManager.SetActiveScene(hostScene);
            EditorSceneManager.CloseScene(fixture, true);
            scenes.RemoveAll(value => value.path == path);
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
#endif
    }
}

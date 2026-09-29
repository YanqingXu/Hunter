using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BigWorld.YouYou2D.Editor;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.HotUpdate;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigWorld.HotUpdate.Editor
{
    public static class HybridProjectBuild
    {
        public const string BootScene = "Assets/_Project/Modules/HotUpdate/Boot/HybridBoot.unity";
        private const string ConfigPath = "ProjectSettings/HotUpdateProject.json";
        private const string BootConfigPath = "Assets/_Project/Modules/HotUpdate/Boot/boot-config.json";
        private const string SceneBundle = "hotupdate/scenes.assetbundle";
        private static readonly string[] HotAssemblies = { "SkillEditorKit.Runtime", "Assembly-CSharp" };
        private static readonly string[] OriginalContent = { "VersionFile.bytes", "AssetInfo.bytes", "download/datatable.assetbundle",
            "download/audio.assetbundle", "download/ui.assetbundle", "download/reporter.assetbundle", "youyou2d/project.assetbundle" };

        [Serializable] public sealed class ProjectConfig
        {
            public string[] scenes;
            public string updateUrl = "", baseId;
            public bool development;
        }
        public static ProjectConfig Config => File.Exists(ConfigPath)
            ? JsonUtility.FromJson<ProjectConfig>(File.ReadAllText(ConfigPath)) : new ProjectConfig();
        private static void Save(ProjectConfig config) => File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
        public static string[] GameScenes => Config.scenes;

        [MenuItem("Tools/BigWorld/热更新/初始化 HybridCLR")]
        public static void Configure()
        {
            var config = Config;
            if (config.scenes == null || config.scenes.Length == 0)
            {
                config.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled && scene.path != BootScene).Select(scene => scene.path).ToArray();
                if (config.scenes.Length == 0) config.scenes = new[] { "Assets/_Project/Game/Scenes/BorderTrial.unity" };
            }
            Save(config);
            var settings = HybridCLRSettings.Instance;
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            settings.hybridclrRepoURL = "https://github.com/focus-creative-games/hybridclr";
            settings.il2cppPlusRepoURL = "https://github.com/focus-creative-games/il2cpp_plus";
            settings.hotUpdateAssemblies = HotAssemblies;
            settings.hotUpdateAssemblyDefinitions = Array.Empty<UnityEditorInternal.AssemblyDefinitionAsset>();
            settings.outputLinkFile = "_Project/Modules/HotUpdate/Generated/link.xml";
            settings.outputAOTGenericReferenceFile = "_Project/Modules/HotUpdate/Generated/AOTGenericReferences.cs";
            Directory.CreateDirectory("Assets/_Project/Modules/HotUpdate/Generated");
            HybridCLRSettings.Save();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
            EnsureBoot(config);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR() || installer.PackageVersion != installer.InstalledLibil2cppVersion)
                installer.InstallDefaultHybridCLR();
            if (!installer.HasInstalledHybridCLR()) throw new BuildFailedException("HybridCLR 本地运行时安装未完成。");
            AssetDatabase.SaveAssets();
            Debug.Log("BIGWORLD_HYBRIDCLR_CONFIGURED: 8.15.0 / IL2CPP");
        }

        public static void SetGameScene(string path)
        {
            var config = Config;
            config.scenes = new[] { path };
            Save(config);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScene))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
        }

        private static void EnsureBoot(ProjectConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootScene));
            File.WriteAllText(BootConfigPath, JsonUtility.ToJson(new HotUpdateBootConfig { baseId = config.baseId ?? "unbuilt", updateUrl = config.updateUrl }, true));
            AssetDatabase.ImportAsset(BootConfigPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScene)) return;
            var mode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            var root = new GameObject("HybridCLR Bootstrap", typeof(HotUpdateBootstrap));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            root.GetComponent<HotUpdateBootstrap>().Configuration = AssetDatabase.LoadAssetAtPath<TextAsset>(BootConfigPath);
            EditorSceneManager.SaveScene(scene, BootScene);
            if (mode == NewSceneMode.Additive) EditorSceneManager.CloseScene(scene, true);
        }

        public static void PreparePlayerContent(bool development)
        {
            ValidateScenes();
            EditorUserBuildSettings.development = development;
            var config = Config;
            config.baseId = "bw-" + Guid.NewGuid().ToString("N");
            config.development = development;
            Save(config);
            EnsureBoot(config);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
            AssetDatabase.SaveAssets();
            PrebuildCommand.GenerateAll();
            var target = BuildTarget.StandaloneWindows64;
            string stripped = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string generated = File.ReadAllText("Assets/" + HybridCLRSettings.Instance.outputAOTGenericReferenceFile);
            string[] metadata = Regex.Matches(generated, @"(?m)^\s*""([^""\s]+\.dll)"",?\s*$").Cast<Match>()
                .Select(match => match.Groups[1].Value).Distinct().OrderBy(value => value).ToArray();
            if (metadata.Length == 0) throw new BuildFailedException("没有生成 AOT 补充元数据清单。");
            HybridCLRSettings.Instance.patchAOTAssemblies = metadata.Select(Path.GetFileNameWithoutExtension).ToArray();
            HybridCLRSettings.Save();
            string root = Application.streamingAssetsPath;
            NativeFramework2DBuild.PrepareContent(target, root);
            BuildScenes(root);
            var files = OriginalContent.Select(path => Record(root, path, "content")).ToList();
            files.Add(Record(root, SceneBundle, "content"));
            foreach (string name in metadata)
            {
                string path = "hotupdate/aot/" + name + ".bytes";
                Copy(Path.Combine(stripped, name), Path.Combine(root, path));
                files.Add(Record(root, path, "aot", Path.GetFileNameWithoutExtension(name)));
            }
            AddHotDlls(root, files);
            var manifest = new HotUpdateManifest { baseId = config.baseId, platform = target.ToString(), version = "base-" + Stamp(),
                entryScene = config.scenes[0], sceneBundle = SceneBundle, files = files.ToArray() };
            manifest.Validate(config.baseId, target.ToString());
            WriteManifest(Path.Combine(root, "hotupdate/manifest.json"), manifest);
            string baseline = BaseDirectory(config.baseId);
            Directory.CreateDirectory(baseline);
            WriteManifest(Path.Combine(baseline, "manifest.json"), manifest);
            foreach (var file in files) Copy(Path.Combine(root, file.path), Path.Combine(baseline, "content", file.path));
            foreach (string dll in Directory.GetFiles(stripped, "*.dll")) Copy(dll, Path.Combine(baseline, "StrippedAOT", Path.GetFileName(dll)));
            foreach (string dll in Directory.GetFiles(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target), "*.dll"))
                if (!HotAssemblies.Contains(Path.GetFileNameWithoutExtension(dll))) Copy(dll, Path.Combine(baseline, "CompiledAOT", Path.GetFileName(dll)));
            File.WriteAllText(Path.Combine(baseline, "config.json"), JsonUtility.ToJson(config, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("BIGWORLD_HYBRIDCLR_CONTENT_READY: " + config.baseId + ", " + metadata.Length + " AOT metadata DLLs");
        }

        [MenuItem("Tools/BigWorld/热更新/生成当前主包的补丁")]
        public static void BuildPatch()
        {
            var config = Config;
            string baseline = BaseDirectory(config.baseId);
            string manifestPath = Path.Combine(baseline, "manifest.json");
            if (!File.Exists(manifestPath)) throw new BuildFailedException("请先构建 HybridCLR 主包；补丁必须匹配它保存的 AOT 基线。");
            var original = JsonUtility.FromJson<HotUpdateManifest>(File.ReadAllText(manifestPath));
            CompileDllCommand.CompileDll(BuildTarget.StandaloneWindows64, config.development);
            string compiled = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
            var baselineAot = Directory.GetFiles(Path.Combine(baseline, "CompiledAOT"), "*.dll").Select(Path.GetFileName).OrderBy(name => name).ToArray();
            var currentAot = Directory.GetFiles(compiled, "*.dll").Where(path => !HotAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
                .Select(Path.GetFileName).OrderBy(name => name).ToArray();
            if (!baselineAot.SequenceEqual(currentAot)) throw new BuildFailedException("AOT 程序集列表已变化，需要重新打主包。");
            foreach (string dll in Directory.GetFiles(Path.Combine(baseline, "CompiledAOT"), "*.dll"))
            {
                string current = Path.Combine(compiled, Path.GetFileName(dll));
                if (!File.Exists(current) || HotUpdateManifest.Hash(File.ReadAllBytes(current)) != HotUpdateManifest.Hash(File.ReadAllBytes(dll)))
                    throw new BuildFailedException("AOT 代码已变化，需要重新打主包：" + Path.GetFileName(dll));
            }
            var checker = new MissingMetadataChecker(Path.Combine(baseline, "StrippedAOT"), HotAssemblies);
            foreach (string assembly in HotAssemblies)
                if (!checker.Check(Path.Combine(compiled, assembly + ".dll"))) throw new BuildFailedException("补丁引用了当前主包已裁剪的 AOT API，需要重新打主包。");
            ValidateScenes();
            string output = Path.GetFullPath("Builds/HotUpdate/Patches/" + config.baseId + "/patch-" + Stamp());
            Directory.CreateDirectory(output);
            NativeFramework2DBuild.PrepareContent(BuildTarget.StandaloneWindows64, output);
            BuildScenes(output);
            var files = OriginalContent.Select(path => Record(output, path, "content")).ToList();
            files.Add(Record(output, SceneBundle, "content"));
            foreach (var file in original.files.Where(file => file.kind == "aot"))
            {
                Copy(Path.Combine(baseline, "content", file.path), Path.Combine(output, file.path));
                files.Add(file);
            }
            AddHotDlls(output, files);
            var patch = new HotUpdateManifest { baseId = config.baseId, platform = original.platform, version = "patch-" + Stamp(),
                entryScene = config.scenes[0], sceneBundle = SceneBundle, files = files.ToArray() };
            patch.Validate(config.baseId, original.platform);
            WriteManifest(Path.Combine(output, "manifest.json"), patch);
            File.WriteAllText("Builds/HotUpdate/latest-patch.json", JsonUtility.ToJson(new PatchReport { output = output, baseId = config.baseId, version = patch.version }, true));
            Debug.Log("BIGWORLD_HYBRIDCLR_PATCH_SUCCEEDED: " + output);
        }

        [Serializable] private sealed class PatchReport { public string output, baseId, version; }
        private static string BaseDirectory(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new BuildFailedException("未生成 HybridCLR 主包。");
            HotUpdateManifest.ValidatePath(id);
            return Path.GetFullPath("Builds/HotUpdate/Bases/" + id);
        }
        private static void ValidateScenes()
        {
            if (GameScenes == null || GameScenes.Length == 0) throw new BuildFailedException("未配置热更游戏场景。");
            foreach (string path in GameScenes)
                if (path == BootScene || !AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) throw new BuildFailedException("无效的热更游戏场景：" + path);
        }
        private static void BuildScenes(string output)
        {
            string cache = Path.GetFullPath("Builds/HotUpdate/SceneCache/StandaloneWindows64");
            Directory.CreateDirectory(cache);
            var manifest = BuildPipeline.BuildAssetBundles(cache, new[] { new AssetBundleBuild { assetBundleName = SceneBundle, assetNames = GameScenes } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            if (!manifest) throw new BuildFailedException("热更场景包构建失败。");
            Copy(Path.Combine(cache, SceneBundle), Path.Combine(output, SceneBundle));
        }
        private static void AddHotDlls(string output, List<HotUpdateFile> files)
        {
            foreach (string name in HotAssemblies)
            {
                string path = "hotupdate/assemblies/" + name + ".dll.bytes";
                Copy(Path.Combine(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64), name + ".dll"), Path.Combine(output, path));
                files.Add(Record(output, path, "assembly", name));
            }
        }
        private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        private static void Copy(string from, string to) { Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(from, to, true); }
        private static void WriteManifest(string path, HotUpdateManifest manifest)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(manifest, true)); }
        private static HotUpdateFile Record(string root, string path, string kind, string assembly = null)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            return new HotUpdateFile { path = path, kind = kind, assembly = assembly, size = bytes.LongLength, sha256 = HotUpdateManifest.Hash(bytes) };
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BigWorld.YouYou2D.Editor;
using BigWorld.HotUpdate.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigWorld.Editor
{
    /// <summary>Builds the AOT startup Player and the configured hot-update game content.</summary>
    public static class BigWorldBuild
    {
        private const string PendingWindowsBuild = "BigWorld.PendingWindowsBuild";
        private const string EditorAssetMode = "DISABLE_ASSETBUNDLE";

        [InitializeOnLoadMethod]
        private static void ResumeBuildAfterReload()
        {
            if (!Application.isBatchMode && SessionState.GetInt(PendingWindowsBuild, -1) >= 0)
                EditorApplication.update += WaitForBuildPreparation;
        }

        private static void WaitForBuildPreparation()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitForBuildPreparation;
            int mode = SessionState.GetInt(PendingWindowsBuild, -1);
            SessionState.EraseInt(PendingWindowsBuild);
            if (mode < 0) return;
            if (EditorUtility.scriptCompilationFailed)
                throw new BuildFailedException("脚本编译失败，请先修复 Console 中的错误后重新打包。");
            RequestWindowsBuild(mode == 1);
        }

        /// <summary>Batch phase one: persist Player-safe settings, then restart Unity before building.</summary>
        public static void PrepareWindowsBuild()
        {
            RemoveEditorAssetMode();
            ConfigureWindowsPlugins();
            HybridProjectBuild.Configure();
            AssetDatabase.SaveAssets();
            Debug.Log("BIGWORLD_WINDOWS_PREPARATION_SUCCEEDED");
        }

        private static bool RemoveEditorAssetMode()
        {
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
            string[] safe = symbols.Split(';').Where(value => value != EditorAssetMode).ToArray();
            if (safe.Length == symbols.Split(';').Length) return false;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, string.Join(";", safe));
            return true;
        }

        private static void RequestWindowsBuild(bool development)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new BuildFailedException("请先退出 Play Mode，并等待当前构建结束。");
            bool needsTarget = EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64;
            bool needsCompilation = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone)
                .Split(';').Contains(EditorAssetMode) ||
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.IL2CPP ||
                PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone) != ApiCompatibilityLevel.NET_Unity_4_8;
#if DISABLE_ASSETBUNDLE
            needsCompilation = true;
#endif
            if (needsCompilation || needsTarget)
            {
                if (Application.isBatchMode)
                    throw new BuildFailedException("请先执行 BigWorldBuild.PrepareWindowsBuild 并重新启动 Unity，或运行 Tools/Build-Windows.ps1。");
                SessionState.SetInt(PendingWindowsBuild, development ? 1 : 0);
                if (needsTarget && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                {
                    SessionState.EraseInt(PendingWindowsBuild);
                    throw new BuildFailedException("无法切换到 Windows 64 位构建目标。");
                }
                RemoveEditorAssetMode();
                HybridProjectBuild.Configure();
                AssetDatabase.SaveAssets();
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                Debug.Log("已关闭编辑器直接读取模式；脚本重编译后会继续 Windows 打包。");
                return;
            }
            ConfigureWindowsPlugins();
            HybridProjectBuild.Configure();
            BuildWindows(development);
        }
        [Serializable]
        private sealed class BuildSummary
        {
            public string unityVersion, target, result, output, archive, utc, scriptingBackend, contentDirectory;
            public string[] scenes;
            public bool development, runtimeFilesVerified;
            public int errors, warnings;
            public long bytes;
            public double seconds;
        }

        [MenuItem("Tools/BigWorld/打包/Windows 64 位（发布）", false, 100)]
        public static void WindowsRelease() { RequestWindowsBuild(false); }

        [MenuItem("Tools/BigWorld/打包/Windows 64 位（开发调试）", false, 101)]
        public static void WindowsDevelopment() { RequestWindowsBuild(true); }

        [MenuItem("Tools/BigWorld/打包/使用 2D 试玩场景", false, 120)]
        public static void UseDemoScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Framework2DDemoBuilder.ScenePath))
                throw new BuildFailedException("找不到 2D 示例，请先通过 2D 框架菜单创建示例资源。");
            HybridProjectBuild.SetGameScene(Framework2DDemoBuilder.ScenePath);
            Debug.Log("打包入口已设置为：" + Framework2DDemoBuilder.ScenePath);
        }

        [MenuItem("Tools/BigWorld/打包/使用正式关卡（边境试炼）", false, 119)]
        public static void UseGameScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(BorderTrialBuilder.ScenePath))
                throw new BuildFailedException("请先通过正式关卡菜单创建边境试炼。");
            HybridProjectBuild.SetGameScene(BorderTrialBuilder.ScenePath);
            Debug.Log("打包入口已设置为：" + BorderTrialBuilder.ScenePath);
        }

        [MenuItem("Tools/BigWorld/打包/打开输出目录", false, 140)]
        public static void OpenOutput()
        {
            string path = Path.Combine(ProjectRoot, "Builds"); Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        public static void ConfigureAndBuildWindowsDemo()
        {
            UseDemoScene();
            RequestWindowsBuild(false);
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static void BuildWindows(bool development)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new BuildFailedException("请先退出 Play Mode，并等待当前构建结束。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new BuildFailedException("当前 Unity 未安装 Windows Build Support。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.SaveAssets();
            string[] scenes = HybridProjectBuild.GameScenes ?? Array.Empty<string>();
            if (scenes.Length == 0) throw new BuildFailedException("HotUpdateProject.json 未配置游戏场景。可先选择“使用正式关卡”。");
            foreach (string scene in scenes)
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(scene)) throw new BuildFailedException("打包场景不存在：" + scene);
            if (scenes.Distinct().Count() != scenes.Length) throw new BuildFailedException("HotUpdateProject.json 中存在重复场景。");

            // This generates the original AssetBundles, VersionFile and AssetInfo for the Player loader.
            HybridProjectBuild.PreparePlayerContent(development);
            AssetDatabase.SaveAssets();

            string mode = development ? "Development" : "Release";
            string folder = Path.Combine(ProjectRoot, "Builds", "Windows64", mode + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            if (Directory.Exists(folder)) throw new BuildFailedException("输出目录已存在：" + folder);
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { HybridProjectBuild.BootScene },
                locationPathName = Path.Combine(folder, "BigWorld.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode | (development ? BuildOptions.Development : BuildOptions.None)
            });
            var result = new BuildSummary
            {
                unityVersion = Application.unityVersion, target = "StandaloneWindows64",
                scriptingBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString(),
                contentDirectory = "BigWorld_Data/StreamingAssets",
                result = report.summary.result.ToString(), output = folder, archive = folder + ".zip",
                utc = DateTime.UtcNow.ToString("o"), scenes = scenes, development = development,
                errors = (int)report.summary.totalErrors, warnings = (int)report.summary.totalWarnings,
                bytes = (long)report.summary.totalSize, seconds = report.summary.totalTime.TotalSeconds
            };
            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText(Path.Combine(folder, "build-report.json"), json);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows 构建失败，报告：" + Path.Combine(folder, "build-report.json"));
            VerifyRuntimeFiles(folder, development);
            result.runtimeFilesVerified = true;
            json = JsonUtility.ToJson(result, true);
            File.WriteAllText(Path.Combine(folder, "build-report.json"), json);
            File.WriteAllText(Path.Combine(folder, "运行说明.txt"),
                "双击 BigWorld.exe。请完整保留同目录的 DLL、BigWorld_Data 和 StreamingAssets 等文件。使用 HybridCLR / IL2CPP 加载 C# 热更新。\n" +
                "边境试炼：Enter 或按钮开始，A/D 移动，空格跳跃（松开短跳），J 射击，K 近战，Esc 暂停，死亡或通关后 R 重试。\n" +
                "击败 3 名守卫并到达右侧出口。中途旗帜是检查点，复活保留本轮击杀进度；重新开始会重置整关。\n");
            string fontLicense = Path.Combine(ProjectRoot, "Assets", "_Project", "Game", "UI", "Fonts", "OFL.txt");
            if (File.Exists(fontLicense)) File.Copy(fontLicense, Path.Combine(folder, "NotoFont-OFL.txt"));
            string licenses = Path.Combine(ProjectRoot, "Docs", "Licenses");
            if (Directory.Exists(licenses))
            {
                Directory.CreateDirectory(Path.Combine(folder, "Licenses"));
                foreach (string license in Directory.GetFiles(licenses, "*.txt"))
                    File.Copy(license, Path.Combine(folder, "Licenses", Path.GetFileName(license)));
            }
            ZipFile.CreateFromDirectory(folder, result.archive, System.IO.Compression.CompressionLevel.Optimal, true);
            File.WriteAllText(Path.Combine(ProjectRoot, "Builds", "latest-windows.json"), json);
            Debug.Log("BIGWORLD_WINDOWS_BUILD_SUCCEEDED: " + result.archive);
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(result.archive);
        }

        private static void ConfigureWindowsPlugins()
        {
            string[] nativeDlls =
            {
                "Assets/Plugins/FMOD/lib/win/x86_64/fmodstudio.dll",
                "Assets/Plugins/FMOD/lib/win/x86_64/fmodstudiol.dll",
                "Assets/Plugins/FMOD/lib/win/x86_64/resonanceaudio.dll"
            };
            foreach (string path in nativeDlls)
            {
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null) throw new BuildFailedException("缺少课程原版 Windows 原生插件：" + path);
                bool changed = !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64) ||
                    importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU") != "x86_64";
                if (!changed) continue;
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.SaveAndReimport();
            }
            // The course metadata incorrectly includes Linux .so files in Windows Players.
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (!importer.assetPath.StartsWith("Assets/Plugins/", StringComparison.Ordinal) ||
                    !importer.assetPath.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                    !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)) continue;
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, false);
                importer.SaveAndReimport();
            }
        }

        private static void VerifyRuntimeFiles(string folder, bool development)
        {
            string data = Path.Combine(folder, "BigWorld_Data");
            if (!File.Exists(Path.Combine(folder, "GameAssembly.dll")))
                throw new BuildFailedException("HybridCLR 发布包缺少 IL2CPP GameAssembly.dll。");
            string plugins = Path.Combine(data, "Plugins");
            foreach (string dll in new[] { development ? "fmodstudiol.dll" : "fmodstudio.dll" })
                if (!Directory.Exists(plugins) || !Directory.EnumerateFiles(plugins, dll, SearchOption.AllDirectories).Any())
                    throw new BuildFailedException("构建缺少原版原生插件：" + dll);
            string content = Path.Combine(data, "StreamingAssets");
            if (!Directory.Exists(content) || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
                throw new BuildFailedException("构建缺少原版框架内容目录：" + content);
            foreach (string manifest in new[] { YouYou.ConstDefine.VersionFileName, YouYou.ConstDefine.AssetInfoName })
                if (!File.Exists(Path.Combine(content, manifest)))
                    throw new BuildFailedException("构建缺少原版资源清单：" + manifest);
            foreach (string bundle in new[] { "download/datatable.assetbundle", "download/audio.assetbundle",
                "download/ui.assetbundle", "download/reporter.assetbundle", "youyou2d/project.assetbundle" })
                if (!File.Exists(Path.Combine(content, bundle)))
                    throw new BuildFailedException("构建缺少原版运行资源包：" + bundle);
            foreach (string path in new[] { "hotupdate/manifest.json", "hotupdate/scenes.assetbundle",
                "hotupdate/assemblies/Assembly-CSharp.dll.bytes", "hotupdate/assemblies/SkillEditorKit.Runtime.dll.bytes" })
                if (!File.Exists(Path.Combine(content, path))) throw new BuildFailedException("构建缺少 HybridCLR 内容：" + path);
        }
    }

    /// <summary>Also protects Players built directly from Unity's Build Settings window.</summary>
    internal sealed class NativeFrameworkPlayerGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            var group = BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            if (PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Contains("DISABLE_ASSETBUNDLE"))
                throw new BuildFailedException("Player 不允许 DISABLE_ASSETBUNDLE；请使用 Tools/BigWorld/打包 或先关闭该宏并等待脚本编译完成。");
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BigWorld.YouYou2D.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigWorld.Editor
{
    /// <summary>Build scene selection comes from Build Settings; output never goes under Assets.</summary>
    public static class BigWorldBuild
    {
        [Serializable]
        private sealed class BuildSummary
        {
            public string unityVersion, target, result, output, archive, utc;
            public string[] scenes;
            public bool development;
            public int errors, warnings;
            public long bytes;
            public double seconds;
        }

        [MenuItem("Tools/BigWorld/打包/Windows 64 位（发布）", false, 100)]
        public static void WindowsRelease() { BuildWindows(false); }

        [MenuItem("Tools/BigWorld/打包/Windows 64 位（开发调试）", false, 101)]
        public static void WindowsDevelopment() { BuildWindows(true); }

        [MenuItem("Tools/BigWorld/打包/使用 2D 试玩场景", false, 120)]
        public static void UseDemoScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Framework2DDemoBuilder.ScenePath))
                throw new BuildFailedException("找不到 2D 示例，请先通过 2D 框架菜单创建示例资源。");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Framework2DDemoBuilder.ScenePath, true) };
            Debug.Log("打包入口已设置为：" + Framework2DDemoBuilder.ScenePath);
        }

        [MenuItem("Tools/BigWorld/打包/使用正式关卡（边境试炼）", false, 119)]
        public static void UseGameScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(BorderTrialBuilder.ScenePath))
                throw new BuildFailedException("请先通过正式关卡菜单创建边境试炼。");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BorderTrialBuilder.ScenePath, true) };
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
            BuildWindows(false);
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
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new BuildFailedException("Build Settings 中没有启用的场景。可先选择“使用 2D 试玩场景”。");
            foreach (string scene in scenes)
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(scene)) throw new BuildFailedException("打包场景不存在：" + scene);
            if (scenes.Distinct().Count() != scenes.Length) throw new BuildFailedException("Build Settings 中存在重复场景。");

            string mode = development ? "Development" : "Release";
            string folder = Path.Combine(ProjectRoot, "Builds", "Windows64", mode + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            if (Directory.Exists(folder)) throw new BuildFailedException("输出目录已存在：" + folder);
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, "BigWorld.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode | (development ? BuildOptions.Development : BuildOptions.None)
            });
            var result = new BuildSummary
            {
                unityVersion = Application.unityVersion, target = "StandaloneWindows64",
                result = report.summary.result.ToString(), output = folder, archive = folder + ".zip",
                utc = DateTime.UtcNow.ToString("o"), scenes = scenes, development = development,
                errors = (int)report.summary.totalErrors, warnings = (int)report.summary.totalWarnings,
                bytes = (long)report.summary.totalSize, seconds = report.summary.totalTime.TotalSeconds
            };
            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText(Path.Combine(folder, "build-report.json"), json);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows 构建失败，报告：" + Path.Combine(folder, "build-report.json"));
            File.WriteAllText(Path.Combine(folder, "运行说明.txt"),
                "双击 BigWorld.exe。请完整保留同目录的 DLL、BigWorld_Data 和 MonoBleedingEdge 等文件。\n" +
                "边境试炼：Enter 或按钮开始，A/D 移动，空格跳跃（松开短跳），J 射击，K 近战，Esc 暂停，死亡或通关后 R 重试。\n" +
                "击败 3 名守卫并到达右侧出口。中途旗帜是检查点，复活保留本轮击杀进度；重新开始会重置整关。\n");
            string fontLicense = Path.Combine(ProjectRoot, "Assets", "_Game", "UI", "Fonts", "OFL.txt");
            if (File.Exists(fontLicense)) File.Copy(fontLicense, Path.Combine(folder, "NotoFont-OFL.txt"));
            ZipFile.CreateFromDirectory(folder, result.archive, System.IO.Compression.CompressionLevel.Optimal, true);
            File.WriteAllText(Path.Combine(ProjectRoot, "Builds", "latest-windows.json"), json);
            Debug.Log("BIGWORLD_WINDOWS_BUILD_SUCCEEDED: " + result.archive);
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(result.archive);
        }
    }
}

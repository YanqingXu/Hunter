using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BigWorld.HotUpdate
{
    /// <summary>The only script on the shipped startup scene. It never references a hot assembly.</summary>
    public sealed class HotUpdateBootstrap : MonoBehaviour
    {
        public TextAsset Configuration;
        private string status = "正在准备游戏…";
        private bool failed;
        private AssetBundle sceneBundle;

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);
            HotUpdateRuntime.Reset();
            try { await Boot(); }
            catch (Exception exception)
            {
                failed = true;
                status = "游戏启动失败，请重新启动。已下载的失败版本将自动回退。";
                Debug.LogException(exception);
            }
        }

        private async Task Boot()
        {
            if (!Configuration) throw new InvalidDataException("缺少热更启动配置。");
            var config = JsonUtility.FromJson<HotUpdateBootConfig>(Configuration.text);
            if (config == null || string.IsNullOrEmpty(config.baseId)) throw new InvalidDataException("无效的主包配置。");
            HotUpdateManifest.ValidatePath(config.baseId);
            string cache = Path.Combine(Application.persistentDataPath, "HybridCLR", config.baseId);
            Directory.CreateDirectory(Path.Combine(cache, "releases"));
            Directory.CreateDirectory(Path.Combine(cache, "failed"));
            string pending = Path.Combine(cache, "booting.txt");
            if (File.Exists(pending))
            {
                string previous = File.ReadAllText(pending);
                if (IsId(previous)) File.WriteAllText(Path.Combine(cache, "failed", previous), "Previous startup did not reach WorldReady.");
                File.Delete(pending);
                Debug.LogWarning("BIGWORLD_HYBRIDCLR_ROLLBACK: previous startup was incomplete");
            }
            byte[] baselineBytes = await Read(Location(Application.streamingAssetsPath, "hotupdate/manifest.json"), config.timeoutSeconds);
            HotUpdateManifest baseline = Parse(baselineBytes, config);
            string baselineId = HotUpdateManifest.Hash(baselineBytes);
            string selectedId = baselineId;
            HotUpdateManifest selected = baseline;
            await Materialize(cache, baselineId, baseline, baselineBytes, Application.streamingAssetsPath, config.timeoutSeconds);
            string active = Path.Combine(cache, "active.txt");
            if (File.Exists(active))
            {
                string id = File.ReadAllText(active);
                if (IsId(id) && !File.Exists(Path.Combine(cache, "failed", id)))
                {
                    try
                    {
                        string root = Path.Combine(cache, "releases", id);
                        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
                        if (HotUpdateManifest.Hash(bytes) != id) throw new InvalidDataException("缓存清单校验失败。");
                        var manifest = Parse(bytes, config);
                        ValidateAot(baseline, manifest);
                        if (!manifest.files.All(file => HotUpdateManifest.VerifyFile(root, file))) throw new InvalidDataException("缓存文件校验失败。");
                        selected = manifest; selectedId = id;
                    }
                    catch (Exception exception) { Debug.LogWarning("BIGWORLD_HYBRIDCLR_CACHE_REJECTED: " + exception.Message); }
                }
            }

            string updateUrl = config.updateUrl;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == "-bigworld-update-url") updateUrl = arguments[i + 1];
            if (!string.IsNullOrWhiteSpace(updateUrl))
            {
                status = "正在检查更新…";
                try
                {
                    var url = new Uri(updateUrl, UriKind.Absolute);
                    if (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback))
                        throw new InvalidDataException("远程热更地址需要 HTTPS；本机验证允许 HTTP。");
                    byte[] bytes = await Read(url.AbsoluteUri, config.timeoutSeconds);
                    var manifest = Parse(bytes, config);
                    ValidateAot(baseline, manifest);
                    string id = HotUpdateManifest.Hash(bytes);
                    if (File.Exists(Path.Combine(cache, "failed", id)))
                        Debug.LogWarning("BIGWORLD_HYBRIDCLR_REJECTED: this release previously failed startup");
                    else
                    {
                        status = "正在下载更新…";
                        await Materialize(cache, id, manifest, bytes, new Uri(url, ".").AbsoluteUri, config.timeoutSeconds);
                        selected = manifest; selectedId = id;
                    }
                }
                catch (Exception exception)
                {
                    // Before loading any assembly, an incomplete update can safely fall back to verified content.
                    Debug.LogWarning("BIGWORLD_HYBRIDCLR_UPDATE_REJECTED: " + exception.Message);
                }
            }
            string content = Path.Combine(cache, "releases", selectedId);
            HotUpdateRuntime.ContentRoot = content;
            HotUpdateRuntime.Version = selected.version;
            HotUpdateRuntime.CacheRoot = cache;
            HotUpdateRuntime.ReleaseId = selectedId;
            HotUpdateRuntime.WriteAtomic(pending, selectedId);
            status = "正在载入游戏…";
#if !UNITY_EDITOR
            foreach (var file in selected.files.Where(file => file.kind == "aot"))
            {
                var result = RuntimeApi.LoadMetadataForAOTAssembly(File.ReadAllBytes(Path.Combine(content, file.path)), HomologousImageMode.SuperSet);
                if (result != LoadImageErrorCode.OK) throw new InvalidOperationException("AOT metadata " + file.assembly + ": " + result);
            }
#endif
            Assembly game = null;
            foreach (string name in new[] { "SkillEditorKit.Runtime", "Assembly-CSharp" })
            {
                var file = selected.files.Single(value => value.kind == "assembly" && value.assembly == name);
#if UNITY_EDITOR
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().Single(value => value.GetName().Name == file.assembly);
#else
                Assembly assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(content, file.path)));
#endif
                if (assembly.GetName().Name != file.assembly) throw new InvalidDataException("程序集名称不符。");
                if (file.assembly == "Assembly-CSharp") game = assembly;
                Debug.Log("BIGWORLD_HYBRIDCLR_ASSEMBLY_LOADED: " + file.assembly);
            }
            var entry = game?.GetType("BigWorld.HotUpdate.GameEntryPoint", true)?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            if (entry == null) throw new MissingMethodException("Missing hot-update entry point.");
            entry.Invoke(null, null);
#if UNITY_EDITOR
            var load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(selected.entryScene,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            var request = AssetBundle.LoadFromFileAsync(Path.Combine(content, selected.sceneBundle));
            while (!request.isDone) await Task.Yield();
            sceneBundle = request.assetBundle;
            string scenePath = sceneBundle ? sceneBundle.GetAllScenePaths().FirstOrDefault(path => string.Equals(path, selected.entryScene, StringComparison.OrdinalIgnoreCase)) : null;
            if (scenePath == null) throw new InvalidDataException("热更场景包无效。");
            var load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
#endif
            if (load == null) throw new InvalidOperationException("无法加载热更场景。");
            while (!load.isDone) await Task.Yield();
            float deadline = Time.realtimeSinceStartup + 90;
            while (!HotUpdateRuntime.IsReady)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("热更关卡未完成初始化。");
                await Task.Yield();
            }
            status = null;
        }

        private static HotUpdateManifest Parse(byte[] bytes, HotUpdateBootConfig config)
        {
            if (bytes.Length > 1048576) throw new InvalidDataException("清单过大。");
            var manifest = JsonUtility.FromJson<HotUpdateManifest>(Encoding.UTF8.GetString(bytes));
            if (manifest == null) throw new InvalidDataException("清单为空。");
            manifest.Validate(config.baseId, config.platform);
            return manifest;
        }

        private static void ValidateAot(HotUpdateManifest baseline, HotUpdateManifest update)
        {
            var original = baseline.files.Where(file => file.kind == "aot").ToDictionary(file => file.path);
            var candidates = update.files.Where(file => file.kind == "aot").ToArray();
            if (original.Count != candidates.Length) throw new InvalidDataException("补丁不能更换主包 AOT 元数据。");
            foreach (var file in candidates)
                if (!original.TryGetValue(file.path, out var expected) || expected.sha256 != file.sha256 || expected.size != file.size || expected.assembly != file.assembly)
                    throw new InvalidDataException("补丁与主包 AOT 元数据不一致。");
        }

        private static bool IsId(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static string Location(string root, string relative)
        {
            if (root.Contains("://") || root.StartsWith("jar:", StringComparison.Ordinal)) return root.TrimEnd('/') + "/" + relative;
            return new Uri(Path.GetFullPath(Path.Combine(root, relative))).AbsoluteUri;
        }

        private static async Task<byte[]> Read(string url, int timeout, long maximumBytes = 1048576)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Math.Max(1, Math.Min(timeout, 120));
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (request.downloadedBytes > (ulong)maximumBytes) { request.Abort(); throw new InvalidDataException("下载超过声明的大小。"); }
                    await Task.Yield();
                }
                if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error + " (" + url + ")");
                if (request.downloadedBytes > (ulong)maximumBytes) throw new InvalidDataException("下载超过声明的大小。");
                return request.downloadHandler.data;
            }
        }

        private static async Task Materialize(string cache, string id, HotUpdateManifest manifest, byte[] manifestBytes, string origin, int timeout)
        {
            string destination = Path.Combine(cache, "releases", id);
            if (Directory.Exists(destination) && manifest.files.All(file => HotUpdateManifest.VerifyFile(destination, file))) return;
            string temporary = Path.Combine(cache, "releases", ".partial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                foreach (var file in manifest.files)
                {
                    byte[] bytes = await Read(Location(origin, file.path), timeout, file.size);
                    if (bytes.LongLength != file.size || !string.Equals(HotUpdateManifest.Hash(bytes), file.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("热更资源校验失败：" + file.path);
                    string path = Path.Combine(temporary, file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, bytes);
                }
                File.WriteAllBytes(Path.Combine(temporary, "manifest.json"), manifestBytes);
                if (Directory.Exists(destination))
                    Directory.Move(destination, destination + ".invalid-" + Guid.NewGuid().ToString("N"));
                Directory.Move(temporary, destination);
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(status)) return;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Label(new Rect(30, 30, Screen.width - 60, 80), status);
            if (failed && GUI.Button(new Rect(30, 110, 150, 40), "退出游戏")) Application.Quit();
        }
    }
}

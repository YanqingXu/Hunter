using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FlatBuffers;
using UnityEditor;
using UnityEngine;
using YouYou;
using YouYou.DataTable;

namespace BigWorld.YouYou2D.Editor
{
    /// <summary>Builds real bundles and the original compressed VersionFile / AssetInfo formats.</summary>
    public static class NativeFramework2DBuild
    {
        private const string ProjectBundle = "youyou2d/project.assetbundle";
        private sealed class AssetRecord
        {
            public string Path, Bundle;
            public AssetCategory Category;
        }

        [MenuItem("Tools/BigWorld/2D 框架/生成原框架运行内容")]
        public static void PrepareContent() => PrepareContent(EditorUserBuildSettings.activeBuildTarget);

        public static void PrepareContent(BuildTarget target)
        {
            PrepareContent(target, Application.streamingAssetsPath);
        }

        public static void PrepareContent(BuildTarget target, string outputRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            NativeHudPrefabRepair.RepairHudPrefabs();
            var settings = AssetDatabase.LoadAssetAtPath<ParamsSettings>("Assets/YouYouFramework/YouYouAssets/ParamsSettings.asset");
            if (!settings) throw new FileNotFoundException("缺少完整原框架 ParamsSettings。");
            var catalogs = AssetDatabase.FindAssets("t:GameAssetCatalog", new[] { "Assets/_Project" })
                .Select(id => AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            if (catalogs.Length == 0) throw new InvalidOperationException("没有项目资源目录 GameAssetCatalog。");
            var records = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
            foreach (var catalog in catalogs)
            {
                catalog.ValidateEntries();
                catalog.FrameworkSettings = settings;
                foreach (var entry in catalog.Entries)
                {
                    entry.ResourcePath = AssetDatabase.GetAssetPath(entry.Asset);
                    if (string.IsNullOrEmpty(entry.ResourcePath)) throw new InvalidOperationException("资源必须是已保存的工程资产：" + entry.Key);
                    AssetCategory category = entry.Key.StartsWith("ui.", StringComparison.Ordinal) ? AssetCategory.UIPrefab :
                        entry.Asset is GameObject ? AssetCategory.RolePrefab : AssetCategory.RoleSources;
                    Add(records, entry.ResourcePath, ProjectBundle, category);
                }
                EditorUtility.SetDirty(catalog);
            }
            WriteUIForms(catalogs);
            Add(records, GameAssetCatalog.NativeUIFormsPath, ProjectBundle, AssetCategory.DataTable);
            AddFiles(records, "Assets/Download/DataTable", ConstDefine.DataTableAssetBundlePath, AssetCategory.DataTable, "*.bytes");
            AddFiles(records, "Assets/Download/Audio", ConstDefine.AudioAssetBundlePath, AssetCategory.Audio, "*.bytes");
            foreach (string path in Directory.GetFiles("Assets/Download/UI", "*", SearchOption.AllDirectories))
            {
                string asset = path.Replace('\\', '/');
                if (asset.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                AssetCategory category = asset.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? AssetCategory.UIPrefab :
                    asset.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? AssetCategory.UIFont : AssetCategory.UIRes;
                Add(records, asset, "download/ui.assetbundle", category);
            }
            AddFiles(records, "Assets/Download/Reporter", "download/reporter.assetbundle", AssetCategory.Reporter, "*.prefab");
            AddFiles(records, "Assets/Download/Reporter", "download/reporter.assetbundle", AssetCategory.Reporter, "*.png");
            AddFiles(records, "Assets/Download/Reporter", "download/reporter.assetbundle", AssetCategory.Reporter, "*.guiskin");
            AssetDatabase.SaveAssets();

            var builds = records.Values.GroupBy(record => record.Bundle).OrderBy(group => group.Key)
                .Select(group => new AssetBundleBuild { assetBundleName = group.Key,
                    assetNames = group.Select(record => record.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray() }).ToArray();
            string cache = Path.GetFullPath("Library/YouYou2DContent/" + target);
            Directory.CreateDirectory(cache);
            var manifest = BuildPipeline.BuildAssetBundles(cache, builds, BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.StrictMode, target);
            if (!manifest) throw new InvalidOperationException("原框架 AssetBundle 构建失败。");
            Directory.CreateDirectory(outputRoot);
            foreach (var build in builds)
            {
                string destination = Path.Combine(outputRoot, build.assetBundleName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(cache, build.assetBundleName), destination, true);
            }
            WriteVersion(cache, builds, outputRoot);
            WriteAssetInfo(records, manifest, outputRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("BIGWORLD_NATIVE_CONTENT_READY: " + builds.Length + " original-format bundles, " + records.Count + " resource records, " + target);
        }

        private static void AddFiles(Dictionary<string, AssetRecord> records, string directory, string bundle, AssetCategory category, string pattern)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            foreach (string path in Directory.GetFiles(directory, pattern, SearchOption.AllDirectories))
                Add(records, path.Replace('\\', '/'), bundle, category);
        }

        private static void Add(Dictionary<string, AssetRecord> records, string path, string bundle, AssetCategory category)
        {
            if (records.TryGetValue(path, out var existing))
            {
                if (existing.Category != category || existing.Bundle != bundle)
                    throw new InvalidOperationException("资源不能重复归属不同分类/包：" + path);
                return;
            }
            records.Add(path, new AssetRecord { Path = path, Bundle = bundle, Category = category });
        }

        private static void WriteVersion(string cache, AssetBundleBuild[] builds, string outputRoot)
        {
            var hashes = new List<string>();
            var lengths = new List<ulong>();
            foreach (var build in builds)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(cache, build.assetBundleName));
                using (var md5 = MD5.Create()) hashes.Add(BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
                lengths.Add((ulong)bytes.Length);
            }
            using (var stream = new MMO_MemoryStream())
            {
                stream.WriteInt(builds.Length + 1);
                string fingerprint = string.Join("\n", builds.Select((build, i) => build.assetBundleName + ":" + hashes[i]));
                string version;
                using (var md5 = MD5.Create()) version = BitConverter.ToString(md5.ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes(fingerprint))).Replace("-", "").ToLowerInvariant();
                stream.WriteUTF8String("bigworld-native-" + version);
                for (int i = 0; i < builds.Length; i++)
                {
                    stream.WriteUTF8String(builds[i].assetBundleName); stream.WriteUTF8String(hashes[i]);
                    stream.WriteULong(lengths[i]); stream.WriteByte(1); stream.WriteByte(0);
                }
                File.WriteAllBytes(Path.Combine(outputRoot, ConstDefine.VersionFileName), ZlibHelper.CompressBytes(stream.ToArray()));
            }
        }

        private static void WriteAssetInfo(Dictionary<string, AssetRecord> records, AssetBundleManifest manifest, string outputRoot)
        {
            using (var stream = new MMO_MemoryStream())
            {
                stream.WriteInt(records.Count);
                foreach (var record in records.Values.OrderBy(value => value.Path, StringComparer.Ordinal))
                {
                    stream.WriteByte((byte)record.Category); stream.WriteUTF8String(record.Path); stream.WriteUTF8String(record.Bundle);
                    string[] dependencies = manifest.GetAllDependencies(record.Bundle);
                    stream.WriteInt(dependencies.Length);
                    foreach (string bundle in dependencies)
                    {
                        var dependency = records.Values.First(value => value.Bundle == bundle);
                        stream.WriteByte((byte)dependency.Category); stream.WriteUTF8String(dependency.Path);
                    }
                }
                File.WriteAllBytes(Path.Combine(outputRoot, ConstDefine.AssetInfoName), ZlibHelper.CompressBytes(stream.ToArray()));
            }
        }

        private static void WriteUIForms(GameAssetCatalog[] catalogs)
        {
            string game = catalogs.SelectMany(catalog => catalog.Entries).First(entry => entry.Key == "ui.game").ResourcePath;
            string demo = catalogs.SelectMany(catalog => catalog.Entries).First(entry => entry.Key == "ui.demo").ResourcePath;
            byte[] source = ZlibHelper.DeCompressBytes(File.ReadAllBytes("Assets/Download/DataTable/DTSys_UIForm.bytes"));
            var original = DTSys_UIFormList.GetRootAsDTSys_UIFormList(new ByteBuffer(source));
            var builder = new FlatBufferBuilder(4096);
            var rows = new List<Offset<DTSys_UIForm>>();
            for (int i = 0; i < original.DTSysUIFormsLength; i++)
            {
                var row = original.DTSysUIForms(i).Value;
                if (row.Id == 9001 || row.Id == 9002) throw new InvalidOperationException("2D UI ID 与课程原表冲突。");
                rows.Add(Row(builder, row.Id, row.Desc, row.Name, row.UIGroupId, row.DisableUILayer, row.IsLock,
                    row.AssetPathChinese, row.AssetPathEnglish, row.CanMulit, row.ShowMode, row.FreezeMode));
            }
            rows.Add(Row(builder, 9001, "边境试炼 HUD", "GameHud2D", 1, 0, 0, game, game, false, 0, 1));
            rows.Add(Row(builder, 9002, "2D 框架示例 HUD", "FrameworkDemoHud2D", 1, 0, 0, demo, demo, false, 0, 1));
            var vector = DTSys_UIFormList.CreateDTSysUIFormsVector(builder, rows.ToArray());
            builder.Finish(DTSys_UIFormList.CreateDTSys_UIFormList(builder, vector).Value);
            Directory.CreateDirectory(Path.GetDirectoryName(GameAssetCatalog.NativeUIFormsPath));
            File.WriteAllBytes(GameAssetCatalog.NativeUIFormsPath, ZlibHelper.CompressBytes(builder.SizedByteArray()));
            AssetDatabase.ImportAsset(GameAssetCatalog.NativeUIFormsPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Offset<DTSys_UIForm> Row(FlatBufferBuilder builder, int id, string desc, string name, byte group,
            int disableLayer, int isLock, string chinese, string english, bool multiple, byte show, byte freeze)
        {
            var description = builder.CreateString(desc ?? ""); var label = builder.CreateString(name ?? "");
            var cn = builder.CreateString(chinese ?? ""); var en = builder.CreateString(english ?? "");
            return DTSys_UIForm.CreateDTSys_UIForm(builder, id, description, label, group, disableLayer, isLock, cn, en, multiple, show, freeze);
        }
    }
}

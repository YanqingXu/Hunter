using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BigWorld.HotUpdate
{
    [Serializable]
    public sealed class HotUpdateFile
    {
        public string path, sha256, kind, assembly;
        public long size;
    }

    [Serializable]
    public sealed class HotUpdateManifest
    {
        public int format = 1;
        public string baseId, version, platform, entryScene, sceneBundle;
        public HotUpdateFile[] files;

        public void Validate(string expectedBase, string expectedPlatform)
        {
            if (format != 1 || string.IsNullOrEmpty(baseId) || baseId != expectedBase || platform != expectedPlatform)
                throw new InvalidDataException("热更包与当前主包或平台不兼容。");
            if (string.IsNullOrEmpty(version) || version.Length > 128 || files == null || files.Length == 0 || files.Length > 256)
                throw new InvalidDataException("无效的热更版本清单。");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblies = new HashSet<string>(StringComparer.Ordinal);
            var hotAssemblies = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (var file in files)
            {
                if (file == null) throw new InvalidDataException("资源记录为空。");
                ValidatePath(file.path);
                if (!paths.Add(file.path) || file.size <= 0 || file.size > 536870912 ||
                    string.IsNullOrEmpty(file.sha256) || file.sha256.Length != 64)
                    throw new InvalidDataException("重复或无效的资源记录：" + file.path);
                foreach (char c in file.sha256)
                    if (!Uri.IsHexDigit(c)) throw new InvalidDataException("无效的 SHA256。");
                total += file.size;
                if (total > 2147483648L) throw new InvalidDataException("热更包超过大小限制。");
                if (file.kind == "assembly" || file.kind == "aot")
                {
                    if (file.kind == "assembly") hotAssemblies.Add(file.assembly);
                    if (string.IsNullOrEmpty(file.assembly) || !assemblies.Add(file.assembly) ||
                        file.assembly.Contains("/") || file.assembly.Contains("\\"))
                        throw new InvalidDataException("重复或无效的程序集记录。");
                }
                else if (file.kind != "content") throw new InvalidDataException("未知的资源种类。");
            }
            ValidatePath(sceneBundle);
            if (!paths.Contains(sceneBundle) || !paths.Contains("VersionFile.bytes") || !paths.Contains("AssetInfo.bytes") ||
                !hotAssemblies.SetEquals(new[] { "Assembly-CSharp", "SkillEditorKit.Runtime" }) ||
                string.IsNullOrEmpty(entryScene) || !entryScene.StartsWith("Assets/", StringComparison.Ordinal) ||
                !entryScene.EndsWith(".unity", StringComparison.Ordinal))
                throw new InvalidDataException("热更包缺少场景、原资源索引或业务程序集。");
        }

        public static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") || path.Contains("%") || path.Contains("?") || path.Contains("#"))
                throw new InvalidDataException("资源路径非法。");
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..") throw new InvalidDataException("资源路径越界。");
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static bool VerifyFile(string root, HotUpdateFile file)
        {
            string path = Path.Combine(root, file.path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.size) return false;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), file.sha256,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class HotUpdateBootConfig
    {
        public string baseId, platform = "StandaloneWindows64", updateUrl = "";
        public int timeoutSeconds = 20;
    }
}

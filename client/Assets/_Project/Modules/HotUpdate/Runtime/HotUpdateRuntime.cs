using System;
using System.IO;
using UnityEngine;

namespace BigWorld.HotUpdate
{
    /// <summary>AOT boundary: hot code can read the selected content root and report successful world startup.</summary>
    public static class HotUpdateRuntime
    {
        public static string ContentRoot { get; internal set; }
        public static string Version { get; internal set; }
        public static bool IsReady { get; private set; }
        internal static string CacheRoot, ReleaseId;

        public static void ConfirmReady()
        {
            if (string.IsNullOrEmpty(CacheRoot) || IsReady) return;
            WriteAtomic(Path.Combine(CacheRoot, "active.txt"), ReleaseId);
            string pending = Path.Combine(CacheRoot, "booting.txt");
            if (File.Exists(pending)) File.Delete(pending);
            IsReady = true;
            Debug.Log("BIGWORLD_HYBRIDCLR_READY: " + Version);
        }

        internal static void Reset()
        {
            ContentRoot = Version = CacheRoot = ReleaseId = null;
            IsReady = false;
        }

        internal static void WriteAtomic(string path, string text)
        {
            string temporary = path + ".new";
            File.WriteAllText(temporary, text);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}

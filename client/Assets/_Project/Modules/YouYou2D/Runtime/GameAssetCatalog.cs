using System;
using System.Collections.Generic;
using UnityEngine;
using YouYou;

namespace BigWorld.YouYou2D
{
    /// <summary>Authoring list for the original YouYou AssetInfo/AssetBundle build, not a second resource provider.</summary>
    [CreateAssetMenu(menuName = "BigWorld/2D Framework/Asset Catalog", fileName = "GameAssetCatalog")]
    public sealed class GameAssetCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
#if UNITY_EDITOR
            public UnityEngine.Object Asset;
#endif
            [HideInInspector] public string ResourcePath;
        }

        public ParamsSettings FrameworkSettings;
        public Entry[] Entries = Array.Empty<Entry>();
        public const string NativeUIFormsPath = "Assets/_Project/Game/Data/Generated/NativeUIForms.bytes";

        public string GetAssetPath(string key)
        {
            foreach (var entry in Entries)
                if (entry != null && entry.Key == key) return entry.ResourcePath;
            throw new KeyNotFoundException("资源表不存在 Key：" + key);
        }

        public void ValidateEntries()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    throw new ArgumentException("资源表 Key 必须填写。");
                if (!keys.Add(entry.Key)) throw new ArgumentException("重复的资源 Key：" + entry.Key);
#if UNITY_EDITOR
                if (!entry.Asset) throw new ArgumentException("资源表 Asset 必须填写：" + entry.Key);
#else
                if (string.IsNullOrWhiteSpace(entry.ResourcePath))
                    throw new ArgumentException("资源索引未生成，请执行原框架内容打包：" + entry.Key);
#endif
            }
        }
    }
}

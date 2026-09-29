using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou.Framework;

namespace BigWorld.YouYou2D
{
    [CreateAssetMenu(menuName = "BigWorld/2D Framework/Asset Catalog", fileName = "GameAssetCatalog")]
    public sealed class GameAssetCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public UnityEngine.Object Asset;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        public CatalogAssetProvider CreateProvider() => new CatalogAssetProvider(Entries);
    }

    /// <summary>Direct project references work in builds without moving assets into Resources.</summary>
    public sealed class CatalogAssetProvider : IAssetProvider
    {
        private readonly Dictionary<string, UnityEngine.Object> assets = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        public CatalogAssetProvider(IEnumerable<GameAssetCatalog.Entry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key) || !entry.Asset)
                    throw new ArgumentException("资源表的 Key 和 Asset 都必须填写。");
                if (assets.ContainsKey(entry.Key)) throw new ArgumentException("重复的资源 Key：" + entry.Key);
                assets.Add(entry.Key, entry.Asset);
            }
        }

        public T Load<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Resource key is required.", nameof(path));
            return assets.TryGetValue(path, out var asset) ? asset as T : null;
        }

        public IEnumerator LoadAsync<T>(string path, Action<T> completed) where T : UnityEngine.Object
        {
            var asset = Load<T>(path);
            yield return null;
            completed?.Invoke(asset);
        }

        // Catalog references have scene/application lifetime, not individual load handles.
        public void Release(UnityEngine.Object asset) { }
    }
}

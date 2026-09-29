using System.Collections;
using System;
using UnityEngine;

namespace YouYou.Framework
{
    /// <summary>Implement in the project to use AssetBundles, Addressables or another loader.</summary>
    public interface IAssetProvider
    {
        T Load<T>(string path) where T : UnityEngine.Object;
        IEnumerator LoadAsync<T>(string path, Action<T> completed) where T : UnityEngine.Object;
        void Release(UnityEngine.Object asset);
    }

    public sealed class ResourcesAssetProvider : IAssetProvider
    {
        public T Load<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Resource path is required.", nameof(path));
            return UnityEngine.Resources.Load<T>(path);
        }
        public IEnumerator LoadAsync<T>(string path, Action<T> completed) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Resource path is required.", nameof(path));
            var request = UnityEngine.Resources.LoadAsync<T>(path);
            yield return request;
            completed?.Invoke(request.asset as T);
        }
        // Resources has no reference-counted handle. Do not unload shared assets behind consumers.
        public void Release(UnityEngine.Object asset) { }
    }
}

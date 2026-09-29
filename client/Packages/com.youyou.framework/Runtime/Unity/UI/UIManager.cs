using System;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou.Framework
{
    /// <summary>One live instance per form id. Each form owns a load/release pair.</summary>
    public sealed class UIManager : IDisposable
    {
        private sealed class OpenForm
        {
            public UIFormBase View;
            public GameObject Prefab;
        }
        private readonly IAssetProvider assets;
        private readonly Transform root;
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>();
        private readonly Dictionary<string, OpenForm> forms = new Dictionary<string, OpenForm>();
        private bool disposed;

        public UIManager(IAssetProvider assets, Transform root)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.root = root ? root : throw new ArgumentNullException(nameof(root));
        }
        public void Register(string id, string prefabPath)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UIManager));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prefabPath)) throw new ArgumentException("Form id and prefab path are required.");
            paths[id] = prefabPath;
        }

        public UIFormBase OpenUIForm(string id, object userData = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UIManager));
            if (forms.TryGetValue(id, out var existing) && existing.View)
            {
                existing.View.transform.SetAsLastSibling();
                return existing.View;
            }
            if (existing != null) { forms.Remove(id); assets.Release(existing.Prefab); }
            if (!paths.TryGetValue(id, out var path)) throw new KeyNotFoundException("Unregistered UI: " + id);
            var prefab = assets.Load<GameObject>(path);
            if (!prefab) throw new InvalidOperationException("UI prefab was not found: " + path);
            GameObject instance = null;
            var item = new OpenForm { Prefab = prefab };
            bool registered = false;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, root, false);
                var view = instance.GetComponent<UIFormBase>();
                if (!view) throw new InvalidOperationException("UI prefab root needs a UIFormBase component: " + path);
                view.FormId = id;
                item.View = view;
                forms.Add(id, item);
                registered = true;
                instance.SetActive(true);
                view.OnOpen(userData);
                return forms.TryGetValue(id, out var current) && ReferenceEquals(current, item) ? view : null;
            }
            catch
            {
                // OnOpen may already have closed the form; only release what we still own.
                if (!registered || (forms.TryGetValue(id, out var current) && ReferenceEquals(current, item)))
                {
                    if (registered) forms.Remove(id);
                    if (instance) DestroyObject(instance);
                    assets.Release(prefab);
                }
                throw;
            }
        }

        public void CloseUIForm(string id)
        {
            if (!forms.TryGetValue(id, out var item)) return;
            forms.Remove(id);
            try { if (item.View) item.View.OnClose(); }
            finally
            {
                if (item.View) DestroyObject(item.View.gameObject);
                assets.Release(item.Prefab);
            }
        }

        internal static void DestroyObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var errors = new List<Exception>();
            foreach (var id in new List<string>(forms.Keys))
                try { CloseUIForm(id); } catch (Exception e) { errors.Add(e); }
            paths.Clear();
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}

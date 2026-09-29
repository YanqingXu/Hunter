using System;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou.Framework
{
    /// <summary>A bounded pool for one prefab. Owns instances; the caller owns the prefab asset.</summary>
    public sealed class GameObjectPool : IDisposable
    {
        private readonly GameObject prefab, container;
        private readonly int capacity;
        private readonly Stack<GameObject> idle = new Stack<GameObject>();
        private readonly HashSet<GameObject> owned = new HashSet<GameObject>();
        private readonly HashSet<GameObject> rented = new HashSet<GameObject>();
        private bool disposed;

        public GameObjectPool(GameObject prefab, Transform parent, int capacity = 32)
        {
            if (!prefab) throw new ArgumentNullException(nameof(prefab));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.prefab = prefab; this.capacity = capacity;
            container = new GameObject(prefab.name + " Pool");
            container.transform.SetParent(parent, false);
            container.SetActive(false);
        }

        public GameObject Rent(Transform parent = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameObjectPool));
            GameObject instance = null;
            while (idle.Count > 0 && !instance) instance = idle.Pop();
            if (!instance) { instance = UnityEngine.Object.Instantiate(prefab, container.transform, false); owned.Add(instance); }
            rented.Add(instance);
            instance.transform.SetParent(parent, false);
            instance.SetActive(true);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameObjectPool));
            if (!instance || !rented.Remove(instance)) throw new InvalidOperationException("Object is destroyed, foreign, or already returned.");
            instance.SetActive(false);
            if (idle.Count >= capacity) { owned.Remove(instance); UIManager.DestroyObject(instance); return; }
            instance.transform.SetParent(container.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = prefab.transform.localScale;
            idle.Push(instance);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var instance in owned) if (instance) UIManager.DestroyObject(instance);
            idle.Clear(); rented.Clear(); owned.Clear();
            if (container) UIManager.DestroyObject(container);
        }
    }
}

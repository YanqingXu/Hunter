using System;
using System.Collections;
using UnityEngine.SceneManagement;

namespace YouYou.Framework
{
    public sealed class SceneService
    {
        public IEnumerator Load(string scene, LoadSceneMode mode = LoadSceneMode.Single, Action<float> progress = null)
        {
            if (string.IsNullOrWhiteSpace(scene)) throw new ArgumentException("Scene name is required.", nameof(scene));
            var operation = SceneManager.LoadSceneAsync(scene, mode);
            if (operation == null) throw new InvalidOperationException("Cannot load scene: " + scene);
            while (!operation.isDone)
            {
                progress?.Invoke(operation.progress);
                yield return null;
            }
            progress?.Invoke(1);
        }
    }
}

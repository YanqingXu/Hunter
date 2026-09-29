using System;
using UnityEngine;

namespace SkillEditorKit
{
    [AddComponentMenu("")]
    public sealed class SkillAudioLifetime : MonoBehaviour, IDisposable
    {
        private double endTime;
        private bool disposed;
        public void Begin(double duration) => endTime = Time.timeAsDouble + Math.Max(0, duration);
        private void Update() { if (Time.timeAsDouble >= endTime) Dispose(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (this == null) return;
            var source = GetComponent<AudioSource>();
            if (source != null) source.Stop();
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
    }
}

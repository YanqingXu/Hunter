using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkillEditorKit
{
    /// <summary>Optional default adapter. Does not require an audio manager, pool, bootstrap scene or settings asset.</summary>
    public sealed class SkillUnityAudio : ISkillAudioService
    {
        public static readonly SkillUnityAudio Instance = new SkillUnityAudio();
        public IDisposable PlayClip(AudioClip clip, Vector3 position, float volume, float clipIn, double duration, Scene scene)
        {
            // Editor timeline audio is handled by the editor's preview transport.
            if (!Application.isPlaying) return null;
            if (clip == null || duration <= 0 || clipIn >= clip.length) return null;
            if (float.IsNaN(volume) || float.IsInfinity(volume) || float.IsNaN(clipIn) || float.IsInfinity(clipIn) || double.IsNaN(duration) || double.IsInfinity(duration))
                throw new ArgumentException("Invalid skill audio range.");
            var root = new GameObject("[Skill audio] " + clip.name);
            try
            {
                if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = position;
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 1;
                source.clip = clip; source.volume = Mathf.Clamp01(volume); source.time = Mathf.Max(0, clipIn);
                var lease = root.AddComponent<SkillAudioLifetime>();
                lease.Begin(Math.Min(duration, clip.length - Mathf.Max(0, clipIn)));
                source.Play();
                return lease;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }
    }
}

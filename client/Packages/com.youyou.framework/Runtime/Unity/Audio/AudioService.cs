using System;
using UnityEngine;

namespace YouYou.Framework
{
    public interface IAudioService : IDisposable
    {
        void PlayMusic(AudioClip clip, bool loop = true);
        void PlaySound(AudioClip clip, float volume = 1);
        void StopMusic();
    }

    public sealed class AudioService : IAudioService
    {
        private readonly GameObject root;
        private readonly AudioSource music, sounds;
        public AudioService(Transform parent)
        {
            root = new GameObject("Audio");
            root.transform.SetParent(parent, false);
            music = root.AddComponent<AudioSource>();
            sounds = root.AddComponent<AudioSource>();
            music.playOnAwake = false; sounds.playOnAwake = false;
        }
        public void PlayMusic(AudioClip clip, bool loop = true) { music.clip = clip; music.loop = loop; music.Play(); }
        public void PlaySound(AudioClip clip, float volume = 1) { if (clip) sounds.PlayOneShot(clip, Mathf.Clamp01(volume)); }
        public void StopMusic() { if (music) music.Stop(); }
        public void Dispose() { if (root) UIManager.DestroyObject(root); }
    }
}

// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Owned editor AudioSources; never calls the global StopAllPreviewClips API.</summary>
[InitializeOnLoad]
public static class EditorAudioUnility
{
    private sealed class Playback { public AudioSource Source; public double EndTime; public double SourceEndTime; }
    private static readonly List<Playback> playing = new List<Playback>();
    internal static int ActiveCount => playing.Count;
    internal static AudioSource LastSource => playing.Count == 0 ? null : playing[playing.Count - 1].Source;

    static EditorAudioUnility()
    {
        EditorApplication.update += Update;
        EditorApplication.quitting += StopAllAudios;
        AssemblyReloadEvents.beforeAssemblyReload += StopAllAudios;
    }

    public static void PlayAudio(AudioClip clip, float start, float volume = 1, float duration = -1, float speed = 1)
    {
        if (clip == null || clip.samples == 0 || clip.loadState != AudioDataLoadState.Loaded) return;
        var root = EditorUtility.CreateGameObjectWithHideFlags("[SkillEditor] Audio Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
        var source = root.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        source.loop = false;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = SanitizeSpeed(speed);
        source.timeSamples = Mathf.Clamp((int)(Mathf.Clamp01(start) * clip.samples), 0, clip.samples - 1);
        source.Play();
        float remaining = clip.length * (1 - Mathf.Clamp01(start));
        float seconds = duration < 0 ? remaining : Mathf.Clamp(duration, 0, remaining);
        var playback = new Playback { Source = source, SourceEndTime = Mathf.Clamp01(start) * clip.length + seconds };
        ScheduleEnd(playback, seconds / source.pitch);
        playing.Add(playback);
    }

    public static void SetPlaybackSpeed(float speed)
    {
        speed = SanitizeSpeed(speed);
        foreach (var playback in playing)
        {
            var source = playback.Source;
            if (source == null || source.clip == null) continue;
            double remaining = Math.Max(0, playback.SourceEndTime - source.timeSamples / (double)source.clip.frequency);
            source.pitch = speed;
            // Update the existing source in place: no restart, duplicate voice or
            // changes to unrelated scene AudioSources when switching preview rates.
            ScheduleEnd(playback, remaining / speed);
        }
    }

    private static float SanitizeSpeed(float speed) => float.IsNaN(speed) || float.IsInfinity(speed) ? 1 : Mathf.Clamp(speed, .25f, 2);
    private static void ScheduleEnd(Playback playback, double seconds)
    {
        playback.Source.SetScheduledEndTime(AudioSettings.dspTime + seconds);
        playback.EndTime = EditorApplication.timeSinceStartup + seconds;
    }

    private static void Update()
    {
        for (int i = playing.Count - 1; i >= 0; i--)
        {
            if (playing[i].Source != null && EditorApplication.timeSinceStartup < playing[i].EndTime) continue;
            Destroy(playing[i].Source);
            playing.RemoveAt(i);
        }
    }

    public static void StopAllAudios()
    {
        foreach (var item in playing) Destroy(item.Source);
        playing.Clear();
    }

    private static void Destroy(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        UnityEngine.Object.DestroyImmediate(source.gameObject);
    }
}

}

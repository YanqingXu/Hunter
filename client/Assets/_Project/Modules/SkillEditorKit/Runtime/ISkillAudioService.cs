using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkillEditorKit
{
    /// <summary>The returned lease belongs to one cast. Disposing it must stop only that sound.</summary>
    public interface ISkillAudioService
    {
        IDisposable PlayClip(AudioClip clip, Vector3 position, float volume, float clipIn, double duration, Scene scene);
    }
}

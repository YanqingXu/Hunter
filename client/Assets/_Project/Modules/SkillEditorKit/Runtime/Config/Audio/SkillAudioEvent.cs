// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;

public class SkillAudioEvent : SkillFrameEventBase
{
#if UNITY_EDITOR
    public string TrackName = "音效轨道";
#endif
    public int FrameIndex = -1;
    public AudioClip AudioClip;
    public float Voluem = 1;
    public bool UseClipRange;
    public float ClipIn;
    public int DurationFrame;
}

}

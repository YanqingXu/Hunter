// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;

public class SkillEffectEvent : SkillFrameEventBase
{
#if UNITY_EDITOR
    public string TrackName = "特效轨道";
#endif
    public int FrameIndex = -1;
    public GameObject Prefab;
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.one;
    public int Duration = 1;
    public bool AutoDestruct;
}

}

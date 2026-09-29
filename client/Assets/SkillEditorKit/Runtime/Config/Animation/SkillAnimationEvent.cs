// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;
/// <summary>
/// 动画帧事件
/// </summary>
public class SkillAnimationEvent : SkillFrameEventBase
{
    public AnimationClip AnimationClip;
    public bool ApplyRootMotion;
    public bool MainWeaponOnLeftHand = true;
    // Retained for old asset serialization only. Skill crossfades now come from clip overlap.
    [HideInInspector] public float TransitionTime = 0.25f;
    public int DurationFrame;
    // Optional source trim; source assets are never modified.
    public bool UseClipRange;
    public float ClipIn;
}

}

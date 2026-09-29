using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>Project adapter for a skill caster. No UI, buffs, resources or state machine types.</summary>
    public interface ISkillActor : ISkillHitTarget
    {
        Transform ModelTransform { get; }
        float GetAttackValue(SkillAttackDetectionEvent attack);
    }
}

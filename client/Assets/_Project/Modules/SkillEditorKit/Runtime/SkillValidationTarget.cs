// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Editor sandbox only; no game health, resources or buff side effects.</summary>
public sealed class SkillValidationTarget : MonoBehaviour, ISkillActor, ISkillTeam
{
    public int Team;
    public float BaseAttack = 10;
    public Action<SkillValidationTarget, SkillHitData> Received;
    public int SkillTeamId => Team;
    public SkillAnimationPlayer SkillAnimationPlayer => null;
    public Transform ModelTransform => transform;
    public float GetAttackValue(SkillAttackDetectionEvent data) => BaseAttack * (data.AttackHitConfig?.AttackMultiply ?? 1);
    public void BeHit(SkillHitData data) => Received?.Invoke(this, data);
    public void OnSkillRotate() { }

    public void ChangeToIdleState() { }
    public void OnSkillMove(Vector3 delta) { }
    public void OnSkillRotate(Quaternion delta) { }
}
#endif

}

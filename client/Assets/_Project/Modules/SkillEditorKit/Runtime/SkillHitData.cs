// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using UnityEngine;

public struct SkillHitData
{
    // Identifies the execution that produced a collision; stale callbacks are ignored.
    public SkillInstance Instance;
    public SkillProjectileContext Projectile;
    internal SkillHitLedger HitLedger;
    internal Vector3 DetectionOrigin;
    public SkillAttackDetectionEvent detectionEvent;
    public ISkillActor soure;
    public Vector3 hitPoint;
    public float attackValue;
}

}

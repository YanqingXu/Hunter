// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using UnityEngine;

public enum SkillHitMode { LegacyOncePerSkill, OncePerAttack, Interval }
public enum SkillTargetGroup { Enemies, Allies, All }

/// <summary>Optional team contract; unknown teams are never guessed to be enemies or allies.</summary>
public interface ISkillTeam { int SkillTeamId { get; } }

[Serializable]
public sealed class SkillHitRules
{
    public SkillHitMode Mode;
    public SkillTargetGroup Targets = SkillTargetGroup.Enemies;
    public bool ExcludeSelf = true;
    public float IntervalSeconds = .5f;
    public bool BlockedByWalls;
    public LayerMask WallLayers;
    public int MaxTargets; // Zero means unlimited unique targets per attack/projectile.

    public SkillHitRules Copy() => (SkillHitRules)MemberwiseClone();
    public static bool Validate(SkillHitRules rules, out string error)
    {
        error = null;
        if (rules == null || rules.Mode == SkillHitMode.LegacyOncePerSkill) return true;
        if (!Enum.IsDefined(typeof(SkillHitMode), rules.Mode) || !Enum.IsDefined(typeof(SkillTargetGroup), rules.Targets) ||
            rules.MaxTargets < 0 || rules.IntervalSeconds <= 0 || float.IsInfinity(rules.IntervalSeconds) || float.IsNaN(rules.IntervalSeconds))
            error = "命中规则无效：间隔必须大于 0，目标上限不能为负。";
        if (rules.BlockedByWalls && rules.WallLayers.value == 0) error = "启用墙壁阻挡后，请选择墙壁所在层。";
        return error == null;
    }

    public static bool Accepts(SkillHitRules rules, ISkillActor source, ISkillHitTarget target, Vector3 origin, Vector3 point, PhysicsScene? scene = null, SkillSpace space = SkillSpace.ThreeD, PhysicsScene2D? scene2D = null)
    {
        if (target == null || (target is UnityEngine.Object obj && obj == null)) return false;
        if (rules == null || rules.Mode == SkillHitMode.LegacyOncePerSkill) return true;
        if (rules.ExcludeSelf && ReferenceEquals(source, target)) return false;
        if (rules.Targets != SkillTargetGroup.All)
        {
            if (!(source is ISkillTeam from) || !(target is ISkillTeam to)) return false;
            bool same = from.SkillTeamId == to.SkillTeamId;
            if (rules.Targets == SkillTargetGroup.Allies ? !same : same) return false;
        }
        if (rules.BlockedByWalls)
        {
            var delta = point - origin;
            if (space == SkillSpace.TwoD)
            {
                foreach (var hit in SkillPhysicsQuery2D.Ray(origin, ((Vector2)delta).normalized, ((Vector2)delta).magnitude, rules.WallLayers, scene2D))
                {
                    if (hit.collider == null) continue;
                    var owner = hit.collider.GetComponentInParent<ISkillHitTarget>();
                    if (!ReferenceEquals(owner, source) && !ReferenceEquals(owner, target)) return false;
                }
                return true;
            }
            foreach (var hit in SkillPhysicsQuery.Ray(origin, delta.normalized, delta.magnitude, rules.WallLayers, scene, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null) continue;
                var owner = hit.collider.GetComponentInParent<ISkillHitTarget>();
                if (!ReferenceEquals(owner, source) && !ReferenceEquals(owner, target)) return false;
            }
        }
        return true;
    }
}

/// <summary>Per-attack/per-projectile records, including multi-collider deduplication.</summary>
internal sealed class SkillHitLedger
{
    private readonly Dictionary<ISkillHitTarget, double> lastHits = new Dictionary<ISkillHitTarget, double>();
    public int TargetCount => lastHits.Count;
    public bool Register(ISkillHitTarget target, SkillHitRules rules, double seconds)
    {
        if (lastHits.TryGetValue(target, out double previous))
        {
            if (rules == null || rules.Mode != SkillHitMode.Interval || seconds - previous + .0000001 < rules.IntervalSeconds) return false;
        }
        else if (rules != null && rules.MaxTargets > 0 && lastHits.Count >= rules.MaxTargets) return false;
        lastHits[target] = seconds;
        return true;
    }
}

}

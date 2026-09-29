// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

/// <summary>Shared trajectory integrator. Homing uses a stable 60 Hz grid and samples
/// fractional frames without committing them, so seeking forward/backward can replay it.</summary>
public sealed class SkillProjectileMotion
{
    public const double StepSeconds = 1d / 60;
    private readonly SkillProjectileEvent data;
    private readonly int rate;
    private readonly SkillAttackShape.Pose launch;
    private readonly double stepSeconds;
    private SkillAttackShape.Pose stable;
    private long step;
    public SkillAttackShape.Pose Pose { get; private set; }
    public double Age { get; private set; }
    // Configured seconds are floats, while the simulation clock is double. A relative
    // tolerance handles 0.1f boundaries without ending genuinely tiny flights at birth.
    public bool Finished => Age >= data.LifeSeconds(rate) * (1 - .0000001);

    public SkillProjectileMotion(SkillProjectileEvent data, int rate, SkillAttackShape.Pose launch)
    {
        this.data = data; this.rate = rate; this.launch = launch; stable = Pose = launch;
        // Bound the parabola's chord error by one quarter of its collision radius,
        // including a complete very-fast arc inside a single logical frame.
        int arcSegments = Math.Max(2, (int)Math.Ceiling(2 * Math.Sqrt(Math.Max(0, data.ArcHeight) / Math.Max(.001, data.Radius))));
        stepSeconds = data.Flight == SkillProjectileFlight.Arc ? Math.Min(StepSeconds, data.FlightSeconds(rate) / arcSegments) : StepSeconds;
    }

    // Sweep returns false if a hit destroyed the projectile. No later targets are processed.
    public void Advance(double delta, Vector3? target, Func<SkillAttackShape.Pose, SkillAttackShape.Pose, bool> sweep = null)
    {
        if (delta < 0 || double.IsNaN(delta) || double.IsInfinity(delta) || Finished) return;
        double desired = Math.Min(data.LifeSeconds(rate), Age + delta);
        while ((step + 1) * stepSeconds <= desired + stepSeconds * .000000001)
        {
            double time = (++step) * stepSeconds;
            stable = At(stable, time, stepSeconds, target);
            var from = Pose; Pose = stable; Age = time;
            if (sweep != null && !sweep(from, Pose)) return;
        }
        double remainder = Math.Max(0, desired - step * stepSeconds);
        var last = Pose;
        Pose = remainder > 0 ? At(stable, desired, remainder, target) : stable;
        Age = desired;
        if ((Pose.Position - last.Position).sqrMagnitude > .0000000001f && sweep != null) sweep(last, Pose);
    }

    private SkillAttackShape.Pose At(SkillAttackShape.Pose previous, double time, double delta, Vector3? target)
    {
        var forward = SkillFacing2D.Forward(launch.Rotation, launch.Space);
        if (data.Flight == SkillProjectileFlight.Homing)
        {
            var direction = SkillFacing2D.Forward(previous.Rotation, launch.Space);
            if (target.HasValue && launch.Space == SkillSpace.TwoD) target = new Vector3(target.Value.x, target.Value.y, launch.Position.z);
            if (target.HasValue && (target.Value - previous.Position).sqrMagnitude > .000001f)
            {
                if (launch.Space == SkillSpace.TwoD)
                {
                    var desired=target.Value-previous.Position;
                    float angle=Mathf.MoveTowardsAngle(Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg,
                        Mathf.Atan2(desired.y,desired.x)*Mathf.Rad2Deg,data.TurnDegreesPerSecond*(float)delta);
                    direction=Quaternion.Euler(0,0,angle)*Vector3.right;
                }
                else direction = Vector3.RotateTowards(direction, (target.Value - previous.Position).normalized,
                    data.TurnDegreesPerSecond * Mathf.Deg2Rad * (float)delta, 0).normalized;
            }
            return new SkillAttackShape.Pose { Space = launch.Space, Position = previous.Position + direction * (float)(data.Distance / data.FlightSeconds(rate) * delta),
                Rotation = launch.Space == SkillSpace.TwoD ? SkillFacing2D.Look(direction) : Quaternion.LookRotation(direction, Vector3.up) };
        }
        float p = Mathf.Clamp01((float)(time / data.FlightSeconds(rate)));
        var position = launch.Position + forward * (data.Distance * p);
        var tangent = forward;
        if (data.Flight == SkillProjectileFlight.Arc)
        {
            position += Vector3.up * (4 * data.ArcHeight * p * (1 - p));
            tangent = forward * data.Distance + Vector3.up * (4 * data.ArcHeight * (1 - 2 * p));
        }
        return new SkillAttackShape.Pose { Space = launch.Space, Position = position, Rotation = launch.Space == SkillSpace.TwoD ? SkillFacing2D.Look(tangent) : Quaternion.LookRotation(tangent.normalized, Vector3.up) };
    }

    public static bool TryLaunch(SkillProjectileEvent data, Transform actor, Transform target, Vector3? point,
        int index, out SkillAttackShape.Pose pose, out string error, SkillSpace space = SkillSpace.ThreeD)
    {
        pose = default;
        var anchor = new AttackSphereDetectionData { UseFootOrigin = true, Anchor = data.Anchor, Facing = data.Facing, SocketPath = data.SocketPath };
        if (!SkillAttackShape.TryCapture(anchor, actor, target, point, out var origin, out error, space)) return false;
        var rotation = space == SkillSpace.TwoD || data.Facing == SkillRangeFacing.SocketForward ? origin.Rotation : SkillAttackShape.Facing(origin.Rotation);
        var position = origin.Position + (space == SkillSpace.TwoD
            ? rotation * Vector3.right * data.LaunchForward + Vector3.up * data.LaunchHeight
            : rotation * new Vector3(0, data.LaunchHeight, data.LaunchForward));
        if (data.Facing == SkillRangeFacing.TowardTarget && target != null && (target.position - position).sqrMagnitude > .000001f)
            rotation = space == SkillSpace.TwoD ? SkillFacing2D.Look(target.position - position) : Quaternion.LookRotation((target.position - position).normalized, Vector3.up);
        float spread = data.Count <= 1 ? 0 : Mathf.Lerp(-data.SpreadAngle * .5f, data.SpreadAngle * .5f, index / (float)(data.Count - 1));
        pose = new SkillAttackShape.Pose { Space = space, Position = position, Rotation = rotation * (space == SkillSpace.TwoD ? Quaternion.Euler(0, 0, spread) : Quaternion.Euler(0, spread, 0)) };
        return true;
    }
}

}

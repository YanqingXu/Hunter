// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

public enum SkillProjectileFlight { Straight, Arc, Homing }
public enum SkillProjectileTiming { DistanceAndTime, SpeedAndDistance }
public enum SkillProjectileEndPolicy { ContinueFlying, Destroy }

/// <summary>A launch event. Its flight interval does not extend the casting action.</summary>
[Serializable]
public sealed class SkillProjectileEvent : SkillFrameEventBase
{
    public int FrameIndex;
    public GameObject Prefab;
    public SkillRangeAnchor Anchor;
    public SkillRangeFacing Facing;
    public string SocketPath;
    public float LaunchHeight = 1;
    public float LaunchForward;
    public int Count = 1;
    public float SpreadAngle;
    public SkillProjectileFlight Flight;
    public SkillProjectileTiming Timing;
    public float Distance = 10;
    public int FlightFrames = 30;
    public float Speed = 10;
    public float ArcHeight = 2;
    public float TurnDegreesPerSecond = 180;
    public float Radius = .15f;
    public int PierceCount;
    public bool StopAtWalls = true;
    public LayerMask WallLayers = 1;
    public float MaxLifetime = 8;
    public SkillProjectileEndPolicy OnSkillEnd = SkillProjectileEndPolicy.ContinueFlying;
    public SkillProjectileEndPolicy OnSkillCancel = SkillProjectileEndPolicy.Destroy;
    public GameObject EndEffectPrefab;
    public float EndEffectLifetime = 2;
    public AttackHitConfig Hit = new AttackHitConfig();
    public float HitEffectLifetime = 2;
    public SkillHitRules HitRules = new SkillHitRules { Mode = SkillHitMode.OncePerAttack };

    public double FlightSeconds(int rate) => Timing == SkillProjectileTiming.DistanceAndTime ?
        Math.Max(1, FlightFrames) / (double)Math.Max(1, rate) : Distance / Math.Max(.001, Speed);
    public double LifeSeconds(int rate) => Math.Min(MaxLifetime, FlightSeconds(rate));
    public int PreviewFrames(int rate) => Math.Max(1, (int)Math.Min(1000000, Math.Ceiling(Math.Max(0, LifeSeconds(rate)) * Math.Max(1, rate))));

    public SkillProjectileEvent Copy()
    {
        var copy = (SkillProjectileEvent)MemberwiseClone();
        copy.HitRules = HitRules?.Copy();
        copy.Hit = Hit == null ? null : new AttackHitConfig { AttackMultiply = Hit.AttackMultiply, RepelStrength = Hit.RepelStrength,
            RepelTime = Hit.RepelTime, HitEffectPrefab = Hit.HitEffectPrefab, HitAudioClip = Hit.HitAudioClip };
        return copy;
    }

    public static bool Validate(SkillProjectileEvent data, int rate, out string error)
    {
        error = null;
        if (data == null) { error = "缺少投射物配置。"; return false; }
        if (rate <= 0 || !Enum.IsDefined(typeof(SkillProjectileFlight), data.Flight) || !Enum.IsDefined(typeof(SkillProjectileTiming), data.Timing) ||
            !Enum.IsDefined(typeof(SkillRangeAnchor), data.Anchor) || !Enum.IsDefined(typeof(SkillRangeFacing), data.Facing) ||
            !Enum.IsDefined(typeof(SkillProjectileEndPolicy), data.OnSkillEnd) || !Enum.IsDefined(typeof(SkillProjectileEndPolicy), data.OnSkillCancel))
            error = "投射物枚举或技能帧率无效。";
        if (!Positive(data.Distance) || !Positive(data.Radius) || data.Radius < .001f || !Positive(data.MaxLifetime) || data.MaxLifetime > 60 ||
            !Positive(data.EndEffectLifetime) || data.EndEffectLifetime > 60 || !Positive(data.HitEffectLifetime) || data.HitEffectLifetime > 60 || !Nonnegative(data.LaunchForward) || !Nonnegative(data.LaunchHeight) ||
            !Nonnegative(data.SpreadAngle) || data.SpreadAngle > 180 || data.Count < 1 || data.Count > 64 || data.PierceCount < 0 || data.PierceCount > 1000)
            error = "投射物数值无效：距离、半径和寿命须大于 0；寿命最多 60 秒，数量 1～64，散射角 0～180°。";
        if (data.Timing == SkillProjectileTiming.DistanceAndTime ? data.FlightFrames < 1 || data.FlightFrames > 1000000 : !Positive(data.Speed))
            error = "请设置有效的飞行帧数或速度。";
        if (data.Flight == SkillProjectileFlight.Arc && (!Nonnegative(data.ArcHeight) || data.ArcHeight > 1000)) error = "抛物线高度应为 0～1000 米。";
        if (data.Flight == SkillProjectileFlight.Homing && !Positive(data.TurnDegreesPerSecond)) error = "追踪转向速度必须大于 0。";
        if ((data.Anchor == SkillRangeAnchor.Socket || data.Facing == SkillRangeFacing.SocketForward) && string.IsNullOrWhiteSpace(data.SocketPath))
            error = "请填写发射挂点路径或唯一名称。";
        if (data.Hit == null || !Nonnegative(data.Hit.AttackMultiply)) error = "缺少有效的投射物伤害配置。";
        if (!SkillHitRules.Validate(data.HitRules, out string hitError)) error = hitError;
        if (data.HitRules == null || data.HitRules.Mode != SkillHitMode.OncePerAttack) error = "投射物应使用独立的单次目标命中记录。";
        return error == null;
    }
    private static bool Positive(float n) => Nonnegative(n) && n > 0;
    private static bool Nonnegative(float n) => !float.IsNaN(n) && !float.IsInfinity(n) && n >= 0;
}

}

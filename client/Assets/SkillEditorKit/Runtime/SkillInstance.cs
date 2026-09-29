// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using Sirenix.Serialization;
using UnityEngine;

public enum SkillInstanceState { Running, Completed, Cancelled }

/// <summary>One clip execution. Mutable event copies and runtime ownership never live on SkillClip.</summary>
public sealed class SkillInstance
{
    public ISkillAudioService AudioService { get; internal set; } = SkillUnityAudio.Instance;
    public Guid Id { get; } = Guid.NewGuid();
    public SkillClip Source { get; }
    public SkillBehaviourBase Behaviour { get; }
    public PhysicsScene? QueryScene { get; internal set; }
    public PhysicsScene2D? QueryScene2D { get; internal set; }
    public SkillSpace Space { get; }
    public int FrameRate { get; }
    public int LastFrame { get; }
    public int CurrentFrame { get; internal set; } = -1;
    public double ElapsedSeconds { get; internal set; }
    public SkillInstanceState State { get; private set; } = SkillInstanceState.Running;
    public bool IsRunning => State == SkillInstanceState.Running;
    private readonly HashSet<ISkillHitTarget> hitTargets = new HashSet<ISkillHitTarget>();

    internal readonly Dictionary<int, List<SkillCustomEvent>> Custom;
    internal readonly Dictionary<int, SkillAnimationEvent> Animation;
    internal readonly Dictionary<int, List<SkillAudioEvent>> Audio;
    internal readonly Dictionary<int, List<SkillEffectEvent>> Effects;
    internal readonly Dictionary<int, List<SkillProjectileEvent>> Projectiles;
    internal readonly List<SkillProjectile> ProjectileInstances = new List<SkillProjectile>();
    internal readonly List<AttackWindow> Attacks = new List<AttackWindow>();
    internal Action<double> SampleAnimation;
    internal int AnimationStartFrame;
    internal SkillAnimationEvent ActiveAnimation;
    internal readonly Dictionary<SkillWeapon, AttackWindow> ActiveWeapons = new Dictionary<SkillWeapon, AttackWindow>();
    private readonly List<OwnedResource> resources = new List<OwnedResource>();

    internal sealed class AttackWindow
    {
        public SkillAttackDetectionEvent Data;
        public int Start;
        public long End;
        public bool HasOrigin;
        public SkillAttackShape.Origin Origin;
        public SkillAttackShape.Pose PreviousPose;
        public int PreviousQueryFrame = -2;
        public bool ReportedOriginError;
        public readonly SkillHitLedger Hits = new SkillHitLedger();
    }

    private sealed class OwnedResource { public Action Release; public long End; }

    public SkillInstance(SkillClip clip, SkillBehaviourBase behaviour)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
        if (clip.FrameRote <= 0 || clip.FrameCount < 0 || clip.FrameCount > 1000000)
            throw new ArgumentException("技能帧率或总帧数非法", nameof(clip));
        if (clip.DataVersion > SkillClip.CurrentDataVersion) throw new ArgumentException("不支持的技能数据版本", nameof(clip));
        if (!Enum.IsDefined(typeof(SkillSpace), clip.Space)) throw new ArgumentException("未知的游戏空间");
        Space = clip.Space; Source = clip; Behaviour = behaviour; FrameRate = clip.FrameRote; LastFrame = clip.FrameCount;
        if (!SkillTrackModel.ValidateStructure(clip, out string structuralError)) throw new ArgumentException(structuralError);
        Custom = new Dictionary<int, List<SkillCustomEvent>>();
        Animation = new Dictionary<int, SkillAnimationEvent>();
        Audio = new Dictionary<int, List<SkillAudioEvent>>();
        Effects = new Dictionary<int, List<SkillEffectEvent>>();
        Projectiles = new Dictionary<int, List<SkillProjectileEvent>>();
        // Each frame bucket retains serialized track order, then clip order.
        foreach (var entry in SkillTrackModel.Read(clip, true))
        {
            var data = Copy(entry.Data);
            SkillTrackModel.SetFrame(data, entry.Frame);
            if (!SkillMediaTiming.Validate(data, FrameRate, out string reason)) throw new ArgumentException(reason);
            if (data is SkillAttackDetectionEvent attack &&
                !SkillAttackShape.Validate(attack.AttackDetectionData as AttackShapeDetectionDataBase, out string shapeError, Space))
                throw new ArgumentException(shapeError);
            if (data is SkillAttackDetectionEvent hitAttack && !SkillHitRules.Validate(hitAttack.HitRules, out string hitError))
                throw new ArgumentException(hitError);
            switch (entry.Kind)
            {
                case SkillEventKind.Custom: Add(Custom, entry.Frame, (SkillCustomEvent)data); break;
                case SkillEventKind.Animation: Animation.Add(entry.Frame, (SkillAnimationEvent)data); break;
                case SkillEventKind.Audio: Add(Audio, entry.Frame, (SkillAudioEvent)data); break;
                case SkillEventKind.Effect: Add(Effects, entry.Frame, (SkillEffectEvent)data); break;
                case SkillEventKind.Projectile:
                    if (!SkillProjectileEvent.Validate((SkillProjectileEvent)data, FrameRate, out string projectileError)) throw new ArgumentException(projectileError);
                    Add(Projectiles, entry.Frame, (SkillProjectileEvent)data); break;
                case SkillEventKind.Attack:
                    if (entry.Frame >= 0) Attacks.Add(new AttackWindow { Data = (SkillAttackDetectionEvent)data, Start = entry.Frame,
                        End = (long)entry.Frame + Math.Max(0, ((SkillAttackDetectionEvent)data).DurationFrame) });
                    break;
            }
        }
        if (!SkillAnimationTiming.ValidateSequence(Animation, out string overlapError)) throw new ArgumentException(overlapError);
    }

    private static T Copy<T>(T value) where T : class => (T)SerializationUtility.CreateCopy(value);
    private static void Add<T>(Dictionary<int, List<T>> index, int frame, T data)
    {
        if (frame < 0 || data == null) return;
        if (!index.TryGetValue(frame, out var list)) index.Add(frame, list = new List<T>());
        list.Add(data);
    }

    // Legacy policy: at most one hit per target per clip execution.
    public bool TryRegisterHit(ISkillHitTarget target) => IsRunning && target != null && hitTargets.Add(target);

    public bool TryRegisterHit(ISkillHitTarget target, SkillHitData data)
    {
        if (!IsRunning || target == null) return false;
        var rules = data.detectionEvent?.HitRules;
        if (rules == null || rules.Mode == SkillHitMode.LegacyOncePerSkill) return TryRegisterHit(target);
        return data.HitLedger != null && SkillHitRules.Accepts(rules, data.soure, target, data.DetectionOrigin, data.hitPoint, QueryScene, Space, QueryScene2D) &&
            data.HitLedger.Register(target, rules, (double)CurrentFrame / FrameRate);
    }

    public void Own(Action release, long endFrame = long.MaxValue)
    {
        if (release == null) return;
        if (!IsRunning) { release(); return; }
        resources.Add(new OwnedResource { Release = release, End = endFrame });
    }

    internal void ReleaseExpired()
    {
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            if (resources[i].End > CurrentFrame) continue;
            var resource = resources[i];
            resources.RemoveAt(i);
            Release(resource);
            // Release may re-enter the player and end this instance.
            if (!IsRunning) return;
        }
    }

    internal void Finish(SkillInstanceState state)
    {
        if (!IsRunning) return;
        State = state;
        SampleAnimation = null;
        var projectiles = ProjectileInstances.ToArray(); ProjectileInstances.Clear();
        foreach (var projectile in projectiles)
        {
            if (projectile == null) continue;
            try { projectile.SkillFinished(state, LastFrame); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
        var owned = resources.ToArray();
        resources.Clear();
        foreach (var resource in owned) Release(resource);
        hitTargets.Clear();
    }

    private static void Release(OwnedResource resource)
    {
        try { resource.Release(); }
        catch (Exception ex) { Debug.LogException(ex); } // One broken resource must not skip the others.
    }
}

}

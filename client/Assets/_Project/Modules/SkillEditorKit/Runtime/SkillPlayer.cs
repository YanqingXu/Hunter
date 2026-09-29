// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

/// <summary>Executes one isolated SkillInstance at a time. SkillClip is input, never working state.</summary>
public class SkillPlayer : SerializedMonoBehaviour
{
    private ISkillAnimationDriver animation_Controller;
    private SkillBehaviourBase skillBehaviour;
    private Transform modelTransform;
    private ISkillActor owner;
    public SkillInstance CurrentInstance { get; private set; }
    public bool IsPlaying => CurrentInstance != null && CurrentInstance.IsRunning;
    public Transform ModelTransform => modelTransform;
    public LayerMask attackDetectionLayer;
    public Transform AimTarget { get; private set; }
    public PhysicsScene? QueryScene { get; set; }
    public PhysicsScene2D? QueryScene2D { get; set; }
    public ISkillAudioService AudioService { get; set; } = SkillUnityAudio.Instance;
    public Vector3? AimPoint { get; private set; }
    public void SetAimContext(Transform target, Vector3? worldPoint = null)
    { AimTarget = target; AimPoint = worldPoint; }

    public void Init(ISkillActor owner, ISkillAnimationDriver animation_Controller, Transform modelTransform)
    {
        StopSkillClip(false);
        this.owner = owner;
        this.animation_Controller = animation_Controller;
        this.modelTransform = modelTransform != null ? modelTransform : transform;
        if (weaponDic == null) weaponDic = new Dictionary<string, SkillWeapon>();
        foreach (var weapon in weaponDic.Values)
            if (weapon != null) weapon.Init(attackDetectionLayer, OnWeaponDetection);
    }

    [SerializeField] private ParentConstraint mainWeaponParentConstraint;
    [SerializeField] private Dictionary<string, SkillWeapon> weaponDic = new Dictionary<string, SkillWeapon>();
    public Dictionary<string, SkillWeapon> WeaponDic => weaponDic;
    public ParentConstraint MainWeaponParentConstraint => mainWeaponParentConstraint;

    public void SetMainWeaponHand(bool isLeft)
    {
        if (mainWeaponParentConstraint == null || mainWeaponParentConstraint.sourceCount < 2) return;
        var left = mainWeaponParentConstraint.GetSource(0);
        var right = mainWeaponParentConstraint.GetSource(1);
        left.weight = isLeft ? 1 : 0;
        right.weight = isLeft ? 0 : 1;
        mainWeaponParentConstraint.SetSource(0, left);
        mainWeaponParentConstraint.SetSource(1, right);
    }

    private void OnWeaponDetection(ISkillHitTarget target, SkillHitData data)
    {
        var instance = data.Instance;
        if (IsCurrent(instance) && target != null) instance.Behaviour.OnAttackDetection(target, data);
    }

    public void StartPlaySkillBehaviour(SkillBehaviourBase behaviour)
    {
        if (behaviour != null && behaviour.CurrentInstance != null &&
            !ReferenceEquals(behaviour.CurrentInstance, CurrentInstance))
            throw new InvalidOperationException("同一个 SkillBehaviour 实例不能同时绑定多个播放器。请为角色创建独立行为实例。");
        skillBehaviour = behaviour;
        skillBehaviour?.Init(owner, this);
    }

    public void PlaySkillClip(SkillClip clip)
    {
        SkillInstance instance;
        try { instance = new SkillInstance(clip, skillBehaviour) { QueryScene = QueryScene, QueryScene2D = QueryScene2D, AudioService = AudioService ?? SkillUnityAudio.Instance }; }
        catch (Exception ex)
        {
            StopSkillClip(false);
            Debug.LogError("无法播放技能：" + ex.Message, this);
            return;
        }
        // Same-behaviour combo releases keep their existing combo/cooldown policy.
        StopSkillClip(false);
        if (CurrentInstance != null) return; // Resource cleanup re-entered and started another clip.
        CurrentInstance = instance;
        instance.Behaviour.AttachInstance(instance);
        EvaluateFrame(instance);
        if (IsCurrent(instance) && instance.LastFrame == 0) End(instance, SkillInstanceState.Completed, true);
    }

    public void StopSkillClip(bool notifySkillBehaviour = true)
    {
        var instance = CurrentInstance;
        if (instance != null) End(instance, SkillInstanceState.Cancelled, notifySkillBehaviour);
    }

    private bool IsCurrent(SkillInstance instance) =>
        instance != null && instance.IsRunning && ReferenceEquals(CurrentInstance, instance);

    private void Update() => AdvanceBy(Time.deltaTime);

    /// <summary>Deterministic clock entry point, also used by replay and regression tests.</summary>
    public void AdvanceBy(float deltaSeconds)
    {
        var instance = CurrentInstance;
        if (!IsCurrent(instance)) return;
        if (instance.Source == null) { End(instance, SkillInstanceState.Cancelled, false); return; }
        if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0) return;
        instance.ElapsedSeconds += deltaSeconds;
        long target = (long)Math.Min(instance.LastFrame, Math.Floor(instance.ElapsedSeconds * instance.FrameRate));
        while (IsCurrent(instance) && instance.CurrentFrame < target) EvaluateFrame(instance);
        if (IsCurrent(instance) && instance.SampleAnimation != null)
        {
            try { instance.SampleAnimation(instance.ElapsedSeconds - instance.AnimationStartFrame / (double)instance.FrameRate); }
            catch (Exception ex) { if (IsCurrent(instance)) End(instance, SkillInstanceState.Cancelled, false); Debug.LogException(ex, this); }
        }
        // Always check the captured instance. An event may have replaced it in the loop.
        if (IsCurrent(instance) && instance.ElapsedSeconds * instance.FrameRate >= instance.LastFrame)
            End(instance, SkillInstanceState.Completed, true);
    }

    private void End(SkillInstance instance, SkillInstanceState state, bool notify)
    {
        if (!IsCurrent(instance)) return;
        CurrentInstance = null;
        instance.Behaviour.DetachInstance(instance);
        animation_Controller?.ClearRootMotionAction();
        animation_Controller?.StopSkillAnimation();
        foreach (var pair in instance.ActiveWeapons)
            if (pair.Key != null) pair.Key.StopDetection();
        instance.ActiveWeapons.Clear();
#if UNITY_EDITOR
        currentAttackDetectionList.Clear();
#endif
        instance.Finish(state);
        // Cleanup precedes notification, and cannot erase a clip started by notification.
        if (notify && CurrentInstance == null)
        {
            if (state == SkillInstanceState.Completed) instance.Behaviour.OnSkillClipEnd();
            else instance.Behaviour.OnReleaseNewSkill();
        }
    }

    private void OnDisable() => StopSkillClip(false);
    private void OnDestroy() => StopSkillClip(false);

    private void EvaluateFrame(SkillInstance instance)
    {
        try
        {
            instance.CurrentFrame++;
            instance.ReleaseExpired();
            if (!IsCurrent(instance)) return;
            instance.SampleAnimation?.Invoke((instance.CurrentFrame - instance.AnimationStartFrame) / (double)instance.FrameRate);
            if (!IsCurrent(instance)) return;
            instance.Behaviour.OnTickSkill(instance.CurrentFrame);
            if (IsCurrent(instance)) TickCustom(instance);
            if (IsCurrent(instance)) TickAnimation(instance);
            if (IsCurrent(instance)) TickAudio(instance);
            if (IsCurrent(instance)) TickEffects(instance);
            if (IsCurrent(instance)) TickAttacks(instance);
            if (IsCurrent(instance)) TickProjectiles(instance);
        }
        catch (Exception ex)
        {
            if (IsCurrent(instance)) End(instance, SkillInstanceState.Cancelled, false);
            Debug.LogException(ex, this);
        }
    }

    private void TickCustom(SkillInstance s)
    {
        if (!s.Custom.TryGetValue(s.CurrentFrame, out var events)) return;
        foreach (var original in events)
        {
            if (!IsCurrent(s)) return;
            var data = s.Behaviour.BeforeSkillCustomEvent(original);
            if (IsCurrent(s) && data != null) s.Behaviour.AfterSkillCustomEvent(data);
        }
    }

    private void TickAnimation(SkillInstance s)
    {
        if (animation_Controller == null || !s.Animation.TryGetValue(s.CurrentFrame, out var data) || data == null) return;
        data = s.Behaviour.BeforeSkillAnimationEvent(data);
        if (!IsCurrent(s) || data == null) return;
        SetMainWeaponHand(data.MainWeaponOnLeftHand);
        if (data.AnimationClip != null)
        {
            if (!SkillMediaTiming.Validate(data, s.FrameRate, out string reason)) throw new ArgumentException(reason);
            var previous = s.ActiveAnimation;
            int previousStart = s.AnimationStartFrame;
            int overlap = SkillAnimationTiming.OverlapFrames(previousStart, previous, s.CurrentFrame, data);
            s.ActiveAnimation = SkillAnimationTiming.Snapshot(data);
            s.AnimationStartFrame = s.CurrentFrame;
            s.SampleAnimation = animation_Controller.PlaySkillAnimation(data, s.FrameRate, previous,
                (s.CurrentFrame - (double)previousStart) / s.FrameRate, overlap / (double)s.FrameRate);
            if (!IsCurrent(s)) return; // Do not clear the replacement animation's root-motion callback.
        }
        if (data.ApplyRootMotion)
            animation_Controller.SetRootMotionAction((position, rotation) =>
            {
                if (IsCurrent(s)) s.Behaviour.OnRootMotion(position, rotation);
            });
        else animation_Controller.ClearRootMotionAction();
        if (IsCurrent(s) && data.AnimationClip != null) s.SampleAnimation?.Invoke(0);
        if (IsCurrent(s)) s.Behaviour.AfterSkillAnimationEvent(data);
    }

    private void TickAudio(SkillInstance s)
    {
        if (!s.Audio.TryGetValue(s.CurrentFrame, out var events)) return;
        foreach (var configured in events)
        {
            var data = s.Behaviour.BeforeSkillAudioEvent(configured);
            if (!IsCurrent(s)) return;
            if (data == null) continue;
            if (data.AudioClip != null)
            {
                if (!SkillMediaTiming.Validate(data, s.FrameRate, out string reason)) throw new ArgumentException(reason);
                var lease = s.AudioService.PlayClip(data.AudioClip, transform.position, data.Voluem,
                    SkillMediaTiming.ClipIn(data), SkillMediaTiming.DurationSeconds(data, s.FrameRate), gameObject.scene);
                if (lease != null) s.Own(lease.Dispose, (long)s.CurrentFrame + SkillMediaTiming.AudioFrames(data, s.FrameRate));
                if (!IsCurrent(s)) return; // A host audio adapter can synchronously replace the cast.
            }
            s.Behaviour.AfterSkillAudioEvent(data);
            if (!IsCurrent(s)) return;
        }
    }

    private void TickEffects(SkillInstance s)
    {
        if (!s.Effects.TryGetValue(s.CurrentFrame, out var events)) return;
        foreach (var configured in events)
        {
            var data = s.Behaviour.BeforeSkillEffectEvent(configured);
            if (!IsCurrent(s)) return;
            if (data == null) continue;
            if (data.Prefab != null)
            {
                // Use an owned instance, not a name-keyed shared pool. No old delayed
                // coroutine can destroy/recycle an object borrowed by a later cast.
                var root = modelTransform != null ? modelTransform : transform;
                var effectRotation=root.rotation;
                var anchor=s.Space == SkillSpace.TwoD ? SkillFacing2D.EffectAnchor(root,out effectRotation) : root.localToWorldMatrix;
                var effect = Instantiate(data.Prefab, anchor.MultiplyPoint3x4(data.Position), effectRotation * Quaternion.Euler(data.Rotation));
                s.Own(() => DestroyOwnedEffect(effect), data.AutoDestruct ? (long)s.CurrentFrame + Math.Max(1, data.Duration) : long.MaxValue);
                if (!IsCurrent(s)) return; // Prefab Awake/OnEnable can re-enter the player.
                effect.name = data.Prefab.name;
                effect.transform.localScale = data.Scale;
            }
            s.Behaviour.AfterSkillEffectEvent(data);
            if (!IsCurrent(s)) return;
        }
    }

    private static void DestroyOwnedEffect(GameObject effect)
    {
        if (effect == null) return;
        effect.SetActive(false);
        if (Application.isPlaying) Destroy(effect);
        else DestroyImmediate(effect);
    }

    private void TickProjectiles(SkillInstance instance)
    {
        if (owner == null || !instance.Projectiles.TryGetValue(instance.CurrentFrame, out var events)) return;
        foreach (var configured in events)
        {
            var data = instance.Behaviour.BeforeSkillProjectileEvent(configured);
            if (!IsCurrent(instance)) return;
            if (data == null) continue;
            if (!SkillProjectileEvent.Validate(data, instance.FrameRate, out string error)) throw new ArgumentException(error);
            var actor = modelTransform != null ? modelTransform : transform;
            for (int index = 0; index < data.Count; index++)
            {
                if (!SkillProjectileMotion.TryLaunch(data, actor, AimTarget, AimPoint, index, out var pose, out error, instance.Space))
                { Debug.LogWarning("投射物未发射：" + error, this); break; }
                if (data.Flight == SkillProjectileFlight.Homing && AimTarget == null)
                { Debug.LogWarning("追踪投射物未发射：未指定目标。", this); break; }
                float damage = owner.GetAttackValue(new SkillAttackDetectionEvent { AttackHitConfig = data.Hit });
                if (!IsCurrent(instance)) return;
                var projectile = SkillProjectile.Spawn(data, instance, owner, damage, AimTarget, pose, attackDetectionLayer, gameObject.scene);
                if (!IsCurrent(instance)) return;
                double elapsed = data.OnSkillEnd == SkillProjectileEndPolicy.Destroy ?
                    Math.Min(instance.ElapsedSeconds, instance.LastFrame / (double)instance.FrameRate) : instance.ElapsedSeconds;
                if (projectile != null && projectile.Alive) projectile.AdvanceBy(Math.Max(0, elapsed - instance.CurrentFrame / (double)instance.FrameRate));
                if (!IsCurrent(instance)) return;
            }
            instance.Behaviour.AfterSkillProjectileEvent(data);
            if (!IsCurrent(instance)) return;
        }
    }

    private void TickAttacks(SkillInstance s)
    {
#if UNITY_EDITOR
        currentAttackDetectionList.Clear();
#endif
        foreach (var window in s.Attacks)
        {
            int frame = s.CurrentFrame;
            if (frame < window.Start || frame > window.End) continue;
            var attackRoot = modelTransform != null ? modelTransform : transform;
            SkillAttackShape.Pose? attackPose = null;
            bool weaponEvent = window.Data.GetAttackDetectionType() == AttackDetectionType.Weapon;
            if (weaponEvent && frame != window.Start && frame != window.End) continue;
            var data = s.Behaviour.BeforeSkillAttackDetectionEvent(window.Data);
            if (!IsCurrent(s)) return;
            if (data != null)
            {
                var type = data.GetAttackDetectionType();
                if (type == AttackDetectionType.Weapon && frame == window.Start && owner != null)
                {
                    var weaponData = (AttackWeaponDetectionData)data.AttackDetectionData;
                    if (weaponDic != null && weaponDic.TryGetValue(weaponData.weaponName ?? "", out var weapon) && weapon != null)
                    {
                        var attack = MakeAttack(s, window, data, Vector3.zero, weapon.transform.position);
                        if (!IsCurrent(s)) return;
                        s.ActiveWeapons[weapon] = window;
                        weapon.StartDetection(attack);
                    }
                }
                else if (type != AttackDetectionType.Weapon && type != AttackDetectionType.None && owner != null)
                {
                    var shapeData = data.AttackDetectionData as AttackShapeDetectionDataBase;
                    if (!SkillAttackShape.Validate(shapeData, out string shapeError, s.Space)) throw new ArgumentException(shapeError);
                    var currentOrigin = window.Origin;
                    if ((!SkillAttackShape.IsFrozen(shapeData) || !window.HasOrigin) &&
                        !SkillAttackShape.TryCapture(shapeData, attackRoot, AimTarget, AimPoint, out currentOrigin, out string originError, s.Space))
                    {
                        if (!window.ReportedOriginError) { Debug.LogWarning("攻击范围跳过：" + originError, this); window.ReportedOriginError = true; }
                        continue;
                    }
                    if (!window.HasOrigin) { window.Origin = currentOrigin; window.HasOrigin = true; }
                    var origin = SkillAttackShape.IsFrozen(shapeData) ? window.Origin : currentOrigin;
                    attackPose = SkillAttackShape.Resolve(shapeData, origin,
                        SkillAttackShape.Progress(frame, window.Start, window.Data.DurationFrame));
                    Component[] colliders = s.Space == SkillSpace.TwoD
                        ? SkillAttackDetection2D.Detect(shapeData, attackPose.Value, attackDetectionLayer,
                            window.PreviousQueryFrame == frame - 1 ? (SkillAttackShape.Pose?)window.PreviousPose : null, s.QueryScene2D)
                        : shapeData.UseFootOrigin
                        ? SkillAttackDetectionTool.ForwardDetection(shapeData, attackPose.Value, attackDetectionLayer,
                            window.PreviousQueryFrame == frame - 1 ? (SkillAttackShape.Pose?)window.PreviousPose : null, s.QueryScene)
                        : SkillAttackDetectionTool.ShapeDetection(attackRoot, data.AttackDetectionData, type, attackDetectionLayer, s.QueryScene);
                    window.PreviousPose = attackPose.Value; window.PreviousQueryFrame = frame;
                    if (colliders != null && data.HitRules != null && data.HitRules.Mode != SkillHitMode.LegacyOncePerSkill)
                    {
                        var centre = origin.Position;
                        Array.Sort(colliders, (a, b) =>
                        {
                            if (a == null) return b == null ? 0 : 1;
                            if (b == null) return -1;
                            int order = (SkillCollider.ClosestPoint(a, centre) - centre).sqrMagnitude.CompareTo((SkillCollider.ClosestPoint(b, centre) - centre).sqrMagnitude);
                            return order != 0 ? order : a.GetInstanceID().CompareTo(b.GetInstanceID());
                        });
                    }
                    if (colliders != null)
                        foreach (var collider in colliders)
                        {
                            if (collider == null) continue;
                            var target = collider.GetComponentInParent<ISkillHitTarget>();
                            if (target == null) continue;
                            var shape = data.AttackDetectionData as AttackShapeDetectionDataBase;
                            if (shape == null) continue;
                            var attack = MakeAttack(s, window, data, SkillCollider.ClosestPoint(collider, attackPose.Value.Position), origin.Position);
                            if (!IsCurrent(s)) return;
                            s.Behaviour.OnAttackDetection(target, attack);
                            if (!IsCurrent(s)) return;
                        }
                }
                if (IsCurrent(s)) s.Behaviour.AfterSkillAttackDetectionEvent(data);
                if (!IsCurrent(s)) return;
#if UNITY_EDITOR
                if (drawAttackDetectionGizmos) currentAttackDetectionList.Add((data, attackPose));
#endif
            }
            // Boundaries come from the original execution schedule, not a mutable
            // Before-hook result. Returning null on the end frame cannot leak a weapon.
            if (frame == window.End)
            {
                var ended = new List<SkillWeapon>();
                foreach (var pair in s.ActiveWeapons)
                    if (ReferenceEquals(pair.Value, window)) ended.Add(pair.Key);
                foreach (var weapon in ended)
                {
                    if (weapon != null) weapon.StopDetection();
                    s.ActiveWeapons.Remove(weapon);
                }
            }
        }
    }

    private SkillHitData MakeAttack(SkillInstance s, SkillInstance.AttackWindow window, SkillAttackDetectionEvent data, Vector3 point, Vector3 origin) =>
        new SkillHitData { Instance = s, HitLedger = window.Hits, DetectionOrigin = origin,
            detectionEvent = data, soure = owner, attackValue = owner.GetAttackValue(data), hitPoint = point };

#if UNITY_EDITOR
    [SerializeField] private bool drawAttackDetectionGizmos;
    private readonly List<(SkillAttackDetectionEvent Data, SkillAttackShape.Pose? Pose)> currentAttackDetectionList = new List<(SkillAttackDetectionEvent, SkillAttackShape.Pose?)>();
    private void OnDrawGizmos()
    {
        if (!drawAttackDetectionGizmos) return;
        foreach (var data in currentAttackDetectionList) SkillGizmosTool.DrawDetection(data.Data, this, data.Pose);
    }
#endif
}

}

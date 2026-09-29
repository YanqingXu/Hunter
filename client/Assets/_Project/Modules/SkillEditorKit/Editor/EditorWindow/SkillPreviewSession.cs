// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>Owns AnimationMode and all manual preview mutations until explicitly restored.</summary>
public sealed class SkillPreviewSession : IDisposable
{
    public GameObject Target { get; }
    public Vector3 OriginPosition { get; }
    public Quaternion OriginRotation { get; }
    private readonly Matrix4x4 originMatrix;
    private readonly AnimationModeDriver driver;
    private readonly List<TransformState> transforms = new List<TransformState>();
    private readonly List<AnimatorState> animators = new List<AnimatorState>();
    private readonly Dictionary<ParentConstraint, ConstraintState> constraints = new Dictionary<ParentConstraint, ConstraintState>();
    private bool disposed;
    private PlayableGraph blendGraph;
    private AnimationMixerPlayable blendMixer;
    private AnimationClipPlayable outgoingPlayable, incomingPlayable;
    private AnimationClip outgoingClip, incomingClip;

    private sealed class TransformState
    {
        public Transform Target, Parent;
        public int Sibling;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public bool Active;
        public void Restore()
        {
            if (Target == null) return;
            if (Target.parent != Parent) Target.SetParent(Parent, false);
            Target.SetSiblingIndex(Sibling);
            Target.localPosition = Position; Target.localRotation = Rotation; Target.localScale = Scale;
            if (Target.gameObject.activeSelf != Active) Target.gameObject.SetActive(Active);
        }
    }

    private sealed class AnimatorState
    {
        public Animator Target;
        public RuntimeAnimatorController Controller;
        public bool RootMotion, FireEvents, Enabled;
        public float Speed;
        public AnimatorCullingMode Culling;
        public AnimatorUpdateMode Update;
        public void Restore()
        {
            if (Target == null) return;
            if (Target.runtimeAnimatorController != Controller) Target.runtimeAnimatorController = Controller;
            Target.applyRootMotion = RootMotion; Target.fireEvents = FireEvents; Target.enabled = Enabled;
            Target.speed = Speed; Target.cullingMode = Culling; Target.updateMode = Update;
        }
    }

    // Keep live Transform references. A JSON round-trip of a native constraint can
    // restore its weights while losing the scene/prefab-instance source references.
    private sealed class ConstraintState
    {
        private readonly ParentConstraint target;
        private readonly List<ConstraintSource> sources = new List<ConstraintSource>();
        private readonly Vector3[] translationOffsets, rotationOffsets;
        private readonly Vector3 translationAtRest, rotationAtRest;
        private readonly Axis translationAxis, rotationAxis;
        private readonly float weight;
        private readonly bool active, locked, enabled;

        public ConstraintState(ParentConstraint target)
        {
            this.target = target;
            target.GetSources(sources);
            translationOffsets = target.translationOffsets;
            rotationOffsets = target.rotationOffsets;
            translationAtRest = target.translationAtRest; rotationAtRest = target.rotationAtRest;
            translationAxis = target.translationAxis; rotationAxis = target.rotationAxis;
            weight = target.weight; active = target.constraintActive; locked = target.locked; enabled = target.enabled;
        }

        public void Restore()
        {
            if (target == null) return;
            if (target.sourceCount != sources.Count) target.SetSources(sources);
            else
                for (int i = 0; i < sources.Count; i++)
                {
                    var current = target.GetSource(i);
                    if (current.sourceTransform != sources[i].sourceTransform || current.weight != sources[i].weight)
                        target.SetSource(i, sources[i]);
                }
            for (int i = 0; i < sources.Count; i++)
            {
                if (target.GetTranslationOffset(i) != translationOffsets[i]) target.SetTranslationOffset(i, translationOffsets[i]);
                if (target.GetRotationOffset(i) != rotationOffsets[i]) target.SetRotationOffset(i, rotationOffsets[i]);
            }
            if (target.translationAtRest != translationAtRest) target.translationAtRest = translationAtRest;
            if (target.rotationAtRest != rotationAtRest) target.rotationAtRest = rotationAtRest;
            if (target.translationAxis != translationAxis) target.translationAxis = translationAxis;
            if (target.rotationAxis != rotationAxis) target.rotationAxis = rotationAxis;
            if (target.weight != weight) target.weight = weight;
            if (target.locked != locked) target.locked = locked;
            if (target.constraintActive != active) target.constraintActive = active;
            if (target.enabled != enabled) target.enabled = enabled;
        }
    }

    public SkillPreviewSession(GameObject target)
    {
        // Unity puts HideAndDontSave clones in its internal, invalid-handle preview scene.
        // They are valid temporary instances; persistent prefab assets remain forbidden.
        if (target == null || EditorUtility.IsPersistent(target) ||
            (!target.scene.IsValid() && (target.hideFlags & HideFlags.DontSaveInEditor) == 0))
            throw new ArgumentException("只能预览场景实例，不能直接修改 Prefab 资源。");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("进入运行模式时禁止编辑预览。");
        if (AnimationMode.InAnimationMode())
            throw new InvalidOperationException("其他窗口正在使用动画预览，请先停止该预览。");
        Target = target;
        OriginPosition = target.transform.position;
        OriginRotation = target.transform.rotation;
        originMatrix = target.transform.localToWorldMatrix;
        var captured = new HashSet<Transform>();
        CaptureTransforms(target.transform, captured);
        var player = target.GetComponent<SkillPlayer>();
        if (player != null)
        {
            if (player.WeaponDic != null)
                foreach (var weapon in player.WeaponDic.Values)
                    if (weapon != null) CaptureTransforms(weapon.transform, captured);
            if (player.MainWeaponParentConstraint != null)
                constraints[player.MainWeaponParentConstraint] = new ConstraintState(player.MainWeaponParentConstraint);
        }
        foreach (var constraint in target.GetComponentsInChildren<ParentConstraint>(true))
            constraints[constraint] = new ConstraintState(constraint);
        foreach (var animator in target.GetComponentsInChildren<Animator>(true))
            animators.Add(new AnimatorState { Target = animator, Controller = animator.runtimeAnimatorController,
                RootMotion = animator.applyRootMotion, FireEvents = animator.fireEvents, Enabled = animator.enabled,
                Speed = animator.speed, Culling = animator.cullingMode, Update = animator.updateMode });
        driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
        driver.hideFlags = HideFlags.HideAndDontSave;
    }

    private void CaptureTransforms(Transform root, HashSet<Transform> captured)
    {
        foreach (var target in root.GetComponentsInChildren<Transform>(true))
            if (captured.Add(target)) transforms.Add(new TransformState { Target = target, Parent = target.parent,
                Sibling = target.GetSiblingIndex(), Position = target.localPosition, Rotation = target.localRotation,
                Scale = target.localScale, Active = target.gameObject.activeSelf });
    }

    public void Restore()
    {
        if (driver != null && AnimationMode.InAnimationMode(driver)) AnimationMode.StopAnimationMode(driver);
        foreach (var animator in animators) animator.Restore();
        foreach (var state in constraints.Values) state.Restore();
        foreach (var state in transforms) state.Restore();
    }

    private void BeginSample()
    {
        if (disposed || Target == null) throw new InvalidOperationException("预览会话已失效。");
        if (AnimationMode.InAnimationMode() && !AnimationMode.InAnimationMode(driver))
            throw new InvalidOperationException("动画预览已被其他窗口接管。");
        Restore();
        AnimationMode.StartAnimationMode(driver);
        foreach (var state in animators)
        {
            if (state.Target == null) continue;
            // SampleAnimationClip supports this project's controller-less characters.
            // Assigning a temporary controller here would dirty the user's scene.
            state.Target.fireEvents = false;
        }
    }

    private void Sample(AnimationClip clip, float time, bool rootMotion, bool normalizeRoot = false)
    {
        BeginSample();
        if (normalizeRoot)
        {
            Target.transform.localPosition = Vector3.zero;
            Target.transform.localRotation = Quaternion.identity;
            Target.transform.localScale = Vector3.one;
        }
        foreach (var state in animators) if (state.Target != null) state.Target.applyRootMotion = rootMotion;
        AnimationMode.BeginSampling();
        try { AnimationMode.SampleAnimationClip(Target, clip, Mathf.Clamp(time, 0, clip.length)); }
        finally { AnimationMode.EndSampling(); }
    }

    private void SampleBlend(SkillAnimationEvent previous, float previousTime, SkillAnimationEvent current, float currentTime, float weight)
    {
        var animator = Target.GetComponent<Animator>();
        if (animator == null) throw new InvalidOperationException("动画融合预览需要角色根节点上的 Animator 组件。");
        if (!blendGraph.IsValid() || outgoingClip != previous.AnimationClip || incomingClip != current.AnimationClip)
        {
            if (blendGraph.IsValid()) blendGraph.Destroy();
            outgoingClip = previous.AnimationClip; incomingClip = current.AnimationClip;
            blendGraph = PlayableGraph.Create("Skill Editor Animation Blend");
            blendGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            blendMixer = AnimationMixerPlayable.Create(blendGraph, 2);
            blendMixer.SetPropagateSetTime(false);
            outgoingPlayable = AnimationClipPlayable.Create(blendGraph, outgoingClip);
            incomingPlayable = AnimationClipPlayable.Create(blendGraph, incomingClip);
            outgoingPlayable.SetSpeed(0); incomingPlayable.SetSpeed(0);
            blendGraph.Connect(outgoingPlayable, 0, blendMixer, 0);
            blendGraph.Connect(incomingPlayable, 0, blendMixer, 1);
            var output = AnimationPlayableOutput.Create(blendGraph, "Skill Preview", animator);
            output.SetSourcePlayable(blendMixer);
        }
        BeginSample();
        foreach (var state in animators) if (state.Target != null) state.Target.applyRootMotion = current.ApplyRootMotion;
        blendMixer.SetInputWeight(0, 1 - weight); blendMixer.SetInputWeight(1, weight);
        outgoingPlayable.SetTime(previousTime); outgoingPlayable.SetTime(previousTime);
        incomingPlayable.SetTime(currentTime); incomingPlayable.SetTime(currentTime);
        AnimationMode.BeginSampling();
        try { AnimationMode.SamplePlayableGraph(blendGraph, 0, 0); }
        finally { AnimationMode.EndSampling(); }
    }

    private struct Motion
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public static Motion Identity => new Motion { Rotation = Quaternion.identity };
        public static Motion Multiply(Motion a, Motion b) => new Motion
        { Position = a.Position + a.Rotation * b.Position, Rotation = a.Rotation * b.Rotation };
        public static Motion Power(Motion value, int count)
        {
            var result = Identity;
            while (count > 0)
            {
                if ((count & 1) != 0) result = Multiply(result, value);
                value = Multiply(value, value);
                count >>= 1;
            }
            return result;
        }
    }

    private Motion SampleRoot(AnimationClip clip, float time)
    {
        try
        {
            Sample(clip, time, true, true);
            return new Motion { Position = Target.transform.localPosition, Rotation = Target.transform.localRotation };
        }
        finally { Restore(); }
    }

    private Motion Delta(AnimationClip clip, float time, float startTime = 0)
    {
        var start = SampleRoot(clip, startTime);
        var end = SampleRoot(clip, time);
        var inverse = Quaternion.Inverse(start.Rotation);
        return new Motion { Position = inverse * (end.Position - start.Position), Rotation = inverse * end.Rotation };
    }

    private Motion RootMotion(SkillClip clip, int frame)
    {
        var result = Motion.Identity;
        if (clip == null) return result;
        var entries = SkillTrackModel.Animation(clip).Where(p => p.Value?.AnimationClip != null && p.Value.AnimationClip.length > 0)
            .OrderBy(p => p.Key).ToList();
        if (!SkillAnimationTiming.ValidateSequence(entries, out string overlapError)) throw new ArgumentException(overlapError);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var data = entry.Value;
            if (entry.Key > frame) break;
            if (!data.ApplyRootMotion) continue;
            long end = Math.Min((long)frame, (long)entry.Key + Math.Max(1, data.DurationFrame));
            if (i + 1 < entries.Count) end = Math.Min(end, entries[i + 1].Key);
            float seconds = Math.Max(0, end - entry.Key) / (float)Math.Max(1, clip.FrameRote);
            if (!SkillMediaTiming.Validate(data, clip.FrameRote, out string reason)) throw new ArgumentException(reason);
            result = Motion.Multiply(result, BlendedRootMotion(entry, seconds, clip.FrameRote,
                i > 0 ? entries[i - 1] : default));
        }
        return result;
    }

    private Motion AnimationMotion(SkillAnimationEvent data, float seconds)
    {
        var animation = data.AnimationClip;
        if (data.UseClipRange)
            return Delta(animation, (float)SkillMediaTiming.AnimationRangeTime(animation, data.ClipIn + seconds),
                (float)SkillMediaTiming.AnimationRangeTime(animation, data.ClipIn));
        if (!animation.isLooping) return Delta(animation, Mathf.Min(seconds, animation.length));
        // Use the same precision for quotient and remainder. float division can round
        // 2 / .6666667 to 3 while '%' still returns almost a full loop, adding one cycle.
        double length = animation.length;
        int loops = (int)Math.Floor((double)seconds / length);
        float remainder = (float)(seconds - loops * length);
        return Motion.Multiply(Motion.Power(Delta(animation, animation.length), loops), Delta(animation, remainder));
    }

    private static Motion Relative(Motion from, Motion to)
    {
        var inverse = Quaternion.Inverse(from.Rotation);
        return new Motion { Position = inverse * (to.Position - from.Position), Rotation = inverse * to.Rotation };
    }

    private Motion BlendedRootMotion(KeyValuePair<int, SkillAnimationEvent> entry, float seconds, int rate,
        KeyValuePair<int, SkillAnimationEvent> outgoing)
    {
        var data = entry.Value;
        int overlapFrames = SkillAnimationTiming.OverlapFrames(outgoing.Key, outgoing.Value, entry.Key, data);
        double blend = overlapFrames / (double)Math.Max(1, rate);
        if (overlapFrames == 0 || seconds <= 0) return AnimationMotion(data, seconds);
        var result = Motion.Identity;
        var previous = Motion.Identity;
        float outgoingOffset = (entry.Key - outgoing.Key) / (float)Math.Max(1, rate);
        var previousOutgoing = AnimationMotion(outgoing.Value, outgoingOffset);
        float time = 0;
        // Both source animations keep advancing through the actual overlap. As in the
        // player's event callback, the incoming clip controls whether root motion applies.
        int steps = Math.Min(overlapFrames, Mathf.CeilToInt(seconds * Math.Max(1, rate)));
        for (int step = 1; step <= steps; step++)
        {
            time = Mathf.Min(seconds, step / (float)Math.Max(1, rate));
            var next = AnimationMotion(data, time);
            var delta = Relative(previous, next);
            var nextOutgoing = AnimationMotion(outgoing.Value, outgoingOffset + time);
            var outgoingDelta = Relative(previousOutgoing, nextOutgoing);
            float weight = SkillAnimationTiming.BlendWeight(time, blend);
            result = Motion.Multiply(result, new Motion { Position = Vector3.LerpUnclamped(outgoingDelta.Position, delta.Position, weight),
                Rotation = Quaternion.SlerpUnclamped(outgoingDelta.Rotation, delta.Rotation, weight) });
            previous = next;
            previousOutgoing = nextOutgoing;
        }
        if (time < seconds) result = Motion.Multiply(result, Relative(previous, AnimationMotion(data, seconds)));
        return result;
    }

    public Vector3 GetRootPosition(SkillClip clip, int frame)
    {
        try { return OriginPosition + originMatrix.MultiplyVector(RootMotion(clip, frame).Position); }
        finally { Restore(); }
    }

    public SkillAttackShape.Origin GetAttackOrigin(SkillClip clip, int startFrame, int displayedFrame, Transform modelRoot)
    {
        try
        {
            Evaluate(clip, startFrame);
            return SkillAttackShape.Origin.Capture(modelRoot != null ? modelRoot : Target.transform, clip.Space);
        }
        finally { Evaluate(clip, displayedFrame); }
    }

    // Effects spawn in world space, using the actor pose at the event's start.
    // Restore only animation sampling here, not the whole track UI (which would recurse).
    public Matrix4x4 GetEffectAnchor(SkillClip clip, int frame, int displayedFrame, out Quaternion rotation)
    {
        try
        {
            Evaluate(clip, frame);
            rotation = Target.transform.rotation;
            if (clip.Space == SkillSpace.TwoD) return SkillFacing2D.EffectAnchor(Target.transform,out rotation);
            return Target.transform.localToWorldMatrix;
        }
        finally { Evaluate(clip, displayedFrame); }
    }

    public void Evaluate(SkillClip clip, int frame)
    {
        try
        {
            Restore();
            var motion = RootMotion(clip, frame);
            if (clip != null)
            {
                var candidates = SkillTrackModel.Animation(clip).Where(p => p.Key <= frame && p.Value?.AnimationClip != null).OrderBy(p => p.Key).ToList();
                if (candidates.Count > 0)
                {
                    var entry = candidates[candidates.Count - 1];
                    var data = entry.Value;
                    double elapsed = Math.Max(0L, (long)frame - entry.Key) / (double)Math.Max(1, clip.FrameRote);
                    if (!SkillMediaTiming.Validate(data, clip.FrameRote, out string reason)) throw new ArgumentException(reason);
                    float time = SkillAnimationTiming.PreviewSourceTime(data, clip.FrameRote, elapsed);
                    var previous = candidates.Count > 1 ? candidates[candidates.Count - 2] : default;
                    int overlap = SkillAnimationTiming.OverlapFrames(previous.Key, previous.Value, entry.Key, data);
                    float weight = SkillAnimationTiming.BlendWeight(elapsed, overlap / (double)Math.Max(1, clip.FrameRote));
                    if (weight < 1)
                    {
                        float previousTime = SkillAnimationTiming.PreviewSourceTime(previous.Value, clip.FrameRote,
                            (frame - (double)previous.Key) / Math.Max(1, clip.FrameRote));
                        SampleBlend(previous.Value, previousTime, data, time, weight);
                    }
                    else Sample(data.AnimationClip, time, data.ApplyRootMotion);
                    Target.GetComponent<SkillPlayer>()?.SetMainWeaponHand(data.MainWeaponOnLeftHand);
                }
            }
            Target.transform.SetPositionAndRotation(OriginPosition + originMatrix.MultiplyVector(motion.Position), OriginRotation * motion.Rotation);
        }
        catch { Restore(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Restore(); }
        finally
        {
            if (blendGraph.IsValid()) blendGraph.Destroy();
            if (driver != null) Object.DestroyImmediate(driver);
        }
    }
}

}

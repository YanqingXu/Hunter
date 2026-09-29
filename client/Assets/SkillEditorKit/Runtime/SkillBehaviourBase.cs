using System;
using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>Per-cast hooks. Host-project gameplay belongs in a derived adapter, never in the editor or core.</summary>
    public class SkillBehaviourBase
    {
        protected ISkillActor owner;
        protected SkillPlayer skillPlayer;
        public SkillInstance CurrentInstance { get; private set; }
        public Action<SkillCustomEvent> CustomEvent;
        public virtual SkillBehaviourBase DeepCopy() => new SkillBehaviourBase();
        public virtual void Init(ISkillActor actor, SkillPlayer player) { owner = actor; skillPlayer = player; }
        internal void AttachInstance(SkillInstance instance) => CurrentInstance = instance;
        internal void DetachInstance(SkillInstance instance)
        { if (ReferenceEquals(CurrentInstance, instance)) CurrentInstance = null; }
        public virtual void OnTickSkill(int frame) { }
        public virtual void OnReleaseNewSkill() => OnClipEndOrReleaseNewSkill();
        public virtual void OnSkillClipEnd() => OnClipEndOrReleaseNewSkill();
        public virtual void OnClipEndOrReleaseNewSkill() { }
        public virtual SkillCustomEvent BeforeSkillCustomEvent(SkillCustomEvent data) => data;
        public virtual SkillAnimationEvent BeforeSkillAnimationEvent(SkillAnimationEvent data) => data;
        public virtual SkillAudioEvent BeforeSkillAudioEvent(SkillAudioEvent data) => data;
        public virtual SkillEffectEvent BeforeSkillEffectEvent(SkillEffectEvent data) => data;
        public virtual SkillAttackDetectionEvent BeforeSkillAttackDetectionEvent(SkillAttackDetectionEvent data) => data;
        public virtual SkillProjectileEvent BeforeSkillProjectileEvent(SkillProjectileEvent data) => data;
        // Legacy event identifiers such as AddBuff are forwarded as data; the host chooses their meaning.
        public virtual void AfterSkillCustomEvent(SkillCustomEvent data) => CustomEvent?.Invoke(data);
        public virtual void AfterSkillAnimationEvent(SkillAnimationEvent data) { }
        public virtual void AfterSkillAudioEvent(SkillAudioEvent data) { }
        public virtual void AfterSkillEffectEvent(SkillEffectEvent data) { }
        public virtual void AfterSkillAttackDetectionEvent(SkillAttackDetectionEvent data) { }
        public virtual void AfterSkillProjectileEvent(SkillProjectileEvent data) { }
        public virtual void OnProjectileStopped(SkillProjectileContext context, SkillProjectileStopReason reason, Vector3 position) { }
        public virtual void OnRootMotion(Vector3 position, Quaternion rotation) { }
        public virtual void OnAttackDetection(ISkillHitTarget target, SkillHitData data)
        {
            var instance = data.Instance ?? CurrentInstance;
            if (instance != null && ReferenceEquals(instance, CurrentInstance) && instance.TryRegisterHit(target, data))
                OnHitTarget(target, data);
        }
        public virtual void OnHitTarget(ISkillHitTarget target, SkillHitData data)
        {
            if (target == null || data.detectionEvent == null) return;
            PlayHitFeedback(data);
            target.BeHit(data);
        }
        public virtual void OnProjectileHitTarget(ISkillHitTarget target, SkillHitData data)
        {
            if (target == null || data.detectionEvent == null) return;
            SkillProjectile.HitFeedback(data);
            target.BeHit(data);
        }
        protected virtual void PlayHitFeedback(SkillHitData data)
        {
            var hit = data.detectionEvent?.AttackHitConfig;
            if (hit == null) return;
            try
            {
                var root = data.soure?.ModelTransform;
                var scene = root == null ? default : root.gameObject.scene;
                if (hit.HitAudioClip != null)
                    (data.Instance?.AudioService ?? SkillUnityAudio.Instance).PlayClip(hit.HitAudioClip, data.hitPoint, 1, 0, hit.HitAudioClip.length, scene);
                if (hit.HitEffectPrefab != null)
                {
                    var effect = UnityEngine.Object.Instantiate(hit.HitEffectPrefab, data.hitPoint, Quaternion.identity);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(effect, 3); else UnityEngine.Object.DestroyImmediate(effect);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}

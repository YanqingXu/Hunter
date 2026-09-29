using System;
using System.Collections.Generic;
using BigWorld.Map2D;
using BigWorld.Pooling.Unity;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.YouYou2D
{
    [DisallowMultipleComponent, AddComponentMenu("BigWorld/2D Framework/Skill Actor")]
    public sealed class SkillActor2D : MonoBehaviour, ISkillActor, ISkillTeam, IPooledLifecycle
    {
        public int Team = 1;
        [Min(1)] public float MaxHealth = 100;
        [Min(0)] public float BaseAttack = 10;
        [Min(0), Tooltip("接受一次伤害后的无敌时间；0 保持原有连续受击行为。")]
        public float InvulnerabilitySeconds;
        public Transform Model;
        public Transform ModelTransform => Model ? Model : transform;
        public int SkillTeamId => Team;
        public float Health => streamed ? (streamed.State == null ? 0 : streamed.Health) : health;
        public bool IsAlive => isActiveAndEnabled && Health > 0;
        public bool IsInvulnerable => Time.time < invulnerableUntil;
        public event Action<SkillHitData> Damaged;
        public event Action Died;
        public SkillPlayer Player { get; private set; }
        private MapStreamedEntity streamed;
        private float health;
        private bool initialized;
        private float invulnerableUntil;
        private readonly Dictionary<SkillClip, float> cooldowns = new Dictionary<SkillClip, float>();

        private void Awake() { InitializeOnce(); }
        public void InitializeOnce()
        {
            if (initialized) return;
            initialized = true;
            streamed = GetComponent<MapStreamedEntity>();
            health = Mathf.Max(1, MaxHealth);
            Player = GetComponent<SkillPlayer>();
            if (!Player) return; // Receiving hits does not require an animation/player stack.
            var animation = GetComponent<SkillAnimationPlayer>();
            if (animation) animation.Init();
            Player.Init(this, animation, ModelTransform);
            Player.StartPlaySkillBehaviour(new SkillBehaviourBase());
        }

        public bool TryPlay(SkillClip clip, float cooldownSeconds = .3f)
        {
            InitializeOnce();
            if (!IsAlive || !Player || !clip || clip.Space != SkillSpace.TwoD || Time.timeScale <= 0 ||
                (GameServices2D.Instance && GameServices2D.Instance.IsPaused)) return false;
            if (Player.IsPlaying || (cooldowns.TryGetValue(clip, out float until) && Time.time < until)) return false;
            Player.PlaySkillClip(clip);
            cooldowns[clip] = Time.time + Mathf.Max(0, cooldownSeconds);
            GameServices2D.Publish(GameEvents2D.SkillStarted, new SkillStarted2D(this, clip));
            return true;
        }

        public float GetAttackValue(SkillAttackDetectionEvent attack) => BaseAttack * (attack?.AttackHitConfig?.AttackMultiply ?? 1);

        public void BeHit(SkillHitData hit)
        {
            InitializeOnce();
            if (!IsAlive || IsInvulnerable || Time.timeScale <= 0 ||
                float.IsNaN(hit.attackValue) || float.IsInfinity(hit.attackValue) || hit.attackValue <= 0) return;
            float before = Health;
            var state = streamed ? streamed.State : null;
            string id = state?.Id;
            if (streamed) streamed.TakeDamage(hit.attackValue);
            else health = Mathf.Max(0, health - hit.attackValue);
            // A destructible map tile may recycle this component synchronously inside TakeDamage.
            float after = state != null ? state.Health : health;
            if (after <= 0 && Player) Player.StopSkillClip(false);
            if (after < before)
            {
                // A streamed actor may already have been returned. Never install transient state on that pooled instance.
                if (isActiveAndEnabled) GrantInvulnerability(InvulnerabilitySeconds);
                GameServices2D.Publish(GameEvents2D.ActorDamaged, new ActorDamage2D(gameObject, id, before - after, after));
                Damaged?.Invoke(hit);
                if (after <= 0) Died?.Invoke();
            }
        }

        /// <summary>Restores a non-streamed player; streamed actors are restored by the map's spawn state.</summary>
        public void RestoreHealth(float fraction = 1, float invulnerableSeconds = 0)
        {
            InitializeOnce();
            if (streamed) return;
            if (Player) Player.StopSkillClip(false);
            cooldowns.Clear();
            health = Mathf.Max(1, MaxHealth) * (float.IsNaN(fraction) ? 1 : Mathf.Clamp01(fraction));
            invulnerableUntil = 0;
            GrantInvulnerability(invulnerableSeconds);
        }

        public void GrantInvulnerability(float seconds)
        {
            if (!float.IsNaN(seconds) && !float.IsInfinity(seconds))
                invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0, seconds));
        }

        public void OnRelease()
        {
            if (Player) { Player.StopSkillClip(false); Player.SetAimContext(null); }
            cooldowns.Clear();
            health = Mathf.Max(1, MaxHealth);
            invulnerableUntil = 0;
        }

        private void OnDisable() { if (Player) Player.StopSkillClip(false); }
    }
}

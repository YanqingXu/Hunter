using System.Collections.Generic;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Scene-owned, telegraphed boss abilities. HuntEnemy owns detection, health thresholds and lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class HunterBossCombat2D : MonoBehaviour
    {
        public HuntEnemy2D Owner { get; private set; }
        public HunterBossAbilityClock Clock { get; } = new HunterBossAbilityClock();
        public bool IsEnraged => rageRemaining > 0;
        public float RageRemaining => rageRemaining;
        public bool IsRushing => rushRemaining > 0;
        public int Facing { get; private set; } = -1;
        public Vector2 AimDirection { get; private set; } = Vector2.left;
        public int ActiveProjectiles => bolts.Count;
        public LineRenderer Warning { get; private set; }
        private readonly List<HunterBossBolt2D> bolts = new List<HunterBossBolt2D>();
        private Rigidbody2D body;
        private BoxCollider2D shape;
        private Material material;
        private float rageRemaining, rushRemaining, flashRemaining;
        private HunterBossAbility flashAbility;
        private bool rushHit;
        private Vector2 Center => shape.bounds.center;

        public void Configure(HuntEnemy2D owner, SpriteRenderer visual)
        {
            Owner = owner; body = GetComponent<Rigidbody2D>(); shape = GetComponent<BoxCollider2D>(); material = visual.sharedMaterial;
            Warning = new GameObject("Boss Ability Warning").AddComponent<LineRenderer>(); Warning.transform.SetParent(transform, false);
            Warning.sharedMaterial = material; Warning.useWorldSpace = true; Warning.sortingOrder = 28;
            Warning.startWidth = Warning.endWidth = .07f; Warning.enabled = false;
        }
        public void TriggerRage()
        {
            CancelAttacks(); rageRemaining = 3;
            AimAtPlayer(); Clock.TriggerRagePulse(false);
        }
        private void AimAtPlayer()
        {
            var level = LevelSession2D.Instance;
            if (!level || !level.Player) return;
            Vector2 delta = (Vector2)level.Player.GetComponent<Collider2D>().bounds.center - Center;
            if (Mathf.Abs(delta.x) > .03f) Facing = delta.x < 0 ? -1 : 1;
            AimDirection = delta.sqrMagnitude > .001f ? delta.normalized : Vector2.right * Facing;
        }
        public void Tick(bool visible)
        {
            var level = LevelSession2D.Instance;
            if (!isActiveAndEnabled || !level || !level.IsGameplayActive) return;
            float dt = Time.fixedDeltaTime;
            rageRemaining = Mathf.Max(0, rageRemaining - dt); flashRemaining = Mathf.Max(0, flashRemaining - dt);
            if (!visible) { CancelAttacks(); Clock.Tick(dt, IsEnraged, false); Stop(); return; }
            if (!IsEnraged && IsRushing) { rushRemaining = 0; Stop(); }
            var effect = Clock.Tick(dt, IsEnraged, false);
            if (effect != HunterBossAbility.None) Execute(effect);
            if (IsRushing) { Rush(dt); DrawWarning(); return; }
            if (Clock.IsWinding) { Stop(); DrawWarning(); return; }
            Vector2 delta = (Vector2)level.Player.transform.position - body.position;
            AimAtPlayer();
            bool close = Mathf.Abs(delta.x) <= 1.6f && Mathf.Abs(delta.y) < 1;
            var next = IsEnraged ? (close ? HunterBossAbility.RagePulse : HunterBossAbility.Rush) :
                close ? HunterBossAbility.Slash : HunterBossAbility.Bolt;
            bool canCast = close || (delta.magnitude <= 8 && (IsEnraged || Mathf.Abs(delta.x) >= 2.5f));
            if (canCast && Clock.TryBegin(next, IsEnraged, false)) Stop();
            else if (close) Stop();
            else Move(IsEnraged ? 3.5f : 2, dt);
            DrawWarning();
        }
        private void Execute(HunterBossAbility ability)
        {
            switch (ability)
            {
                case HunterBossAbility.Slash: Strike(1.6f, 18, true); break;
                case HunterBossAbility.Bolt:
                    bolts.Add(HunterBossBolt2D.Create(this, Center, AimDirection, material)); break;
                case HunterBossAbility.RagePulse: Strike(2.5f, 25, false); break;
                case HunterBossAbility.Rush: rushRemaining = .45f; rushHit = false; break;
            }
            flashAbility = ability; flashRemaining = .15f;
        }
        private void Strike(float range, float damage, bool frontal)
        {
            var level = LevelSession2D.Instance; var target = level.Player.GetComponent<SkillActor2D>();
            var collider = level.Player.GetComponent<Collider2D>();
            Vector2 point = collider.ClosestPoint(Center), delta = point - Center;
            if (delta.sqrMagnitude <= range * range && (!frontal || delta.x * Facing >= 0) && !SolidBetween(Center, point))
                target.BeHit(new SkillHitData { soure = Owner.Actor, attackValue = damage, hitPoint = point });
        }
        private void Rush(float dt)
        {
            if (!Move(8, dt)) { rushRemaining = 0; Stop(); return; }
            if (!rushHit)
            {
                var player = LevelSession2D.Instance.Player;
                var targetShape = player.GetComponent<Collider2D>();
                Vector2 point = targetShape.ClosestPoint(Center), delta = point - Center;
                // One target check for the swept next body step; a player with several colliders is still hit once.
                if (delta.x * Facing >= -.05f && Mathf.Abs(delta.x) <= shape.size.x * .5f + 8 * dt + .1f &&
                    Mathf.Abs(delta.y) <= shape.size.y * .5f && !SolidBetween(Center, point))
                {
                    rushHit = true;
                    player.GetComponent<SkillActor2D>().BeHit(new SkillHitData { soure = Owner.Actor, attackValue = 22, hitPoint = point });
                }
            }
            rushRemaining = Mathf.Max(0, rushRemaining - dt);
            if (!IsRushing) Stop();
        }
        private bool Move(float speed, float dt)
        {
            Vector2 edge = new Vector2(shape.bounds.center.x + Facing * (shape.bounds.extents.x + speed * dt + .12f), shape.bounds.min.y + .2f);
            bool floor = false;
            foreach (var hit in Physics2D.RaycastAll(edge, Vector2.down, .65f, 1))
                if (hit.collider && !hit.collider.isTrigger) { floor = true; break; }
            bool blocked = !floor || !TerrainReady(edge) || !TerrainReady(edge + Vector2.up * 1.4f);
            foreach (var hit in Physics2D.BoxCastAll(Center, shape.size * .95f, 0, Vector2.right * Facing, speed * dt + .08f))
                if (hit.collider && !hit.collider.isTrigger && !hit.collider.GetComponentInParent<SkillActor2D>()) { blocked = true; break; }
            body.velocity = new Vector2(blocked ? 0 : Facing * speed, body.velocity.y);
            return !blocked;
        }
        internal static bool TerrainReady(Vector2 point)
        {
            var level = LevelSession2D.Instance;
            if (!level || !level.World || !level.World.IsReady) return false;
            var cell = level.World.Map.WorldToCell(point); return level.World.Map.IsCellLoaded(cell.x, cell.y);
        }
        internal static bool SolidBetween(Vector2 from, Vector2 to)
        {
            foreach (var hit in Physics2D.LinecastAll(from, to))
                if (hit.collider && !hit.collider.isTrigger && !hit.collider.GetComponentInParent<SkillActor2D>()) return true;
            return false;
        }
        private void Stop() { if (body) body.velocity = new Vector2(0, body.velocity.y); }
        public void CancelAttacks()
        {
            Clock.Cancel(); rushRemaining = 0; flashRemaining = 0;
            for (int i = bolts.Count - 1; i >= 0; i--) if (bolts[i]) bolts[i].Cancel();
            bolts.Clear(); if (Warning) Warning.enabled = false;
            Stop();
        }
        internal void Release(HunterBossBolt2D bolt) { bolts.Remove(bolt); }
        private void DrawWarning()
        {
            var ability = IsRushing ? HunterBossAbility.Rush : Clock.IsWinding ? Clock.Active : flashRemaining > 0 ? flashAbility : HunterBossAbility.None;
            Warning.enabled = ability != HunterBossAbility.None;
            if (!Warning.enabled) return;
            var color = Clock.IsWinding ? new Color(1, .8f, .15f) : new Color(1, .2f, .15f);
            Warning.startColor = Warning.endColor = color; Warning.loop = false;
            if (ability == HunterBossAbility.RagePulse || ability == HunterBossAbility.Slash)
            {
                int points = ability == HunterBossAbility.RagePulse ? 33 : 17; Warning.positionCount = points;
                float radius = ability == HunterBossAbility.RagePulse ? 2.5f : 1.6f;
                for (int i = 0; i < points; i++)
                {
                    float angle = ability == HunterBossAbility.RagePulse ? i * Mathf.PI * 2 / (points - 1) : -Mathf.PI * .5f + i * Mathf.PI / (points - 1);
                    Warning.SetPosition(i, Center + new Vector2(Mathf.Cos(angle) * Facing, Mathf.Sin(angle)) * radius);
                }
            }
            else if (ability == HunterBossAbility.Bolt)
            { Warning.positionCount = 2; Warning.SetPosition(0, Center); Warning.SetPosition(1, Center + AimDirection * 8); }
            else
            {
                Warning.positionCount = 5; var a = Center + Vector2.down * .65f; var b = a + Vector2.right * Facing * 3.6f;
                Warning.SetPosition(0, a); Warning.SetPosition(1, b); Warning.SetPosition(2, b + Vector2.up * 1.3f);
                Warning.SetPosition(3, a + Vector2.up * 1.3f); Warning.SetPosition(4, a);
            }
        }
        private void OnDisable() { CancelAttacks(); }
        private void OnDestroy() { CancelAttacks(); }
    }
}

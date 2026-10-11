using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Scene-owned elite or summon, with no dependency on the streamed guard pool.</summary>
    public sealed class HunterElite2D : MonoBehaviour
    {
        public HunterEliteKind Kind { get; private set; }
        public bool IsSummon { get; private set; }
        public int Wave { get; private set; }
        public SkillActor2D Actor { get; private set; }
        public HunterElite2D Owner { get; private set; }
        public HunterElite2D Summon { get; private set; }
        public HunterSummonCycle Cycle { get; private set; }
        public float DetectionDistance, PatrolSpeed, ChaseSpeed, AttackDamage, AttackRange, AttackInterval, WindupSeconds;
        public float PatrolDistance = 2, AlertForgetSeconds = 1;
        public bool IsAlerted { get; private set; }
        public float AlertUnseenSeconds => alert.UnseenSeconds;
        public bool IsWinding => winding;
        public bool CanBasicAttack => IsSummon || HunterEliteRules.CanBasicAttack(Kind);
        public float WindupRemaining => windupRemaining;
        public bool DetectsPlayer
        {
            get
            {
                var level = director ? director.Level : null;
                if (!isActiveAndEnabled || !Actor || !Actor.IsAlive || !level || !level.Player || level.IsPlayerUndetected) return false;
                var playerActor = level.Player.GetComponent<SkillActor2D>();
                Vector2 delta = level.Player.GetComponent<Rigidbody2D>().position - body.position;
                return playerActor.IsAlive && delta.sqrMagnitude <= DetectionDistance * DetectionDistance && Mathf.Abs(delta.y) < 3.5f &&
                    !Blocked(shape.bounds.center, level.Player.GetComponent<Rigidbody2D>().position + Vector2.up * .7f);
            }
        }
        private HunterEliteDirector2D director;
        private Rigidbody2D body;
        private BoxCollider2D shape;
        private SpriteRenderer visual, badge;
        private Vector2 spawnPosition;
        private float windupRemaining, cooldown;
        private HunterAlertMemory alert = new HunterAlertMemory(1);
        private int direction = -1;
        private bool winding;

        internal void Configure(HunterEliteDirector2D manager, HunterEliteKind kind, int wave, HunterElite2D owner = null)
        {
            director = manager; Kind = kind; Wave = wave; Owner = owner; IsSummon = owner != null; spawnPosition = transform.position;
            alert = new HunterAlertMemory(AlertForgetSeconds);
            bool armored = !IsSummon && kind == HunterEliteKind.Armored;
            DetectionDistance = (IsSummon ? 8 : armored ? 9 : 14) * (wave == 2 ? 1.4f : 1);
            PatrolSpeed = IsSummon ? 1.5f : armored ? 1.3f : .8f; ChaseSpeed = IsSummon ? 3 : armored ? 2.4f : 1.4f;
            AttackDamage = IsSummon ? 10 : armored ? 18 : 0; AttackRange = IsSummon ? 1.2f : 1.5f;
            AttackInterval = IsSummon ? 1 : 1.3f; WindupSeconds = IsSummon ? .35f : armored ? .6f : .75f;
            if (!IsSummon && kind == HunterEliteKind.Summoner) Cycle = new HunterSummonCycle(manager.AbandonSeconds, manager.SummonCooldown);
            gameObject.layer = 30;
            body = gameObject.AddComponent<Rigidbody2D>(); body.gravityScale = 2.5f; body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            shape = gameObject.AddComponent<BoxCollider2D>(); shape.size = new Vector2(IsSummon ? .55f : .75f, 1.45f); shape.offset = Vector2.up * .75f;
            shape.sharedMaterial = manager.Level.Hunt.EnemyMaterial;
            visual = new GameObject("Elite Visual").AddComponent<SpriteRenderer>(); visual.transform.SetParent(transform, false);
            visual.sprite = manager.Level.Hunt.EnemySprite; visual.sortingOrder = 25;
            visual.transform.localScale = IsSummon ? new Vector3(.8f, 1, 1) : armored ? new Vector3(1.15f, 1, 1) : Vector3.one;
            badge = new GameObject(armored ? "Armor Badge" : "Summon Sigil").AddComponent<SpriteRenderer>(); badge.transform.SetParent(transform, false);
            badge.sprite = manager.Level.Hunt.MarkerSprite; badge.sortingOrder = 26;
            badge.transform.localPosition = new Vector2(0, 1.7f); badge.transform.localScale = new Vector3(armored ? .55f : .25f, .12f, 1);
            Actor = gameObject.AddComponent<SkillActor2D>(); Actor.Team = 2; Actor.MaxHealth = IsSummon ? 35 : armored ? 120 : 40;
            Actor.IncomingDamageMultiplier = IsSummon ? 1 : HunterEliteRules.IncomingDamage(kind, 1);
            Actor.RestoreHealth(); Actor.Died += OnDied; UpdateVisual();
        }
        private void FixedUpdate()
        {
            if (!Actor || !Actor.IsAlive || !director || !director.Level.IsGameplayActive)
            { if (body) body.velocity = Vector2.zero; return; }
            if (body.position.y < director.Level.FallDeathY) { body.position = spawnPosition; transform.position = spawnPosition; body.velocity = Vector2.zero; }
            bool detects = DetectsPlayer;
            if (detects) director.Level.Player.NotifyCombat();
            IsAlerted = alert.Tick(detects, Time.fixedDeltaTime, false);
            if (director.Level.IsPlayerUndetected) ForgetPlayer();
            if (!detects) winding = false;
            cooldown = Mathf.Max(0, cooldown - Time.fixedDeltaTime);
            if (Cycle != null)
            {
                bool suspendedSummon = Summon && !Summon.gameObject.activeInHierarchy;
                if (Cycle.Tick(Time.fixedDeltaTime, detects, Summon && Summon.DetectsPlayer, suspendedSummon)) RemoveSummon();
                if (winding)
                {
                    Stop();
                    if (!detects) winding = false;
                    else if ((windupRemaining -= Time.fixedDeltaTime) <= 0)
                    {
                        winding = false;
                        if (TrySummon()) cooldown = 0; else cooldown = .5f;
                    }
                }
                else if (detects && Cycle.State == HunterSummonState.Ready && cooldown <= 0)
                { winding = true; windupRemaining = WindupSeconds; Stop(); }
                else if (detects && InStrikeRange(false)) { FacePlayer(); Stop(); }
                else Move(detects, true);
            }
            else if (winding)
            {
                Stop(); windupRemaining -= Time.fixedDeltaTime;
                if (windupRemaining <= 0)
                {
                    winding = false;
                    if (detects && InStrikeRange()) director.Level.Player.GetComponent<SkillActor2D>().BeHit(new SkillHitData {
                        soure = Actor, attackValue = AttackDamage, hitPoint = director.Level.Player.transform.position });
                    cooldown = AttackInterval;
                }
            }
            else if (detects && InStrikeRange(false))
            {
                FacePlayer(); Stop();
                if (cooldown <= 0) { winding = true; windupRemaining = WindupSeconds; }
            }
            else Move(detects, true);
            UpdateVisual();
        }
        private bool InStrikeRange(bool requireFacing = true)
        {
            Vector2 delta = director.Level.Player.GetComponent<Rigidbody2D>().position - body.position;
            return Mathf.Abs(delta.x) <= AttackRange && Mathf.Abs(delta.y) < .9f && (!requireFacing || delta.x * direction >= 0);
        }
        private void FacePlayer() { float dx = director.Level.Player.GetComponent<Rigidbody2D>().position.x - body.position.x; if (Mathf.Abs(dx) > .03f) direction = dx < 0 ? -1 : 1; }
        private void Move(bool detects, bool chase)
        {
            if (detects) { FacePlayer(); if (!chase) { Stop(); return; } }
            else if (IsAlerted) { Stop(); return; }
            else if (body.position.x >= spawnPosition.x + PatrolDistance) direction = -1;
            else if (body.position.x <= spawnPosition.x - PatrolDistance) direction = 1;
            float speed = detects ? ChaseSpeed : PatrolSpeed;
            Vector2 edge = new Vector2(shape.bounds.center.x + direction * (.7f + speed * Time.fixedDeltaTime), shape.bounds.min.y + .2f);
            bool floor = Physics2D.Raycast(edge, Vector2.down, .65f, 1);
            bool wall = Blocked(shape.bounds.center, (Vector2)shape.bounds.center + Vector2.right * direction * .8f);
            body.velocity = new Vector2(floor && !wall ? speed * direction : 0, body.velocity.y);
        }
        private bool TrySummon()
        {
            if (Summon || Cycle.State != HunterSummonState.Ready) return false;
            for (int side = 0; side < 2; side++)
            {
                Vector2 point = body.position + Vector2.right * (side == 0 ? direction : -direction) * 1.25f;
                if (!director.TerrainReady(point)) continue;
                var floor = Physics2D.Raycast(point + Vector2.up * .5f, Vector2.down, 1, 1);
                if (!floor.collider || floor.collider.isTrigger) continue;
                point.y = floor.point.y + .05f; bool clear = true;
                foreach (var collider in Physics2D.OverlapBoxAll(point + Vector2.up * .75f, new Vector2(.55f, 1.4f), 0))
                    if (collider && !collider.isTrigger) { clear = false; break; }
                if (!clear) continue;
                var created = director.Create(kind: Kind, wave: Wave, position: point, owner: this);
                if (!Cycle.TrySummon(false)) { created.gameObject.SetActive(false); Destroy(created.gameObject); return false; }
                Summon = created; Summon.Actor.Died += OnSummonDied; return true;
            }
            return false;
        }
        private bool Blocked(Vector2 from, Vector2 to)
        {
            foreach (var hit in Physics2D.LinecastAll(from, to, 1)) if (hit.collider && !hit.collider.isTrigger) return true;
            return false;
        }
        private void Stop() { body.velocity = new Vector2(0, body.velocity.y); }
        private void UpdateVisual()
        {
            if (!visual) return;
            visual.flipX = direction < 0;
            visual.color = Actor.Health <= 0 ? new Color(.22f, .24f, .26f) : winding ? Color.yellow :
                IsSummon ? new Color(.45f, 1, .65f) : Kind == HunterEliteKind.Armored ? new Color(.4f, .68f, 1) : new Color(.2f, .85f, .8f);
            badge.color = visual.color;
        }
        private void OnSummonDied() { Cycle.SummonDied(); RemoveSummon(); }
        internal void RemoveSummon()
        {
            var old = Summon; Summon = null;
            if (!old) return;
            if (old.Actor) old.Actor.Died -= OnSummonDied;
            old.gameObject.SetActive(false); Destroy(old.gameObject);
        }
        private void OnDied()
        {
            ForgetPlayer(); body.velocity = Vector2.zero; body.simulated = false; shape.enabled = false;
            if (Cycle != null) { Cycle.OwnerDied(); RemoveSummon(); }
            UpdateVisual();
        }
        public void ForgetPlayer() { alert.Forget(); IsAlerted = winding = false; }
        private void OnDisable() { ForgetPlayer(); if (body) body.velocity = Vector2.zero; }
        private void OnDestroy()
        {
            if (Owner && Owner.Summon == this) { Owner.Summon = null; Owner.Cycle.SummonDied(); }
            RemoveSummon(); if (Actor) Actor.Died -= OnDied;
        }
    }
}

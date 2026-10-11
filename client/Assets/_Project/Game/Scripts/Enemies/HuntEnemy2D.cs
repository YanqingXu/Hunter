using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Scene-owned boss/reinforcement. Its lifetime is owned by the hunt, independent of first-wave map spawns.</summary>
    public sealed class HuntEnemy2D : MonoBehaviour
    {
        public bool IsBoss;
        public float DetectionDistance = 14;
        [Min(0)] public float PatrolSpeed = 2, ChaseSpeed = 3, AlertForgetSeconds = 1;
        [Min(.1f)] public float PatrolDistance = 3;
        public SkillActor2D Actor { get; private set; }
        public HunterBossCombat2D BossCombat { get; private set; }
        public bool IsEnraged => IsBoss && BossCombat && BossCombat.IsEnraged;
        public bool IsAlerted { get; private set; }
        public bool DetectsPlayer { get; private set; }
        public bool IsWinding => winding;
        public float AlertUnseenSeconds => alert.UnseenSeconds;
        private Rigidbody2D body;
        private BoxCollider2D shape;
        private SpriteRenderer visual;
        private float windupUntil, nextAttack;
        private int threshold = 2, direction = -1;
        private bool winding;
        private Vector2 spawnPosition;
        private HunterAlertMemory alert = new HunterAlertMemory(1);
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        public void Configure(bool boss, Sprite sprite, PhysicsMaterial2D material)
        {
            IsBoss = boss; DetectionDistance = boss ? 10 : 14;
            alert = new HunterAlertMemory(AlertForgetSeconds);
            spawnPosition = transform.position;
            gameObject.layer = 30;
            body = gameObject.AddComponent<Rigidbody2D>(); body.gravityScale = 2.5f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            shape = gameObject.AddComponent<BoxCollider2D>(); shape.size = new Vector2(boss ? .95f : .65f, 1.45f);
            shape.offset = new Vector2(0, .75f); shape.sharedMaterial = material;
            visual = new GameObject("Visual").AddComponent<SpriteRenderer>(); visual.transform.SetParent(transform, false);
            visual.sprite = sprite; visual.sortingOrder = 25;
            visual.transform.localScale = boss ? new Vector3(1.25f, 1.25f, 1) : Vector3.one;
            Actor = gameObject.AddComponent<SkillActor2D>(); Actor.Team = 2; Actor.MaxHealth = boss ? 150 : 50;
            Actor.RestoreHealth(); Actor.Damaged += OnDamaged; Actor.Died += OnDied;
            if (IsBoss) { BossCombat = gameObject.AddComponent<HunterBossCombat2D>(); BossCombat.Configure(this, visual); }
        }
        private void OnDamaged(SkillHitData hit)
        {
            if (!IsBoss || !Actor.IsAlive) return;
            bool crossed = false;
            while (threshold > 0 && Actor.Health <= Actor.MaxHealth * threshold / 3f) { threshold--; crossed = true; }
            if (!crossed) return;
            BossCombat.TriggerRage();
        }
        private void FixedUpdate()
        {
            var level = LevelSession2D.Instance;
            if (!Actor || !Actor.IsAlive || !level || !level.IsGameplayActive || !level.Player)
            { ForgetPlayer(); if (body) body.velocity = Vector2.zero; return; }
            if (level.IsPlayerUndetected)
            {
                ForgetPlayer();
                // Detection grace suppresses attacks, but the running world's rage duration still elapses.
                if (BossCombat) BossCombat.Tick(false);
                return;
            }
            var player = level.Player;
            Vector2 delta = player.transform.position - transform.position;
            bool visible = delta.sqrMagnitude <= DetectionDistance * DetectionDistance && !Blocked(shape.bounds.center, player.transform.position + Vector3.up * .7f);
            DetectsPlayer = visible;
            IsAlerted = IsBoss ? visible : alert.Tick(visible, Time.fixedDeltaTime, false);
            if (visible) player.NotifyCombat();
            if (IsBoss)
            {
                BossCombat.Tick(visible); direction = BossCombat.Facing;
                visual.flipX = direction < 0;
                visual.color = BossCombat.Clock.IsWinding ? Color.yellow : IsEnraged ? new Color(1, .15f, .1f) : new Color(.8f, .3f, 1);
                return;
            }
            const float range = 1.3f;
            if (!visible)
            {
                winding = false;
                if (IsAlerted) body.velocity = new Vector2(0, body.velocity.y);
                else
                {
                    if (body.position.x >= spawnPosition.x + PatrolDistance) direction = -1;
                    else if (body.position.x <= spawnPosition.x - PatrolDistance) direction = 1;
                    MoveIfSafe(PatrolSpeed, true);
                }
            }
            else if (winding)
            {
                body.velocity = new Vector2(0, body.velocity.y);
                if (Time.time >= windupUntil)
                {
                    winding = false;
                    if (Mathf.Abs(delta.x) <= range && Mathf.Abs(delta.y) < 1 && delta.x * direction >= 0)
                        player.GetComponent<SkillActor2D>().BeHit(new SkillHitData { soure = Actor, attackValue = 12, hitPoint = player.transform.position });
                }
            }
            else if (visible)
            {
                if (Mathf.Abs(delta.x) > .03f) direction = delta.x < 0 ? -1 : 1;
                if (Mathf.Abs(delta.x) <= range && Mathf.Abs(delta.y) < 1)
                {
                    body.velocity = new Vector2(0, body.velocity.y);
                    if (Time.time >= nextAttack) { winding = true; windupUntil = Time.time + .5f; nextAttack = windupUntil + 1.2f; }
                }
                else
                {
                    MoveIfSafe(ChaseSpeed, false);
                }
            }
            else body.velocity = new Vector2(0, body.velocity.y);
            visual.flipX = direction < 0;
            visual.color = winding ? Color.yellow : IsAlerted && !visible ? new Color(1, .7f, .25f) : new Color(1, .45f, .25f);
        }
        private void MoveIfSafe(float speed, bool turnAtObstacle)
        {
            speed = Mathf.Max(0, speed);
            float reach = shape.bounds.extents.x + .2f + speed * Time.fixedDeltaTime;
            var edge = new Vector2(shape.bounds.center.x + direction * reach, shape.bounds.min.y + .2f);
            bool floor = false;
            foreach (var hit in Physics2D.RaycastAll(edge, Vector2.down, .65f, 1))
                if (hit.collider && !hit.collider.isTrigger) { floor = true; break; }
            bool wall = Blocked(shape.bounds.center, (Vector2)shape.bounds.center + Vector2.right * direction * reach) ||
                Blocked(new Vector2(shape.bounds.center.x, shape.bounds.min.y + .22f), new Vector2(shape.bounds.center.x + direction * reach, shape.bounds.min.y + .22f));
            body.velocity = new Vector2(floor && !wall ? direction * speed : 0, body.velocity.y);
            if ((!floor || wall) && turnAtObstacle) direction = -direction;
        }
        private bool Blocked(Vector2 from, Vector2 to)
        {
            int count = Physics2D.LinecastNonAlloc(from, to, hits, 1);
            for (int i = 0; i < count; i++) if (hits[i].collider && !hits[i].collider.isTrigger) return true;
            return false;
        }
        public void ForgetPlayer()
        {
            winding = false; IsAlerted = false; DetectsPlayer = false; alert.Forget();
            if (BossCombat) BossCombat.CancelAttacks();
            if (body) body.velocity = new Vector2(0, body.velocity.y);
            if (visual && Actor && Actor.IsAlive) visual.color = IsEnraged ? new Color(1, .15f, .1f) : IsBoss ? new Color(.8f, .3f, 1) : new Color(1, .45f, .25f);
        }
        private void OnDied() { ForgetPlayer(); body.velocity = Vector2.zero; body.simulated = false; shape.enabled = false; visual.color = new Color(.25f, .25f, .3f); }
        public void ReturnToSpawnIfFallen(float deathY)
        {
            if (!Actor || Actor.Health <= 0 || transform.position.y >= deathY) return;
            body.position = spawnPosition; transform.position = spawnPosition; body.velocity = Vector2.zero; winding = false;
            ForgetPlayer();
        }
        private void OnDisable() { ForgetPlayer(); if (body) body.velocity = Vector2.zero; }
        private void OnDestroy() { if (Actor) { Actor.Damaged -= OnDamaged; Actor.Died -= OnDied; } }
    }
}

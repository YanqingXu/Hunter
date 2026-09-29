using BigWorld.Map2D;
using BigWorld.Pooling.Unity;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    public enum PatrolEnemyState { Patrol, Chase, Windup, Recover, Dead }

    /// <summary>A grounded melee enemy whose identity and health belong to the map streamer.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SkillActor2D))]
    [RequireComponent(typeof(SkillFacing2D), typeof(MapStreamedEntity))]
    [AddComponentMenu("BigWorld/Gameplay/Patrol Enemy 2D")]
    public sealed class PatrolEnemy2D : MonoBehaviour, IPooledLifecycle
    {
        [Min(0)] public float MoveSpeed = 2;
        [Min(0)] public float ChaseSpeed = 3;
        [Min(.1f)] public float PatrolDistance = 3;
        [Min(.1f)] public float DetectionDistance = 7;
        [Min(.1f)] public float AttackRange = 1.3f;
        [Min(0)] public float AttackDamage = 12;
        [Min(.1f)] public float AttackCooldown = 1.2f;
        [Min(.05f)] public float AttackWindup = .4f;
        [Min(.1f)] public float AttackHeightTolerance = .85f;
        public LayerMask GroundLayers = 1;

        public PatrolEnemyState State { get; private set; } = PatrolEnemyState.Dead;
        public Transform Target { get; private set; }

        private Rigidbody2D body;
        private BoxCollider2D shape;
        private SkillActor2D actor;
        private SkillFacing2D facing;
        private MapStreamedEntity streamed;
        private SpriteRenderer visual;
        private Color originalColor;
        private Vector2 patrolCenter;
        private int direction = 1;
        private float stateUntil, nextAttackAt, strikeFlashUntil;
        private bool initialized;
        private readonly RaycastHit2D[] rayHits = new RaycastHit2D[16];

        private void Awake() { InitializeOnce(); }

        public void InitializeOnce()
        {
            if (initialized) return;
            initialized = true;
            body = GetComponent<Rigidbody2D>();
            shape = GetComponent<BoxCollider2D>();
            actor = GetComponent<SkillActor2D>();
            facing = GetComponent<SkillFacing2D>();
            streamed = GetComponent<MapStreamedEntity>();
            visual = facing.Sprite ? facing.Sprite : GetComponentInChildren<SpriteRenderer>();
            if (visual) originalColor = visual.color;
        }

        private void OnEnable()
        {
            InitializeOnce();
            ClearRuntimeState();
            patrolCenter = transform.position;
            direction = facing.Direction < 0 ? -1 : 1;
            facing.SetFacing(direction);
            State = PatrolEnemyState.Patrol;
        }

        private void FixedUpdate()
        {
            var level = LevelSession2D.Instance;
            if (!actor.IsAlive)
            {
                StopHorizontal();
                Target = null;
                streamed.CombatPinned = false;
                State = PatrolEnemyState.Dead;
                RestoreColor();
                return;
            }
            if (!level || !level.IsGameplayActive || !level.Player)
            {
                Suspend();
                return;
            }

            var player = level.Player.transform;
            var playerActor = player.GetComponent<SkillActor2D>();
            if (!playerActor || !playerActor.IsAlive)
            {
                Suspend();
                return;
            }

            bool canSeePlayer = CanDetect(player);
            Target = canSeePlayer ? player : null;
            streamed.CombatPinned = canSeePlayer && State != PatrolEnemyState.Patrol;

            // A strike keeps the facing chosen at the start of its visible windup.
            // At impact the current target position is checked again, so stepping away or
            // crossing behind the enemy avoids the blow.
            if (State == PatrolEnemyState.Windup)
            {
                StopHorizontal();
                if (Time.time >= stateUntil)
                {
                    if (Target && CanStrike(Target))
                    {
                        playerActor.BeHit(new SkillHitData
                        {
                            soure = actor,
                            attackValue = Mathf.Max(0, AttackDamage),
                            hitPoint = Target.position + Vector3.up * .7f
                        });
                    }
                    State = PatrolEnemyState.Recover;
                    stateUntil = Time.time + .3f;
                    strikeFlashUntil = Time.time + .12f;
                }
                UpdateColor();
                return;
            }
            if (State == PatrolEnemyState.Recover && Time.time < stateUntil)
            {
                StopHorizontal();
                UpdateColor();
                return;
            }

            if (canSeePlayer)
            {
                State = PatrolEnemyState.Chase;
                streamed.CombatPinned = true;
                float delta = player.position.x - transform.position.x;
                if (Mathf.Abs(delta) > .03f) SetDirection(delta < 0 ? -1 : 1);
                if (CanStrike(player))
                {
                    StopHorizontal();
                    if (Time.time >= nextAttackAt)
                    {
                        State = PatrolEnemyState.Windup;
                        stateUntil = Time.time + Mathf.Max(.05f, AttackWindup);
                        nextAttackAt = stateUntil + Mathf.Max(.1f, AttackCooldown);
                    }
                }
                else MoveIfSafe(Mathf.Max(0, ChaseSpeed), false);
            }
            else
            {
                State = PatrolEnemyState.Patrol;
                streamed.CombatPinned = false;
                if (body.position.x >= patrolCenter.x + Mathf.Max(.1f, PatrolDistance)) SetDirection(-1);
                else if (body.position.x <= patrolCenter.x - Mathf.Max(.1f, PatrolDistance)) SetDirection(1);
                MoveIfSafe(Mathf.Max(0, MoveSpeed), true);
            }
            UpdateColor();
        }

        private bool CanDetect(Transform candidate)
        {
            Vector2 delta = candidate.position - transform.position;
            float distance = Mathf.Max(.1f, DetectionDistance);
            return delta.sqrMagnitude <= distance * distance && Mathf.Abs(delta.y) < 3.5f &&
                   !TerrainBetween(ChestPosition(), candidate.position + Vector3.up * .7f);
        }

        private bool CanStrike(Transform candidate)
        {
            Vector2 delta = candidate.position - transform.position;
            return delta.x * direction >= -.03f && Mathf.Abs(delta.x) <= Mathf.Max(.1f, AttackRange) &&
                   Mathf.Abs(delta.y) <= Mathf.Max(.1f, AttackHeightTolerance) &&
                   !TerrainBetween(ChestPosition(), candidate.position + Vector3.up * .7f);
        }

        private void MoveIfSafe(float speed, bool turnAtObstacle)
        {
            Bounds bounds = shape.bounds;
            float reach = bounds.extents.x + .2f + speed * Time.fixedDeltaTime;
            Vector2 wallOrigin = bounds.center;
            Vector2 floorOrigin = new Vector2(bounds.center.x + direction * reach, bounds.min.y + .2f);
            bool wall = HasTerrain(wallOrigin, Vector2.right * direction, reach) ||
                        HasTerrain(new Vector2(bounds.center.x, bounds.min.y + .22f), Vector2.right * direction, reach);
            bool floor = HasTerrain(floorOrigin, Vector2.down, .65f);
            if (wall || !floor)
            {
                StopHorizontal();
                if (turnAtObstacle) SetDirection(-direction);
                return;
            }
            body.velocity = new Vector2(direction * speed, body.velocity.y);
        }

        private Vector3 ChestPosition() => shape.bounds.center;

        private bool TerrainBetween(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return delta.sqrMagnitude > .0001f && HasTerrain(from, delta.normalized, delta.magnitude);
        }

        private bool HasTerrain(Vector2 origin, Vector2 rayDirection, float distance)
        {
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = GroundLayers, useTriggers = false };
            int count = Physics2D.Raycast(origin, rayDirection, filter, rayHits, distance);
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i].collider;
                if (!hit || hit.transform.IsChildOf(transform) || hit.GetComponentInParent<PatrolEnemy2D>() ||
                    hit.GetComponentInParent<PlayerController2D>()) continue;
                return true;
            }
            return false;
        }

        private void SetDirection(int value)
        {
            direction = value < 0 ? -1 : 1;
            facing.SetFacing(direction);
        }

        private void Suspend()
        {
            StopHorizontal();
            Target = null;
            streamed.CombatPinned = false;
            State = PatrolEnemyState.Patrol;
            stateUntil = 0;
            RestoreColor();
        }

        private void UpdateColor()
        {
            if (!visual) return;
            if (State == PatrolEnemyState.Windup)
            {
                float progress = 1 - Mathf.Clamp01((stateUntil - Time.time) / Mathf.Max(.05f, AttackWindup));
                visual.color = Color.Lerp(new Color(1, .72f, .12f), Color.white, progress * .55f);
            }
            else if (Time.time < strikeFlashUntil) visual.color = new Color(1, .22f, .12f);
            else visual.color = originalColor;
        }

        private void StopHorizontal()
        {
            if (body) body.velocity = new Vector2(0, body.velocity.y);
        }

        private void RestoreColor() { if (visual) visual.color = originalColor; }

        private void ClearRuntimeState()
        {
            Target = null;
            stateUntil = nextAttackAt = strikeFlashUntil = 0;
            State = PatrolEnemyState.Dead;
            if (streamed) streamed.CombatPinned = false;
            if (body) { body.velocity = Vector2.zero; body.angularVelocity = 0; }
            RestoreColor();
        }

        private void OnDisable() { ClearRuntimeState(); }
        public void OnRelease() { ClearRuntimeState(); }
    }
}

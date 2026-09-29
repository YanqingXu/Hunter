using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    public enum PlayerMotionState { Idle, Run, Rise, Fall, Hurt, Dead }

    /// <summary>Physics locomotion for the playable level. Input can also come from a test or input adapter.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SkillActor2D))]
    [RequireComponent(typeof(SkillFacing2D))]
    [AddComponentMenu("BigWorld/Game/Player Controller 2D")]
    public sealed class PlayerController2D : MonoBehaviour
    {
        public WorldSession2D World;
        public SkillClip Shoot, Melee;
        public bool ReadKeyboard = true;

        [Header("移动")]
        [Min(0)] public float MoveSpeed = 6;
        [Min(0)] public float GroundAcceleration = 60;
        [Min(0)] public float GroundDeceleration = 75;
        [Min(0)] public float AirAcceleration = 30;
        [Min(0)] public float AirDeceleration = 20;

        [Header("跳跃")]
        [Min(0)] public float JumpSpeed = 10;
        [Min(0)] public float CoyoteTime = .12f;
        [Min(0)] public float JumpBufferTime = .12f;
        [Range(.05f, 1)] public float JumpReleaseMultiplier = .45f;
        public LayerMask GroundLayers = 1;
        [Min(.005f)] public float GroundProbeDistance = .08f;

        [Header("受伤")]
        [Min(0)] public float HurtDuration = .18f;
        [Min(0)] public float KnockbackSpeed = 5;
        [Min(0)] public float KnockbackLift = 2.5f;

        public bool ControlEnabled { get; private set; } = true;
        public bool IsGrounded { get; private set; }
        public bool IsHurt => actor && actor.IsAlive && Time.time < hurtUntil;
        public PlayerMotionState State { get; private set; } = PlayerMotionState.Idle;

        private Rigidbody2D body;
        private BoxCollider2D shape;
        private SkillActor2D actor;
        private SkillFacing2D facing;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[16];
        private float movement;
        private bool jumpHeld;
        private bool canCutJump;
        private float jumpBufferedUntil = float.NegativeInfinity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float hurtUntil = float.NegativeInfinity;

        private void Awake() => CacheComponents();

        private void OnEnable()
        {
            CacheComponents();
            actor.Damaged += OnDamaged;
            actor.Died += OnDied;
        }

        private void OnDisable()
        {
            if (actor)
            {
                actor.Damaged -= OnDamaged;
                actor.Died -= OnDied;
            }
            ClearInput();
        }

        private void CacheComponents()
        {
            if (!body) body = GetComponent<Rigidbody2D>();
            if (!shape) shape = GetComponent<BoxCollider2D>();
            if (!actor) actor = GetComponent<SkillActor2D>();
            if (!facing) facing = GetComponent<SkillFacing2D>();
        }

        private bool CanAct() => isActiveAndEnabled && ControlEnabled && World && World.IsReady &&
            body && body.simulated && actor && actor.IsAlive && Time.timeScale > 0 &&
            (!GameServices2D.Instance || !GameServices2D.Instance.IsPaused);

        private void Update()
        {
            if (!CanAct() || IsHurt)
            {
                ClearInput();
                UpdateState();
                return;
            }

            if (ReadKeyboard)
            {
                float horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                                   (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                SetInput(horizontal, Input.GetKeyDown(KeyCode.Space), Input.GetKey(KeyCode.Space));
                if (Input.GetKey(KeyCode.J)) TryShoot();
                if (Input.GetKeyDown(KeyCode.K)) TryMelee();
            }
            UpdateState();
        }

        /// <summary>jumpPressed is an edge; jumpHeld is the current button state. Paused input is discarded.</summary>
        public void SetInput(float horizontal, bool jumpPressed, bool jumpHeld)
        {
            if (!CanAct() || IsHurt) { ClearInput(); return; }
            movement = float.IsNaN(horizontal) || float.IsInfinity(horizontal) ? 0 : Mathf.Clamp(horizontal, -1, 1);
            this.jumpHeld = jumpHeld;
            if (jumpPressed) jumpBufferedUntil = Time.time + Mathf.Max(0, JumpBufferTime);
        }

        public bool TryShoot() => CanAct() && !IsHurt && actor.TryPlay(Shoot);
        public bool TryMelee() => CanAct() && !IsHurt && actor.TryPlay(Melee, .5f);

        public void SetControlEnabled(bool enabled)
        {
            CacheComponents();
            ControlEnabled = enabled;
            ClearInput();
            if (!enabled && body) body.velocity = new Vector2(0, body.velocity.y);
            UpdateState();
        }

        /// <summary>The level owns health, simulation, and control unlock; this only resets locomotion and pose.</summary>
        public void ResetForRespawn(Vector2 position)
        {
            CacheComponents();
            ClearInput();
            lastGroundedTime = hurtUntil = float.NegativeInfinity;
            IsGrounded = false;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0;
            body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
            if (facing) facing.SetFacing(1);
            var visual = GetComponent<PlayerVisual2D>();
            if (visual) visual.ResetPose();
            UpdateState();
        }

        private void FixedUpdate()
        {
            IsGrounded = ProbeGround();
            if (!CanAct())
            {
                ClearInput();
                lastGroundedTime = float.NegativeInfinity;
                body.velocity = actor && actor.IsAlive ? new Vector2(0, body.velocity.y) : Vector2.zero;
                UpdateState();
                return;
            }

            if (IsHurt)
            {
                // Preserve the hit impulse throughout hit stun instead of overwriting it with movement.
                ClearInput();
                lastGroundedTime = float.NegativeInfinity;
                UpdateState();
                return;
            }

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                canCutJump = false;
            }

            float acceleration = Mathf.Abs(movement) > .001f
                ? (IsGrounded ? GroundAcceleration : AirAcceleration)
                : (IsGrounded ? GroundDeceleration : AirDeceleration);
            body.velocity = new Vector2(Mathf.MoveTowards(body.velocity.x, movement * MoveSpeed,
                Mathf.Max(0, acceleration) * Time.fixedDeltaTime), body.velocity.y);
            if (Mathf.Abs(movement) > .001f && facing) facing.SetFacing(movement < 0 ? -1 : 1);

            if (Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= Mathf.Max(0, CoyoteTime))
            {
                body.velocity = new Vector2(body.velocity.x, Mathf.Max(0, JumpSpeed));
                jumpBufferedUntil = lastGroundedTime = float.NegativeInfinity;
                IsGrounded = false;
                canCutJump = true;
            }
            if (canCutJump && !jumpHeld && body.velocity.y > 0)
            {
                body.velocity = new Vector2(body.velocity.x, body.velocity.y * Mathf.Clamp01(JumpReleaseMultiplier));
                canCutJump = false;
            }
            if (body.velocity.y <= 0) canCutJump = false;
            UpdateState();
        }

        private bool ProbeGround()
        {
            if (!body || !body.simulated || !shape || !shape.enabled || body.velocity.y > .1f) return false;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(GroundLayers);
            filter.useTriggers = false;
            int count = shape.Cast(Vector2.down, filter, groundHits, Mathf.Max(.005f, GroundProbeDistance));
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.rigidbody == body || hit.normal.y < .65f) continue;
                // Enemy bodies, hurtboxes, and other players are not platforms, even on the terrain layer.
                if (hit.collider.GetComponentInParent<SkillActor2D>()) continue;
                return true;
            }
            return false;
        }

        private void ClearInput()
        {
            movement = 0;
            jumpHeld = false;
            canCutJump = false;
            jumpBufferedUntil = float.NegativeInfinity;
        }

        private void OnDamaged(SkillHitData hit)
        {
            ClearInput();
            if (!actor.IsAlive) { OnDied(); return; }
            hurtUntil = Time.time + Mathf.Max(0, HurtDuration);
            lastGroundedTime = float.NegativeInfinity;
            IsGrounded = false;
            float sourceX = hit.soure != null && hit.soure.ModelTransform
                ? hit.soure.ModelTransform.position.x : hit.hitPoint.x;
            float direction = Mathf.Abs(transform.position.x - sourceX) > .01f
                ? Mathf.Sign(transform.position.x - sourceX) : -(facing ? facing.Direction : 1);
            body.velocity = new Vector2(direction * Mathf.Max(0, KnockbackSpeed), Mathf.Max(body.velocity.y, KnockbackLift));
            UpdateState();
        }

        private void OnDied()
        {
            ClearInput();
            ControlEnabled = false;
            hurtUntil = float.NegativeInfinity;
            body.velocity = Vector2.zero;
            State = PlayerMotionState.Dead;
        }

        private void UpdateState()
        {
            if (!actor || !actor.IsAlive) State = PlayerMotionState.Dead;
            else if (IsHurt) State = PlayerMotionState.Hurt;
            else if (!body || !body.simulated || !ControlEnabled || !World || !World.IsReady) State = PlayerMotionState.Idle;
            else if (!IsGrounded) State = body.velocity.y > .1f ? PlayerMotionState.Rise : PlayerMotionState.Fall;
            else State = Mathf.Abs(body.velocity.x) > .1f ? PlayerMotionState.Run : PlayerMotionState.Idle;
        }
    }
}

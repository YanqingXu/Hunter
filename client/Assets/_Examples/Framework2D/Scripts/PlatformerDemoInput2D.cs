using SkillEditorKit;
using UnityEngine;

namespace BigWorld.YouYou2D
{
    /// <summary>Small keyboard demo. Production input/locomotion can call SkillActor2D directly.</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SkillActor2D))]
    public sealed class PlatformerDemoInput2D : MonoBehaviour
    {
        public WorldSession2D World;
        public SkillClip Shoot, Melee;
        public LayerMask GroundLayers = 1;
        public float MoveSpeed = 5, JumpSpeed = 9;
        public bool ReadKeyboard = true;
        private Rigidbody2D body;
        private BoxCollider2D shape;
        private SkillActor2D actor;
        private SkillFacing2D facing;
        private float movement;
        private bool jump;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        private Vector3 startPosition;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>(); shape = GetComponent<BoxCollider2D>();
            actor = GetComponent<SkillActor2D>(); facing = GetComponent<SkillFacing2D>();
            startPosition = transform.position;
        }
        private void Update()
        {
            if (!ReadKeyboard) return;
            var services = GameServices2D.Instance;
            if (Input.GetKeyDown(KeyCode.Escape) && services) services.SetPaused(!services.IsPaused);
            if (!CanMove()) { movement = 0; jump = false; return; }
            float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                      (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            SetInput(x, Input.GetKeyDown(KeyCode.Space));
            if (Input.GetKey(KeyCode.J)) actor.TryPlay(Shoot);
            if (Input.GetKeyDown(KeyCode.K)) actor.TryPlay(Melee, .5f);
            if (transform.position.y < startPosition.y - 15)
            {
                body.position = startPosition; body.velocity = Vector2.zero;
                transform.position = startPosition;
                var follow = Camera.main ? Camera.main.GetComponent<CameraFollow2D>() : null;
                if (follow && follow.Target == transform) follow.Snap();
            }
        }
        public void SetInput(float horizontal, bool jumpPressed)
        { movement = Mathf.Clamp(horizontal, -1, 1); jump |= jumpPressed; }
        private bool CanMove() => World && World.IsReady && actor.IsAlive && Time.timeScale > 0;
        private void FixedUpdate()
        {
            if (!CanMove()) { body.velocity = new Vector2(0, body.velocity.y); jump = false; return; }
            body.velocity = new Vector2(movement * MoveSpeed, body.velocity.y);
            if (movement != 0 && facing) facing.SetFacing(movement < 0 ? -1 : 1);
            if (jump && IsGrounded()) body.velocity = new Vector2(body.velocity.x, JumpSpeed);
            jump = false;
        }
        public bool IsGrounded()
        {
            var filter = new ContactFilter2D(); filter.SetLayerMask(GroundLayers); filter.useTriggers = false;
            int count = shape.Cast(Vector2.down, filter, groundHits, .08f);
            for (int i = 0; i < count; i++) if (groundHits[i].normal.y > .5f) return true;
            return false;
        }
    }
}

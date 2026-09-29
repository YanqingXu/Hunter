using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Procedural poses only: never changes a Sprite or the player's physics transform.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerController2D))]
    [AddComponentMenu("BigWorld/Game/Player Visual 2D")]
    public sealed class PlayerVisual2D : MonoBehaviour
    {
        public Transform Visual;
        public SpriteRenderer Sprite;
        [Min(0)] public float RunBobHeight = .035f;
        [Min(0)] public float PoseSmoothing = 18;
        [Min(0)] public float HitFlashDuration = .09f;

        private PlayerController2D controller;
        private SkillActor2D actor;
        private Vector3 restPosition, restScale;
        private Color restColor;
        private bool cached;
        private float hitFlashUntil = float.NegativeInfinity;

        private void Awake() => CachePose();

        private void OnEnable()
        {
            CachePose();
            if (actor) actor.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (actor) actor.Damaged -= OnDamaged;
            ResetPose();
        }

        private void CachePose()
        {
            if (cached) return;
            controller = GetComponent<PlayerController2D>();
            actor = GetComponent<SkillActor2D>();
            if (!Visual && actor) Visual = actor.Model;
            if (!Sprite)
            {
                var facing = GetComponent<SkillFacing2D>();
                Sprite = facing && facing.Sprite ? facing.Sprite : GetComponentInChildren<SpriteRenderer>();
            }
            if (!Visual && Sprite) Visual = Sprite.transform;
            if (!Visual || Visual == transform || !Visual.IsChildOf(transform)) return;
            restPosition = Visual.localPosition;
            restScale = Visual.localScale;
            restColor = Sprite ? Sprite.color : Color.white;
            cached = true;
        }

        public void ResetPose()
        {
            hitFlashUntil = float.NegativeInfinity;
            if (!cached) return;
            if (Visual) { Visual.localPosition = restPosition; Visual.localScale = restScale; }
            if (Sprite) Sprite.color = restColor;
        }

        private void OnDamaged(SkillHitData hit) => hitFlashUntil = Time.time + Mathf.Max(0, HitFlashDuration);

        private void LateUpdate()
        {
            if (!cached) CachePose();
            if (!cached || !Visual || !controller || !actor) return;
            bool dead = controller.State == PlayerMotionState.Dead;
            if (Time.timeScale <= 0 && !dead) return;
            Vector3 position = restPosition;
            Vector3 stretch = Vector3.one;
            bool playingSkill = actor.Player && actor.Player.IsPlaying;
            if (!playingSkill || controller.State == PlayerMotionState.Dead || controller.IsHurt)
            {
                switch (controller.State)
                {
                    case PlayerMotionState.Run:
                        float stride = Mathf.Sin(Time.time * 18);
                        position.y += Mathf.Abs(stride) * RunBobHeight;
                        stretch = new Vector3(1 + stride * .025f, 1 - stride * .025f, 1);
                        break;
                    case PlayerMotionState.Rise: stretch = new Vector3(.94f, 1.06f, 1); break;
                    case PlayerMotionState.Fall: stretch = new Vector3(1.035f, .97f, 1); break;
                    case PlayerMotionState.Hurt: stretch = new Vector3(1.06f, .94f, 1); break;
                    case PlayerMotionState.Dead:
                        stretch = new Vector3(1.15f, .55f, 1);
                        position.y -= .2f;
                        break;
                }
            }
            // The death menu pauses simulation immediately; still let its visual death pose settle.
            float poseDelta = dead ? Time.unscaledDeltaTime : Time.deltaTime;
            float blend = 1 - Mathf.Exp(-Mathf.Max(0, PoseSmoothing) * poseDelta);
            Visual.localPosition = Vector3.Lerp(Visual.localPosition, position, blend);
            Visual.localScale = Vector3.Lerp(Visual.localScale, Vector3.Scale(restScale, stretch), blend);

            if (!Sprite) return;
            Color color = restColor;
            if (controller.State == PlayerMotionState.Dead) color = Color.Lerp(restColor, new Color(.4f, .25f, .25f, restColor.a), .7f);
            else if (Time.time < hitFlashUntil) color = new Color(1, 1, 1, restColor.a);
            if (actor.IsInvulnerable && actor.IsAlive && Mathf.FloorToInt(Time.time * 14) % 2 == 0) color.a *= .35f;
            Sprite.color = color;
        }
    }
}

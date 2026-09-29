using UnityEngine;

namespace BigWorld.Gameplay
{
    [RequireComponent(typeof(BoxCollider2D)), DisallowMultipleComponent]
    public sealed class ExitGate2D : MonoBehaviour
    {
        public SpriteRenderer Indicator;
        private void Awake() { GetComponent<BoxCollider2D>().isTrigger = true; }
        private void Update()
        {
            var level = LevelSession2D.Instance;
            if (Indicator && level) Indicator.color = level.DefeatedEnemies >= level.RequiredKills ? new Color(.25f, 1, .7f) : new Color(1, .45f, .2f);
        }
        private void OnTriggerEnter2D(Collider2D other) { TryExit(other); }
        private void OnTriggerStay2D(Collider2D other) { TryExit(other); }
        private void TryExit(Collider2D other)
        {
            var level = LevelSession2D.Instance;
            if (level && level.Player && other.attachedRigidbody == level.Player.GetComponent<Rigidbody2D>()) level.TryComplete();
        }
    }
}

using UnityEngine;

namespace BigWorld.Gameplay
{
    [RequireComponent(typeof(BoxCollider2D)), DisallowMultipleComponent]
    public sealed class Checkpoint2D : MonoBehaviour
    {
        public int Order = 1;
        public string DisplayName = "营地";
        public Transform SpawnPoint;
        public SpriteRenderer Flag;
        public bool Activated { get; private set; }
        public Vector2 SpawnPosition => SpawnPoint ? (Vector2)SpawnPoint.position : (Vector2)transform.position;
        public void SetActivated(bool value)
        {
            Activated = value;
            if (Flag) Flag.color = value ? new Color(.2f, 1, .65f) : new Color(.45f, .55f, .65f);
        }
        private void Awake() { GetComponent<BoxCollider2D>().isTrigger = true; SetActivated(false); }
        private void OnTriggerEnter2D(Collider2D other)
        {
            var session = LevelSession2D.Instance;
            if (session && session.Player && other.attachedRigidbody == session.Player.GetComponent<Rigidbody2D>())
                session.ActivateCheckpoint(this);
        }
    }
}

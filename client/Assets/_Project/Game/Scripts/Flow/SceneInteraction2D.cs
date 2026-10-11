using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>A run-owned cabinet. Opening reveals a real ammo supply; it does not grant ammo itself.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneInteraction2D : MonoBehaviour
    {
        public string DisplayName = "弹药柜";
        [Min(0)] public float DelaySeconds;
        [Min(.1f)] public float InteractionDistance = 1.6f;
        public LayerMask GroundLayers = 1;
        public SpriteRenderer Visual;
        public AmmoSupply2D Supply { get; private set; }
        public bool Used { get; private set; }
        public float Progress => timer.Progress;
        private readonly HunterInteractionTimer timer = new HunterInteractionTimer();
        private PlayerController2D user;
        private LevelSession2D level;
        private HunterSceneInteractions2D owner;
        private SpriteRenderer latch;

        internal void Configure(HunterSceneInteractions2D manager, Sprite sprite, string title, float delay)
        {
            owner = manager; level = manager.GetComponent<LevelSession2D>(); DisplayName = title; DelaySeconds = delay;
            Visual = MakeVisual("Cabinet", sprite, new Vector2(.95f, 1), 24);
            latch = MakeVisual("Latch", sprite, new Vector2(.15f, .4f), 25);
            var reward = new GameObject("Unlocked Ammo Supply"); reward.SetActive(false); reward.transform.SetParent(transform, false);
            reward.transform.localScale = new Vector3(.8f, .65f, 1);
            var visual = reward.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = 27;
            Supply = reward.AddComponent<AmmoSupply2D>(); Supply.Visual = visual; Supply.InteractionDistance = InteractionDistance;
            Show(false);
        }
        private SpriteRenderer MakeVisual(string title, Sprite sprite, Vector2 size, int order)
        {
            var child = new GameObject(title); child.transform.SetParent(transform, false); child.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = child.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = order; return visual;
        }
        public bool TerrainReady()
        {
            if (!level || !level.World || !level.World.IsReady) return false;
            var map = level.World.Map; var cell = map.WorldToCell(transform.position);
            return map.IsCellLoaded(cell.x, cell.y);
        }
        public bool InReach(PlayerController2D player)
        {
            if (!player || player.gameObject.scene != gameObject.scene) return false;
            // A body can be repositioned before its interpolated render transform catches up.
            Vector2 position = player.GetComponent<Rigidbody2D>().position;
            if (Vector2.Distance(position, transform.position) > Mathf.Max(.1f, InteractionDistance)) return false;
            foreach (var hit in Physics2D.LinecastAll(position + Vector2.up * .7f, transform.position, GroundLayers))
                if (hit.collider && !hit.collider.isTrigger && !hit.collider.transform.IsChildOf(player.transform) &&
                    !hit.collider.transform.IsChildOf(transform)) return false;
            return true;
        }
        internal bool Begin(PlayerController2D player)
        {
            if (!isActiveAndEnabled || !owner || !owner.isActiveAndEnabled || !level || !level.IsGameplayActive ||
                player != level.Player || !player.CanInteract || Used || !TerrainReady() || !InReach(player) ||
                (DelaySeconds > 0 && !player.IsGrounded) || !Supply || !timer.TryStart(DelaySeconds)) return false;
            user = player; return true;
        }
        internal void Advance(PlayerController2D player, float delta)
        {
            if (player != user) return;
            bool paused = level && level.Phase == LevelPhase.Paused;
            if (!level || (!level.IsGameplayActive && !paused) || !isActiveAndEnabled || !owner || !owner.isActiveAndEnabled ||
                !player.isActiveAndEnabled || (!player.ControlEnabled && !paused) || player.IsHurt ||
                !player.GetComponent<BigWorld.YouYou2D.SkillActor2D>().IsAlive || !TerrainReady() || !InReach(player) || !Supply)
            { player.CancelSceneInteraction("交互条件改变，已取消。"); return; }
            if (!timer.Tick(delta, paused, false)) return;
            Used = true; user = null; Show(true);
            player.CompleteSceneInteraction(this);
        }
        internal void Cancel(PlayerController2D player)
        { if (user == player) { user = null; timer.Cancel(); } }
        internal void Show(bool visible)
        {
            if (Visual) { Visual.enabled = visible && !Used; Visual.color = DelaySeconds > 0 ? new Color(.58f, .4f, .23f) : new Color(.35f, .52f, .59f); }
            if (latch) { latch.enabled = visible && !Used; latch.color = DelaySeconds > 0 ? new Color(1, .66f, .24f) : new Color(.3f, 1, .8f); }
            if (Supply && Supply.gameObject.activeSelf != (visible && Used)) Supply.gameObject.SetActive(visible && Used);
        }
        public string Hint => Used ? "弹药柜已开启" : "E " + DisplayName + (DelaySeconds > 0 ? "（" + DelaySeconds.ToString("0.#") + "秒）" : "（即时）");
        private void OnDisable() { if (user) user.CancelSceneInteraction("交互物已停用，已取消。"); Show(false); }
    }
}

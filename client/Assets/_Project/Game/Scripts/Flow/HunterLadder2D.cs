using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Fixed ladder beside the authored raised route. It never disables terrain collisions.</summary>
    [DisallowMultipleComponent]
    public sealed class HunterLadder2D : MonoBehaviour
    {
        public Vector2 Bottom { get; private set; }
        public Vector2 Top { get; private set; }
        public Vector2 TopExit { get; private set; }
        public HunterLadderMotion Motion { get; private set; }
        public float InteractionDistance = 1.3f;
        private HunterSceneInteractions2D owner;
        private LevelSession2D level;

        internal void Configure(HunterSceneInteractions2D manager, Sprite sprite, Vector2 bottom, Vector2 top, Vector2 exit, float speed)
        {
            owner = manager; level = manager.GetComponent<LevelSession2D>(); Bottom = bottom; Top = top; TopExit = exit;
            transform.position = bottom; Motion = new HunterLadderMotion(bottom.y, top.y, speed);
            float height = top.y - bottom.y + .6f;
            MakeVisual(sprite, new Vector2(-.3f, height / 2), new Vector2(.09f, height));
            MakeVisual(sprite, new Vector2(.3f, height / 2), new Vector2(.09f, height));
            for (float y = .15f; y < height; y += .32f) MakeVisual(sprite, new Vector2(0, y), new Vector2(.6f, .07f));
            Show(false);
        }
        private void MakeVisual(Sprite sprite, Vector2 offset, Vector2 size)
        {
            var child = new GameObject("Ladder Rail"); child.transform.SetParent(transform, false);
            child.transform.localPosition = offset; child.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = child.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = 20;
            visual.color = new Color(.85f, .68f, .37f);
        }
        public bool TerrainReady(Vector2 point)
        {
            if (!level || !level.World || !level.World.IsReady) return false;
            var map = level.World.Map; var cell = map.WorldToCell(point);
            return map.IsCellLoaded(cell.x, cell.y);
        }
        public bool Available => isActiveAndEnabled && owner && owner.isActiveAndEnabled && level && !level.TrainingMode &&
            Motion != null && TerrainReady(Bottom) && TerrainReady(Top) && TerrainReady(TopExit);
        public bool TryGetMount(PlayerController2D player, out Vector2 point)
        {
            point = Bottom;
            if (!Available || !player || player != level.Player) return false;
            Vector2 position = player.GetComponent<Rigidbody2D>().position;
            if ((position - Top).sqrMagnitude < (position - Bottom).sqrMagnitude) point = Top;
            if (Vector2.Distance(position, point) > InteractionDistance) return false;
            foreach (var hit in Physics2D.LinecastAll(position + Vector2.up * .7f, point + Vector2.up * .7f, player.GroundLayers))
                if (hit.collider && !hit.collider.isTrigger && !hit.collider.transform.IsChildOf(player.transform)) return false;
            return true;
        }
        internal void Show(bool visible)
        { foreach (var visual in GetComponentsInChildren<SpriteRenderer>()) visual.enabled = visible && isActiveAndEnabled; }
        private void OnDisable()
        {
            if (level && level.Player && level.Player.Ladder == this) level.Player.DetachLadder("梯子已停用。");
            Show(false);
        }
    }
}

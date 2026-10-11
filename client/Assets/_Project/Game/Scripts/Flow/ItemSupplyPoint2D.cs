using UnityEngine;

namespace BigWorld.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ItemSupplyPoint2D : MonoBehaviour
    {
        [Min(.1f)] public float InteractionDistance = 1.6f;
        public LayerMask GroundLayers = 1;
        public HunterItemLoot2D Owner { get; private set; }
        public bool IsSupply { get; private set; }
        public HunterItemId Item { get; private set; }
        public bool Available { get; private set; }
        public bool Used { get; private set; }
        private HunterItemSupplyPlan pending;
        private SpriteRenderer body, mark, cross;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[32];
        internal void Configure(HunterItemLoot2D owner, Sprite sprite, bool supply, HunterItemId item)
        {
            Owner = owner; IsSupply = supply; Item = item;
            Available = supply || (HunterItems.Find(item) != null && HunterItems.Find(item).Consumable);
            body = Visual("Box", sprite, new Vector2(supply ? .85f : .4f, .55f), 26);
            mark = Visual("Mark", sprite, new Vector2(.3f, .09f), 27);
            cross = Visual("Cross", sprite, new Vector2(.09f, .3f), 27);
            Show(false);
        }
        private SpriteRenderer Visual(string title, Sprite sprite, Vector2 size, int order)
        {
            var child = new GameObject(title); child.transform.SetParent(transform, false);
            child.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = child.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = order; return visual;
        }
        internal void Show(bool visible)
        {
            if (!body) return;
            body.enabled = mark.enabled = visible && Available;
            cross.enabled = visible && Available && !IsSupply && Item == HunterItemId.HealthShot;
            body.color = Used ? new Color(.28f, .34f, .4f) : IsSupply ? new Color(.3f, .65f, 1) : new Color(1, .58f, .42f);
            mark.color = cross.color = Used ? new Color(.4f, .43f, .48f) : Color.white;
        }
        internal void Clear() { Available = false; pending = null; Show(false); }
        internal void MarkUsed() { Used = true; pending = null; }
        internal HunterItemSupplyPlan PrepareSupply(HunterRecord hunter)
        {
            if (!HunterItemSupply.Matches(hunter, pending))
                pending = HunterItemSupply.Plan(hunter, Owner.Consumables, max => Random.Range(0, max));
            return pending;
        }
        internal bool InReach(PlayerController2D player)
        {
            if (!player || gameObject.scene != player.gameObject.scene ||
                ((Vector2)(transform.position - player.transform.position)).sqrMagnitude > InteractionDistance * InteractionDistance) return false;
            int count = Physics2D.LinecastNonAlloc(player.transform.position + Vector3.up * .7f, transform.position, hits, GroundLayers);
            if (count == hits.Length) return false; // An overflow cannot silently omit a wall.
            for (int i = 0; i < count; i++)
                if (hits[i].collider && !hits[i].collider.isTrigger && !hits[i].collider.GetComponentInParent<PlayerController2D>() &&
                    !hits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }
        public string Hint(HunterRecord hunter)
        {
            if (!Available) return "";
            if (Used) return "道具已领取（本局一次）";
            if (!IsSupply) return HunterItemSupply.HasConsumableSlot(hunter) ? "E 拾取" + HunterItems.Find(Item).Name : "消耗品栏已满，道具保留";
            if (HunterItemSupply.RefillableTools(hunter).Count > 0) return "E 补给一个工具的使用次数";
            return HunterItemSupply.CanSupply(hunter, Owner.Consumables) ? "E 随机获得一个消耗品" : "道具已满或无可用补给，箱子保留";
        }
        public bool TryCollect(PlayerController2D player)
        {
            var level = LevelSession2D.Instance;
            return level && player == level.Player && level.TryCollectItem(this);
        }
    }
}

using UnityEngine;

namespace BigWorld.Gameplay
{
    [DisallowMultipleComponent, AddComponentMenu("BigWorld/Game/Skill Pickup 2D")]
    public sealed class SkillPickup2D : MonoBehaviour
    {
        [Min(.1f)] public float InteractionDistance = 1.6f;
        public LayerMask GroundLayers = 1;
        public HunterSkillId Skill { get; private set; }
        public bool Used { get; private set; }
        public HunterSkillLoot2D Owner { get; private set; }
        private SpriteRenderer card, stripe, pedestal;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        internal void Configure(HunterSkillLoot2D owner, Sprite sprite)
        {
            Owner = owner;
            card = MakeVisual("Skill Card", sprite, Vector2.zero, new Vector2(.55f, .65f), 26);
            stripe = MakeVisual("Card Mark", sprite, new Vector2(0, .02f), new Vector2(.32f, .09f), 27);
            pedestal = MakeVisual("Pedestal", sprite, new Vector2(0, -.45f), new Vector2(.85f, .12f), 25);
            Show(false);
        }
        private SpriteRenderer MakeVisual(string title, Sprite sprite, Vector2 position, Vector2 size, int order)
        {
            var child = new GameObject(title); child.transform.SetParent(transform, false);
            child.transform.localPosition = position; child.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = child.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = order; return visual;
        }
        internal void SetSkill(HunterSkillId skill) { Skill = skill; Used = false; }
        internal void MarkCollected() { Used = true; }
        internal void Clear() { Skill = 0; Used = false; Show(false); }
        internal void Show(bool visible)
        {
            if (!card) return;
            card.enabled = stripe.enabled = pedestal.enabled = visible && HunterSkills.Find(Skill) != null;
            card.color = Used ? new Color(.23f, .32f, .36f) : new Color(.2f, .92f, .82f);
            stripe.color = Used ? new Color(.35f, .42f, .45f) : Color.white;
            pedestal.color = new Color(.17f, .42f, .45f);
        }
        internal bool InReach(PlayerController2D player)
        {
            if (!player || gameObject.scene != player.gameObject.scene ||
                ((Vector2)(transform.position - player.transform.position)).sqrMagnitude > InteractionDistance * InteractionDistance) return false;
            int count = Physics2D.LinecastNonAlloc(player.transform.position + Vector3.up * .7f, transform.position, hits, GroundLayers);
            for (int i = 0; i < count; i++)
                if (hits[i].collider && !hits[i].collider.isTrigger && !hits[i].collider.transform.IsChildOf(player.transform) &&
                    !hits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }
        public string Hint(HunterRecord hunter)
        {
            var skill = HunterSkills.Find(Skill);
            if (skill == null) return "";
            if (Used) return "技能卡已拾取（本局一次）";
            if (hunter != null && hunter.HasSkill(Skill)) return "已学会" + skill.Name + "，技能卡保留";
            if (hunter != null && !HunterTemplates.CanAddSkill(hunter.TemplateId, hunter.CarriedSkillValue, skill.Cost))
                return "技能携带容量不足（需要 " + skill.Cost + " 点），技能卡保留";
            return "E 拾取「" + skill.Name + "」· 立即生效";
        }
        public bool TryCollect(PlayerController2D player)
        {
            var level = LevelSession2D.Instance;
            return level && player == level.Player && level.TryCollectSkill(this);
        }
    }
}

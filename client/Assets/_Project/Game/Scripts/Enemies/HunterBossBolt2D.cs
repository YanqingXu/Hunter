using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>A boss energy bolt, with a fixed launch vector, swept collision and explicit owner cleanup.</summary>
    public sealed class HunterBossBolt2D : MonoBehaviour
    {
        public HunterBossCombat2D Owner { get; private set; }
        public Vector2 Direction { get; private set; }
        private float distance;
        private bool cancelled;
        private LineRenderer trail;
        internal static HunterBossBolt2D Create(HunterBossCombat2D owner, Vector2 point, Vector2 direction, Material material)
        {
            var root = new GameObject("Boss Energy Bolt"); root.transform.SetParent(owner.transform.parent, false); root.transform.position = point;
            var bolt = root.AddComponent<HunterBossBolt2D>(); bolt.Owner = owner; bolt.Direction = direction;
            bolt.trail = root.AddComponent<LineRenderer>(); bolt.trail.sharedMaterial = material; bolt.trail.useWorldSpace = true;
            bolt.trail.positionCount = 2; bolt.trail.startWidth = bolt.trail.endWidth = .18f; bolt.trail.sortingOrder = 29;
            bolt.trail.startColor = bolt.trail.endColor = new Color(.9f, .35f, 1); bolt.Draw(); return bolt;
        }
        private void FixedUpdate()
        {
            var level = LevelSession2D.Instance;
            if (cancelled) return;
            if (!Owner || !Owner.isActiveAndEnabled || !Owner.Owner || !Owner.Owner.isActiveAndEnabled || !Owner.Owner.Actor.IsAlive ||
                !level || !level.Player || level.IsPlayerUndetected || level.Phase == LevelPhase.Ready || level.Phase == LevelPhase.Dead ||
                level.Phase == LevelPhase.Won || level.Phase == LevelPhase.Reviving || level.Phase == LevelPhase.Respawning)
            { Cancel(); return; }
            if (!level.IsGameplayActive) return;
            float step = Mathf.Min(10 - distance, 9 * Time.fixedDeltaTime);
            Vector2 point = transform.position, next = point + Direction * step;
            if (!HunterBossCombat2D.TerrainReady(next)) { Cancel(); return; }
            foreach (var hit in Physics2D.CircleCastAll(point, .1f, Direction, step))
            {
                if (!hit.collider || hit.collider.isTrigger) continue;
                var actor = hit.collider.GetComponentInParent<SkillActor2D>();
                if (actor && (actor == Owner.Owner.Actor || !actor.IsAlive || actor.SkillTeamId == Owner.Owner.Actor.SkillTeamId)) continue;
                if (actor) actor.BeHit(new SkillHitData { soure = Owner.Owner.Actor, attackValue = 16, hitPoint = hit.point });
                Cancel(); return;
            }
            transform.position = next; distance += step; Draw();
            if (distance >= 10) Cancel();
        }
        private void Draw() { trail.SetPosition(0, transform.position); trail.SetPosition(1, transform.position - (Vector3)(Direction * .4f)); }
        public void Cancel()
        {
            if (cancelled) return; cancelled = true;
            if (Owner) Owner.Release(this); gameObject.SetActive(false); Destroy(gameObject);
        }
        private void OnDestroy() { if (Owner) Owner.Release(this); }
    }
}

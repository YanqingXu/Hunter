using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Scene-owned short-range throwable. Sweeps each flight step and respects terrain cover.</summary>
    public sealed class HunterThrowable2D : MonoBehaviour
    {
        private LevelSession2D level;
        private SkillActor2D owner;
        private HunterItemDefinition item;
        private Vector2 origin;
        private float travelled, fuse;
        private HunterThrowPath path;
        private Vector2 trailDirection;
        public Vector2 LaunchDirection => new Vector2(path.X, path.Y);
        public Vector2 LaunchOrigin => origin;
        public float Range => path.Range;
        private bool landed;
        private LineRenderer trail;
        public static HunterThrowable2D Prepare(PlayerController2D player, HunterItemDefinition definition, int facing)
        { return Prepare(player, definition, new Vector2(facing < 0 ? -1 : 1, 0), player.PostureHeightMultiplier); }

        public static HunterThrowable2D Prepare(PlayerController2D player, HunterItemDefinition definition, Vector2 direction, float heightMultiplier)
        {
            if (float.IsNaN(heightMultiplier) || float.IsInfinity(heightMultiplier) || heightMultiplier <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(heightMultiplier));
            var path = new HunterThrowPath(direction.x, direction.y, definition.Range, definition.Radius > 0 ? 1.2f : .25f);
            var go = new GameObject(definition.Name); go.SetActive(false);
            go.transform.SetParent(LevelSession2D.Instance.transform, false);
            var projectile = go.AddComponent<HunterThrowable2D>();
            projectile.level = LevelSession2D.Instance; projectile.owner = player.GetComponent<SkillActor2D>();
            projectile.item = definition; projectile.path = path; projectile.trailDirection = new Vector2(path.X, path.Y);
            projectile.origin = (Vector2)player.transform.position + Vector2.up * (.75f * heightMultiplier);
            go.transform.position = projectile.origin;
            projectile.trail = go.AddComponent<LineRenderer>();
            var visual = player.GetComponentInChildren<SpriteRenderer>();
            if (visual) projectile.trail.sharedMaterial = visual.sharedMaterial;
            projectile.trail.positionCount = 2; projectile.trail.useWorldSpace = true;
            projectile.trail.startWidth = projectile.trail.endWidth = .13f;
            projectile.trail.startColor = projectile.trail.endColor = definition.Radius > 0 ? new Color(1, .45f, .2f) : new Color(.8f, .95f, 1);
            projectile.trail.sortingOrder = 20; projectile.Draw();
            return projectile;
        }
        private void Update()
        {
            if (!level || level.Phase == LevelPhase.Ready || level.Phase == LevelPhase.Respawning || level.Phase == LevelPhase.Dead || level.Phase == LevelPhase.Won)
            { Destroy(gameObject); return; }
            if (!level.IsGameplayActive || Time.deltaTime <= 0) return;
            if (landed)
            {
                fuse += Time.deltaTime;
                if (fuse >= .6f) Explode();
                return;
            }
            travelled = Mathf.Min(item.Range, travelled + 10 * Time.deltaTime);
            path.Sample(travelled, out float x, out float y);
            var next = origin + new Vector2(x, y);
            Vector2 previous = transform.position, step = next - previous;
            if (step.sqrMagnitude > .000001f) trailDirection = step.normalized;
            var hits = Physics2D.CircleCastAll(previous, .08f, step.normalized, step.magnitude);
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.collider.isTrigger) continue;
                var target = hit.collider.GetComponentInParent<SkillActor2D>();
                if (target && (target == owner || !target.IsAlive || target.SkillTeamId == owner.SkillTeamId)) continue;
                transform.position = hit.centroid; Draw();
                if (item.Radius > 0) landed = true;
                else
                {
                    if (target) Hit(target);
                    Destroy(gameObject);
                }
                return;
            }
            transform.position = next; Draw();
            if (travelled >= item.Range)
            {
                if (item.Radius > 0) landed = true;
                else Destroy(gameObject);
            }
        }
        private void Draw()
        {
            trail.SetPosition(0, transform.position);
            trail.SetPosition(1, transform.position - (Vector3)(trailDirection * .35f));
        }
        private void Explode()
        {
            HunterExplosion2D.Detonate(level, transform.position, item.Radius, item.Value, owner);
            Destroy(gameObject);
        }
        private void Hit(SkillActor2D target)
        { target.BeHit(new SkillHitData { soure = owner, attackValue = item.Value, hitPoint = transform.position }); }
    }
}

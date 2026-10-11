using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Run-owned scenery; unloaded or disabled barrels retain their fuse and spent state.</summary>
    [DisallowMultipleComponent]
    public sealed class HunterExplosiveBarrel2D : MonoBehaviour, ISkillHitTarget, ISkillTeam
    {
        // Neutral scenery can be struck by either of the two authored combat teams.
        public int SkillTeamId => 0;
        public float Radius { get; private set; }
        public float Damage { get; private set; }
        public bool IsArmed => fuse != null && fuse.IsArmed;
        public bool HasExploded => fuse != null && fuse.HasExploded;
        public float Progress => fuse == null ? 0 : fuse.Progress;
        public int Detonations { get; private set; }
        private HunterBarrelFuse fuse;
        private HunterSceneInteractions2D owner;
        private LevelSession2D level;
        private BoxCollider2D shape;
        private SpriteRenderer shell, stripe, warning;
        private LineRenderer flash;
        private float flashSeconds;
        public bool Available => isActiveAndEnabled && owner && owner.isActiveAndEnabled && level && !level.TrainingMode && TerrainReady();

        internal void Configure(HunterSceneInteractions2D manager, Sprite sprite, float delay, float radius, float damage)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 ||
                float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0) throw new System.ArgumentOutOfRangeException();
            owner = manager; level = manager.GetComponent<LevelSession2D>(); Radius = radius; Damage = damage;
            fuse = new HunterBarrelFuse(delay); gameObject.layer = 30;
            shape = gameObject.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.8f, 1.1f);
            shell = MakeVisual("Red Barrel", sprite, Vector2.zero, new Vector2(.8f, 1.1f), 22);
            stripe = MakeVisual("Warning Band", sprite, Vector2.zero, new Vector2(.84f, .18f), 23);
            stripe.color = new Color(1, .76f, .15f);
            warning = MakeVisual("Burning Fuse", sprite, new Vector2(0, .65f), new Vector2(.12f, .16f), 24);
            flash = new GameObject("Blast Ring").AddComponent<LineRenderer>(); flash.transform.SetParent(transform, false);
            flash.sharedMaterial = shell.sharedMaterial; flash.useWorldSpace = false; flash.loop = true;
            flash.positionCount = 32; flash.startWidth = flash.endWidth = .09f; flash.sortingOrder = 26;
            for (int i = 0; i < 32; i++) { float a = i * Mathf.PI * 2 / 32; flash.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * radius); }
            Show(false);
        }
        private SpriteRenderer MakeVisual(string title, Sprite sprite, Vector2 offset, Vector2 size, int sorting)
        {
            var child = new GameObject(title); child.transform.SetParent(transform, false); child.transform.localPosition = offset;
            child.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = child.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.sortingOrder = sorting; return visual;
        }
        public bool TerrainReady()
        {
            if (!level || !level.World || !level.World.IsReady || !level.World.Map.Map) return false;
            var map = level.World.Map; Vector2 position = transform.position;
            var min = map.WorldToCell(position - Vector2.one * Radius); var max = map.WorldToCell(position + Vector2.one * Radius);
            // The entire blast area needs its collision terrain. Beyond the finite map is not a missing chunk.
            for (int x = Mathf.Max(0, min.x); x <= Mathf.Min(map.Map.Width - 1, max.x); x++)
                for (int y = Mathf.Max(0, min.y); y <= Mathf.Min(map.Map.Height - 1, max.y); y++)
                    if (!map.IsCellLoaded(x, y)) return false;
            var center = map.WorldToCell(position); return map.IsCellLoaded(center.x, center.y);
        }
        public void BeHit(SkillHitData hit)
        {
            if (!Available || !level.IsGameplayActive || hit.Projectile == null || hit.attackValue <= 0 ||
                float.IsNaN(hit.attackValue) || float.IsInfinity(hit.attackValue)) return;
            if (fuse.Hit(HunterBarrelHit.Bullet, false)) Detonate();
        }
        internal bool ClaimExplosion()
        {
            if (!Available || !level.IsGameplayActive || !fuse.Hit(HunterBarrelHit.Explosion, false)) return false;
            CommitBlast(); return true;
        }
        private void CommitBlast()
        {
            Detonations++; shape.enabled = false; flashSeconds = .3f;
            shell.transform.localPosition = Vector2.down * .48f; shell.transform.localScale = new Vector3(.9f, .15f, 1);
            Show(true);
        }
        private void Detonate()
        {
            CommitBlast(); HunterExplosion2D.Detonate(level, transform.position, Radius, Damage, null);
        }
        private void Update()
        {
            bool available = Available;
            if (available && level.IsGameplayActive)
            {
                flashSeconds = Mathf.Max(0, flashSeconds - Time.deltaTime);
                if (fuse.Tick(Time.deltaTime, false)) Detonate();
            }
            Show(available && (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused));
        }
        internal void Show(bool visible)
        {
            visible &= isActiveAndEnabled;
            if (shape) shape.enabled = visible && !HasExploded;
            if (!shell) return;
            shell.enabled = visible; stripe.enabled = visible && !HasExploded; warning.enabled = visible && IsArmed;
            shell.color = HasExploded ? new Color(.19f, .17f, .16f) : IsArmed && Mathf.FloorToInt(fuse.Elapsed * 10) % 2 == 0 ?
                new Color(1, .7f, .3f) : new Color(.72f, .16f, .12f);
            warning.color = Color.Lerp(Color.yellow, Color.red, Progress);
            flash.enabled = visible && flashSeconds > 0;
            flash.startColor = flash.endColor = new Color(1, .5f, .15f, Mathf.Clamp01(flashSeconds / .3f));
        }
        private void OnDisable() { Show(false); }
    }
}

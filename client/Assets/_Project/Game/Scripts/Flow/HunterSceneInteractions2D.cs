using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class HunterSceneInteractions2D : MonoBehaviour
    {
        public Vector2 InstantPosition = new Vector2(28.5f, 2.55f);
        public Vector2 DelayedPosition = new Vector2(41.5f, 2.55f);
        [Min(.1f)] public float CabinetDelay = 3;
        public Vector2[] BarrelPositions = { new Vector2(70, 2.55f), new Vector2(72.5f, 2.55f), new Vector2(75, 2.55f) };
        public bool BallisticCoverEnabled = true;
        public Vector2[] CoverPositions = { new Vector2(30.5f, 2.8f), new Vector2(32.5f, 2.8f) };
        public IList<HunterPenetrableProp2D> Covers => covers.AsReadOnly();
        private readonly List<HunterPenetrableProp2D> covers = new List<HunterPenetrableProp2D>();
        [Min(0)] public float BarrelDelay = 1.5f;
        [Min(.1f)] public float BarrelRadius = 2.8f;
        [Min(.1f)] public float BarrelDamage = 60;
        public IList<HunterExplosiveBarrel2D> Barrels => barrels.AsReadOnly();
        private readonly List<HunterExplosiveBarrel2D> barrels = new List<HunterExplosiveBarrel2D>();
        public HunterLadder2D Ladder { get; private set; }
        public IList<SceneInteraction2D> Points => points.AsReadOnly();
        private readonly List<SceneInteraction2D> points = new List<SceneInteraction2D>();
        private LevelSession2D level;
        private void Awake() { level = GetComponent<LevelSession2D>(); }
        public void BeginRun()
        {
            foreach (var cover in covers) if (cover) { cover.gameObject.SetActive(false); Destroy(cover.gameObject); }
            covers.Clear();
            foreach (var barrel in barrels) if (barrel) { barrel.gameObject.SetActive(false); Destroy(barrel.gameObject); }
            barrels.Clear();
            if (Ladder) { Ladder.gameObject.SetActive(false); Destroy(Ladder.gameObject); Ladder = null; }
            foreach (var point in points) if (point) { point.gameObject.SetActive(false); Destroy(point.gameObject); }
            points.Clear();
            if (!isActiveAndEnabled || !level || level.TrainingMode) return;
            if (BallisticCoverEnabled && CoverPositions != null) foreach (var position in CoverPositions) MakeCover(position);
            MakePoint(InstantPosition, "打开路边弹药柜", 0);
            MakePoint(DelayedPosition, "撬开封锁弹药柜", CabinetDelay);
            var root = new GameObject("Raised Route Ladder"); root.transform.SetParent(transform, false);
            Ladder = root.AddComponent<HunterLadder2D>();
            Ladder.Configure(this, level.Hunt ? level.Hunt.MarkerSprite : null,
                new Vector2(43.25f, 2.05f), new Vector2(43.25f, 4.05f), new Vector2(44.45f, 4.05f), 2.5f);
            foreach (var position in BarrelPositions)
            {
                var barrelRoot = new GameObject("Explosive Barrel"); barrelRoot.transform.SetParent(transform, false); barrelRoot.transform.position = position;
                var barrel = barrelRoot.AddComponent<HunterExplosiveBarrel2D>();
                barrel.Configure(this, level.Hunt ? level.Hunt.MarkerSprite : null, BarrelDelay, BarrelRadius, BarrelDamage); barrels.Add(barrel);
            }
        }
        private void MakePoint(Vector2 position, string title, float seconds)
        {
            var root = new GameObject(title); root.transform.SetParent(transform, false); root.transform.position = position;
            var point = root.AddComponent<SceneInteraction2D>();
            point.Configure(this, level.Hunt ? level.Hunt.MarkerSprite : null, title, seconds); points.Add(point);
        }
        private void MakeCover(Vector2 position)
        {
            var root = new GameObject("Penetrable Wood Cover"); root.transform.SetParent(transform, false); root.transform.position = position;
            var cover = root.AddComponent<HunterPenetrableProp2D>(); cover.Kind = 1;
            root.AddComponent<BoxCollider2D>().size = new Vector2(.4f, 1.6f);
            for (int i = 0; i < 3; i++)
            {
                var plank = new GameObject("Wood Plank"); plank.transform.SetParent(root.transform, false);
                plank.transform.localPosition = new Vector3((i - 1) * .14f, 0, 0);
                plank.transform.localScale = new Vector3(.12f, 1.6f, 1);
                var visual = plank.AddComponent<SpriteRenderer>(); visual.sprite = level.Hunt ? level.Hunt.MarkerSprite : null;
                visual.color = new Color(.62f + i * .06f, .36f, .16f); visual.sortingOrder = 23;
            }
            covers.Add(cover); cover.Show(false);
        }
        private void Update()
        {
            foreach (var cover in covers)
                if (cover)
                {
                    bool visible = BallisticCoverEnabled && level && (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused) && level.World && level.World.IsReady;
                    if (visible) { var cell = level.World.Map.WorldToCell(cover.transform.position); visible = level.World.Map.IsCellLoaded(cell.x, cell.y); }
                    cover.Show(visible);
                }
            if (Ladder) Ladder.Show(Ladder.Available && level && (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused));
            foreach (var point in points) if (point) point.Show(isActiveAndEnabled && point.isActiveAndEnabled && level && !level.TrainingMode &&
                (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused) && point.TerrainReady());
        }
        private void OnDisable()
        {
            foreach (var cover in covers) if (cover) cover.Show(false);
            foreach (var barrel in barrels) if (barrel) barrel.Show(false);
            if (level && level.Player) level.Player.DetachLadder("场景交互已停用。");
            if (Ladder) Ladder.Show(false);
            if (level && level.Player) level.Player.CancelSceneInteraction("场景交互已停用。");
            foreach (var point in points) if (point) point.Show(false);
        }
        public SceneInteraction2D FindNearest(PlayerController2D player)
        {
            if (!isActiveAndEnabled || !level || level.TrainingMode || !player || player != level.Player) return null;
            SceneInteraction2D nearest = null; float distance = float.PositiveInfinity;
            foreach (var point in points)
            {
                if (!point || !point.isActiveAndEnabled || point.Used || !point.TerrainReady() || !point.InReach(player)) continue;
                float value = ((Vector2)(point.transform.position - player.transform.position)).sqrMagnitude;
                if (value < distance) { nearest = point; distance = value; }
            }
            return nearest;
        }
    }
}

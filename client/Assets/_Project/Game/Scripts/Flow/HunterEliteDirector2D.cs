using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Gameplay
{
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class HunterEliteDirector2D : MonoBehaviour
    {
        public Vector2 ArmoredPosition = new Vector2(36, 2.05f), SummonerPosition = new Vector2(59, 2.05f);
        [Min(0)] public float AbandonSeconds = 3, SummonCooldown = 6;
        public LevelSession2D Level { get; private set; }
        public IList<HunterElite2D> Elites => elites.AsReadOnly();
        public int DefeatedCount { get { int count = 0; foreach (var elite in elites) if (elite && elite.Actor.Health <= 0) count++; return count; } }
        private readonly List<HunterElite2D> elites = new List<HunterElite2D>();
        private readonly bool[] spawned = new bool[4];
        private bool started;
        private void Awake() { Level = GetComponent<LevelSession2D>(); }
        public void ClearRun()
        {
            started = false;
            foreach (var elite in elites) if (elite) { elite.RemoveSummon(); elite.gameObject.SetActive(false); Destroy(elite.gameObject); }
            elites.Clear(); System.Array.Clear(spawned, 0, spawned.Length);
        }
        public void BeginRun() { ClearRun(); started = isActiveAndEnabled && Level && !Level.TrainingMode; }
        public bool TerrainReady(Vector2 position)
        {
            if (!Level || !Level.World || !Level.World.IsReady) return false;
            var map = Level.World.Map; var feet = map.WorldToCell(position + Vector2.down * .12f); var head = map.WorldToCell(position + Vector2.up * 1.5f);
            return map.IsCellLoaded(feet.x, feet.y) && map.IsCellLoaded(head.x, head.y);
        }
        internal HunterElite2D Create(HunterEliteKind kind, int wave, Vector2 position, HunterElite2D owner = null)
        {
            var root = new GameObject(owner ? "Elite Summon" : kind == HunterEliteKind.Armored ? "Armored Elite" : "Summoner Elite");
            root.transform.SetParent(transform, false); root.transform.position = position;
            var elite = root.AddComponent<HunterElite2D>(); elite.Configure(this, kind, wave, owner); return elite;
        }
        private void Update()
        {
            bool visible = started && Level && !Level.TrainingMode && Level.Hunt && Level.Hunt.enabled &&
                (Level.Phase == LevelPhase.Playing || Level.Phase == LevelPhase.Paused || Level.Phase == LevelPhase.Reviving);
            if (visible && Level.IsGameplayActive)
                for (int i = 0; i < spawned.Length; i++)
                {
                    if (spawned[i] || (i >= 2 && !Level.Hunt.State.SecondWaveStarted)) continue;
                    Vector2 point = i % 2 == 0 ? ArmoredPosition : SummonerPosition;
                    if (!TerrainReady(point) || !TrySpawnPosition(point, out point)) continue;
                    elites.Add(Create(i % 2 == 0 ? HunterEliteKind.Armored : HunterEliteKind.Summoner, i < 2 ? 1 : 2, point)); spawned[i] = true;
                }
            foreach (var elite in elites)
                if (elite)
                {
                    bool loaded = visible && TerrainReady(elite.transform.position);
                    elite.gameObject.SetActive(loaded);
                    if (elite.Summon) elite.Summon.gameObject.SetActive(loaded && TerrainReady(elite.Summon.transform.position));
                }
        }
        public bool Contests(Vector2 exit)
        {
            if (!isActiveAndEnabled) return false;
            foreach (var elite in elites)
                if (elite && ((elite.Actor.IsAlive && Vector2.Distance(elite.transform.position, exit) < 4) ||
                    (elite.Summon && elite.Summon.Actor.IsAlive && Vector2.Distance(elite.Summon.transform.position, exit) < 4))) return true;
            return false;
        }
        private bool TrySpawnPosition(Vector2 origin, out Vector2 point)
        {
            foreach (float offset in new[] { 0f, -1.25f, 1.25f })
            {
                point = origin + Vector2.right * offset;
                if (!TerrainReady(point)) continue;
                var floor = Physics2D.Raycast(point + Vector2.up * .5f, Vector2.down, 1, 1);
                if (!floor.collider || floor.collider.isTrigger) continue;
                point.y = floor.point.y + .05f; bool clear = true;
                foreach (var collider in Physics2D.OverlapBoxAll(point + Vector2.up * .75f, new Vector2(.75f, 1.4f), 0))
                    if (collider && !collider.isTrigger) { clear = false; break; }
                if (clear) return true;
            }
            point = origin; return false;
        }
        private void OnDisable()
        { foreach (var elite in elites) if (elite) { elite.gameObject.SetActive(false); if (elite.Summon) elite.Summon.gameObject.SetActive(false); } }
    }
}

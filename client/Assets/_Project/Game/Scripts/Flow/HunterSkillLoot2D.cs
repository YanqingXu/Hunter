using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Run-owned pickup sockets; terrain streaming only changes visibility, never rerolls loot.</summary>
    [DisallowMultipleComponent, AddComponentMenu("BigWorld/Game/Hunter Skill Loot 2D")]
    public sealed class HunterSkillLoot2D : MonoBehaviour
    {
        public Vector2[] Positions = { new Vector2(17, 2.6f), new Vector2(37, 2.6f), new Vector2(55, 2.6f) };
        [Range(0, 1)] public float SpawnChance = .75f;
        public bool RandomizeSkills = true;
        public IList<SkillPickup2D> Pickups => pickups.AsReadOnly();
        private readonly List<SkillPickup2D> pickups = new List<SkillPickup2D>();
        private LevelSession2D level;

        private void Awake() { level = GetComponent<LevelSession2D>(); }

        public void BeginRun()
        {
            Clear();
            if (!level || level.TrainingMode || Positions == null) return;
            // Sockets are rebuilt only between runs, when designers may have changed their configuration.
            foreach (var pickup in pickups) if (pickup) { pickup.gameObject.SetActive(false); Destroy(pickup.gameObject); }
            pickups.Clear();
            for (int i = 0; i < Positions.Length; i++)
            {
                var root = new GameObject("Skill Pickup " + (i + 1)); root.transform.SetParent(transform, false);
                root.transform.position = Positions[i];
                var pickup = root.AddComponent<SkillPickup2D>();
                pickup.Configure(this, level.Hunt ? level.Hunt.MarkerSprite : null);
                if (Random.value < Mathf.Clamp01(SpawnChance))
                    pickup.SetSkill(HunterSkills.Catalogue[RandomizeSkills ? Random.Range(0, HunterSkills.Catalogue.Count) : i % HunterSkills.Catalogue.Count].Id);
                pickups.Add(pickup);
            }
        }

        public void Clear() { foreach (var pickup in pickups) if (pickup) pickup.Clear(); }

        private void Update()
        {
            foreach (var pickup in pickups)
                if (pickup) pickup.Show(level && !level.TrainingMode &&
                    (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused) && TerrainReady(pickup));
        }

        private bool TerrainReady(SkillPickup2D pickup)
        {
            if (!level || !level.World || !level.World.IsReady) return false;
            var map = level.World.Map; var cell = map.WorldToCell(pickup.transform.position);
            return map.IsCellLoaded(cell.x, cell.y);
        }

        public SkillPickup2D FindNearest(PlayerController2D player)
        {
            if (!isActiveAndEnabled || !level || level.TrainingMode || player != level.Player) return null;
            SkillPickup2D nearest = null; float distance = float.PositiveInfinity;
            foreach (var pickup in pickups)
            {
                if (!pickup || !pickup.isActiveAndEnabled || HunterSkills.Find(pickup.Skill) == null || !TerrainReady(pickup) || !pickup.InReach(player)) continue;
                float candidate = ((Vector2)(pickup.transform.position - player.transform.position)).sqrMagnitude;
                if (candidate < distance) { nearest = pickup; distance = candidate; }
            }
            return nearest;
        }

        internal bool CanCollect(SkillPickup2D pickup, PlayerController2D player)
        {
            return isActiveAndEnabled && level && !level.TrainingMode && level.IsGameplayActive && player == level.Player &&
                player && player.CanInteract && pickup && pickup.isActiveAndEnabled && pickup.Owner == this && pickups.Contains(pickup) &&
                !pickup.Used && HunterSkills.Find(pickup.Skill) != null && TerrainReady(pickup) && pickup.InReach(player);
        }
    }
}

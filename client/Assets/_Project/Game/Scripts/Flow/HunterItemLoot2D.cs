using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Gameplay
{
    [DisallowMultipleComponent, AddComponentMenu("BigWorld/Game/Hunter Item Loot 2D")]
    public sealed class HunterItemLoot2D : MonoBehaviour
    {
        public Vector2[] SupplyPositions = { new Vector2(9, 2.6f), new Vector2(30, 2.6f), new Vector2(54, 2.6f) };
        public Vector2[] PickupPositions = { new Vector2(22, 2.6f), new Vector2(47, 2.6f), new Vector2(69, 2.6f) };
        [Range(0, 1)] public float PickupChance = .75f;
        public HunterConsumableWeight[] Consumables = {
            new HunterConsumableWeight { Item = HunterItemId.HealthShot, Weight = 3 },
            new HunterConsumableWeight { Item = HunterItemId.Dynamite, Weight = 1 }
        };
        public IList<ItemSupplyPoint2D> Points => points.AsReadOnly();
        private readonly List<ItemSupplyPoint2D> points = new List<ItemSupplyPoint2D>();
        private LevelSession2D level;
        private void Awake() { level = GetComponent<LevelSession2D>(); }
        public void BeginRun()
        {
            foreach (var point in points) if (point) { point.gameObject.SetActive(false); Destroy(point.gameObject); }
            points.Clear();
            if (!isActiveAndEnabled || !level || level.TrainingMode) return;
            if (SupplyPositions != null) foreach (var position in SupplyPositions) MakePoint(position, true, 0);
            if (PickupPositions != null) foreach (var position in PickupPositions)
            {
                var item = Random.value < Mathf.Clamp01(PickupChance) ? HunterItemSupply.ChooseConsumable(Consumables, max => Random.Range(0, max)) : 0;
                MakePoint(position, false, item);
            }
        }
        private void MakePoint(Vector2 position, bool supply, HunterItemId item)
        {
            var root = new GameObject(supply ? "Item Supply" : "Consumable Pickup"); root.transform.SetParent(transform, false);
            root.transform.position = position;
            var point = root.AddComponent<ItemSupplyPoint2D>();
            point.Configure(this, level.Hunt ? level.Hunt.MarkerSprite : null, supply, item); points.Add(point);
        }
        public void Clear() { foreach (var point in points) if (point) point.Clear(); }
        private void Update()
        {
            foreach (var point in points) if (point) point.Show(level && !level.TrainingMode && isActiveAndEnabled &&
                (level.Phase == LevelPhase.Playing || level.Phase == LevelPhase.Paused) && TerrainReady(point));
        }
        private void OnDisable() { foreach (var point in points) if (point) point.Show(false); }
        private bool TerrainReady(ItemSupplyPoint2D point)
        {
            if (!level || !level.World || !level.World.IsReady) return false;
            var map = level.World.Map; var cell = map.WorldToCell(point.transform.position);
            return map.IsCellLoaded(cell.x, cell.y);
        }
        public ItemSupplyPoint2D FindNearest(PlayerController2D player)
        {
            if (!isActiveAndEnabled || !level || level.TrainingMode || player != level.Player) return null;
            ItemSupplyPoint2D nearest = null; float score = float.PositiveInfinity;
            foreach (var point in points)
            {
                if (!point || !point.isActiveAndEnabled || !point.Available || !TerrainReady(point) || !point.InReach(player)) continue;
                float candidate = ((Vector2)(point.transform.position - player.transform.position)).sqrMagnitude + (point.Used ? 10000 : 0);
                if (candidate < score) { nearest = point; score = candidate; }
            }
            return nearest;
        }
        internal bool CanCollect(ItemSupplyPoint2D point, PlayerController2D player)
        {
            return isActiveAndEnabled && level && !level.TrainingMode && level.IsGameplayActive && player == level.Player &&
                player && player.CanInteract && point && point.isActiveAndEnabled && point.Owner == this && points.Contains(point) &&
                point.Available && !point.Used && TerrainReady(point) && point.InReach(player);
        }
    }
}

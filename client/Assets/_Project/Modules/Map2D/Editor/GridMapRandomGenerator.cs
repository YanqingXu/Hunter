using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed class GridMapRandomOptions
    {
        public RectInt Region;
        public MapTileType Ground;
        public MapTileType Barrel;
        public MapTileType Chest;
        public MapTileType Spawn;
        public bool GenerateTerrain = true;
        public bool PlaceSpawn = true;
        public int? BarrelCount;
        public int? ChestCount;
        public int Seed;
    }

    /// <summary>An immutable proposal. Creating it never changes the source map.</summary>
    public sealed class GridMapRandomPlan
    {
        private readonly GridMapAsset source;
        private readonly int sourceWidth;
        private readonly int sourceHeight;
        private readonly float sourceCellSize;
        private readonly Vector2 sourceOrigin;
        private readonly RectInt region;
        private readonly int seed;
        private readonly int barrelCount;
        private readonly int chestCount;
        private readonly int spawnCount;
        private readonly int availableSlots;
        private readonly MapTileType[] originalTerrain;
        private readonly MapTileType[] originalObjects;
        private readonly MapTileType[] originalSupportRow;
        private readonly MapTileType[] plannedTerrain;
        private readonly MapTileType[] plannedObjects;
        private readonly List<TypeState> typeStates = new List<TypeState>();

        private sealed class TypeState
        {
            public MapTileType Type;
            public MapLayer Layer;
            public MapTileCollisionMode Collision;
        }

        public RectInt Region { get { return region; } }
        public int Seed { get { return seed; } }
        public int BarrelCount { get { return barrelCount; } }
        public int ChestCount { get { return chestCount; } }
        public int SpawnCount { get { return spawnCount; } }
        /// <summary>Total supported empty terrain cells before reserving any objects or spawn.</summary>
        public int AvailableSlots { get { return availableSlots; } }

        internal GridMapRandomPlan(GridMapAsset map, RectInt area, int randomSeed,
            MapTileType[] beforeTerrain, MapTileType[] beforeObjects, MapTileType[] supportRow,
            MapTileType[] afterTerrain, MapTileType[] afterObjects,
            int barrels, int chests, int spawns, int capacity, IEnumerable<MapTileType> selectedTypes)
        {
            source = map;
            sourceWidth = map.Width;
            sourceHeight = map.Height;
            sourceCellSize = map.CellSize;
            sourceOrigin = map.Origin;
            region = area;
            seed = randomSeed;
            originalTerrain = beforeTerrain;
            originalObjects = beforeObjects;
            originalSupportRow = supportRow;
            plannedTerrain = afterTerrain;
            plannedObjects = afterObjects;
            barrelCount = barrels;
            chestCount = chests;
            spawnCount = spawns;
            availableSlots = capacity;

            var captured = new HashSet<MapTileType>();
            CaptureTypes(beforeTerrain, captured);
            CaptureTypes(beforeObjects, captured);
            CaptureTypes(supportRow, captured);
            CaptureTypes(selectedTypes, captured);
        }

        /// <summary>
        /// Applies both layers inside Region and returns the number of changed layer cells.
        /// All source state is checked before the first write. An outdated proposal throws
        /// InvalidOperationException without changing any cell. Editor callers own Undo/save.
        /// </summary>
        public int Apply(GridMapAsset map)
        {
            ValidateSource(map);
            int changed = 0;
            for (int localY = 0; localY < region.height; localY++)
            {
                for (int localX = 0; localX < region.width; localX++)
                {
                    int index = localY * region.width + localX;
                    int x = region.x + localX;
                    int y = region.y + localY;
                    if (map.SetCell(x, y, plannedTerrain[index], MapLayer.Terrain)) changed++;
                    if (map.SetCell(x, y, plannedObjects[index], MapLayer.Objects)) changed++;
                }
            }
            return changed;
        }

        private void ValidateSource(GridMapAsset map)
        {
            if (map == null || !ReferenceEquals(map, source)) throw Stale("地图已更换");
            if (map.Width != sourceWidth || map.Height != sourceHeight || !map.CellSize.Equals(sourceCellSize) || !map.Origin.Equals(sourceOrigin))
                throw Stale("地图尺寸或原点已变化");
            foreach (TypeState state in typeStates)
            {
                if (state.Type == null || state.Type.Layer != state.Layer || state.Type.Collision != state.Collision)
                    throw Stale("相关格子类型的图层或碰撞已变化");
            }
            for (int localY = 0; localY < region.height; localY++)
            {
                for (int localX = 0; localX < region.width; localX++)
                {
                    int index = localY * region.width + localX;
                    int x = region.x + localX;
                    int y = region.y + localY;
                    if (map.GetCell(x, y, MapLayer.Terrain) != originalTerrain[index] || map.GetCell(x, y, MapLayer.Objects) != originalObjects[index])
                        throw Stale("生成区域内的地图内容已变化");
                }
            }
            if (originalSupportRow != null)
            {
                for (int localX = 0; localX < region.width; localX++)
                    if (map.GetCell(region.x + localX, region.y - 1, MapLayer.Terrain) != originalSupportRow[localX])
                        throw Stale("区域下方的支撑地形已变化");
            }
        }

        private void CaptureTypes(IEnumerable<MapTileType> values, HashSet<MapTileType> captured)
        {
            if (values == null) return;
            foreach (MapTileType type in values)
            {
                if (type == null || !captured.Add(type)) continue;
                typeStates.Add(new TypeState { Type = type, Layer = type.Layer, Collision = type.Collision });
            }
        }

        private static InvalidOperationException Stale(string reason)
        {
            return new InvalidOperationException("生成方案已过期：" + reason + "，请重新生成方案。");
        }
    }

    public static class GridMapRandomGenerator
    {
        /// <summary>Builds and validates a deterministic plan without editing assets or Unity's random state.</summary>
        public static bool TryCreatePlan(GridMapAsset map, GridMapRandomOptions options,
            out GridMapRandomPlan plan, out string error)
        {
            plan = null;
            error = ValidateOptions(map, options);
            if (error != null) return false;

            RectInt region = options.Region;
            int count = region.width * region.height;
            var originalTerrain = new MapTileType[count];
            var originalObjects = new MapTileType[count];
            for (int y = 0; y < region.height; y++)
            {
                for (int x = 0; x < region.width; x++)
                {
                    int index = y * region.width + x;
                    originalTerrain[index] = map.GetCell(region.x + x, region.y + y, MapLayer.Terrain);
                    originalObjects[index] = map.GetCell(region.x + x, region.y + y, MapLayer.Objects);
                }
            }
            MapTileType[] supportRow = null;
            if (!options.GenerateTerrain && region.y > 0)
            {
                supportRow = new MapTileType[region.width];
                for (int x = 0; x < region.width; x++) supportRow[x] = map.GetCell(region.x + x, region.y - 1, MapLayer.Terrain);
            }

            var random = new System.Random(options.Seed);
            var terrain = options.GenerateTerrain ? new MapTileType[count] : (MapTileType[])originalTerrain.Clone();
            if (options.GenerateTerrain) GenerateTerrain(terrain, region.width, region.height, options.Ground, random);
            var candidates = FindSupportedSlots(terrain, supportRow, region.width, region.height);
            int capacity = candidates.Count;
            int spawns = options.PlaceSpawn ? 1 : 0;
            long required = (long)(options.BarrelCount ?? 0) + (options.ChestCount ?? 0) + spawns;
            if (required > capacity)
            {
                error = "生成区域只有 " + capacity + " 个有地形支撑的空格，指定物件与出生点共需要 " + required + " 个。请扩大区域、减少数量或补充支撑地形。";
                return false;
            }

            int remaining = capacity - (int)required;
            // Random objects occupy at most one quarter of all candidate cells. Each
            // random type gets a cap of ceil(capacity / 8), and exact counts reserve first.
            int randomBudget = Math.Min(remaining, capacity / 4);
            int perTypeCap = (capacity + 7) / 8;
            int barrels = options.BarrelCount ?? PickRandomCount(random, ref randomBudget, perTypeCap);
            int chests = options.ChestCount ?? PickRandomCount(random, ref randomBudget, perTypeCap);
            var objects = new MapTileType[count];

            if (spawns != 0)
            {
                int best = 0;
                for (int i = 1; i < candidates.Count; i++)
                {
                    Vector2Int candidate = candidates[i];
                    Vector2Int current = candidates[best];
                    if (candidate.x < current.x || (candidate.x == current.x && candidate.y < current.y)) best = i;
                }
                Vector2Int position = candidates[best];
                objects[position.y * region.width + position.x] = options.Spawn;
                candidates.RemoveAt(best);
            }

            int objectCount = barrels + chests;
            for (int i = 0; i < objectCount; i++)
            {
                int chosen = random.Next(i, candidates.Count);
                Vector2Int position = candidates[chosen];
                candidates[chosen] = candidates[i];
                candidates[i] = position;
                objects[position.y * region.width + position.x] = i < barrels ? options.Barrel : options.Chest;
            }

            var selectedTypes = new List<MapTileType>();
            if (options.GenerateTerrain) selectedTypes.Add(options.Ground);
            if (!options.BarrelCount.HasValue || options.BarrelCount.Value > 0) selectedTypes.Add(options.Barrel);
            if (!options.ChestCount.HasValue || options.ChestCount.Value > 0) selectedTypes.Add(options.Chest);
            if (options.PlaceSpawn) selectedTypes.Add(options.Spawn);
            plan = new GridMapRandomPlan(map, region, options.Seed, originalTerrain, originalObjects, supportRow,
                terrain, objects, barrels, chests, spawns, capacity, selectedTypes);
            error = null;
            return true;
        }

        private static string ValidateOptions(GridMapAsset map, GridMapRandomOptions options)
        {
            if (map == null) return "请先选择地图。";
            if (options == null) return "缺少随机生成设置。";
            RectInt region = options.Region;
            long right = (long)region.x + region.width;
            long top = (long)region.y + region.height;
            if (region.width <= 0 || region.height <= 0 || region.x < 0 || region.y < 0 || right > map.Width || top > map.Height)
                return "生成区域必须是完全位于地图内的非空矩形。";
            if (options.GenerateTerrain && region.height < 2) return "生成地形时区域高度至少需要 2 格，才能保留地面上方的空间。";
            if (options.BarrelCount.HasValue && options.BarrelCount.Value < 0) return "炸药桶数量不能为负数。";
            if (options.ChestCount.HasValue && options.ChestCount.Value < 0) return "宝箱数量不能为负数。";
            if (options.GenerateTerrain && (options.Ground == null || options.Ground.Layer != MapLayer.Terrain || !options.Ground.BlocksMovement))
                return "请选择属于地形层、具有实体或单向碰撞的地面类型。";

            bool barrelsEnabled = !options.BarrelCount.HasValue || options.BarrelCount.Value > 0;
            bool chestsEnabled = !options.ChestCount.HasValue || options.ChestCount.Value > 0;
            if (barrelsEnabled && (options.Barrel == null || options.Barrel.Layer != MapLayer.Objects))
                return "请选择属于物件层的炸药桶类型，或把数量明确设为 0。";
            if (chestsEnabled && (options.Chest == null || options.Chest.Layer != MapLayer.Objects))
                return "请选择属于物件层的宝箱类型，或把数量明确设为 0。";
            if (options.Barrel != null && options.Chest != null && options.Barrel == options.Chest) return "炸药桶与宝箱必须使用不同的格子类型；数量为 0 时可将该类型留空。";
            if (options.PlaceSpawn)
            {
                if (options.Spawn == null || options.Spawn.Layer != MapLayer.Objects) return "请选择属于物件层的出生点类型，或关闭“生成 1 个出生点”。";
                if (options.Spawn == options.Barrel || options.Spawn == options.Chest)
                    return "出生点必须使用不同于炸药桶和宝箱的格子类型。";
            }
            return null;
        }

        private static int PickRandomCount(System.Random random, ref int budget, int cap)
        {
            int count = random.Next(Math.Min(budget, cap) + 1);
            budget -= count;
            return count;
        }

        private static List<Vector2Int> FindSupportedSlots(MapTileType[] terrain, MapTileType[] supportRow, int width, int height)
        {
            var result = new List<Vector2Int>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (terrain[y * width + x] != null) continue;
                    MapTileType below = y > 0 ? terrain[(y - 1) * width + x] : supportRow == null ? null : supportRow[x];
                    if (below != null && below.BlocksMovement) result.Add(new Vector2Int(x, y));
                }
            }
            return result;
        }

        private static void GenerateTerrain(MapTileType[] terrain, int width, int height, MapTileType ground, System.Random random)
        {
            int maxGroundHeight = Math.Max(1, Math.Min(Math.Min(6, height / 5), height - 1));
            int groundHeight = random.Next(1, maxGroundHeight + 1);
            for (int x = 0; x < width; x++)
            {
                if (x > 0 && random.NextDouble() < 0.45)
                    groundHeight = Math.Max(1, Math.Min(maxGroundHeight, groundHeight + random.Next(-1, 2)));
                for (int y = 0; y < groundHeight; y++) terrain[y * width + x] = ground;
            }

            int firstPlatformRow = maxGroundHeight + 2;
            int lastPlatformRow = height - 2;
            if (width < 2 || firstPlatformRow > lastPlatformRow) return;
            int desiredPlatforms = Math.Min(64, Math.Max(1, width / 8 + height / 8));
            int placed = 0;
            for (int attempt = 0; attempt < desiredPlatforms * 6 && placed < desiredPlatforms; attempt++)
            {
                int length = random.Next(2, Math.Min(7, width) + 1);
                int startX = random.Next(0, width - length + 1);
                int y = random.Next(firstPlatformRow, lastPlatformRow + 1);
                bool empty = true;
                for (int x = startX; x < startX + length; x++)
                {
                    if (terrain[y * width + x] != null || terrain[(y - 1) * width + x] != null || terrain[(y + 1) * width + x] != null)
                    {
                        empty = false;
                        break;
                    }
                }
                if (!empty) continue;
                for (int x = startX; x < startX + length; x++) terrain[y * width + x] = ground;
                placed++;
            }
        }
    }
}

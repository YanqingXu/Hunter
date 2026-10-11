using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Editor
{
    /// <summary>
    /// Branching castle and underground routes inspired by art/地图参考素材/地图1.png.
    /// Interior connectivity is an authoring check, not a substitute for a playthrough.
    /// Existing authored assets are preserved by the generator.
    /// </summary>
    public static class HunterReferenceMapLayout
    {
        public const string Root = "Assets/_Project/Game/SSWMaps/ReferenceCitadel";
        public const string MapPath = Root + "/Data/ReferenceCitadel.asset";
        public const string PalettePath = Root + "/Data/ReferencePalette.asset";
        public const int Width = 192;
        public const int Height = 144;
        private const string SourcePath = "Assets/SSW_SET1/Sliced/MainLevBuild.png";
        private const string TileFolder = Root + "/Data/TileTypes";
        private static Dictionary<string, Sprite> sourceSprites;

        // Coordinates identify the cell above the support tile: the player's feet are y + .05.
        public static readonly Vector2Int StartCell = new Vector2Int(28, 70);
        public static readonly Vector2Int ExitCell = new Vector2Int(128, 32);
        public static readonly Vector2Int[] GuardCells =
        {
            new Vector2Int(102, 84), new Vector2Int(75, 118), new Vector2Int(62, 28)
        };
        public static readonly Vector2Int[] CheckpointCells =
        {
            new Vector2Int(82, 84), new Vector2Int(128, 120),
            new Vector2Int(74, 28), new Vector2Int(115, 32)
        };

        // Theme 0: castle; 1: sewer/cavern; 2: garden. Rect uses exclusive xMax/yMax.
        public struct Region
        {
            public string Name;
            public RectInt Rect;
            public int Theme;
            public Region(string name, RectInt rect, int theme)
            { Name = name; Rect = rect; Theme = theme; }
        }

        public static readonly Region[] Regions =
        {
            new Region("城墙边陲", new RectInt(24, 70, 24, 6), 0),
            new Region("十字枢纽", new RectInt(74, 84, 38, 8), 0),
            new Region("中枢礼拜堂", new RectInt(87, 92, 22, 8), 0),
            new Region("排水长廊", new RectInt(30, 99, 47, 6), 1),
            new Region("排水竖井", new RectInt(74, 84, 8, 21), 1),
            new Region("上层城堡大殿", new RectInt(56, 118, 44, 18), 0),
            new Region("西侧偏殿", new RectInt(52, 120, 6, 8), 0),
            new Region("上层长廊", new RectInt(96, 120, 64, 6), 0),
            new Region("上层拱顶", new RectInt(106, 126, 26, 14), 0),
            new Region("东侧高塔", new RectInt(140, 80, 8, 46), 0),
            new Region("高塔西井", new RectInt(124, 96, 8, 22), 0),
            new Region("高塔上回廊", new RectInt(124, 112, 24, 6), 0),
            new Region("高塔中回廊", new RectInt(124, 104, 24, 6), 0),
            new Region("高塔下回廊", new RectInt(124, 96, 24, 6), 0),
            new Region("东侧外廊", new RectInt(146, 104, 38, 6), 0),
            new Region("东侧圣物室", new RectInt(166, 104, 18, 10), 0),
            new Region("高塔侧室", new RectInt(148, 88, 18, 8), 0),
            new Region("枢纽东桥", new RectInt(116, 80, 31, 6), 0),
            new Region("中央深井", new RectInt(88, 42, 8, 58), 1),
            new Region("深井侧廊", new RectInt(88, 66, 20, 6), 1),
            new Region("地下前室", new RectInt(94, 60, 16, 10), 1),
            new Region("深井下层廊", new RectInt(74, 42, 24, 6), 1),
            new Region("有罪者大道", new RectInt(24, 62, 46, 6), 1),
            new Region("地下连接廊", new RectInt(68, 62, 28, 6), 1),
            new Region("宝库", new RectInt(18, 48, 34, 8), 1),
            new Region("宝库上廊", new RectInt(26, 56, 24, 6), 1),
            new Region("宝库西井", new RectInt(32, 48, 8, 20), 1),
            new Region("宝库东井", new RectInt(44, 28, 8, 40), 1),
            new Region("左侧军械库", new RectInt(8, 34, 44, 6), 1),
            new Region("下层战斗大厅", new RectInt(50, 28, 36, 10), 1),
            new Region("下层返回廊", new RectInt(82, 28, 14, 6), 1),
            new Region("中央下井", new RectInt(88, 28, 8, 20), 1),
            new Region("底部下行井", new RectInt(50, 20, 8, 14), 1),
            new Region("深层回路", new RectInt(78, 8, 26, 6), 1),
            new Region("植物园回井", new RectInt(96, 8, 8, 30), 2),
            new Region("植物园长廊", new RectInt(100, 32, 34, 6), 2),
            new Region("植物园出口", new RectInt(122, 32, 16, 10), 2),
        };

        /// <summary>Fresh [x,y] mask of the connected, empty interior. Exterior space stays empty.</summary>
        public static bool[,] CreateInteriorMask()
        {
            var interior = new bool[Width, Height];
            foreach (Region region in Regions) Carve(interior, region.Rect);
            // Each tread is two cells wide and rises/drops by only one cell.
            StairHall(interior, 46, 74, 70, 1);
            StairHall(interior, 110, 118, 84, -1);
            StairHall(interior, 56, 80, 20, -1);
            return interior;
        }

        public static Sprite SourceSprite(string name)
        {
            if (sourceSprites == null)
            {
                sourceSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(SourcePath))
                    if (asset is Sprite sprite) sourceSprites[sprite.name] = sprite;
            }
            if (name != null && sourceSprites.TryGetValue(name, out Sprite found) && found) return found;
            throw new InvalidOperationException("缺少 Sliced 地图切片：" + name);
        }

        public static MapTilePalette LoadPalette()
            => AssetDatabase.LoadAssetAtPath<MapTilePalette>(PalettePath);

        public static GridMapAsset EnsureMap()
        {
            Folder(TileFolder);
            var shell = new MapTileType[3, 2];
            Color[] shellColors =
            {
                new Color(.90f, .85f, .96f), new Color(.63f, .76f, .87f),
                new Color(.73f, .86f, .71f)
            };
            var paletteTypes = new List<MapTileType>();
            for (int theme = 0; theme < 3; theme++)
                for (int variant = 0; variant < 2; variant++)
                {
                    string key = "Shell" + theme + (variant == 0 ? "A" : "B");
                    shell[theme, variant] = EnsureTile(key, "城堡壳墙 / 区域 " + theme + " / " + variant,
                        SourceSprite(variant == 0 ? "MainLevBuild_780" : "MainLevBuild_781"),
                        shellColors[theme], MapTileCollisionMode.Solid);
                    paletteTypes.Add(shell[theme, variant]);
                }
            var platform = new[]
            {
                EnsureTile("StepBridgeA", "错位木桥跳台 A", SourceSprite("MainLevBuild_129"),
                    new Color(.85f, .88f, .95f), MapTileCollisionMode.OneWay),
                EnsureTile("StepBridgeB", "错位木桥跳台 B", SourceSprite("MainLevBuild_130"),
                    new Color(.85f, .88f, .95f), MapTileCollisionMode.OneWay)
            };
            paletteTypes.AddRange(platform);
            MapTileType start = ExistingMarker("PlayerStart");
            MapTileType exit = ExistingMarker("LevelExit");
            paletteTypes.Add(start);
            paletteTypes.Add(exit);

            if (!LoadPalette())
            {
                var palette = ScriptableObject.CreateInstance<MapTilePalette>();
                palette.Types = paletteTypes;
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            GridMapAsset existing = AssetDatabase.LoadAssetAtPath<GridMapAsset>(MapPath);
            if (existing) return existing;

            bool[,] interior = CreateInteriorMask();
            ValidateMask(interior);
            var map = ScriptableObject.CreateInstance<GridMapAsset>();
            map.name = "ReferenceCitadel";
            map.Initialize(Width, Height, 1f);
            // Only the one-cell shell of the carved network is solid. Diagonal neighbors
            // close outside corners, without covering the silhouette in a giant rectangle.
            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                {
                    if (interior[x, y] || !TouchesInterior(interior, x, y)) continue;
                    int theme = ThemeAt(x, y);
                    map.SetCell(x, y, shell[theme, (x * 7 + y * 3) % 5 == 0 ? 1 : 0]);
                }

            // Eight-cell shafts have three-cell shelves at either wall. Their central
            // two-cell lane is never covered, so descending does not require a drop key.
            ShaftPlatforms(map, interior, platform, 74, 84, 105);
            ShaftPlatforms(map, interior, platform, 140, 80, 126);
            ShaftPlatforms(map, interior, platform, 124, 96, 118);
            ShaftPlatforms(map, interior, platform, 88, 42, 100);
            ShaftPlatforms(map, interior, platform, 32, 48, 68);
            ShaftPlatforms(map, interior, platform, 44, 28, 68);
            ShaftPlatforms(map, interior, platform, 88, 28, 48);
            ShaftPlatforms(map, interior, platform, 50, 20, 34);
            ShaftPlatforms(map, interior, platform, 96, 8, 38);

            // Castle mezzanines remain optional. Every upward link is at most two cells.
            Bridge(map, interior, platform, 60, 62, 119);
            Bridge(map, interior, platform, 64, 66, 121);
            Bridge(map, interior, platform, 68, 72, 123);
            Bridge(map, interior, platform, 74, 89, 125);
            Bridge(map, interior, platform, 106, 108, 121);
            Bridge(map, interior, platform, 110, 113, 123);
            Bridge(map, interior, platform, 115, 128, 125);
            Bridge(map, interior, platform, 98, 105, 91);

            map.SetCell(StartCell.x, StartCell.y, start, MapLayer.Objects);
            map.SetCell(ExitCell.x, ExitCell.y, exit, MapLayer.Objects);
            var guard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Game/Prefabs/PatrolGuard.prefab");
            if (!guard) throw new InvalidOperationException("新地图需要既有 PatrolGuard.prefab。");
            for (int i = 0; i < GuardCells.Length; i++)
                map.Spawns.Add(new MapSpawnDefinition
                {
                    Id = "trial-guard-" + (i + 1),
                    DisplayName = new[] { "中枢守卫", "大殿守卫", "地牢守卫" }[i],
                    Prefab = guard, Cell = GuardCells[i], Kind = MapSpawnKind.Monster,
                    Count = 1, MaxHealth = 50, RespawnSeconds = 0
                });

            foreach (Vector2Int cell in KeyCells()) ValidateStandingSpace(map, cell);
            AssetDatabase.CreateAsset(map, MapPath);
            AssetDatabase.SaveAssets();
            return map;
        }

        /// <summary>Returns authoring checks; this does not certify a physics playthrough.</summary>
        public static string[] ValidateDesign(GridMapAsset map)
        {
            if (!map || map.Width != Width || map.Height != Height || !Mathf.Approximately(map.CellSize, 1f))
                throw new InvalidOperationException("参考城堡地图必须为 192×144、每格 1 单位。");
            bool[,] interior = CreateInteriorMask();
            int connected = ValidateMask(interior);
            var checks = new List<string> { "通道轮廓四邻接连通：" + connected + " 格（编辑几何检查）" };
            foreach (Vector2Int cell in KeyCells())
            {
                ValidateStandingSpace(map, cell);
                checks.Add("关键点支撑与两格净空：" + cell);
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Game/Prefabs/Player.prefab");
            var controller = player ? player.GetComponent<BigWorld.Gameplay.PlayerController2D>() : null;
            var body = player ? player.GetComponent<Rigidbody2D>() : null;
            if (!controller || !body) throw new InvalidOperationException("玩家预制体移动组件缺失。");
            float gravity = Mathf.Abs(Physics2D.gravity.y * body.gravityScale);
            float jumpHeight = controller.JumpSpeed * controller.JumpSpeed / (2 * gravity);
            float rise = 2f;
            float discriminant = controller.JumpSpeed * controller.JumpSpeed - 2 * gravity * rise;
            float rangeAtRise = discriminant > 0
                ? controller.MoveSpeed * (controller.JumpSpeed + Mathf.Sqrt(discriminant)) / gravity : 0;
            if (!(gravity > 0) || jumpHeight < 2.2f || rangeAtRise < 3.2f)
                throw new InvalidOperationException("玩家跳跃参数不满足两格高错位跳台的设计余量。");
            checks.Add("按预制体参数估算：最大跳高 " + jumpHeight.ToString("F2") +
                "，升高两格的水平落地范围 " + rangeAtRise.ToString("F2") + "；尚需操作验证");
            checks.Add("竖井错位台阶每次升高两格；中心两格下落通道保留");
            return checks.ToArray();
        }

        private static IEnumerable<Vector2Int> KeyCells()
        {
            yield return StartCell; yield return ExitCell;
            foreach (Vector2Int cell in GuardCells) yield return cell;
            foreach (Vector2Int cell in CheckpointCells) yield return cell;
        }

        private static MapTileType ExistingMarker(string name)
        {
            var marker = AssetDatabase.LoadAssetAtPath<MapTileType>(
                "Assets/_Project/Game/SSWMaps/Data/TileTypes/" + name + ".asset");
            if (!marker) throw new InvalidOperationException("既有地图标记类型缺失：" + name);
            return marker;
        }

        private static MapTileType EnsureTile(string key, string label, Sprite sprite, Color tint,
            MapTileCollisionMode collision)
        {
            string path = TileFolder + "/" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<MapTileType>(path);
            if (existing) return existing;
            if (!sprite || Mathf.Abs(sprite.pixelsPerUnit - 32) > .01f ||
                sprite.rect.width != 32 || sprite.rect.height != 32)
                throw new InvalidOperationException("参考地图需要 32px / 32 PPU 的完整切片：" + key);
            var type = ScriptableObject.CreateInstance<MapTileType>();
            type.Id = "hunter.referencecitadel." + key.ToLowerInvariant();
            type.DisplayName = label; type.Layer = MapLayer.Terrain; type.Sprite = sprite;
            type.Tint = tint; type.Collision = collision; type.Walkable = false;
            type.Tags.Add(collision == MapTileCollisionMode.OneWay ? "platform" : "ground");
            type.Description = collision == MapTileCollisionMode.OneWay
                ? "参考城堡错位跳台：向上可穿过，升高间隔两格，竖井中心留出下落槽。"
                : "直接引用 Sliced 原始完整砖块；每格一单位，实体壳墙，不修改源素材。";
            AssetDatabase.CreateAsset(type, path);
            return type;
        }

        private static void Carve(bool[,] mask, RectInt rect)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    if (!Inside(x, y)) throw new InvalidOperationException("房间超出地图范围：" + rect);
                    mask[x, y] = true;
                }
        }

        private static void StairHall(bool[,] mask, int startX, int endX, int floor, int direction)
        {
            for (int x = startX; x <= endX; x++)
                Carve(mask, new RectInt(x, floor + direction * ((x - startX) / 2), 1, 6));
        }

        private static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        private static bool TouchesInterior(bool[,] mask, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (Inside(x + dx, y + dy) && mask[x + dx, y + dy]) return true;
            return false;
        }

        private static int ThemeAt(int x, int y)
        {
            // Overlapping connectors take the last region's theme; no existing tile type changes.
            for (int i = Regions.Length - 1; i >= 0; i--)
            {
                RectInt r = Regions[i].Rect;
                if (x >= r.xMin - 1 && x <= r.xMax && y >= r.yMin - 1 && y <= r.yMax)
                    return Regions[i].Theme;
            }
            return y < 68 ? 1 : 0;
        }

        private static void ShaftPlatforms(GridMapAsset map, bool[,] mask, MapTileType[] tiles,
            int left, int bottom, int top)
        {
            int index = 0;
            for (int y = bottom + 1; y < top - 2; y += 2, index++)
            {
                int x = index % 2 == 0 ? left : left + 5;
                Bridge(map, mask, tiles, x, x + 2, y);
            }
        }

        private static void Bridge(GridMapAsset map, bool[,] mask, MapTileType[] tiles,
            int left, int right, int y)
        {
            for (int x = left; x <= right; x++)
            {
                // At closed ceilings no platform may leave less than two cells of standing space.
                if (!Inside(x, y + 2) || !mask[x, y] || !mask[x, y + 1] || !mask[x, y + 2]) continue;
                var current = map.GetCell(x, y);
                if (current && current.Collision == MapTileCollisionMode.Solid) continue;
                map.SetCell(x, y, tiles[(x + y) & 1]);
            }
        }

        private static int ValidateMask(bool[,] mask)
        {
            var visited = new bool[Width, Height];
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(StartCell); visited[StartCell.x, StartCell.y] = true;
            int count = 0;
            Vector2Int[] directions = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            while (pending.Count != 0)
            {
                Vector2Int cell = pending.Dequeue(); count++;
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (!Inside(next.x, next.y) || !mask[next.x, next.y] || visited[next.x, next.y]) continue;
                    visited[next.x, next.y] = true; pending.Enqueue(next);
                }
            }
            int total = 0;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++) if (mask[x, y]) total++;
            if (count != total) throw new InvalidOperationException("通道轮廓存在断开的房间：" + count + " / " + total);
            foreach (Vector2Int cell in KeyCells())
                if (!Inside(cell.x, cell.y) || !visited[cell.x, cell.y])
                    throw new InvalidOperationException("关键点不在连通通道内：" + cell);
            return count;
        }

        private static void ValidateStandingSpace(GridMapAsset map, Vector2Int cell)
        {
            MapTileType support = map.GetCell(cell.x, cell.y - 1);
            if (!support || support.Collision != MapTileCollisionMode.Solid)
                throw new InvalidOperationException("关键点缺少实体支撑：" + cell);
            for (int y = cell.y; y <= cell.y + 1; y++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                {
                    MapTileType tile = map.GetCell(cell.x, y, layer);
                    if (tile && tile.Collision == MapTileCollisionMode.Solid)
                        throw new InvalidOperationException("关键点站立净空不足两格：" + cell);
                }
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

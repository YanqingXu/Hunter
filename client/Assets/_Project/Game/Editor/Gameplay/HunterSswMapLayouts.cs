using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Editor
{
    /// <summary>Hand-authored SSW layouts using the existing two-layer Map2D authoring model.</summary>
    public static class HunterSswMapLayouts
    {
        public const string Root = "Assets/_Project/Game/SSWMaps";
        private const string PalettePath = Root + "/Data/SSWPalette.asset";
        private static readonly Dictionary<string, MapTileType> Tiles = new Dictionary<string, MapTileType>();

        public static GridMapAsset[] EnsureMaps()
        {
            HunterSswMaterials.Ensure();
            Folder(Root + "/Data/Maps");
            Folder(Root + "/Data/TileTypes");
            Tiles.Clear();
            foreach (string key in new[] { "ForestTop", "ForestFill", "ForestFillAlt", "ForestLeft", "ForestRight",
                "CaveTop", "CaveFill", "CaveFillAlt", "CaveLeft", "CaveRight", "CaveRoof",
                "KeepTop", "KeepFill", "KeepFillAlt", "KeepLeft", "KeepRight" })
                EnsureTile(key, MapLayer.Terrain, MapTileCollisionMode.Solid);
            EnsureTile("Bridge", MapLayer.Terrain, MapTileCollisionMode.OneWay);
            EnsureTile("BridgeAlt", MapLayer.Terrain, MapTileCollisionMode.OneWay);
            foreach (string key in new[] { "Grass", "BushTile", "RockTile" })
                EnsureTile(key, MapLayer.Objects, MapTileCollisionMode.None);
            EnsureMarker("PlayerStart", "玩家出生点", "spawn");
            EnsureMarker("LevelExit", "关卡出口", "exit");
            var palette = AssetDatabase.LoadAssetAtPath<MapTilePalette>(PalettePath);
            if (!palette)
            {
                palette = ScriptableObject.CreateInstance<MapTilePalette>();
                palette.Types = new List<MapTileType>(Tiles.Values);
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            var maps = new[] { EnsureMap("ForestOutpost", 0), EnsureMap("CavernPass", 1), EnsureMap("RuinedKeep", 2) };
            AssetDatabase.SaveAssets();
            return maps;
        }

        public static MapTilePalette LoadPalette() => AssetDatabase.LoadAssetAtPath<MapTilePalette>(PalettePath);
        public static Sprite Decoration(string name) => HunterSswMaterials.Sprite(name);

        public static void OpenEditor(GridMapAsset map)
        {
            GridMapEditorWindow.OpenMap(map);
            var window = EditorWindow.GetWindow<GridMapEditorWindow>();
            var settings = new SerializedObject(window);
            settings.FindProperty("palette").objectReferenceValue = LoadPalette();
            settings.FindProperty("selectedType").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            window.Repaint();
        }

        /// <summary>Only considers the main floor, never the optional route or cave ceiling.</summary>
        public static int GroundTop(GridMapAsset map, int x)
        {
            if (!map || x < 0 || x >= map.Width) return -1;
            for (int y = 5; y >= 0; --y)
            {
                var tile = map.GetCell(x, y);
                if (tile && tile.Collision == MapTileCollisionMode.Solid) return y + 1;
            }
            return -1;
        }

        private static MapTileType EnsureTile(string key, MapLayer layer, MapTileCollisionMode collision)
        {
            string path = Root + "/Data/TileTypes/" + key + ".asset";
            var type = AssetDatabase.LoadAssetAtPath<MapTileType>(path);
            if (!type)
            {
                var sprite = HunterSswMaterials.Sprite(key);
                if (!sprite) throw new InvalidOperationException("SSW 切片缺失：" + key);
                type = ScriptableObject.CreateInstance<MapTileType>();
                type.Id = "hunter.ssw." + key.ToLowerInvariant();
                type.DisplayName = TileLabel(key);
                type.Layer = layer;
                type.Sprite = sprite;
                type.Tint = Color.white;
                type.Collision = collision;
                type.Walkable = collision == MapTileCollisionMode.None;
                type.PropertySchema = AssetDatabase.LoadAssetAtPath<MapTilePropertySchema>("Assets/_Project/Game/Data/Maps/TileProperties.asset");
                type.Tags.Add(layer == MapLayer.Terrain ? (collision == MapTileCollisionMode.OneWay ? "platform" : "ground") : "decoration");
                type.Description = collision == MapTileCollisionMode.Solid ? "SSW 32px 地形：每格 1 单位，实体碰撞。" :
                    collision == MapTileCollisionMode.OneWay ? "木桥单向平台：可从下方跳上，平台上沿有碰撞。" : "SSW 装饰：无碰撞，不遮挡行走。";
                AssetDatabase.CreateAsset(type, path);
            }
            Tiles.Add(key, type);
            return type;
        }

        private static void EnsureMarker(string key, string label, string tag)
        {
            string path = Root + "/Data/TileTypes/" + key + ".asset";
            var type = AssetDatabase.LoadAssetAtPath<MapTileType>(path);
            if (!type)
            {
                type = ScriptableObject.CreateInstance<MapTileType>();
                type.Id = "hunter.ssw." + key.ToLowerInvariant(); type.DisplayName = label;
                type.Sprite = HunterSswMaterials.Sprite("Grass"); type.Layer = MapLayer.Objects;
                type.Collision = MapTileCollisionMode.None; type.Walkable = true;
                type.Tint = new Color(1, 1, 1, 0); type.Tags.Add(tag);
                type.Description = "关卡编辑标记。场景生成器配置对应出生/出口对象；运行时标记不可见。";
                AssetDatabase.CreateAsset(type, path);
            }
            Tiles.Add(key, type);
        }

        private static GridMapAsset EnsureMap(string name, int theme)
        {
            string path = Root + "/Data/Maps/" + name + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<GridMapAsset>(path);
            if (map) return map;
            map = ScriptableObject.CreateInstance<GridMapAsset>(); map.Initialize(96, 24);
            string prefix = theme == 0 ? "Forest" : theme == 1 ? "Cave" : "Keep";
            for (int x = 0; x < map.Width; ++x)
            {
                if (Gap(theme, x)) continue;
                int top = FloorRow(theme, x);
                for (int y = 0; y <= top; ++y)
                {
                    string key = y == top ? prefix + "Top" : prefix + (((x * 7 + y * 3) % 5 == 0) ? "FillAlt" : "Fill");
                    map.SetCell(x, y, Tiles[key]);
                }
            }
            // Short optional routes: every required jump remains within the actual player's range.
            if (theme == 0)
            {
                Platform(map, 18, 21, 6); Platform(map, 38, 39, 5); Platform(map, 40, 45, 6);
                Platform(map, 59, 61, 5); Platform(map, 62, 67, 7);
                SolidShelf(map, 83, 86, 5, prefix);
            }
            else if (theme == 1)
            {
                Platform(map, 15, 17, 5); Platform(map, 18, 22, 6);
                Platform(map, 38, 40, 5); Platform(map, 41, 47, 7);
                Platform(map, 64, 66, 5); Platform(map, 67, 73, 6);
                for (int x = 0; x < 96; ++x)
                {
                    int ceiling = 15 + (x / 12 % 2);
                    for (int y = ceiling; y < 24; ++y) map.SetCell(x, y, Tiles[y == ceiling ? "CaveRoof" : "CaveFill"]);
                }
            }
            else
            {
                Platform(map, 17, 21, 6); Platform(map, 38, 40, 5); Platform(map, 41, 46, 6);
                Platform(map, 62, 65, 5); Platform(map, 66, 72, 7);
                SolidShelf(map, 82, 86, 5, prefix);
            }
            // Edge walls keep combat actors inside without blocking the exit route.
            for (int y = 4; y < (theme == 1 ? 15 : 8); ++y)
            { map.SetCell(0, y, Tiles[prefix + "Fill"]); map.SetCell(95, y, Tiles[prefix + "Fill"]); }
            foreach (int x in new[] { 8, 11, 29, 37, 48, 59, 70, 81, 88 })
            {
                int floor = GroundTop(map, x);
                if (floor > 0) map.SetCell(x, floor, Tiles[theme == 0 ? (x % 3 == 0 ? "BushTile" : "Grass") : "RockTile"], MapLayer.Objects);
            }
            map.SetCell(4, GroundTop(map, 4), Tiles["PlayerStart"], MapLayer.Objects);
            map.SetCell(91, GroundTop(map, 91), Tiles["LevelExit"], MapLayer.Objects);
            var guard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Game/Prefabs/PatrolGuard.prefab");
            if (!guard) throw new InvalidOperationException("需要现有 PatrolGuard.prefab。");
            int[] guardXs = { 16, 44, 74 };
            for (int i = 0; i < guardXs.Length; ++i)
                map.Spawns.Add(new MapSpawnDefinition { Id = "trial-guard-" + (i + 1), DisplayName = ThemeLabel(theme) + "守卫 " + (i + 1),
                    Prefab = guard, Cell = new Vector2Int(guardXs[i], GroundTop(map, guardXs[i])), Kind = MapSpawnKind.Monster,
                    Count = 1, MaxHealth = 50, RespawnSeconds = 0 });
            AssetDatabase.CreateAsset(map, path);
            return map;
        }

        private static int FloorRow(int theme, int x)
        {
            if (theme == 0 && ((x >= 12 && x <= 15) || (x >= 33 && x <= 36) || (x >= 65 && x <= 69))) return 4;
            if (theme == 1 && ((x >= 34 && x <= 38) || (x >= 80 && x <= 85))) return 4;
            if (theme == 2 && ((x >= 12 && x <= 15) || (x >= 33 && x <= 36) || (x >= 55 && x <= 58))) return 4;
            return 3;
        }

        private static bool Gap(int theme, int x)
        {
            if (theme == 0) return (x >= 23 && x <= 24) || (x >= 52 && x <= 54) || (x >= 78 && x <= 79);
            if (theme == 1) return (x >= 25 && x <= 27) || (x >= 54 && x <= 55);
            return (x >= 25 && x <= 26) || (x >= 49 && x <= 51) || (x >= 77 && x <= 78);
        }

        private static void Platform(GridMapAsset map, int start, int end, int y)
        { for (int x = start; x <= end; ++x) map.SetCell(x, y, Tiles[x % 2 == 0 ? "Bridge" : "BridgeAlt"]); }
        private static void SolidShelf(GridMapAsset map, int start, int end, int y, string prefix)
        { for (int x = start; x <= end; ++x) map.SetCell(x, y, Tiles[prefix + "Top"]); }

        private static string ThemeLabel(int theme) => theme == 0 ? "林地哨站" : theme == 1 ? "地下洞窟" : "废弃要塞";
        private static string TileLabel(string key)
        {
            if (key == "Bridge" || key == "BridgeAlt") return key == "Bridge" ? "木桥平台 A" : "木桥平台 B";
            if (key == "Grass") return "草丛"; if (key == "BushTile") return "灌木"; if (key == "RockTile") return "碎石";
            string label = key.StartsWith("Forest") ? "林地" : key.StartsWith("Cave") ? "洞窟" : "要塞";
            return label + (key.EndsWith("Top") ? "地表" : key.EndsWith("Roof") ? "洞顶" : key.EndsWith("Left") ? "左边缘" :
                key.EndsWith("Right") ? "右边缘" : key.EndsWith("Alt") ? "填充 B" : "填充 A");
        }

        internal static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

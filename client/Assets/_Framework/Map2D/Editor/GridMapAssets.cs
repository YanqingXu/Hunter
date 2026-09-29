using System;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public static class GridMapAssets
    {
        public const string Root = "Assets/_Game/Data/Maps";
        public const string TypesFolder = Root + "/TileTypes";
        public const string PalettePath = Root + "/PlatformPalette.asset";
        public const string ExamplePath = "Assets/_Examples/Map2D/Data/PlatformExample.asset";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            if (separator < 0) throw new ArgumentException("Expected an Assets folder path.", nameof(path));
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        public static MapTilePalette EnsureDefaults()
        {
            GridMapPropertyAssets.ConnectExistingTypes();
            EnsureFolder(TypesFolder);
            var palette = AssetDatabase.LoadAssetAtPath<MapTilePalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<MapTilePalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
                palette.Types.Add(CreateDefault("Ground", "地面", new Color(.34f, .57f, .37f), MapLayer.Terrain,
                    MapTileCollisionMode.Solid, "ground", "实体地形。可配置精灵；默认生成方格碰撞。"));
                palette.Types.Add(CreateDefault("Spikes", "尖刺", new Color(.93f, .30f, .29f), MapLayer.Objects,
                    MapTileCollisionMode.None, "hazard", "危险物件。绑定带伤害触发器的预制体以接入游戏逻辑。"));
                palette.Types.Add(CreateDefault("SpawnPoint", "出生点", new Color(.25f, .75f, .94f), MapLayer.Objects,
                    MapTileCollisionMode.None, "spawn", "出生位置标记。通过类型或 spawn 标签查找格子。"));
                palette.Types.Add(CreateDefault("ExplosiveBarrel", "炸药桶", new Color(.95f, .52f, .18f), MapLayer.Objects,
                    MapTileCollisionMode.None, "explosive", "可绑定炸药桶预制体，由预制体实现受击与爆炸。"));
                palette.Types.Add(CreateDefault("TreasureChest", "宝箱", new Color(.95f, .77f, .27f), MapLayer.Objects,
                    MapTileCollisionMode.None, "chest", "可绑定宝箱预制体，由预制体实现互动与掉落。"));
                EditorUtility.SetDirty(palette);
            }
            EnsurePaletteType(palette, "Wall", "墙", new Color(.61f, .43f, .32f), MapLayer.Terrain,
                MapTileCollisionMode.Solid, "wall", "建筑外墙与楼板，使用实体格子碰撞。");
            EnsurePaletteType(palette, "Window", "窗户", new Color(.47f, .77f, .87f), MapLayer.Objects,
                MapTileCollisionMode.None, "window", "建筑窗户。默认可被销毁，具体行为由游戏逻辑实现。", true);
            EnsurePaletteType(palette, "WoodenDoor", "木门", new Color(.65f, .39f, .19f), MapLayer.Objects,
                MapTileCollisionMode.Solid, "door", "实体木门。默认可被销毁、可被点燃；开门与燃烧由游戏逻辑实现。", true, true);
            EnsurePaletteType(palette, "Stairs", "楼梯", new Color(.63f, .57f, .45f), MapLayer.Terrain,
                MapTileCollisionMode.OneWay, "stairs", "逐格上升的单向阶梯，可从下方穿过、从上方落地。");
            if (AssetDatabase.LoadAssetAtPath<GridMapAsset>(ExamplePath) == null)
            {
                EnsureFolder("Assets/_Examples/Map2D/Data");
                var map = ScriptableObject.CreateInstance<GridMapAsset>();
                map.Initialize(40, 22, 1f);
                MapTileType ground = Find(palette, "Ground");
                MapTileType spike = Find(palette, "Spikes");
                MapTileType spawn = Find(palette, "SpawnPoint");
                MapTileType barrel = Find(palette, "ExplosiveBarrel");
                MapTileType chest = Find(palette, "TreasureChest");
                for (int x = 0; x < map.Width; x++)
                    for (int y = 0; y < 2; y++) map.SetCell(x, y, ground);
                for (int x = 7; x <= 14; x++) map.SetCell(x, 6, ground);
                for (int x = 19; x <= 26; x++) map.SetCell(x, 10, ground);
                for (int x = 30; x <= 35; x++) map.SetCell(x, 6, ground);
                map.SetCell(3, 2, spawn, MapLayer.Objects);
                for (int x = 17; x <= 19; x++) map.SetCell(x, 2, spike, MapLayer.Objects);
                map.SetCell(26, 2, barrel, MapLayer.Objects);
                map.SetCell(31, 7, barrel, MapLayer.Objects);
                map.SetCell(11, 7, chest, MapLayer.Objects);
                map.SetCell(22, 11, chest, MapLayer.Objects);
                AssetDatabase.CreateAsset(map, ExamplePath);
            }
            AssetDatabase.SaveAssetIfDirty(palette);
            var example = AssetDatabase.LoadAssetAtPath<GridMapAsset>(ExamplePath);
            if (example != null) AssetDatabase.SaveAssetIfDirty(example);
            return palette;
        }

        private static void EnsurePaletteType(MapTilePalette palette, string id, string displayName, Color tint,
            MapLayer layer, MapTileCollisionMode collision, string tag, string description,
            bool destructible = false, bool flammable = false)
        {
            // Existing definitions and palette edits are authoritative. Add only missing building types.
            if (Find(palette, id) != null) return;
            MapTileType type = CreateDefault(id, displayName, tint, layer, collision, tag, description, destructible, flammable);
            if (palette.Types.Contains(type)) return;
            palette.Types.Add(type);
            EditorUtility.SetDirty(palette);
        }

        private static MapTileType CreateDefault(string id, string displayName, Color tint, MapLayer layer,
            MapTileCollisionMode collision, string tag, string description, bool destructible = false, bool flammable = false)
        {
            string path = TypesFolder + "/" + id + ".asset";
            var type = AssetDatabase.LoadAssetAtPath<MapTileType>(path);
            if (type != null) return type;
            if (System.IO.File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("默认格子类型路径已被其他资源占用：" + path);
            type = ScriptableObject.CreateInstance<MapTileType>();
            type.Id = id;
            type.DisplayName = displayName;
            type.Tint = tint;
            type.Sprite = MapTileIcons.EnsureSprite(id);
            if (type.Sprite != null) type.Tint = Color.white;
            type.Layer = layer;
            type.Collision = collision;
            type.Walkable = collision == MapTileCollisionMode.None;
            type.PropertySchema = GridMapPropertyAssets.EnsureDefaultSchema();
            type.Description = description;
            type.Tags.Add(tag);
            if (destructible)
                type.SetPropertyOverride(new MapTilePropertyValue { Key = MapTilePropertyKeys.Destructible, Kind = MapTilePropertyKind.Boolean, BoolValue = true });
            if (flammable)
                type.SetPropertyOverride(new MapTilePropertyValue { Key = MapTilePropertyKeys.Flammable, Kind = MapTilePropertyKind.Boolean, BoolValue = true });
            AssetDatabase.CreateAsset(type, path);
            return type;
        }

        private static MapTileType Find(MapTilePalette palette, string id)
        {
            return palette.Types.Find(type => type != null && type.Id == id);
        }

        public static GridMapAsset CreateMap(string path, int width, int height, float cellSize)
        {
            if (System.IO.File.Exists(path)) throw new InvalidOperationException("目标文件已存在，请使用新文件名。");
            var map = ScriptableObject.CreateInstance<GridMapAsset>();
            map.Initialize(width, height, cellSize);
            AssetDatabase.CreateAsset(map, path);
            AssetDatabase.SaveAssetIfDirty(map);
            return map;
        }

        public static GridMapAsset CreateMapCopy(string path, GridMapAsset source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("目标文件已存在，请使用新文件名。");
            // Cloning the map itself also works when it is a house's layout subasset.
            var copy = UnityEngine.Object.Instantiate(source);
            copy.NewIdentity();
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                AssetDatabase.CreateAsset(copy, path);
                AssetDatabase.SaveAssetIfDirty(copy);
                return copy;
            }
            catch
            {
                if (copy != null && !EditorUtility.IsPersistent(copy)) UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
        }

        [MenuItem("Tools/BigWorld/地图/定位示例地图")]
        public static void LocateExample()
        {
            EnsureDefaults();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GridMapAsset>(ExamplePath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}

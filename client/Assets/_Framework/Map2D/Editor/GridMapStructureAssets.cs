using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Creates a small editable house library without replacing existing templates.</summary>
    public static class GridMapStructureAssets
    {
        public const string Folder = GridMapAssets.Root + "/Structures";
        public const string HutPath = Folder + "/WoodenHut.asset";
        public const string HousePath = Folder + "/TwoStoreyHouse.asset";
        public const string WarehousePath = Folder + "/Warehouse.asset";

        public static List<MapStructureTemplate> EnsureDefaults()
        {
            MapTilePalette palette = GridMapAssets.EnsureDefaults();
            GridMapAssets.EnsureFolder(Folder);
            var types = new BuildingTypes(palette);
            return new List<MapStructureTemplate>
            {
                EnsureTemplate(HutPath, "小木屋", "9 × 7 侧视小屋，包含门窗、镂空房间和少量储物。", () => BuildHut(types)),
                EnsureTemplate(HousePath, "双层住宅", "13 × 11 双层住宅，楼板留有阶梯入口，可继续编辑室内布局。", () => BuildHouse(types)),
                EnsureTemplate(WarehousePath, "仓库", "15 × 8 仓库，包含储物区、阶梯和小阁楼。", () => BuildWarehouse(types))
            };
        }

        /// <summary>Lists templates throughout Assets, including templates saved outside the default library folder.</summary>
        public static List<MapStructureTemplate> LoadAll()
        {
            var templates = new List<MapStructureTemplate>();
            foreach (string guid in AssetDatabase.FindAssets("t:MapStructureTemplate", new[] { "Assets" }))
            {
                var template = AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template != null) templates.Add(template);
            }
            templates.Sort((left, right) =>
            {
                int comparison = string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
                return comparison != 0 ? comparison : string.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right), StringComparison.Ordinal);
            });
            return templates;
        }

        /// <summary>Snapshots both layers into an independently editable layout sub-asset.</summary>
        public static MapStructureTemplate CreateFromRegion(string path, string displayName, GridMapAsset map, RectInt region)
        {
            path = ValidateNewPath(path);
            GridMapAsset layout = GridMapStructureEditing.Capture(map, region);
            return SaveTemplate(path, displayName, "从地图矩形区域保存的独立布局；格子类型仍与原地图共享。", layout);
        }

        private static MapStructureTemplate EnsureTemplate(string path, string name, string description, Func<GridMapAsset> createLayout)
        {
            var existing = AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(path);
            if (existing != null) return existing;
            path = ValidateNewPath(path);
            return SaveTemplate(path, name, description, createLayout());
        }

        private static MapStructureTemplate SaveTemplate(string path, string displayName, string description, GridMapAsset layout)
        {
            MapStructureTemplate template = null;
            bool created = false;
            try
            {
                GridMapAssets.EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                template = ScriptableObject.CreateInstance<MapStructureTemplate>();
                template.name = Path.GetFileNameWithoutExtension(path);
                template.DisplayName = string.IsNullOrWhiteSpace(displayName) ? template.name : displayName.Trim();
                template.Description = description;
                AssetDatabase.CreateAsset(template, path);
                created = true;
                layout.name = "StructureLayout";
                AssetDatabase.AddObjectToAsset(layout, template);
                template.Layout = layout;
                EditorUtility.SetDirty(layout);
                EditorUtility.SetDirty(template);
                AssetDatabase.SaveAssetIfDirty(template);
                return template;
            }
            catch
            {
                // This path was verified to be new, so cleanup can only remove this failed creation.
                if (created) AssetDatabase.DeleteAsset(path);
                if (layout != null && !EditorUtility.IsPersistent(layout)) Object.DestroyImmediate(layout);
                if (template != null && !EditorUtility.IsPersistent(template)) Object.DestroyImmediate(template);
                throw;
            }
        }

        private static string ValidateNewPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择模板保存位置。", nameof(path));
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("模板必须保存为 Assets 内的 .asset 文件。", nameof(path));
            foreach (string segment in path.Split('/'))
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
                    throw new ArgumentException("模板路径不能包含空目录或相对跳转。", nameof(path));
            string assetRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assetRoot), path));
            if (!fullPath.StartsWith(assetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("模板保存位置超出 Assets。", nameof(path));
            if (File.Exists(fullPath) || Directory.Exists(fullPath) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("目标文件已存在，请使用新的模板文件名。");
            return path;
        }

        private sealed class BuildingTypes
        {
            internal readonly MapTileType Wall;
            internal readonly MapTileType Window;
            internal readonly MapTileType Door;
            internal readonly MapTileType Stairs;
            internal readonly MapTileType Barrel;
            internal readonly MapTileType Chest;

            internal BuildingTypes(MapTilePalette palette)
            {
                Wall = Find(palette, "Wall");
                Window = Find(palette, "Window");
                Door = Find(palette, "WoodenDoor");
                Stairs = Find(palette, "Stairs");
                Barrel = Find(palette, "ExplosiveBarrel");
                Chest = Find(palette, "TreasureChest");
            }

            private static MapTileType Find(MapTilePalette palette, string id)
            {
                MapTileType type = palette.Types.Find(candidate => candidate != null && candidate.Id == id);
                if (type == null) type = AssetDatabase.LoadAssetAtPath<MapTileType>(GridMapAssets.TypesFolder + "/" + id + ".asset");
                if (type == null) throw new InvalidOperationException("缺少房屋模板使用的格子类型：" + id);
                return type;
            }
        }

        private static GridMapAsset BuildHut(BuildingTypes type)
        {
            GridMapAsset map = CreateShell(9, 7, 4, type.Wall);
            Horizontal(map, 1, 7, 5, type.Wall);
            Horizontal(map, 3, 5, 6, type.Wall);
            Opening(map, 0, 1, type.Door);
            Opening(map, 0, 2, type.Door);
            Opening(map, 8, 2, type.Window);
            map.SetCell(5, 2, type.Window, MapLayer.Objects);
            map.SetCell(2, 1, type.Barrel, MapLayer.Objects);
            map.SetCell(6, 1, type.Chest, MapLayer.Objects);
            return map;
        }

        private static GridMapAsset BuildHouse(BuildingTypes type)
        {
            GridMapAsset map = CreateShell(13, 11, 9, type.Wall);
            Horizontal(map, 2, 10, 10, type.Wall);
            // The opening above the stair run keeps a continuous route through the middle floor.
            Horizontal(map, 0, 1, 4, type.Wall);
            Horizontal(map, 6, 12, 4, type.Wall);
            for (int step = 0; step < 4; step++) map.SetCell(2 + step, 1 + step, type.Stairs);
            Opening(map, 0, 1, type.Door);
            Opening(map, 0, 2, type.Door);
            Opening(map, 12, 2, type.Window);
            Opening(map, 0, 6, type.Window);
            Opening(map, 12, 6, type.Window);
            map.SetCell(8, 7, type.Window, MapLayer.Objects);
            map.SetCell(9, 1, type.Barrel, MapLayer.Objects);
            map.SetCell(9, 5, type.Chest, MapLayer.Objects);
            return map;
        }

        private static GridMapAsset BuildWarehouse(BuildingTypes type)
        {
            GridMapAsset map = CreateShell(15, 8, 6, type.Wall);
            Horizontal(map, 1, 13, 7, type.Wall);
            Horizontal(map, 8, 13, 3, type.Wall);
            for (int step = 0; step < 3; step++) map.SetCell(5 + step, 1 + step, type.Stairs);
            Opening(map, 0, 1, type.Door);
            Opening(map, 0, 2, type.Door);
            Opening(map, 14, 4, type.Window);
            Opening(map, 0, 4, type.Window);
            map.SetCell(3, 1, type.Barrel, MapLayer.Objects);
            map.SetCell(10, 1, type.Barrel, MapLayer.Objects);
            map.SetCell(12, 1, type.Chest, MapLayer.Objects);
            map.SetCell(11, 4, type.Chest, MapLayer.Objects);
            return map;
        }

        private static GridMapAsset CreateShell(int width, int height, int ceiling, MapTileType wall)
        {
            var map = ScriptableObject.CreateInstance<GridMapAsset>();
            map.Initialize(width, height);
            Horizontal(map, 0, width - 1, 0, wall);
            Horizontal(map, 0, width - 1, ceiling, wall);
            for (int y = 1; y < ceiling; y++)
            {
                map.SetCell(0, y, wall);
                map.SetCell(width - 1, y, wall);
            }
            return map;
        }

        private static void Horizontal(GridMapAsset map, int left, int right, int y, MapTileType type)
        {
            for (int x = left; x <= right; x++) map.SetCell(x, y, type);
        }

        private static void Opening(GridMapAsset map, int x, int y, MapTileType type)
        {
            map.SetCell(x, y, null, MapLayer.Terrain);
            map.SetCell(x, y, type, MapLayer.Objects);
        }
    }
}

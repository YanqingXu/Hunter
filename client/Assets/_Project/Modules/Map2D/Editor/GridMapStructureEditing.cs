using System;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Grid-only structure copying. Callers are responsible for Undo and asset persistence.</summary>
    public static class GridMapStructureEditing
    {
        /// <summary>
        /// Stamps the complete template rectangle at its lower-left cell. Both source layers
        /// are snapshotted before writing, including when the source and target are the same map.
        /// With replaceEmpty=false, each layer preserves its target cells wherever its source is empty.
        /// </summary>
        public static bool TryPlace(GridMapAsset map, MapStructureTemplate template, Vector2Int origin,
            bool flipX, bool replaceEmpty, out int changed, out string error)
        {
            changed = 0;
            error = null;
            if (map == null) { error = "请先选择目标地图。"; return false; }
            if (template == null) { error = "请选择房屋模板。"; return false; }
            GridMapAsset layout = template.Layout;
            if (layout == null) { error = "此模板还没有格子布局，请先编辑模板。"; return false; }
            int width = layout.Width;
            int height = layout.Height;
            if (width < 1 || height < 1 || width > GridMapAsset.MaxDimension || height > GridMapAsset.MaxDimension)
            {
                error = "模板尺寸必须在 1 至 " + GridMapAsset.MaxDimension + " 格之间。";
                return false;
            }
            if (!Fits(map, origin.x, origin.y, width, height))
            {
                error = "房屋模板必须完整放在地图内，请调整位置或扩大地图。";
                return false;
            }

            MapTileType[] terrain;
            MapTileType[] objects;
            Snapshot(layout, 0, 0, width, height, out terrain, out objects);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceX = flipX ? width - 1 - x : x;
                    int index = y * width + sourceX;
                    if ((replaceEmpty || terrain[index] != null) && map.SetCell(origin.x + x, origin.y + y, terrain[index], MapLayer.Terrain)) changed++;
                    if ((replaceEmpty || objects[index] != null) && map.SetCell(origin.x + x, origin.y + y, objects[index], MapLayer.Objects)) changed++;
                }
            }
            return true;
        }

        /// <summary>
        /// Creates an independent, unsaved map containing exactly the source rectangle.
        /// Its origin is local zero, its cell size is inherited, and tile type assets remain shared.
        /// Invalid or partially out-of-bounds rectangles throw before any object is created.
        /// </summary>
        public static GridMapAsset Capture(GridMapAsset map, RectInt region)
        {
            if (map == null) throw new ArgumentException("请先选择来源地图。", nameof(map));
            if (!Fits(map, region.x, region.y, region.width, region.height))
                throw new ArgumentException("截取区域必须是完全位于地图内的非空矩形，宽高均不能超过 " + GridMapAsset.MaxDimension + " 格。", nameof(region));

            MapTileType[] terrain;
            MapTileType[] objects;
            Snapshot(map, region.x, region.y, region.width, region.height, out terrain, out objects);
            GridMapAsset copy = ScriptableObject.CreateInstance<GridMapAsset>();
            copy.name = "StructureLayout";
            copy.Initialize(region.width, region.height, map.CellSize);
            for (int y = 0; y < region.height; y++)
            {
                for (int x = 0; x < region.width; x++)
                {
                    int index = y * region.width + x;
                    copy.SetCell(x, y, terrain[index], MapLayer.Terrain);
                    copy.SetCell(x, y, objects[index], MapLayer.Objects);
                }
            }
            return copy;
        }

        private static bool Fits(GridMapAsset map, int x, int y, int width, int height)
        {
            return width > 0 && height > 0 && width <= GridMapAsset.MaxDimension && height <= GridMapAsset.MaxDimension &&
                x >= 0 && y >= 0 && (long)x + width <= map.Width && (long)y + height <= map.Height;
        }

        private static void Snapshot(GridMapAsset map, int originX, int originY, int width, int height,
            out MapTileType[] terrain, out MapTileType[] objects)
        {
            terrain = new MapTileType[width * height];
            objects = new MapTileType[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    terrain[index] = map.GetCell(originX + x, originY + y, MapLayer.Terrain);
                    objects[index] = map.GetCell(originX + x, originY + y, MapLayer.Objects);
                }
            }
        }
    }
}

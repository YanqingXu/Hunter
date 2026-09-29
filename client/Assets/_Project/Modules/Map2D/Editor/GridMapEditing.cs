using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Pure map edits. Callers own Undo recording and marking assets dirty.</summary>
    public static class GridMapEditing
    {
        public const int MaximumBrushSize = 32;

        /// <summary>Paints a square; even sizes extend one additional cell right and up.</summary>
        public static int PaintBrush(GridMapAsset map, int x, int y, MapTileType type, int size,
            MapLayer layer = MapLayer.Terrain)
        {
            if (map == null) return 0;
            size = Mathf.Clamp(size, 1, MaximumBrushSize);
            int negativeExtent = (size - 1) / 2;
            int positiveExtent = size / 2;
            int changed = 0;
            // Use long before clamping so even extreme pointer coordinates cannot wrap.
            int minX = (int)Math.Max(0L, (long)x - negativeExtent);
            int maxX = (int)Math.Min(map.Width - 1L, (long)x + positiveExtent);
            int minY = (int)Math.Max(0L, (long)y - negativeExtent);
            int maxY = (int)Math.Min(map.Height - 1L, (long)y + positiveExtent);
            if ((long)x - negativeExtent > map.Width - 1L || (long)x + positiveExtent < 0L ||
                (long)y - negativeExtent > map.Height - 1L || (long)y + positiveExtent < 0L)
                return 0;
            for (int row = minY; row <= maxY; row++)
                for (int column = minX; column <= maxX; column++)
                    if (map.SetCell(column, row, type, layer)) changed++;
            return changed;
        }

        /// <summary>Joins drag samples with a continuous Bresenham stroke and square brush.</summary>
        public static int PaintLine(GridMapAsset map, Vector2Int from, Vector2Int to,
            MapTileType type, int size, MapLayer layer = MapLayer.Terrain)
        {
            if (map == null) return 0;
            size = Mathf.Clamp(size, 1, MaximumBrushSize);
            int negativeExtent = (size - 1) / 2;
            int positiveExtent = size / 2;
            // Clip the center line against the map expanded by the brush extents. This
            // bounds work even when a drag starts or ends very far outside the canvas.
            if (!ClipLine(ref from, ref to, -positiveExtent, -positiveExtent,
                    map.Width - 1 + negativeExtent, map.Height - 1 + negativeExtent))
                return 0;

            int x = from.x;
            int y = from.y;
            int dx = Math.Abs(to.x - x);
            int dy = -Math.Abs(to.y - y);
            int stepX = x < to.x ? 1 : -1;
            int stepY = y < to.y ? 1 : -1;
            int error = dx + dy;
            int changed = 0;
            while (true)
            {
                changed += PaintBrush(map, x, y, type, size, layer);
                if (x == to.x && y == to.y) break;
                int doubled = 2 * error;
                if (doubled >= dy) { error += dy; x += stepX; }
                if (doubled <= dx) { error += dx; y += stepY; }
            }
            return changed;
        }

        /// <summary>Replaces the connected four-neighbor region, including empty cells.</summary>
        public static int Fill(GridMapAsset map, int x, int y, MapTileType type,
            MapLayer layer = MapLayer.Terrain)
        {
            if (map == null || !map.Contains(x, y)) return 0;
            MapTileType original = map.GetCell(x, y, layer);
            if (original == type) return 0;

            int width = map.Width;
            bool[] visited = new bool[width * map.Height];
            Queue<int> pending = new Queue<int>();
            int start = y * width + x;
            pending.Enqueue(start);
            visited[start] = true;
            int changed = 0;
            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                int column = index % width;
                int row = index / width;
                if (map.GetCell(column, row, layer) != original) continue;
                if (map.SetCell(column, row, type, layer)) changed++;
                if (column > 0) Enqueue(index - 1, visited, pending);
                if (column + 1 < width) Enqueue(index + 1, visited, pending);
                if (row > 0) Enqueue(index - width, visited, pending);
                if (row + 1 < map.Height) Enqueue(index + width, visited, pending);
            }
            return changed;
        }

        /// <summary>Paints an inclusive rectangle. An outline keeps its original edges when clipped.</summary>
        public static int PaintRectangle(GridMapAsset map, Vector2Int from, Vector2Int to,
            MapTileType type, bool outline = false, MapLayer layer = MapLayer.Terrain)
        {
            if (map == null) return 0;
            int left = Math.Min(from.x, to.x);
            int right = Math.Max(from.x, to.x);
            int bottom = Math.Min(from.y, to.y);
            int top = Math.Max(from.y, to.y);
            int minX = Math.Max(0, left);
            int maxX = Math.Min(map.Width - 1, right);
            int minY = Math.Max(0, bottom);
            int maxY = Math.Min(map.Height - 1, top);
            int changed = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (outline && x != left && x != right && y != bottom && y != top) continue;
                    if (map.SetCell(x, y, type, layer)) changed++;
                }
            }
            return changed;
        }

        private static void Enqueue(int index, bool[] visited, Queue<int> pending)
        {
            if (visited[index]) return;
            visited[index] = true;
            pending.Enqueue(index);
        }

        private static bool ClipLine(ref Vector2Int from, ref Vector2Int to,
            int left, int bottom, int right, int top)
        {
            double x = from.x;
            double y = from.y;
            double dx = (double)to.x - from.x;
            double dy = (double)to.y - from.y;
            double start = 0d;
            double end = 1d;
            if (!ClipBoundary(-dx, x - left, ref start, ref end) ||
                !ClipBoundary(dx, right - x, ref start, ref end) ||
                !ClipBoundary(-dy, y - bottom, ref start, ref end) ||
                !ClipBoundary(dy, top - y, ref start, ref end))
                return false;

            from = new Vector2Int(Mathf.Clamp((int)Math.Round(x + start * dx), left, right),
                Mathf.Clamp((int)Math.Round(y + start * dy), bottom, top));
            to = new Vector2Int(Mathf.Clamp((int)Math.Round(x + end * dx), left, right),
                Mathf.Clamp((int)Math.Round(y + end * dy), bottom, top));
            return true;
        }

        private static bool ClipBoundary(double direction, double distance, ref double start, ref double end)
        {
            if (direction == 0d) return distance >= 0d;
            double ratio = distance / direction;
            if (direction < 0d)
            {
                if (ratio > end) return false;
                if (ratio > start) start = ratio;
            }
            else
            {
                if (ratio < start) return false;
                if (ratio < end) end = ratio;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>Two independent XY cell layers, stored with x + y * Width indexing.</summary>
    [CreateAssetMenu(fileName = "GridMap", menuName = "BigWorld/2D Map/Map")]
    public sealed class GridMapAsset : ScriptableObject
    {
        public const int MaxDimension = 256;
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        [SerializeField, HideInInspector] private string assetIdentity;
        [SerializeField] private List<MapSpawnDefinition> spawns = new List<MapSpawnDefinition>();
        [SerializeField, HideInInspector] private int width = 32;
        [SerializeField, HideInInspector] private int height = 18;
        [SerializeField, Min(0.01f)] private float cellSize = 1f;
        [SerializeField] private Vector2 origin;
        [SerializeField, HideInInspector] private MapTileType[] terrainCells = new MapTileType[32 * 18];
        [SerializeField, HideInInspector] private MapTileType[] objectCells = new MapTileType[32 * 18];

        public int Width { get { return width; } }
        public string Id { get { return id; } }
        public List<MapSpawnDefinition> Spawns { get { return spawns ?? (spawns = new List<MapSpawnDefinition>()); } }
        public int Height { get { return height; } }
        public int CellCount { get { return width * height; } }
        public int TileCount { get { return GetTileCount(MapLayer.Terrain) + GetTileCount(MapLayer.Objects); } }
        public float CellSize { get { return cellSize; } set { cellSize = ValidCellSize(value); } }
        public Vector2 Origin
        {
            get { return origin; }
            set { origin = new Vector2(ValidCoordinate(value.x), ValidCoordinate(value.y)); }
        }

        public void Initialize(int newWidth, int newHeight, float newCellSize = 1f)
        {
            Resize(newWidth, newHeight, false);
            CellSize = newCellSize;
            Origin = Vector2.zero;
        }

        public void ConfigureGeometry(float newCellSize, Vector2 newOrigin)
        {
            CellSize = newCellSize;
            Origin = newOrigin;
        }

        public bool Contains(int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        public MapTileType GetCell(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            if (!Contains(x, y)) return null;
            MapTileType[] cells = GetStorage(layer);
            int index = y * width + x;
            return cells != null && index < cells.Length ? cells[index] : null;
        }

        public bool SetCell(int x, int y, MapTileType type, MapLayer layer = MapLayer.Terrain)
        {
            if (!Contains(x, y)) return false;
            EnsureStorage();
            MapTileType[] cells = GetStorage(layer);
            int index = y * width + x;
            if (cells[index] == type) return false;
            cells[index] = type;
            return true;
        }

        public int GetTileCount(MapLayer layer)
        {
            MapTileType[] cells = GetStorage(layer);
            if (cells == null) return 0;
            int count = 0;
            for (int i = 0; i < cells.Length; i++) if (cells[i] != null) count++;
            return count;
        }

        public void Resize(int newWidth, int newHeight, bool preserve = true)
        {
            newWidth = Mathf.Clamp(newWidth, 1, MaxDimension);
            newHeight = Mathf.Clamp(newHeight, 1, MaxDimension);
            if (preserve && width == newWidth && height == newHeight)
            {
                EnsureStorage();
                return;
            }
            terrainCells = ResizeLayer(terrainCells, newWidth, newHeight, preserve);
            objectCells = ResizeLayer(objectCells, newWidth, newHeight, preserve);
            width = newWidth;
            height = newHeight;
        }

        public void Clear()
        {
            Clear(MapLayer.Terrain);
            Clear(MapLayer.Objects);
        }

        public void Clear(MapLayer layer)
        {
            MapTileType[] cells = GetStorage(layer);
            if (cells != null) Array.Clear(cells, 0, cells.Length);
        }

        private MapTileType[] ResizeLayer(MapTileType[] source, int newWidth, int newHeight, bool preserve)
        {
            MapTileType[] result = new MapTileType[newWidth * newHeight];
            if (!preserve || source == null) return result;
            int columns = Mathf.Min(width, newWidth);
            int rows = Mathf.Min(height, newHeight);
            for (int y = 0; y < rows; y++)
            {
                int start = y * width;
                int available = Mathf.Min(columns, source.Length - start);
                if (available > 0) Array.Copy(source, start, result, y * newWidth, available);
            }
            return result;
        }

        private MapTileType[] GetStorage(MapLayer layer)
        {
            if (layer == MapLayer.Terrain) return terrainCells;
            if (layer == MapLayer.Objects) return objectCells;
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown map layer.");
        }

        public void NewIdentity()
        {
            id = Guid.NewGuid().ToString("N");
            // An explicit copy already has a fresh ID; its first asset registration must keep it.
            assetIdentity = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Called by editor asset maintenance with GUID + local file ID. The first registration
        /// preserves existing saves; a copied persistent asset receives a new session identity.
        /// Contains no AssetDatabase calls and is never invoked by OnValidate or OnEnable.
        /// </summary>
        public bool SynchronizeEditorIdentity(string persistentIdentity)
        {
            if (string.IsNullOrEmpty(persistentIdentity)) throw new ArgumentException("Persistent asset identity is required.", nameof(persistentIdentity));
            if (string.Equals(assetIdentity, persistentIdentity, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(assetIdentity)) NewIdentity();
            assetIdentity = persistentIdentity;
            return true;
        }
#endif

        public void ValidateSpawns()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapSpawnDefinition spawn in Spawns)
            {
                if (spawn == null) continue;
                spawn.Validate();
                if (!ids.Add(spawn.Id)) { spawn.Id = Guid.NewGuid().ToString("N"); ids.Add(spawn.Id); }
            }
        }

        private void OnEnable() { EnsureStorage(); if (string.IsNullOrEmpty(id)) NewIdentity(); ValidateSpawns(); }

        private void OnValidate()
        {
            width = Mathf.Clamp(width, 1, MaxDimension);
            height = Mathf.Clamp(height, 1, MaxDimension);
            CellSize = cellSize;
            Origin = origin;
            EnsureStorage();
            if (string.IsNullOrEmpty(id)) NewIdentity();
            ValidateSpawns();
        }

        private void EnsureStorage()
        {
            if (terrainCells == null || terrainCells.Length != CellCount) Array.Resize(ref terrainCells, CellCount);
            if (objectCells == null || objectCells.Length != CellCount) Array.Resize(ref objectCells, CellCount);
        }

        private static float ValidCellSize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(0.01f, value);
        }

        private static float ValidCoordinate(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}

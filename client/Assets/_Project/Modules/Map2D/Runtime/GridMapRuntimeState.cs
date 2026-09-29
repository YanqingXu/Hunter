using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D
{
    [Serializable]
    public sealed class MapEntityState
    {
        public string Id;
        public Vector3 LocalPosition;
        public float Health;
        public bool Destroyed;
        public bool Opened;
        public bool Ignited;
        public double RespawnAt;
    }

    /// <summary>Session changes are independent of the authored asset and of loaded chunks.</summary>
    public sealed class GridMapRuntimeState
    {
        [Serializable] private sealed class CellChange
        {
            public int X, Y;
            public MapLayer Layer;
            public bool HasOverride;
            public string TypeId;
            public bool Ignited;
            [NonSerialized] public MapTileType Type;
        }

        [Serializable] private sealed class Snapshot
        {
            public int Version = 1;
            public string MapId;
            public int Width, Height;
            public List<CellChange> Cells = new List<CellChange>();
            public List<MapEntityState> Entities = new List<MapEntityState>();
        }

        private readonly Dictionary<int, CellChange> cells = new Dictionary<int, CellChange>();
        private readonly Dictionary<string, MapEntityState> entities = new Dictionary<string, MapEntityState>(StringComparer.Ordinal);
        public GridMapAsset Map { get; private set; }
        public IReadOnlyDictionary<string, MapEntityState> Entities { get { return entities; } }
        public event Action<int, int, MapLayer> CellChanged;
        public event Action StateRestored;

        public GridMapRuntimeState(GridMapAsset map)
        {
            Map = map != null ? map : throw new ArgumentNullException(nameof(map));
        }

        public MapTileType GetCell(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            if (!Map.Contains(x, y)) return null;
            CellChange change;
            return cells.TryGetValue(Key(x, y, layer), out change) && change.HasOverride ? change.Type : Map.GetCell(x, y, layer);
        }

        public bool SetCell(int x, int y, MapTileType type, MapLayer layer = MapLayer.Terrain)
        {
            if (!Map.Contains(x, y) || GetCell(x, y, layer) == type) return false;
            CellChange change = GetChange(x, y, layer);
            change.HasOverride = true; change.Type = type; change.TypeId = type == null ? null : type.Id; change.Ignited = false;
            CellChanged?.Invoke(x, y, layer);
            return true;
        }

        public bool DestroyCell(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            MapTileType type = GetCell(x, y, layer);
            return type != null && type.CanBeDestroyed && SetCell(x, y, null, layer);
        }

        public bool IgniteCell(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            MapTileType type = GetCell(x, y, layer);
            if (type == null || !type.CanBeIgnited || IsCellIgnited(x, y, layer)) return false;
            GetChange(x, y, layer).Ignited = true;
            CellChanged?.Invoke(x, y, layer);
            return true;
        }

        public bool IsCellIgnited(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            CellChange change;
            return Map.Contains(x, y) && cells.TryGetValue(Key(x, y, layer), out change) && change.Ignited;
        }

        public MapEntityState GetEntity(string id, Vector3 initialLocalPosition, float maxHealth)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Entity id is required.", nameof(id));
            MapEntityState state;
            if (!entities.TryGetValue(id, out state))
            {
                state = new MapEntityState { Id = id, LocalPosition = initialLocalPosition, Health = Mathf.Max(.01f, maxHealth) };
                entities.Add(id, state);
            }
            return state;
        }

        public void Reset()
        {
            cells.Clear(); entities.Clear(); StateRestored?.Invoke();
        }

        /// <summary>Capture live actors first. Respawn deadlines are stored as remaining game time.</summary>
        public string ToJson()
        {
            BuildTypeLookup(null);
            var snapshot = new Snapshot { MapId = Map.Id, Width = Map.Width, Height = Map.Height };
            foreach (CellChange change in cells.Values)
            {
                // A type's editable identifier may have changed since this cell was set.
                change.TypeId = change.Type == null ? null : change.Type.Id;
                snapshot.Cells.Add(change);
            }
            foreach (MapEntityState state in entities.Values)
            {
                var copy = new MapEntityState
                {
                    Id = state.Id, LocalPosition = state.LocalPosition, Health = state.Health,
                    Destroyed = state.Destroyed, Opened = state.Opened, Ignited = state.Ignited,
                    RespawnAt = state.RespawnAt > 0 ? Math.Max(.000001d, state.RespawnAt - Time.timeAsDouble) : 0d
                };
                snapshot.Entities.Add(copy);
            }
            return JsonUtility.ToJson(snapshot, true);
        }

        /// <summary>Validates the complete snapshot before replacing the current session state.</summary>
        public void LoadJson(string json, IEnumerable<MapTileType> additionalTypes = null)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("存档内容为空。", nameof(json));
            Snapshot snapshot = JsonUtility.FromJson<Snapshot>(json);
            if (snapshot == null || snapshot.Version != 1 || snapshot.MapId != Map.Id || snapshot.Width != Map.Width || snapshot.Height != Map.Height)
                throw new ArgumentException("存档与当前地图不匹配。", nameof(json));
            Dictionary<string, MapTileType> types = BuildTypeLookup(additionalTypes);
            var restoredCells = new Dictionary<int, CellChange>();
            foreach (CellChange change in snapshot.Cells ?? new List<CellChange>())
            {
                if (change == null || !Map.Contains(change.X, change.Y) || !ValidLayer(change.Layer)) throw new ArgumentException("存档格子坐标无效。");
                if (change.HasOverride && !string.IsNullOrEmpty(change.TypeId) && !types.TryGetValue(change.TypeId, out change.Type))
                    throw new ArgumentException("存档中的格子类型不可用：" + change.TypeId);
                int key = Key(change.X, change.Y, change.Layer);
                if (restoredCells.ContainsKey(key)) throw new ArgumentException("存档含重复格子。");
                restoredCells.Add(key, change);
            }
            var restoredEntities = new Dictionary<string, MapEntityState>(StringComparer.Ordinal);
            foreach (MapEntityState state in snapshot.Entities ?? new List<MapEntityState>())
            {
                if (state == null || string.IsNullOrWhiteSpace(state.Id) || restoredEntities.ContainsKey(state.Id) ||
                    !Finite(state.LocalPosition.x) || !Finite(state.LocalPosition.y) || !Finite(state.LocalPosition.z) ||
                    !Finite(state.Health) || state.Health < 0 || double.IsNaN(state.RespawnAt) || double.IsInfinity(state.RespawnAt) || state.RespawnAt < 0)
                    throw new ArgumentException("存档角色状态无效。");
                if (state.RespawnAt > 0) state.RespawnAt += Time.timeAsDouble;
                restoredEntities.Add(state.Id, state);
            }
            cells.Clear(); entities.Clear();
            foreach (var pair in restoredCells) cells.Add(pair.Key, pair.Value);
            foreach (var pair in restoredEntities) entities.Add(pair.Key, pair.Value);
            StateRestored?.Invoke();
        }

        private CellChange GetChange(int x, int y, MapLayer layer)
        {
            int key = Key(x, y, layer);
            CellChange change;
            if (!cells.TryGetValue(key, out change)) cells.Add(key, change = new CellChange { X = x, Y = y, Layer = layer });
            return change;
        }

        private int Key(int x, int y, MapLayer layer)
        {
            if (!ValidLayer(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
            return x + y * Map.Width + (int)layer * Map.CellCount;
        }
        private static bool ValidLayer(MapLayer layer) { return layer == MapLayer.Terrain || layer == MapLayer.Objects; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private Dictionary<string, MapTileType> BuildTypeLookup(IEnumerable<MapTileType> additionalTypes)
        {
            var types = new Dictionary<string, MapTileType>(StringComparer.Ordinal);
            for (int y = 0; y < Map.Height; y++)
                for (int x = 0; x < Map.Width; x++)
                    for (int l = 0; l < 2; l++) AddType(types, Map.GetCell(x, y, (MapLayer)l));
            if (additionalTypes != null) foreach (MapTileType type in additionalTypes) AddType(types, type);
            foreach (CellChange current in cells.Values) AddType(types, current.Type);
            return types;
        }

        private static void AddType(Dictionary<string, MapTileType> types, MapTileType type)
        {
            if (type == null) return;
            if (string.IsNullOrWhiteSpace(type.Id))
                throw new InvalidOperationException("格子类型缺少类型标识：" + type.DisplayName);
            MapTileType existing;
            if (types.TryGetValue(type.Id, out existing) && existing != type)
                throw new InvalidOperationException("格子类型标识重复，请在类型配置中改为不同标识：" + type.Id + "（" + existing.DisplayName + "、" + type.DisplayName + "）");
            types[type.Id] = type;
        }
    }
}

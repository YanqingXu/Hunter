using System;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BigWorld.Map2D
{
    /// <summary>Renders an asset in the local XY plane. Rebuild after changing map data at runtime.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/2D Map Renderer")]
    public sealed partial class GridMapRenderer : MonoBehaviour
    {
        [SerializeField] private GridMapAsset map;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField, HideInInspector] private MapGeneratedPart generatedRoot;
        [SerializeField, HideInInspector] private Tilemap[] generatedTilemaps = new Tilemap[6];
        [SerializeField, HideInInspector] private bool previewCleared;
        [SerializeField, HideInInspector] private GridMapAsset builtMap;
        private bool isRebuilding;
#if UNITY_EDITOR
        private bool rebuildQueued;
        private bool suppressAutomaticRebuild;
#endif

        public GridMapAsset Map
        {
            get { return map; }
            set
            {
                if (map == value) return;
                map = value;
                Rebuild();
            }
        }

        public string SortingLayerName { get { return sortingLayerName; } set { sortingLayerName = value; } }
        public int SortingOrder { get { return sortingOrder; } set { sortingOrder = value; } }
        public bool IsPreviewCleared { get { return previewCleared; } }
        public bool HasGeneratedPreview
        {
            get
            {
                if (!IsOwnedRoot(generatedRoot) || generatedTilemaps == null || generatedTilemaps.Length != 6) return false;
                foreach (Tilemap tilemap in generatedTilemaps) if (!IsOwnedTilemap(tilemap)) return false;
                return true;
            }
        }

        /// <summary>Assigns and rebuilds in one operation, optionally recording editor Undo.</summary>
        public void SetMap(GridMapAsset value, bool registerUndo = false)
        {
            RecordHierarchyUndo(registerUndo);
            map = value;
            RebuildInternal(registerUndo, false);
        }

        public void Rebuild()
        {
            Rebuild(false);
        }

        /// <summary>Records both existing data and newly created preview objects when requested.</summary>
        public void Rebuild(bool registerUndo)
        {
            RebuildInternal(registerUndo, true);
        }

        private void RebuildInternal(bool registerUndo, bool recordExistingHierarchy)
        {
            if (isRebuilding) return;
#if UNITY_EDITOR
            CancelQueuedRebuild();
#endif
            if (recordExistingHierarchy) RecordHierarchyUndo(registerUndo);
            isRebuilding = true;
            try
            {
                StopRuntimeStreaming(true);
                builtMap = map;
                if (map == null)
                {
                    ReleaseRuntimeState();
                    ClearPreview();
                    return;
                }
                if (Application.IsPlaying(gameObject)) EnsureRuntimeState();
                previewCleared = false;
                EnsureGeneratedObjects(registerUndo);
                // RegisterCreatedObjectUndo captures new tilemaps while they are still
                // empty and flushes earlier records. Capture the now-complete hierarchy
                // before assigning tile data so Redo restores the painted cells as well.
                RecordHierarchyUndo(registerUndo);
                Transform rootTransform = generatedRoot.transform;
                rootTransform.localPosition = new Vector3(map.Origin.x, map.Origin.y, 0f);
                rootTransform.localRotation = Quaternion.identity;
                // Scale the complete grid so placeholder sprites and grid colliders remain identical.
                // Custom sprites should be imported at one Unity unit per cell.
                rootTransform.localScale = new Vector3(map.CellSize, map.CellSize, 1f);
                Grid grid = generatedRoot.GetComponent<Grid>();
                grid.cellSize = Vector3.one;
                grid.cellGap = Vector3.zero;
                grid.cellLayout = GridLayout.CellLayout.Rectangle;
                grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

                if (Application.IsPlaying(gameObject) && streamingEnabled)
                {
                    if (isActiveAndEnabled) BeginRuntimeStreaming();
                    else ClearGeneratedRuntimeTiles();
                    return;
                }

                BoundsInt bounds = new BoundsInt(0, 0, 0, map.Width, map.Height, 1);
                TileBase[] tiles = new TileBase[map.CellCount];
                for (int layerIndex = 0; layerIndex < 2; layerIndex++)
                {
                    MapLayer layer = (MapLayer)layerIndex;
                    for (int collisionIndex = 0; collisionIndex < 3; collisionIndex++)
                    {
                        MapTileCollisionMode collision = (MapTileCollisionMode)collisionIndex;
                        Tilemap tilemap = GetTilemap(layer, collision);
                        Array.Clear(tiles, 0, tiles.Length);
                        for (int y = 0; y < map.Height; y++)
                        {
                            for (int x = 0; x < map.Width; x++)
                            {
                                MapTileType type = GetRenderedCell(x, y, layer);
                                if (type != null && type.Collision == collision) tiles[y * map.Width + x] = type;
                            }
                        }
                        tilemap.ClearAllTiles();
                        tilemap.SetTilesBlock(bounds, tiles);
                        tilemap.RefreshAllTiles();
                        tilemap.CompressBounds();
                        TilemapRenderer tileRenderer = tilemap.GetComponent<TilemapRenderer>();
                        tileRenderer.sortingLayerName = sortingLayerName;
                        tileRenderer.sortingOrder = sortingOrder + layerIndex * 10 + collisionIndex;
                        TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                        if (collider != null) collider.ProcessTilemapChanges();
                    }
                }
            }
            finally { isRebuilding = false; }
        }

        /// <summary>Empties generated tilemaps while preserving their hierarchy and user components.</summary>
        public void ClearPreview()
        {
#if UNITY_EDITOR
            CancelQueuedRebuild();
#endif
            StopRuntimeStreaming(true);
            previewCleared = true;
            builtMap = map;
            if (generatedTilemaps == null) return;
            foreach (Tilemap tilemap in generatedTilemaps)
            {
                if (!IsOwnedTilemap(tilemap)) continue;
                tilemap.ClearAllTiles();
                tilemap.CompressBounds();
                TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                if (collider != null) collider.ProcessTilemapChanges();
            }
        }

        public Tilemap GetTilemap(MapLayer layer, MapTileCollisionMode collision)
        {
            int index = GetLayerIndex(layer, collision);
            if (generatedTilemaps == null || index >= generatedTilemaps.Length) return null;
            Tilemap tilemap = generatedTilemaps[index];
            return IsOwnedTilemap(tilemap) ? tilemap : null;
        }

        /// <summary>World position to asset coordinates. The result can be outside the map.</summary>
        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            if (map == null) return new Vector2Int(-1, -1);
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return new Vector2Int(Mathf.FloorToInt((local.x - map.Origin.x) / map.CellSize),
                Mathf.FloorToInt((local.y - map.Origin.y) / map.CellSize));
        }

        public Vector3 CellToWorldCenter(int x, int y)
        {
            if (map == null) return transform.position;
            return transform.TransformPoint(new Vector3(map.Origin.x + (x + 0.5f) * map.CellSize,
                map.Origin.y + (y + 0.5f) * map.CellSize, 0f));
        }

        public bool TryGetCell(Vector3 worldPosition, out MapTileType type, MapLayer layer = MapLayer.Terrain)
        {
            type = null;
            if (map == null) return false;
            Vector2Int cell = WorldToCell(worldPosition);
            type = GetRenderedCell(cell.x, cell.y, layer);
            return type != null;
        }

        public bool IsWalkable(Vector3 worldPosition)
        {
            if (map == null) return false;
            Vector2Int cell = WorldToCell(worldPosition);
            if (!map.Contains(cell.x, cell.y)) return false;
            MapTileType terrain = GetRenderedCell(cell.x, cell.y, MapLayer.Terrain);
            MapTileType obj = GetRenderedCell(cell.x, cell.y, MapLayer.Objects);
            return AllowsMovement(terrain) && AllowsMovement(obj);
        }

        private static bool AllowsMovement(MapTileType type)
        {
            return type == null || (!type.BlocksMovement && type.Walkable);
        }

        [ContextMenu("Rebuild Map")]
        private void RebuildFromContextMenu() { Rebuild(true); }

        [ContextMenu("Clear Map Preview")]
        private void ClearFromContextMenu()
        {
            RecordHierarchyUndo(true);
            ClearPreview();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
#endif
            // Clearing an editor preview must not disable the saved map in the game.
            if (Application.IsPlaying(gameObject))
            {
                Rebuild();
                return;
            }
            ScheduleRebuild();
        }

        private void OnValidate()
        {
            ScheduleRebuild();
        }

        private void OnDisable()
        {
            StopRuntimeStreaming(true);
            UnsubscribeRuntimeState();
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.delayCall -= ResumeAutomaticRebuild;
            CancelQueuedRebuild();
            suppressAutomaticRebuild = false;
#endif
        }

        private void ScheduleRebuild()
        {
            if (isRebuilding || !isActiveAndEnabled || (map == null && generatedRoot == null)) return;
#if UNITY_EDITOR
            if (!Application.IsPlaying(gameObject))
            {
                if (suppressAutomaticRebuild || (previewCleared && builtMap == map)) return;
                if (!rebuildQueued)
                {
                    rebuildQueued = true;
                    EditorApplication.delayCall += DelayedRebuild;
                }
                return;
            }
#endif
            Rebuild();
        }

#if UNITY_EDITOR
        private void DelayedRebuild()
        {
            rebuildQueued = false;
            if (this == null || !isActiveAndEnabled || suppressAutomaticRebuild || (previewCleared && builtMap == map)) return;
            if (EditorUtility.IsPersistent(this)) return;
            Rebuild();
        }

        private void OnUndoRedo()
        {
            // Undo restores the exact hierarchy and tile data. A queued OnValidate rebuild
            // must not immediately recreate children that the user has just undone.
            CancelQueuedRebuild();
            ReconnectGeneratedReferences();
            suppressAutomaticRebuild = true;
            EditorApplication.delayCall -= ResumeAutomaticRebuild;
            EditorApplication.delayCall += ResumeAutomaticRebuild;
        }

        private void ResumeAutomaticRebuild() { suppressAutomaticRebuild = false; }

        private void CancelQueuedRebuild()
        {
            EditorApplication.delayCall -= DelayedRebuild;
            rebuildQueued = false;
        }
#endif

        private void ReconnectGeneratedReferences()
        {
            // Unity can recreate a registered GameObject after the owning component's
            // reference snapshot was captured. Recover references only from existing,
            // explicitly owned parts; never create or repopulate anything during Undo.
            generatedRoot = null;
            MapGeneratedPart[] parts = GetComponentsInChildren<MapGeneratedPart>(true);
            foreach (MapGeneratedPart part in parts)
            {
                if (IsOwnedRoot(part)) { generatedRoot = part; break; }
            }
            generatedTilemaps = new Tilemap[6];
            if (generatedRoot == null) return;
            foreach (MapGeneratedPart part in parts)
            {
                if (part == null || part.Owner != this || part.IsRoot || part.transform.parent != generatedRoot.transform) continue;
                if ((int)part.Layer < 0 || (int)part.Layer > 1 || (int)part.Collision < 0 || (int)part.Collision > 2) continue;
                generatedTilemaps[GetLayerIndex(part.Layer, part.Collision)] = part.GetComponent<Tilemap>();
            }
        }

        private void EnsureGeneratedObjects(bool registerUndo)
        {
            if (generatedTilemaps == null || generatedTilemaps.Length != 6) generatedTilemaps = new Tilemap[6];
            if (!IsOwnedRoot(generatedRoot))
            {
                generatedRoot = null;
                foreach (MapGeneratedPart part in GetComponentsInChildren<MapGeneratedPart>(true))
                {
                    if (IsOwnedRoot(part)) { generatedRoot = part; break; }
                }
                if (generatedRoot == null)
                {
                    GameObject rootObject = new GameObject("Map2D Generated Grid", typeof(Grid), typeof(MapGeneratedPart));
                    rootObject.transform.SetParent(transform, false);
                    generatedRoot = rootObject.GetComponent<MapGeneratedPart>();
                    generatedRoot.Configure(this, true, MapLayer.Terrain, MapTileCollisionMode.None);
                    RegisterCreatedObject(rootObject, registerUndo);
                }
            }
            if (generatedRoot.GetComponent<Grid>() == null) AddOwnedComponent<Grid>(generatedRoot.gameObject, registerUndo);
            for (int layerIndex = 0; layerIndex < 2; layerIndex++)
            {
                for (int collisionIndex = 0; collisionIndex < 3; collisionIndex++)
                {
                    MapLayer layer = (MapLayer)layerIndex;
                    MapTileCollisionMode collision = (MapTileCollisionMode)collisionIndex;
                    int index = GetLayerIndex(layer, collision);
                    Tilemap tilemap = generatedTilemaps[index];
                    if (!IsOwnedTilemap(tilemap))
                    {
                        tilemap = null;
                        foreach (MapGeneratedPart part in generatedRoot.GetComponentsInChildren<MapGeneratedPart>(true))
                        {
                            if (part.Owner == this && !part.IsRoot && part.Layer == layer && part.Collision == collision)
                            {
                                tilemap = part.GetComponent<Tilemap>();
                                if (tilemap != null) break;
                            }
                        }
                    }
                    if (tilemap == null)
                    {
                        GameObject layerObject = new GameObject(layer + " - " + collision, typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGeneratedPart));
                        layerObject.transform.SetParent(generatedRoot.transform, false);
                        layerObject.GetComponent<MapGeneratedPart>().Configure(this, false, layer, collision);
                        tilemap = layerObject.GetComponent<Tilemap>();
                        RegisterCreatedObject(layerObject, registerUndo);
                    }
                    generatedTilemaps[index] = tilemap;
                    tilemap.transform.localPosition = Vector3.zero;
                    tilemap.transform.localRotation = Quaternion.identity;
                    tilemap.transform.localScale = Vector3.one;
                    tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);
                    tilemap.color = Color.white;
                    if (tilemap.GetComponent<TilemapRenderer>() == null) AddOwnedComponent<TilemapRenderer>(tilemap.gameObject, registerUndo);
                    if (collision == MapTileCollisionMode.None) continue;
                    TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                    if (collider == null) collider = AddOwnedComponent<TilemapCollider2D>(tilemap.gameObject, registerUndo);
                    collider.isTrigger = false;
                    if (collision == MapTileCollisionMode.OneWay)
                    {
                        PlatformEffector2D effector = tilemap.GetComponent<PlatformEffector2D>();
                        if (effector == null) effector = AddOwnedComponent<PlatformEffector2D>(tilemap.gameObject, registerUndo);
                        effector.useOneWay = true;
                        effector.useOneWayGrouping = true;
                        effector.surfaceArc = 180f;
                        effector.useSideFriction = false;
                        effector.useSideBounce = false;
                    }
                    collider.usedByEffector = collision == MapTileCollisionMode.OneWay;
                }
            }
        }

        private void RecordHierarchyUndo(bool registerUndo)
        {
#if UNITY_EDITOR
            if (registerUndo && !Application.IsPlaying(gameObject))
                Undo.RegisterFullObjectHierarchyUndo(gameObject, "生成 2D 地图");
#endif
        }

        private static void RegisterCreatedObject(GameObject created, bool registerUndo)
        {
#if UNITY_EDITOR
            if (registerUndo && !Application.IsPlaying(created))
                Undo.RegisterCreatedObjectUndo(created, "生成 2D 地图");
#endif
        }

        private static T AddOwnedComponent<T>(GameObject target, bool registerUndo) where T : Component
        {
#if UNITY_EDITOR
            if (registerUndo && !Application.IsPlaying(target)) return Undo.AddComponent<T>(target);
#endif
            return target.AddComponent<T>();
        }

        private bool IsOwnedRoot(MapGeneratedPart part)
        {
            return part != null && part.Owner == this && part.IsRoot && part.transform.parent == transform;
        }

        private bool IsOwnedTilemap(Tilemap tilemap)
        {
            if (tilemap == null || !IsOwnedRoot(generatedRoot) || tilemap.transform.parent != generatedRoot.transform) return false;
            MapGeneratedPart part = tilemap.GetComponent<MapGeneratedPart>();
            return part != null && part.Owner == this && !part.IsRoot;
        }

        private static int GetLayerIndex(MapLayer layer, MapTileCollisionMode collision)
        {
            if (layer != MapLayer.Terrain && layer != MapLayer.Objects) throw new ArgumentOutOfRangeException(nameof(layer));
            if (collision < MapTileCollisionMode.None || collision > MapTileCollisionMode.OneWay) throw new ArgumentOutOfRangeException(nameof(collision));
            return (int)layer * 3 + (int)collision;
        }
    }
}

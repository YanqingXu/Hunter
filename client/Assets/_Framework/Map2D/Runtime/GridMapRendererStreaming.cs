using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigWorld.Map2D
{
    public sealed partial class GridMapRenderer
    {
        [Header("Runtime streaming")]
        [SerializeField] private bool streamingEnabled = true;
        [SerializeField] private Transform loadingTarget;
        [SerializeField, Range(8, 128)] private int chunkSize = 32;
        [SerializeField] private Vector2Int loadingRadiusCells = new Vector2Int(32, 24);
        [SerializeField, Min(0)] private int unloadPaddingCells = 16;
        [SerializeField] private bool useCameraViewport = true;
        [SerializeField, Min(1)] private int chunksPerFrame = 2;
        [SerializeField, Min(0.1f)] private float streamingBudgetMilliseconds = 2f;

        private GridMapRuntimeState runtimeState;
        private bool stateSubscribed;
        private bool runtimeStreamingActive;
        private GridMapEntityStreamer entityStreamer;
        private readonly HashSet<Vector2Int> loadedChunks = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> requiredActorChunks = new HashSet<Vector2Int>();
        private readonly List<Vector3> requiredActorPositions = new List<Vector3>();
        private readonly List<Vector2Int> loadQueue = new List<Vector2Int>();
        private readonly List<Vector2Int> unloadQueue = new List<Vector2Int>();
        private readonly Dictionary<MapTileType, MapRuntimeTile> runtimeTiles = new Dictionary<MapTileType, MapRuntimeTile>();
        private readonly Dictionary<int, TileBase[]> chunkBuffers = new Dictionary<int, TileBase[]>();
        private System.Comparison<Vector2Int> loadComparison;
        private System.Comparison<Vector2Int> unloadComparison;
        private Vector2Int queueFocusChunk;
        private Vector3 streamingFocusPosition;
        private Transform fallbackPlayer;
        private Camera streamingCamera;
        private float nextFocusLookupTime;
        private Vector2Int fallbackSpawnCell;
        private int runtimeCollisionChanges;
        private int lastChunksLoaded;
        private int lastChunksUnloaded;

        public GridMapRuntimeState RuntimeState { get { return runtimeState; } }
        public bool StreamingEnabled
        {
            get { return streamingEnabled; }
            set
            {
                if (streamingEnabled == value) return;
                streamingEnabled = value;
                if (Application.IsPlaying(gameObject) && isActiveAndEnabled) Rebuild();
            }
        }
        public Transform LoadingTarget { get { return loadingTarget; } set { loadingTarget = value; } }
        public int ChunkSize
        {
            get { return Mathf.Clamp(chunkSize, 8, 128); }
            set
            {
                int next = Mathf.Clamp(value, 8, 128);
                if (chunkSize == next) return;
                chunkSize = next;
                if (runtimeStreamingActive) Rebuild();
            }
        }
        public Vector2Int LoadingRadiusCells
        {
            get { return new Vector2Int(Mathf.Max(0, loadingRadiusCells.x), Mathf.Max(0, loadingRadiusCells.y)); }
            set { loadingRadiusCells = new Vector2Int(Mathf.Max(0, value.x), Mathf.Max(0, value.y)); }
        }
        public int UnloadPaddingCells { get { return Mathf.Max(0, unloadPaddingCells); } set { unloadPaddingCells = Mathf.Max(0, value); } }
        public bool UseCameraViewport { get { return useCameraViewport; } set { useCameraViewport = value; } }
        public int ChunksPerFrame { get { return Mathf.Max(1, chunksPerFrame); } set { chunksPerFrame = Mathf.Max(1, value); } }
        public float StreamingBudgetMilliseconds
        {
            get { return float.IsNaN(streamingBudgetMilliseconds) || float.IsInfinity(streamingBudgetMilliseconds) ? 2f : Mathf.Max(0.1f, streamingBudgetMilliseconds); }
            set { streamingBudgetMilliseconds = float.IsNaN(value) || float.IsInfinity(value) ? 2f : Mathf.Max(0.1f, value); }
        }
        public int LoadedChunkCount { get { return loadedChunks.Count; } }
        public int LastChunksLoaded { get { return lastChunksLoaded; } }
        public int LastChunksUnloaded { get { return lastChunksUnloaded; } }
        public Vector3 StreamingFocusPosition
        {
            get { return ResolveStreamingFocus(); }
        }

        /// <summary>Captures active entities before serializing the session changes.</summary>
        public string SaveRuntimeState()
        {
            if (runtimeState == null) throw new System.InvalidOperationException("The map runtime has not been initialized.");
            if (entityStreamer != null) entityStreamer.CaptureRuntimeState();
            return runtimeState.ToJson();
        }

        /// <summary>Invalid saves leave the current scene and session untouched.</summary>
        public void LoadRuntimeState(string json, IEnumerable<MapTileType> additionalTypes = null)
        {
            if (runtimeState == null) throw new System.InvalidOperationException("The map runtime has not been initialized.");
            runtimeState.LoadJson(json, additionalTypes);
        }

        /// <summary>True only after this cell's terrain/collision data has been submitted.</summary>
        public bool IsCellLoaded(int x, int y)
        {
            if (map == null || !map.Contains(x, y) || previewCleared || !isActiveAndEnabled) return false;
            if (!Application.IsPlaying(gameObject) || !streamingEnabled) return HasGeneratedPreview;
            return runtimeStreamingActive && loadedChunks.Contains(CellChunk(x, y));
        }

        /// <summary>
        /// Refreshes one layer of one loaded cell, including a change of collision mode.
        /// Collision changes are committed together in LateUpdate; unloaded cells stay data only.
        /// </summary>
        public void RefreshCell(int x, int y, MapLayer layer = MapLayer.Terrain)
        {
            if (!IsCellLoaded(x, y)) return;
            MapTileType type = GetRenderedCell(x, y, layer);
            Vector3Int position = new Vector3Int(x, y, 0);
            for (int collisionIndex = 0; collisionIndex < 3; collisionIndex++)
            {
                MapTileCollisionMode collision = (MapTileCollisionMode)collisionIndex;
                Tilemap tilemap = GetTilemap(layer, collision);
                if (tilemap == null) continue;
                TileBase tile = type != null && type.Collision == collision ? RuntimeTileFor(type) : null;
                if (tilemap.GetTile(position) != tile) tilemap.SetTile(position, tile);
                else if (tile != null) tilemap.RefreshTile(position);
                runtimeCollisionChanges |= 1 << GetLayerIndex(layer, collision);
            }
        }

        private void EnsureRuntimeState()
        {
            if (runtimeState == null || runtimeState.Map != map)
            {
                ReleaseRuntimeState();
                runtimeState = new GridMapRuntimeState(map);
            }
            if (!stateSubscribed)
            {
                runtimeState.CellChanged += OnRuntimeCellChanged;
                runtimeState.StateRestored += OnRuntimeStateRestored;
                stateSubscribed = true;
            }
        }

        private void UnsubscribeRuntimeState()
        {
            if (runtimeState != null && stateSubscribed)
            {
                runtimeState.CellChanged -= OnRuntimeCellChanged;
                runtimeState.StateRestored -= OnRuntimeStateRestored;
            }
            stateSubscribed = false;
        }

        private void ReleaseRuntimeState()
        {
            UnsubscribeRuntimeState();
            runtimeState = null;
        }

        private void OnRuntimeCellChanged(int x, int y, MapLayer layer) { RefreshCell(x, y, layer); }

        private void OnRuntimeStateRestored()
        {
            // Old actors must not write their pre-load values into the restored state.
            if (entityStreamer != null) entityStreamer.Shutdown(false);
            if (Application.IsPlaying(gameObject) && isActiveAndEnabled && !previewCleared) Rebuild();
        }

        private MapTileType GetRenderedCell(int x, int y, MapLayer layer)
        {
            if (map == null) return null;
            return Application.IsPlaying(gameObject) && runtimeState != null && runtimeState.Map == map
                ? runtimeState.GetCell(x, y, layer) : map.GetCell(x, y, layer);
        }

        private TileBase RuntimeTileFor(MapTileType source)
        {
            if (source == null) return null;
            if (!Application.IsPlaying(gameObject) || !streamingEnabled) return source;
            if (runtimeTiles.TryGetValue(source, out MapRuntimeTile tile) && tile != null) return tile;
            tile = ScriptableObject.CreateInstance<MapRuntimeTile>();
            tile.Initialize(source);
            runtimeTiles[source] = tile;
            return tile;
        }

        private void BeginRuntimeStreaming()
        {
            // Scene previews may contain the whole authored map; clear them before the
            // first sparse load. Runtime proxies never ask Tilemap to create prefabs.
            ClearGeneratedRuntimeTiles();
            fallbackPlayer = null;
            streamingCamera = null;
            nextFocusLookupTime = 0f;
            fallbackSpawnCell = FindSpawnCell();
            runtimeStreamingActive = true;
            if (loadComparison == null) loadComparison = CompareLoadChunks;
            if (unloadComparison == null) unloadComparison = CompareUnloadChunks;
            streamingFocusPosition = ResolveStreamingFocus();
            Vector2Int focusCell = ClampedFocusCell();
            lastChunksLoaded = lastChunksUnloaded = 0;
            LoadChunk(CellChunk(focusCell.x, focusCell.y));
            lastChunksLoaded = 1;
            FlushRuntimeColliders();
            entityStreamer = GetComponent<GridMapEntityStreamer>();
            if (entityStreamer == null) entityStreamer = gameObject.AddComponent<GridMapEntityStreamer>();
            entityStreamer.Initialize(this);
        }

        private void StopRuntimeStreaming(bool clearTiles)
        {
            if (entityStreamer != null) entityStreamer.Shutdown();
            bool hadRuntimeTiles = runtimeStreamingActive || runtimeTiles.Count > 0;
            runtimeStreamingActive = false;
            loadedChunks.Clear();
            requiredActorChunks.Clear();
            requiredActorPositions.Clear();
            loadQueue.Clear();
            unloadQueue.Clear();
            lastChunksLoaded = lastChunksUnloaded = 0;
            if (!hadRuntimeTiles) return;
            if (clearTiles) ClearGeneratedRuntimeTiles();
            foreach (MapRuntimeTile tile in runtimeTiles.Values)
            {
                if (tile == null) continue;
                if (Application.isPlaying) Destroy(tile);
                else DestroyImmediate(tile);
            }
            runtimeTiles.Clear();
            foreach (TileBase[] buffer in chunkBuffers.Values) System.Array.Clear(buffer, 0, buffer.Length);
        }

        private void ClearGeneratedRuntimeTiles()
        {
            if (generatedTilemaps == null) return;
            foreach (Tilemap tilemap in generatedTilemaps)
            {
                if (!IsOwnedTilemap(tilemap)) continue;
                tilemap.ClearAllTiles();
                TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                if (collider != null) collider.ProcessTilemapChanges();
            }
            runtimeCollisionChanges = 0;
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || !runtimeStreamingActive || map == null) return;
            lastChunksLoaded = lastChunksUnloaded = 0;
            streamingFocusPosition = ResolveStreamingFocus();
            Vector2Int focusCell = ClampedFocusCell();
            Vector2Int focusChunk = CellChunk(focusCell.x, focusCell.y);
            RectInt loadBounds = DesiredCellBounds(focusCell);
            RectInt keepBounds = ExpandBounds(loadBounds, UnloadPaddingCells);
            CollectActorChunks();
            BuildChunkQueues(loadBounds, keepBounds, focusChunk);

            double started = Time.realtimeSinceStartupAsDouble;
            int operations = 0;
            int loadIndex = 0;
            int unloadIndex = 0;
            while (operations < ChunksPerFrame && (loadIndex < loadQueue.Count || unloadIndex < unloadQueue.Count))
            {
                if (operations > 0 && (Time.realtimeSinceStartupAsDouble - started) * 1000d >= StreamingBudgetMilliseconds) break;
                if (loadIndex < loadQueue.Count)
                {
                    LoadChunk(loadQueue[loadIndex++]);
                    lastChunksLoaded++;
                }
                else
                {
                    UnloadChunk(unloadQueue[unloadIndex++]);
                    lastChunksUnloaded++;
                }
                operations++;
            }
            FlushRuntimeColliders();
        }

        private void LateUpdate()
        {
            if (Application.IsPlaying(gameObject)) FlushRuntimeColliders();
        }

        private void OnDestroy()
        {
            StopRuntimeStreaming(true);
            ReleaseRuntimeState();
        }

        private Vector2Int FindSpawnCell()
        {
            if (map == null) return Vector2Int.zero;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    MapTileType type = map.GetCell(x, y, MapLayer.Objects);
                    if (type != null && (type.HasTag("spawn") || type.Id == "SpawnPoint")) return new Vector2Int(x, y);
                }
            return Vector2Int.zero;
        }

        private Vector3 ResolveStreamingFocus()
        {
            if (loadingTarget != null) return loadingTarget.position;
            if (Application.IsPlaying(gameObject) && Time.unscaledTime >= nextFocusLookupTime)
            {
                nextFocusLookupTime = Time.unscaledTime + 1f;
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                fallbackPlayer = player == null ? null : player.transform;
                streamingCamera = Camera.main;
            }
            if (fallbackPlayer != null && fallbackPlayer.gameObject.activeInHierarchy) return fallbackPlayer.position;
            if (streamingCamera == null) streamingCamera = Camera.main;
            if (streamingCamera != null && streamingCamera.isActiveAndEnabled)
            {
                Ray center = streamingCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                Plane plane = new Plane(transform.forward, transform.position);
                if (plane.Raycast(center, out float distance)) return center.GetPoint(distance);
                return plane.ClosestPointOnPlane(streamingCamera.transform.position);
            }
            return CellToWorldCenter(fallbackSpawnCell.x, fallbackSpawnCell.y);
        }

        private Vector2Int ClampedFocusCell()
        {
            Vector2Int cell = WorldToCell(streamingFocusPosition);
            return new Vector2Int(Mathf.Clamp(cell.x, 0, map.Width - 1), Mathf.Clamp(cell.y, 0, map.Height - 1));
        }

        private RectInt DesiredCellBounds(Vector2Int focus)
        {
            Vector2Int radius = LoadingRadiusCells;
            int left = (int)System.Math.Max(0L, (long)focus.x - radius.x);
            int bottom = (int)System.Math.Max(0L, (long)focus.y - radius.y);
            int right = (int)System.Math.Min(map.Width, (long)focus.x + radius.x + 1);
            int top = (int)System.Math.Min(map.Height, (long)focus.y + radius.y + 1);
            RectInt desired = new RectInt(left, bottom, right - left, top - bottom);
            if (!useCameraViewport) return desired;
            if (streamingCamera == null) streamingCamera = Camera.main;
            if (streamingCamera == null || !streamingCamera.isActiveAndEnabled) return desired;
            Plane mapPlane = new Plane(transform.forward, transform.position);
            int viewLeft = map.Width, viewBottom = map.Height, viewRight = 0, viewTop = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                Ray ray = streamingCamera.ViewportPointToRay(new Vector3(corner % 2, corner / 2, 0f));
                if (!mapPlane.Raycast(ray, out float distance)) return desired;
                Vector2Int cell = WorldToCell(ray.GetPoint(distance));
                viewLeft = Mathf.Min(viewLeft, cell.x);
                viewBottom = Mathf.Min(viewBottom, cell.y);
                viewRight = Mathf.Max(viewRight, cell.x);
                viewTop = Mathf.Max(viewTop, cell.y);
            }
            // An unrelated camera must not retain a long strip between it and the player.
            const int viewPadding = 8;
            if ((long)focus.x < (long)viewLeft - viewPadding || (long)focus.x > (long)viewRight + viewPadding ||
                (long)focus.y < (long)viewBottom - viewPadding || (long)focus.y > (long)viewTop + viewPadding) return desired;
            left = (int)System.Math.Max(0L, System.Math.Min(left, (long)viewLeft - viewPadding));
            bottom = (int)System.Math.Max(0L, System.Math.Min(bottom, (long)viewBottom - viewPadding));
            right = (int)System.Math.Min(map.Width, System.Math.Max(right, (long)viewRight + viewPadding + 1));
            top = (int)System.Math.Min(map.Height, System.Math.Max(top, (long)viewTop + viewPadding + 1));
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        private RectInt ExpandBounds(RectInt bounds, int padding)
        {
            int left = (int)System.Math.Max(0L, (long)bounds.xMin - padding);
            int bottom = (int)System.Math.Max(0L, (long)bounds.yMin - padding);
            int right = (int)System.Math.Min(map.Width, (long)bounds.xMax + padding);
            int top = (int)System.Math.Min(map.Height, (long)bounds.yMax + padding);
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        private void CollectActorChunks()
        {
            requiredActorChunks.Clear();
            requiredActorPositions.Clear();
            if (entityStreamer == null) return;
            entityStreamer.GetRequiredTerrainPositions(requiredActorPositions);
            foreach (Vector3 worldPosition in requiredActorPositions)
            {
                Vector2Int cell = WorldToCell(worldPosition);
                // Retain one cell around a moving actor so its support and a neighboring
                // chunk are present before it crosses a boundary during physics.
                int left = (int)System.Math.Max(0L, (long)cell.x - 1);
                int right = (int)System.Math.Min(map.Width - 1L, (long)cell.x + 1);
                int bottom = (int)System.Math.Max(0L, (long)cell.y - 1);
                int top = (int)System.Math.Min(map.Height - 1L, (long)cell.y + 1);
                for (int y = bottom; y <= top; y++)
                    for (int x = left; x <= right; x++) requiredActorChunks.Add(CellChunk(x, y));
            }
        }

        private void BuildChunkQueues(RectInt loadBounds, RectInt keepBounds, Vector2Int focusChunk)
        {
            loadQueue.Clear();
            unloadQueue.Clear();
            Vector2Int first = CellChunk(loadBounds.xMin, loadBounds.yMin);
            Vector2Int last = CellChunk(loadBounds.xMax - 1, loadBounds.yMax - 1);
            for (int y = first.y; y <= last.y; y++)
                for (int x = first.x; x <= last.x; x++)
                {
                    Vector2Int chunk = new Vector2Int(x, y);
                    if (!loadedChunks.Contains(chunk)) loadQueue.Add(chunk);
                }
            foreach (Vector2Int chunk in requiredActorChunks)
                if (!loadedChunks.Contains(chunk) && !loadQueue.Contains(chunk)) loadQueue.Add(chunk);
            foreach (Vector2Int chunk in loadedChunks)
                if (!requiredActorChunks.Contains(chunk) && !ChunkCellBounds(chunk).Overlaps(keepBounds)) unloadQueue.Add(chunk);
            queueFocusChunk = focusChunk;
            if (loadQueue.Count > 1) loadQueue.Sort(loadComparison);
            if (unloadQueue.Count > 1) unloadQueue.Sort(unloadComparison);
        }

        private int CompareLoadChunks(Vector2Int left, Vector2Int right)
        {
            if (left == queueFocusChunk) return right == queueFocusChunk ? 0 : -1;
            if (right == queueFocusChunk) return 1;
            int leftPriority = requiredActorChunks.Contains(left) ? 0 : 1;
            int rightPriority = requiredActorChunks.Contains(right) ? 0 : 1;
            int comparison = leftPriority.CompareTo(rightPriority);
            return comparison != 0 ? comparison : ChunkDistance(left, queueFocusChunk).CompareTo(ChunkDistance(right, queueFocusChunk));
        }

        private int CompareUnloadChunks(Vector2Int left, Vector2Int right)
        {
            return ChunkDistance(right, queueFocusChunk).CompareTo(ChunkDistance(left, queueFocusChunk));
        }

        private static int ChunkDistance(Vector2Int left, Vector2Int right)
        {
            int x = left.x - right.x, y = left.y - right.y;
            return x * x + y * y;
        }

        private Vector2Int CellChunk(int x, int y) { return new Vector2Int(x / ChunkSize, y / ChunkSize); }

        private RectInt ChunkCellBounds(Vector2Int chunk)
        {
            int x = chunk.x * ChunkSize, y = chunk.y * ChunkSize;
            return new RectInt(x, y, Mathf.Min(ChunkSize, map.Width - x), Mathf.Min(ChunkSize, map.Height - y));
        }

        private void LoadChunk(Vector2Int chunk)
        {
            if (loadedChunks.Contains(chunk)) return;
            WriteChunk(chunk, true);
            loadedChunks.Add(chunk);
        }

        private void UnloadChunk(Vector2Int chunk)
        {
            if (!loadedChunks.Contains(chunk)) return;
            WriteChunk(chunk, false);
            loadedChunks.Remove(chunk);
        }

        private void WriteChunk(Vector2Int chunk, bool populate)
        {
            RectInt cells = ChunkCellBounds(chunk);
            int count = cells.width * cells.height;
            if (!chunkBuffers.TryGetValue(count, out TileBase[] chunkBuffer))
            {
                chunkBuffer = new TileBase[count];
                chunkBuffers.Add(count, chunkBuffer);
            }
            BoundsInt bounds = new BoundsInt(cells.x, cells.y, 0, cells.width, cells.height, 1);
            for (int layerIndex = 0; layerIndex < 2; layerIndex++)
            {
                MapLayer layer = (MapLayer)layerIndex;
                for (int collisionIndex = 0; collisionIndex < 3; collisionIndex++)
                {
                    MapTileCollisionMode collision = (MapTileCollisionMode)collisionIndex;
                    System.Array.Clear(chunkBuffer, 0, chunkBuffer.Length);
                    if (populate)
                    {
                        for (int y = 0; y < cells.height; y++)
                            for (int x = 0; x < cells.width; x++)
                            {
                                MapTileType type = GetRenderedCell(cells.x + x, cells.y + y, layer);
                                if (type != null && type.Collision == collision) chunkBuffer[y * cells.width + x] = RuntimeTileFor(type);
                            }
                    }
                    Tilemap tilemap = GetTilemap(layer, collision);
                    tilemap.SetTilesBlock(bounds, chunkBuffer);
                    TilemapRenderer tileRenderer = tilemap.GetComponent<TilemapRenderer>();
                    tileRenderer.sortingLayerName = sortingLayerName;
                    tileRenderer.sortingOrder = sortingOrder + layerIndex * 10 + collisionIndex;
                    runtimeCollisionChanges |= 1 << GetLayerIndex(layer, collision);
                }
            }
        }

        private void FlushRuntimeColliders()
        {
            if (runtimeCollisionChanges == 0 || generatedTilemaps == null) return;
            for (int index = 0; index < generatedTilemaps.Length; index++)
            {
                if ((runtimeCollisionChanges & (1 << index)) == 0) continue;
                Tilemap tilemap = generatedTilemaps[index];
                if (!IsOwnedTilemap(tilemap)) continue;
                TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                if (collider != null) collider.ProcessTilemapChanges();
            }
            runtimeCollisionChanges = 0;
        }
    }
}

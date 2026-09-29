using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigWorld.Pooling;
using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>
    /// Distance-managed actors with stable session identities. Terrain readiness gates spawning;
    /// live actors use their current position for unloading and retain their supporting terrain.
    /// All pool and Unity operations run on the Unity main thread.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/Map Entity Streamer")]
    public sealed class GridMapEntityStreamer : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float loadDistance = 32f;
        [SerializeField, Min(.1f)] private float unloadDistance = 48f;
        [SerializeField, Range(1, 4096)] private int maxActors = 128;
        [SerializeField, Range(1, 128)] private int spawnRequestsPerFrame = 4;
        [SerializeField, Range(0, 4096)] private int maxIdlePerPrefab = 8;
        [SerializeField, Min(.1f)] private float failedSpawnRetrySeconds = 1f;

        private sealed class Entry
        {
            internal string Id;
            internal int Version;
            internal GameObject Prefab;
            internal MapTileType TileType;
            internal MapSpawnDefinition Spawn;
            internal int Ordinal;
            internal Vector2Int Cell;
            internal MapLayer Layer;
            internal Vector3 InitialPosition;
            internal float MaxHealth, RespawnSeconds;
            internal MapSpawnKind Kind;
            internal MapEntityState State;
            internal MapStreamedEntity Actor;
            internal PoolLease<GameObject> Lease;
            internal SpawnPending Request;
            internal double RetryAt;
            internal bool Removed;
        }

        private sealed class SpawnPending
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal int Session;
            internal Task<RentResult<GameObject>> Task;
            internal MapStreamedEntity PreparedActor;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<Entry> candidates = new List<Entry>();
        private readonly List<Entry> active = new List<Entry>();
        private readonly List<Entry> pending = new List<Entry>();
        private readonly Dictionary<GameObject, PrefabPool<MapEntitySpawnArgs>> pools = new Dictionary<GameObject, PrefabPool<MapEntitySpawnArgs>>();
        private GridMapRenderer renderer;
        private GridMapRuntimeState runtimeState;
        private PoolDriver driver;
        private PoolScope scope;
        private string poolPrefix;
        private int session, nextVersion, scanCursor, counterFrame = -1;
        private int lastActorsSpawned, lastActorsRecycled, lastSpawnRequestsStarted;
        private bool initialized;

        public float LoadDistance { get => Positive(loadDistance, 32f); set => loadDistance = Positive(value, 32f); }
        public float UnloadDistance { get => Mathf.Max(LoadDistance, Positive(unloadDistance, 48f)); set => unloadDistance = Positive(value, 48f); }
        public int MaxActors { get => Mathf.Clamp(maxActors, 1, 4096); set => maxActors = Mathf.Clamp(value, 1, 4096); }
        public int SpawnRequestsPerFrame { get => Mathf.Clamp(spawnRequestsPerFrame, 1, 128); set => spawnRequestsPerFrame = Mathf.Clamp(value, 1, 128); }
        public int MaxIdlePerPrefab { get => Mathf.Clamp(maxIdlePerPrefab, 0, 4096); set => maxIdlePerPrefab = Mathf.Clamp(value, 0, 4096); }
        public float FailedSpawnRetrySeconds { get => Positive(failedSpawnRetrySeconds, 1f); set => failedSpawnRetrySeconds = Positive(value, 1f); }
        public int ActiveCount => active.Count;
        public int PendingCount => pending.Count;
        /// <summary>Only request submissions are budgeted. Asynchronous completions can share a later frame.</summary>
        public int LastActorsSpawned { get { ResetFrameCounters(); return lastActorsSpawned; } }
        public int LastActorsRecycled { get { ResetFrameCounters(); return lastActorsRecycled; } }
        public int LastSpawnRequestsStarted { get { ResetFrameCounters(); return lastSpawnRequestsStarted; } }
        public Task CleanupTask { get; private set; } = Task.CompletedTask;

        /// <summary>Build the source cache once; later cell edits update only the affected cache entry.</summary>
        public void Initialize(GridMapRenderer source)
        {
            if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || source == null || !source.isActiveAndEnabled ||
                !source.StreamingEnabled || source.IsPreviewCleared || source.Map == null ||
                source.RuntimeState == null || source.RuntimeState.Map != source.Map) return;
            if (initialized && renderer == source && runtimeState == source.RuntimeState) return;
            Shutdown();
            renderer = source; runtimeState = source.RuntimeState;
            driver = FindDriver();
            poolPrefix = "map-entities-" + Guid.NewGuid().ToString("N");
            scope = driver.Service.CreateScope(poolPrefix);
            initialized = true; session++; scanCursor = 0;
            GridMapAsset map = source.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    for (int layer = 0; layer < 2; layer++) AddCell(x, y, (MapLayer)layer, false);
            if (map.Spawns != null)
                foreach (MapSpawnDefinition spawn in map.Spawns)
                {
                    if (spawn == null || string.IsNullOrWhiteSpace(spawn.Id) || spawn.Prefab == null || !map.Contains(spawn.Cell.x, spawn.Cell.y)) continue;
                    Vector3 origin = source.transform.InverseTransformPoint(source.CellToWorldCenter(spawn.Cell.x, spawn.Cell.y));
                    for (int i = 0; i < Mathf.Clamp(spawn.Count, 1, 128); i++)
                    {
                        var entry = new Entry
                        {
                            Id = "spawn:" + spawn.Id + ":" + i, Prefab = spawn.Prefab, Spawn = spawn, Ordinal = i,
                            Cell = spawn.Cell, InitialPosition = origin + Vector3.right * (i * Mathf.Max(0f, spawn.Spacing) * map.CellSize),
                            MaxHealth = Positive(spawn.MaxHealth, 100f), RespawnSeconds = Mathf.Max(0f, spawn.RespawnSeconds), Kind = spawn.Kind
                        };
                        AddEntry(entry, false);
                    }
                }
            runtimeState.CellChanged += OnCellChanged;
        }

        private void Update()
        {
            ResetFrameCounters();
            if (!initialized) return;
            if (renderer == null || !renderer.isActiveAndEnabled || !renderer.StreamingEnabled || runtimeState != renderer.RuntimeState)
            { Shutdown(); return; }
            Vector3 focus = renderer.StreamingFocusPosition;
            float unloadSquared = UnloadDistance * UnloadDistance;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Entry entry = active[i];
                if (entry.Actor == null || !entry.Lease.TryGet(out _)) { Recycle(entry, true); continue; }
                entry.Actor.CaptureState();
                if (!SourceValid(entry) || entry.State.Destroyed ||
                    (!entry.Actor.CombatPinned && PlaneDistanceSquared(entry.Actor.transform.position, focus) > unloadSquared)) Recycle(entry, true);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Entry entry = pending[i];
                if (!CanSpawn(entry, focus)) CancelRequest(entry);
            }
            // A bounded rotating scan avoids revisiting every cell of a large map every frame.
            int checks = Mathf.Min(candidates.Count, Mathf.Max(512, MaxActors * 4));
            for (int checkedCount = 0; checkedCount < checks && active.Count + pending.Count < MaxActors && lastSpawnRequestsStarted < SpawnRequestsPerFrame; checkedCount++)
            {
                if (scanCursor >= candidates.Count) scanCursor = 0;
                Entry entry = candidates[scanCursor++];
                if (entry.Removed || entry.Actor != null || entry.Request != null || Time.timeAsDouble < entry.RetryAt || !SourceValid(entry)) continue;
                if (entry.State.Destroyed && entry.RespawnSeconds > 0f && entry.State.RespawnAt > 0d && Time.timeAsDouble >= entry.State.RespawnAt)
                    ResetState(entry);
                if (CanSpawn(entry, focus)) StartSpawn(entry);
            }
        }

        public bool TryGetActor(string entityId, out MapStreamedEntity actor)
        {
            actor = null;
            if (entityId == null || !entries.TryGetValue(entityId, out Entry entry) || entry.Actor == null) return false;
            actor = entry.Actor; return true;
        }

        /// <summary>Append live actors so the renderer retains terrain until the actors have returned to the pool.</summary>
        public void GetRequiredTerrainPositions(List<Vector3> positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            foreach (Entry entry in active)
                if (entry.Actor != null && entry.Actor.gameObject.activeInHierarchy) positions.Add(entry.Actor.transform.position);
            // Pool activation can finish before its Task continuation is dispatched by Unity.
            foreach (Entry entry in pending)
                if (entry.Request?.PreparedActor != null && entry.Request.PreparedActor.gameObject.activeInHierarchy)
                    positions.Add(entry.Request.PreparedActor.transform.position);
        }

        public void CaptureRuntimeState()
        {
            foreach (Entry entry in active) if (entry.Actor != null) entry.Actor.CaptureState();
            foreach (Entry entry in pending) if (entry.Request?.PreparedActor != null) entry.Request.PreparedActor.CaptureState();
        }
        public void CaptureActiveStates() { CaptureRuntimeState(); }

        /// <summary>
        /// Cancel and return synchronously, then drain owned pools on the persistent driver.
        /// Pass false after replacing session state from a save. Never closes the shared driver.
        /// </summary>
        public void Shutdown(bool captureState = true)
        {
            if (!initialized && scope == null) return;
            if (captureState) CaptureRuntimeState();
            if (runtimeState != null) runtimeState.CellChanged -= OnCellChanged;
            initialized = false; session++;
            foreach (Entry entry in pending)
            {
                if (entry.Request == null) continue;
                entry.Request.Cancellation.Cancel(); ReturnCompletedRequest(entry.Request, false); entry.Request = null;
            }
            pending.Clear();
            for (int i = active.Count - 1; i >= 0; i--) Recycle(active[i], false);
            PoolScope oldScope = scope;
            PoolService service = driver == null ? null : driver.Service;
            var oldPools = new List<PrefabPool<MapEntitySpawnArgs>>(pools.Values);
            scope = null; pools.Clear(); entries.Clear(); candidates.Clear();
            renderer = null; runtimeState = null; driver = null;
            Task scopeClosed = oldScope == null ? Task.CompletedTask : oldScope.CloseAsync();
            Task cleanup = ClosePoolsAsync(service, oldPools, scopeClosed);
            CleanupTask = Task.WhenAll(CleanupTask, cleanup);
        }

        internal bool IsBindingCurrent(string id, int version, MapEntityState state)
        {
            return initialized && id != null && entries.TryGetValue(id, out Entry entry) && !entry.Removed &&
                entry.Version == version && ReferenceEquals(entry.State, state);
        }

        internal void ActorPrepared(MapStreamedEntity actor, int version)
        {
            if (actor != null && actor.EntityId != null && entries.TryGetValue(actor.EntityId, out Entry entry) &&
                entry.Version == version && entry.Request != null) entry.Request.PreparedActor = actor;
        }

        internal bool CanDamageEntity(string id, int version, MapEntityState state)
        {
            if (!IsBindingCurrent(id, version, state)) return false;
            Entry entry = entries[id];
            return SourceValid(entry) && (entry.Spawn != null || (entry.TileType != null && entry.TileType.CanBeDestroyed));
        }

        internal void EntityKilled(string id, int version, MapEntityState state)
        {
            if (!IsBindingCurrent(id, version, state)) return;
            Entry entry = entries[id];
            if (entry.Spawn == null && entry.TileType != null && entry.TileType.CanBeDestroyed)
                runtimeState.SetCell(entry.Cell.x, entry.Cell.y, null, entry.Layer);
        }

        internal void IgniteEntity(string id, int version, MapEntityState state)
        {
            if (!IsBindingCurrent(id, version, state)) return;
            Entry entry = entries[id];
            if (entry.Spawn != null) state.Ignited = true;
            else if (entry.TileType != null && entry.TileType.CanBeIgnited)
            {
                runtimeState.IgniteCell(entry.Cell.x, entry.Cell.y, entry.Layer);
                if (runtimeState.IsCellIgnited(entry.Cell.x, entry.Cell.y, entry.Layer)) state.Ignited = true;
            }
        }

        private void AddCell(int x, int y, MapLayer layer, bool resetState)
        {
            MapTileType type = runtimeState.GetCell(x, y, layer);
            if (type == null || type.Prefab == null) return;
            MapStreamedEntity settings = type.Prefab.GetComponent<MapStreamedEntity>();
            var entry = new Entry
            {
                Id = CellId(x, y, layer), Cell = new Vector2Int(x, y), Layer = layer,
                TileType = type, Prefab = type.Prefab, Kind = MapSpawnKind.Interactable,
                InitialPosition = renderer.transform.InverseTransformPoint(renderer.CellToWorldCenter(x, y)),
                MaxHealth = settings == null ? 100f : settings.DefaultMaxHealth
            };
            AddEntry(entry, resetState);
            if (runtimeState.IsCellIgnited(x, y, layer)) entry.State.Ignited = true;
        }

        private void AddEntry(Entry entry, bool resetState)
        {
            if (entries.ContainsKey(entry.Id))
            { Debug.LogWarning("地图包含重复生成点标识，已忽略：" + entry.Id, this); return; }
            entry.Version = ++nextVersion;
            entry.State = runtimeState.GetEntity(entry.Id, entry.InitialPosition, entry.MaxHealth);
            if (resetState) ResetState(entry);
            entries.Add(entry.Id, entry); candidates.Add(entry);
        }

        private void OnCellChanged(int x, int y, MapLayer layer)
        {
            string id = CellId(x, y, layer);
            MapTileType type = runtimeState.GetCell(x, y, layer);
            if (entries.TryGetValue(id, out Entry entry))
            {
                if (type == entry.TileType && type != null && type.Prefab == entry.Prefab)
                {
                    if (runtimeState.IsCellIgnited(x, y, layer)) entry.State.Ignited = true;
                    return;
                }
                // Invalidate the binding before returning: an old in-flight lease must not overwrite a replacement.
                entry.Removed = true; CancelRequest(entry); Recycle(entry, false);
                entry.State.Destroyed = true; entry.State.RespawnAt = 0d;
                entries.Remove(id);
                candidates.Remove(entry);
            }
            AddCell(x, y, layer, true);
        }

        private static void ResetState(Entry entry)
        {
            entry.State.LocalPosition = entry.InitialPosition; entry.State.Health = entry.MaxHealth;
            entry.State.Destroyed = false; entry.State.Opened = false; entry.State.Ignited = false; entry.State.RespawnAt = 0d;
        }

        private bool SourceValid(Entry entry)
        {
            if (entry.Removed || entry.Prefab == null) return false;
            if (entry.Spawn != null)
                return entry.Spawn.Enabled && entry.Spawn.Prefab == entry.Prefab && entry.Ordinal < entry.Spawn.Count;
            MapTileType type = runtimeState.GetCell(entry.Cell.x, entry.Cell.y, entry.Layer);
            return type != null && type == entry.TileType && type.Prefab == entry.Prefab;
        }

        private bool CanSpawn(Entry entry, Vector3 focus)
        {
            if (!initialized || !SourceValid(entry) || entry.State.Destroyed) return false;
            Vector3 world = renderer.transform.TransformPoint(entry.State.LocalPosition);
            if (PlaneDistanceSquared(world, focus) > LoadDistance * LoadDistance) return false;
            Vector2Int cell = renderer.WorldToCell(world);
            return renderer.IsCellLoaded(cell.x, cell.y);
        }

        private void StartSpawn(Entry entry)
        {
            try
            {
                PrefabPool<MapEntitySpawnArgs> pool = GetPool(entry.Prefab);
                var request = new SpawnPending { Session = session };
                entry.Request = request; pending.Add(entry); lastSpawnRequestsStarted++;
                Vector3 scale = entry.Prefab.transform.localScale;
                // Tilemap prefabs previously inherited the generated Grid's cell-size scale.
                // Actors from independent spawn points keep their authored prefab dimensions.
                if (entry.Spawn == null)
                    scale = Vector3.Scale(scale, new Vector3(runtimeState.Map.CellSize, runtimeState.Map.CellSize, 1f));
                var pose = new SpawnTransform(renderer.transform, entry.State.LocalPosition,
                    entry.Prefab.transform.localRotation, scale);
                var args = new MapEntitySpawnArgs(this, entry.State, renderer.transform, entry.Kind, entry.MaxHealth, entry.RespawnSeconds, entry.Version);
                request.Task = pool.SpawnAsync(scope, pose, args, cancellationToken: request.Cancellation.Token);
                _ = CompleteSpawnAsync(entry, request, request.Task);
            }
            catch (Exception exception)
            {
                SpawnPending request = entry.Request;
                if (request != null)
                {
                    entry.Request = null; pending.Remove(entry);
                    request.Cancellation.Cancel(); request.Cancellation.Dispose();
                }
                entry.RetryAt = Time.timeAsDouble + FailedSpawnRetrySeconds;
                Debug.LogException(exception, this);
            }
        }

        private async Task CompleteSpawnAsync(Entry entry, SpawnPending request, Task<RentResult<GameObject>> task)
        {
            PoolLease<GameObject> lease = default;
            bool accepted = false;
            try
            {
                RentResult<GameObject> result = await task;
                lease = result.Lease;
                if (!result.Succeeded) { entry.RetryAt = Time.timeAsDouble + FailedSpawnRetrySeconds; return; }
                if (!initialized || request.Session != session || entry.Request != request || request.Cancellation.IsCancellationRequested ||
                    !CanSpawn(entry, renderer.StreamingFocusPosition) || !lease.TryGet(out GameObject instance)) return;
                MapStreamedEntity actor = instance.GetComponent<MapStreamedEntity>();
                if (actor == null || !ReferenceEquals(actor.State, entry.State)) return;
                entry.Lease = lease; entry.Actor = actor; active.Add(entry); accepted = true;
                ResetFrameCounters(); lastActorsSpawned++;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                entry.RetryAt = Time.timeAsDouble + FailedSpawnRetrySeconds;
                if (this != null) Debug.LogException(exception, this);
            }
            finally
            {
                if (!accepted)
                {
                    if (lease.TryGet(out GameObject instance))
                    {
                        MapStreamedEntity actor = instance.GetComponent<MapStreamedEntity>();
                        if (actor != null) actor.ReleaseBinding(false);
                    }
                    lease.TryReturn();
                }
                if (entry.Request == request) { entry.Request = null; pending.Remove(entry); }
                request.Cancellation.Dispose();
            }
        }

        private void CancelRequest(Entry entry)
        {
            SpawnPending request = entry.Request;
            if (request == null) return;
            // Keep its actor slot reserved until completion returns any already-prepared lease.
            // This also prevents a second request for the same identity while cancellation settles.
            if (!request.Cancellation.IsCancellationRequested) request.Cancellation.Cancel();
            ReturnCompletedRequest(request, true);
        }

        private static void ReturnCompletedRequest(SpawnPending request, bool captureState)
        {
            Task<RentResult<GameObject>> task = request.Task;
            if (task == null || task.Status != TaskStatus.RanToCompletion || !task.Result.Succeeded) return;
            PoolLease<GameObject> lease = task.Result.Lease;
            if (lease.TryGet(out GameObject instance))
            {
                MapStreamedEntity actor = instance.GetComponent<MapStreamedEntity>();
                if (actor != null) actor.ReleaseBinding(captureState);
            }
            lease.TryReturn();
        }

        private void Recycle(Entry entry, bool captureState)
        {
            bool wasActive = active.Remove(entry);
            if (entry.Actor != null) entry.Actor.ReleaseBinding(captureState);
            entry.Actor = null; entry.Lease.TryReturn(); entry.Lease = default;
            if (wasActive) { ResetFrameCounters(); lastActorsRecycled++; }
        }

        private PrefabPool<MapEntitySpawnArgs> GetPool(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out PrefabPool<MapEntitySpawnArgs> pool)) return pool;
            var settings = new PoolSettings
            {
                InitialStorageCapacity = Mathf.Min(16, MaxActors + MaxIdlePerPrefab),
                MaxBorrowed = MaxActors, MaxIdle = MaxIdlePerPrefab, MaxResident = MaxActors + MaxIdlePerPrefab,
                MaxPendingRequests = MaxActors, IdleTimeoutSeconds = 30d, RequestTimeoutSeconds = 10d
            };
            var provider = new EntityProvider(new SharedPrefabResource(prefab).Acquire());
            try
            {
                pool = new PrefabPool<MapEntitySpawnArgs>(driver.Service,
                    new PoolKey(PoolKind.Prefab, poolPrefix + ":" + prefab.GetInstanceID()), settings.Freeze(), provider, driver.CacheRoot);
                driver.Service.Register(pool); pools.Add(prefab, pool); return pool;
            }
            catch { provider.ReleaseResources(); throw; }
        }

        private static PoolDriver FindDriver()
        {
            foreach (PoolDriver candidate in FindObjectsOfType<PoolDriver>())
                if (candidate.isActiveAndEnabled && candidate.Service != null && candidate.Service.State == PoolState.Running && candidate.CacheRoot != null)
                    return candidate;
            return new GameObject("Map Entity Pool Driver").AddComponent<PoolDriver>();
        }

        private static async Task ClosePoolsAsync(PoolService service, List<PrefabPool<MapEntitySpawnArgs>> ownedPools, Task scopeClosed)
        {
            try
            {
                await scopeClosed;
                var tasks = new List<Task>(ownedPools.Count);
                foreach (PrefabPool<MapEntitySpawnArgs> pool in ownedPools) tasks.Add(pool.CloseAsync());
                await Task.WhenAll(tasks);
                if (service != null && service.State == PoolState.Running)
                    foreach (PrefabPool<MapEntitySpawnArgs> pool in ownedPools)
                        if (service.HasPool(pool.Key)) service.UnregisterClosedPool(pool.Key);
            }
            catch (Exception exception) { Debug.LogException(exception); throw; }
        }

        private void ResetFrameCounters()
        {
            if (counterFrame == Time.frameCount) return;
            counterFrame = Time.frameCount; lastActorsSpawned = 0; lastActorsRecycled = 0; lastSpawnRequestsStarted = 0;
        }
        private void OnEnable()
        {
            if (!Application.IsPlaying(gameObject)) return;
            GridMapRenderer source = GetComponent<GridMapRenderer>();
            // When the whole host is re-enabled, the renderer may not have rebuilt yet.
            // Its BeginRuntimeStreaming path will initialize us after the first terrain chunk.
            if (source != null && source.isActiveAndEnabled && source.LoadedChunkCount > 0) Initialize(source);
        }
        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { Shutdown(); }
        private float PlaneDistanceSquared(Vector3 first, Vector3 second)
        {
            return Vector3.ProjectOnPlane(first - second, renderer.transform.forward).sqrMagnitude;
        }
        private static string CellId(int x, int y, MapLayer layer) => "cell:" + layer + ":" + x + ":" + y;
        private static float Positive(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(.01f, value);

        /// <summary>Add the state component inside the pool's budgeted creation, before lifecycle discovery.</summary>
        private sealed class EntityProvider : IPrefabInstanceProvider
        {
            private readonly IPrefabInstanceProvider inner;
            internal EntityProvider(IPrefabInstanceProvider inner) { this.inner = inner; }
            public ResourceState State => inner.State;
            public Exception Error => inner.Error;
            public long SharedResourceEstimatedBytes => inner.SharedResourceEstimatedBytes;
            public void BeginLoad() { inner.BeginLoad(); }
            public void Poll() { inner.Poll(); }
            public GameObject CreateInactive(Transform cacheRoot)
            {
                GameObject instance = inner.CreateInactive(cacheRoot);
                try
                {
                    if (instance.GetComponent<MapStreamedEntity>() == null) instance.AddComponent<MapStreamedEntity>();
                    return instance;
                }
                catch { inner.RequestDestroy(instance); throw; }
            }
            public void RequestDestroy(GameObject instance) { inner.RequestDestroy(instance); }
            public bool IsDestroyComplete(GameObject instance, long issuedFrame, long currentFrame) => inner.IsDestroyComplete(instance, issuedFrame, currentFrame);
            public void ReleaseResources() { inner.ReleaseResources(); }
        }
    }
}

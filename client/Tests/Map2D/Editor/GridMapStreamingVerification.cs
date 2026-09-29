using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BigWorld.Map2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Isolated-project verification using actual Play Mode Update frames and the real object pool.
/// Run Unity -batchmode -nographics -executeMethod GridMapStreamingVerification.BeginBatch
/// -mapStreamingReport <json path>, without -quit. Ordinary editor sessions do nothing.
/// </summary>
[InitializeOnLoad]
public static class GridMapStreamingVerification
{
    private const string SessionKey = "BigWorld.Map2D.StreamingVerification.State";
    private const string FolderPrefix = "Assets/__Map2DStreamingVerification_";
    private const double TimeoutSeconds = 150d;
    private const int InitialActorCount = 8;
    private const string DamagedId = "spawn:npcs:0";
    private const string ChestId = "spawn:chest:0";
    private const string PermanentId = "spawn:permanent:0";
    private const string RespawningId = "spawn:respawning:0";
    private const string CombatId = "spawn:combat:0";
    private const string FarId = "spawn:far:0";

    [Serializable] private sealed class Result
    {
        public string name;
        public bool passed;
        public string error;
    }

    [Serializable] private sealed class FrameObservation
    {
        public int frame;
        public string stage;
        public float gameTime;
        public float fixedTime;
        public int loadedChunks;
        public int chunksLoaded;
        public int chunksUnloaded;
        public int activeActors;
        public int pendingActors;
        public int spawnRequestsStarted;
        public int actorsSpawned;
        public int actorsRecycled;
    }

    [Serializable] private sealed class Report
    {
        public string generatedUtc;
        public string completedUtc;
        public string unityVersion;
        public int passed;
        public int failed;
        public bool temporaryAssetsCleaned;
        public int observedFrames;
        public int observedFixedTimeChanges;
        public int framesWithChunkWork;
        public int framesWithSpawnRequests;
        public int maximumChunkWorkPerFrame;
        public int maximumSpawnRequestsPerFrame;
        public int duplicateIdentityFrames;
        public int pendingAtDisable;
        public int pendingAtMapReplacement;
        public List<Result> results = new List<Result>();
        public List<FrameObservation> frames = new List<FrameObservation>();
    }

    [Serializable] private sealed class State
    {
        public string stage;
        public string startedUtc;
        public string leavingUtc;
        public string folder;
        public string scenePath;
        public string hostName;
        public string reportPath;
        public string mapHash;
        public string alternateMapHash;
        public string capabilityMapHash;
        public bool temporaryFolderCreated;
        public bool timedOut;
        public Report report;
    }

    private sealed class Step
    {
        internal string Name;
        internal Action Begin;
        internal Func<bool> Ready;
        internal Action Verify;
        internal int MinimumFrames = 5;
        internal float MinimumSeconds;
        internal double Timeout = 12d;
    }

    private static State state;
    private static bool finishing;
    private static GridMapRenderer renderer;
    private static GridMapEntityStreamer streamer;
    private static Transform target;
    private static GridMapAsset sourceMap;
    private static GridMapAsset alternateMap;
    private static GridMapAsset capabilityMap;
    private static MapTileType ground;
    private static MapTileType alternateGround;
    private static List<Step> steps;
    private static int stepIndex;
    private static bool stepStarted;
    private static int stepStartFrame;
    private static float stepStartTime;
    private static DateTime stepStartUtc;
    private static int lastFrame = -1;
    private static float lastFixedTime;
    private static readonly HashSet<int> originalInstances = new HashSet<int>();
    private static int farInstance;
    private static int terrainTilemapInstance;
    private static int pinnedInstance;
    private static int preEditChunks;
    private static string savedJson;
    private static MapStreamedEntity releasedCellActor;
    private static MapEntityState destroyedCellState;

    static GridMapStreamingVerification()
    {
        if (Application.isBatchMode && !string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty)))
            EditorApplication.delayCall += Resume;
    }

    public static void BeginBatch()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("This verification is restricted to its own Unity batch process.");
        if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty)))
            throw new InvalidOperationException("A streaming verification is already running in this process.");
        string token = Guid.NewGuid().ToString("N");
        state = new State
        {
            stage = "Setup", startedUtc = DateTime.UtcNow.ToString("O"),
            folder = FolderPrefix + token, hostName = "Map2D Streaming Verification " + token,
            reportPath = GetReportPath(),
            report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion }
        };
        state.scenePath = state.folder + "/StreamingVerification.unity";
        SaveState();
        AttachCallbacks();
        try
        {
            PrepareScene();
            AddResult("Create a saved isolated scene with a large map and real pooled actor prefab", true, null);
            state.stage = "Entering";
            SaveState();
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            AddResult("Prepare isolated streaming verification", false, exception.ToString());
            LeavePlayMode();
        }
    }

    private static void PrepareScene()
    {
        Assert(!EditorApplication.isPlayingOrWillChangePlaymode, "Verification must begin in Edit Mode.");
        Assert(!AssetDatabase.IsValidFolder(state.folder), "Temporary folder must not already exist.");
        Assert(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", state.folder.Substring(7))), "Could not create temporary assets folder.");
        state.temporaryFolderCreated = true;
        SaveState();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var prefabSource = new GameObject("Streaming Verification Actor");
        prefabSource.AddComponent<MapStreamedEntity>();
        var marker = new GameObject("Streaming Verification Prefab Marker");
        marker.transform.SetParent(prefabSource.transform, false);
        GameObject prefab;
        try
        {
            prefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, state.folder + "/Actor.prefab");
            Assert(prefab != null, "Actor prefab was not saved.");
        }
        finally { Object.DestroyImmediate(prefabSource); }

        ground = CreateGround("Ground", Color.gray);
        alternateGround = CreateGround("AlternateGround", Color.green);
        sourceMap = CreateMap("Map", 192, 64, ground);
        AddSpawn(sourceMap, "npcs", prefab, new Vector2Int(18, 10), MapSpawnKind.Npc, 3);
        AddSpawn(sourceMap, "chest", prefab, new Vector2Int(20, 12), MapSpawnKind.Interactable);
        AddSpawn(sourceMap, "permanent", prefab, new Vector2Int(21, 10), MapSpawnKind.Monster);
        AddSpawn(sourceMap, "respawning", prefab, new Vector2Int(21, 11), MapSpawnKind.Monster, 1, .35f);
        AddSpawn(sourceMap, "combat", prefab, new Vector2Int(22, 10), MapSpawnKind.Monster);
        AddSpawn(sourceMap, "far", prefab, new Vector2Int(25, 10), MapSpawnKind.Npc);
        EditorUtility.SetDirty(sourceMap);
        AssetDatabase.SaveAssetIfDirty(sourceMap);
        alternateMap = CreateMap("AlternateMap", 48, 32, alternateGround);
        capabilityMap = CreateMap("CapabilityMap", 192, 32, ground);
        capabilityMap.CellSize = 1.5f;
        AddSpawn(capabilityMap, "scale-npc", prefab, new Vector2Int(20, 12), MapSpawnKind.Npc);
        var schema = ScriptableObject.CreateInstance<MapTilePropertySchema>();
        schema.EnsureBuiltInDefinitions();
        AssetDatabase.CreateAsset(schema, state.folder + "/Capabilities.asset");
        AddCapabilityCell(18, "DestroyAndBurn", true, true, prefab, schema);
        AddCapabilityCell(20, "NeitherCapability", false, false, prefab, schema);
        AddCapabilityCell(22, "DestroyOnly", true, false, prefab, schema);
        AddCapabilityCell(24, "BurnOnly", false, true, prefab, schema);
        EditorUtility.SetDirty(capabilityMap);
        AssetDatabase.SaveAssetIfDirty(capabilityMap);

        var host = new GameObject(state.hostName);
        // Use a transformed map so entity positions must use the renderer's cell conversion.
        host.transform.position = new Vector3(7f, -3f, 0f);
        host.transform.localScale = new Vector3(1.25f, 1.25f, 1f);
        target = new GameObject(state.hostName + " Target").transform;
        renderer = host.AddComponent<GridMapRenderer>();
        renderer.StreamingEnabled = true;
        renderer.ChunkSize = 8;
        renderer.LoadingRadiusCells = new Vector2Int(8, 8);
        renderer.UnloadPaddingCells = 8;
        renderer.UseCameraViewport = false;
        renderer.ChunksPerFrame = 1;
        renderer.StreamingBudgetMilliseconds = 100f;
        renderer.LoadingTarget = target;
        renderer.Map = sourceMap;
        target.position = renderer.CellToWorldCenter(20, 10);
        streamer = host.GetComponent<GridMapEntityStreamer>();
        if (streamer == null) streamer = host.AddComponent<GridMapEntityStreamer>();
        streamer.LoadDistance = 8f;
        streamer.UnloadDistance = 14f;
        streamer.SpawnRequestsPerFrame = 1;
        streamer.MaxActors = 32;
        renderer.ClearPreview();
        Assert(EditorSceneManager.SaveScene(scene, state.scenePath), "Could not save verification scene.");
        state.mapHash = HashAsset(state.folder + "/Map.asset");
        state.alternateMapHash = HashAsset(state.folder + "/AlternateMap.asset");
        state.capabilityMapHash = HashAsset(state.folder + "/CapabilityMap.asset");
    }

    private static MapTileType CreateGround(string name, Color color)
    {
        var type = ScriptableObject.CreateInstance<MapTileType>();
        type.Id = "streaming-verification-" + name;
        type.DisplayName = name;
        type.Layer = MapLayer.Terrain;
        type.Collision = MapTileCollisionMode.Solid;
        type.Tint = color;
        AssetDatabase.CreateAsset(type, state.folder + "/" + name + ".asset");
        return type;
    }

    private static GridMapAsset CreateMap(string name, int width, int height, MapTileType type)
    {
        var map = ScriptableObject.CreateInstance<GridMapAsset>();
        map.Initialize(width, height);
        for (int x = 0; x < width; x++)
        {
            map.SetCell(x, 0, type);
            map.SetCell(x, 8, type);
            map.SetCell(x, 16, type);
        }
        AssetDatabase.CreateAsset(map, state.folder + "/" + name + ".asset");
        return map;
    }

    private static void AddSpawn(GridMapAsset map, string id, GameObject prefab, Vector2Int cell,
        MapSpawnKind kind, int count = 1, float respawn = 0f)
    {
        map.Spawns.Add(new MapSpawnDefinition
        {
            Id = id, DisplayName = id, Prefab = prefab, Cell = cell, Kind = kind,
            Count = count, Spacing = 1f, MaxHealth = 100f, RespawnSeconds = respawn, Enabled = true
        });
    }

    private static void AddCapabilityCell(int x, string name, bool destructible, bool flammable, GameObject prefab, MapTilePropertySchema schema)
    {
        var type = ScriptableObject.CreateInstance<MapTileType>();
        type.Id = "verification-" + name;
        type.DisplayName = name;
        type.Layer = MapLayer.Objects;
        type.Collision = MapTileCollisionMode.Solid;
        type.PropertySchema = schema;
        type.Prefab = prefab;
        type.SetPropertyOverride(new MapTilePropertyValue { Key = MapTilePropertyKeys.Destructible, Kind = MapTilePropertyKind.Boolean, BoolValue = destructible });
        type.SetPropertyOverride(new MapTilePropertyValue { Key = MapTilePropertyKeys.Flammable, Kind = MapTilePropertyKind.Boolean, BoolValue = flammable });
        AssetDatabase.CreateAsset(type, state.folder + "/" + name + ".asset");
        capabilityMap.SetCell(x, 10, type, MapLayer.Objects);
    }

    private static void Resume()
    {
        if (!Application.isBatchMode || finishing) return;
        string json = SessionState.GetString(SessionKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;
        state = JsonUtility.FromJson<State>(json);
        AttachCallbacks();
        if (state.stage == "Entering" && EditorApplication.isPlaying) StartRuntimeSteps();
        else if (state.stage == "Leaving" && !EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Finish;
    }

    private static void AttachCallbacks()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (state == null || finishing) return;
        if (change == PlayModeStateChange.EnteredPlayMode && state.stage == "Entering") StartRuntimeSteps();
        else if (change == PlayModeStateChange.EnteredEditMode && state.stage == "Leaving")
            EditorApplication.delayCall += Finish;
    }

    private static void StartRuntimeSteps()
    {
        Assert(SceneManager.GetActiveScene().path == state.scenePath, "Only the saved temporary scene may run.");
        renderer = FindRoot(state.hostName).GetComponent<GridMapRenderer>();
        target = FindRoot(state.hostName + " Target").transform;
        Assert(renderer != null, "Saved renderer did not survive domain reload.");
        streamer = renderer.GetComponent<GridMapEntityStreamer>();
        Assert(streamer != null, "Saved entity streamer is missing.");
        sourceMap = AssetDatabase.LoadAssetAtPath<GridMapAsset>(state.folder + "/Map.asset");
        alternateMap = AssetDatabase.LoadAssetAtPath<GridMapAsset>(state.folder + "/AlternateMap.asset");
        capabilityMap = AssetDatabase.LoadAssetAtPath<GridMapAsset>(state.folder + "/CapabilityMap.asset");
        ground = AssetDatabase.LoadAssetAtPath<MapTileType>(state.folder + "/Ground.asset");
        alternateGround = AssetDatabase.LoadAssetAtPath<MapTileType>(state.folder + "/AlternateGround.asset");
        originalInstances.Clear();
        steps = BuildSteps();
        stepIndex = 0;
        stepStarted = false;
        lastFrame = Time.frameCount;
        lastFixedTime = Time.fixedTime;
        state.stage = "Running";
        SaveState();
        renderer.StartCoroutine(DriveRuntimeFrames());
    }

    private static List<Step> BuildSteps()
    {
        return new List<Step>
        {
            new Step
            {
                Name = "Large map startup loads nearby chunks and actual prefab actors only",
                Ready = () => renderer.IsCellLoaded(12, 8) && renderer.IsCellLoaded(28, 16) && streamer.ActiveCount == InitialActorCount,
                Verify = () =>
                {
                    Assert(renderer.RuntimeState != null && renderer.RuntimeState.Map == sourceMap, "Renderer lacks the source map session state.");
                    Assert(renderer.LoadedChunkCount > 0 && renderer.LoadedChunkCount < 48, "Startup loaded too much of the 192-chunk map: " + renderer.LoadedChunkCount);
                    Assert(!renderer.IsCellLoaded(132, 8), "Far map cells were loaded at startup.");
                    Assert(Vector3.Distance(renderer.StreamingFocusPosition, target.position) < .01f, "Loading target world position was ignored.");
                    Assert(ActiveActors().Count == InitialActorCount, "Active GameObject count differs from streamer count.");
                    foreach (var actor in ActiveActors())
                    {
                        Assert(actor.transform.Find("Streaming Verification Prefab Marker") != null, "Entity is not an instance of the saved test prefab.");
                        Assert(originalInstances.Add(actor.GetInstanceID()), "Duplicate live actor instance.");
                    }
                    Assert(Vector3.Distance(FindActor(DamagedId).transform.position, renderer.CellToWorldCenter(18, 10)) < .01f,
                        "NPC was not spawned at its transformed map cell center.");
                }
            },
            new Step
            {
                Name = "Loaded chunk borders have real colliders on both sides",
                Verify = () =>
                {
                    Physics2D.SyncTransforms();
                    AssertGroundCollider(23, 8);
                    AssertGroundCollider(24, 8);
                    AssertGroundCollider(24, 16);
                }
            },
            new Step
            {
                Name = "One runtime cell edit preserves distant actors, tilemaps, and authored data",
                Begin = () =>
                {
                    farInstance = FindActor(FarId).GetInstanceID();
                    terrainTilemapInstance = GroundTilemap().GetInstanceID();
                    preEditChunks = renderer.LoadedChunkCount;
                    Assert(renderer.RuntimeState.SetCell(18, 8, null), "Expected to remove the session ground cell.");
                    renderer.RefreshCell(18, 8, MapLayer.Terrain);
                },
                Verify = () =>
                {
                    Assert(renderer.RuntimeState.GetCell(18, 8) == null, "Session edit was lost.");
                    Assert(sourceMap.GetCell(18, 8) == ground, "Runtime edit changed the authoring map.");
                    Assert(GroundTilemap().GetTile(new Vector3Int(18, 8, 0)) == null, "Edited cell remains in the rendered tilemap.");
                    Assert(RenderedSource(25, 8) == ground, "Unrelated ground changed.");
                    Assert(FindActor(FarId).GetInstanceID() == farInstance, "Local edit rebuilt a distant pooled actor.");
                    Assert(GroundTilemap().GetInstanceID() == terrainTilemapInstance, "Local edit replaced the tilemap hierarchy.");
                    Assert(renderer.LoadedChunkCount == preEditChunks, "Local edit unloaded unrelated chunks.");
                    Physics2D.SyncTransforms();
                    Assert(Physics2D.OverlapPoint(renderer.CellToWorldCenter(18, 8)) == null, "Removed cell kept a physics collider.");
                    AssertGroundCollider(25, 8);
                }
            },
            new Step
            {
                Name = "Real pooled entities record health, interaction, fire, and permanent death",
                Begin = () =>
                {
                    FindActor(DamagedId).TakeDamage(25f);
                    FindActor(ChestId).MarkOpened();
                    FindActor(ChestId).Ignite();
                    FindActor(PermanentId).Kill();
                    FindActor(RespawningId).Kill();
                },
                MinimumSeconds = .5f,
                Ready = () => TryFindActor(RespawningId) != null && TryFindActor(PermanentId) == null,
                Verify = () =>
                {
                    Assert(Mathf.Approximately(FindActor(DamagedId).Health, 75f), "Damage did not persist to actor state.");
                    Assert(FindActor(ChestId).Opened && FindActor(ChestId).Ignited, "Interaction or ignition state was lost.");
                    Assert(EntityState(PermanentId).Destroyed, "Permanent death was not recorded.");
                    Assert(!FindActor(RespawningId).Destroyed && Mathf.Approximately(FindActor(RespawningId).Health, 100f), "Timed respawn failed to restore health.");
                    Assert(TryFindActor(PermanentId) == null, "RespawnSeconds=0 revived a dead actor.");
                }
            },
            new Step
            {
                Name = "Crossing one chunk loads the next region and retains the hysteresis band",
                Begin = () => MoveFocus(28, 10),
                Ready = () => renderer.IsCellLoaded(36, 8),
                Verify = () => Assert(renderer.IsCellLoaded(12, 8), "A chunk inside the unload padding was removed immediately.")
            },
            new Step
            {
                Name = "Moving far unloads old chunks and recycles all unpinned actors",
                Begin = () => MoveFocus(132, 10),
                Ready = () => renderer.IsCellLoaded(132, 8) && !renderer.IsCellLoaded(18, 8) && streamer.ActiveCount == 0 && streamer.PendingCount == 0,
                Verify = () =>
                {
                    Assert(!renderer.IsCellLoaded(12, 8), "Old hysteresis region was retained after a distant move.");
                    Assert(ActiveActors().Count == 0, "Recycled actors are still active in the scene.");
                    Assert(renderer.LoadedChunkCount < 48, "Movement accumulated distant chunks without unloading.");
                    Assert(CountOriginalInstancesStillAlive() == InitialActorCount, "Actors were destroyed instead of returned to their pool.");
                }
            },
            new Step
            {
                Name = "Session JSON restores cell and entity states and rejects another map atomically",
                Verify = VerifyStateJson
            },
            new Step
            {
                Name = "Returning restores edits and entity state using the same pooled objects",
                Begin = () => MoveFocus(20, 10),
                Ready = () => renderer.IsCellLoaded(18, 8) && streamer.ActiveCount == InitialActorCount - 1,
                Verify = () =>
                {
                    Assert(GroundTilemap().GetTile(new Vector3Int(18, 8, 0)) == null, "Destroyed terrain returned after chunk reload.");
                    Assert(sourceMap.GetCell(18, 8) == ground, "Authoring map was modified during reload.");
                    Assert(Mathf.Approximately(FindActor(DamagedId).Health, 75f), "HP reset during pooling.");
                    Assert(FindActor(ChestId).Opened && FindActor(ChestId).Ignited, "Opened or Ignited reset during pooling.");
                    Assert(EntityState(PermanentId).Destroyed && TryFindActor(PermanentId) == null, "Destroyed actor revived after unloading.");
                    foreach (var actor in ActiveActors()) Assert(originalInstances.Contains(actor.GetInstanceID()), "Reload instantiated a new actor despite available pooled objects.");
                }
            },
            new Step
            {
                Name = "Loading session JSON updates live actors without old-state writeback",
                Begin = () =>
                {
                    savedJson = renderer.SaveRuntimeState();
                    FindActor(DamagedId).TakeDamage(20f);
                    Assert(Mathf.Approximately(FindActor(DamagedId).Health, 55f), "Live actor mutation did not occur before loading.");
                    renderer.RuntimeState.SetCell(18, 8, ground);
                    renderer.LoadRuntimeState(savedJson);
                },
                Ready = () => TryFindActor(DamagedId) != null && renderer.IsCellLoaded(18, 8) && streamer.ActiveCount == InitialActorCount - 1,
                Verify = () =>
                {
                    Assert(Mathf.Approximately(FindActor(DamagedId).Health, 75f), "Old active actor overwrote the restored HP.");
                    Assert(renderer.RuntimeState.GetCell(18, 8) == null && RenderedSource(18, 8) == null, "Loading JSON did not rebuild restored terrain.");
                    Assert(TryFindActor(PermanentId) == null && EntityState(PermanentId).Destroyed, "Loading JSON revived permanent death.");
                }
            },
            new Step
            {
                Name = "Combat-pinned pursuit stays active across regions and retains terrain at its current position",
                Begin = () =>
                {
                    var actor = FindActor(CombatId);
                    pinnedInstance = actor.GetInstanceID();
                    actor.CombatPinned = true;
                    actor.transform.position = renderer.CellToWorldCenter(60, 10);
                    actor.CaptureState();
                    MoveFocus(132, 10);
                },
                Ready = () => streamer.ActiveCount == 1 && renderer.IsCellLoaded(60, 8),
                Verify = () =>
                {
                    var actor = FindActor(CombatId);
                    Assert(actor.GetInstanceID() == pinnedInstance && actor.CombatPinned, "Combat-pinned actor was recycled.");
                    Assert(Vector3.Distance(actor.transform.position, renderer.CellToWorldCenter(60, 10)) < .01f, "Pursuit position reset to its spawn point.");
                    AssertGroundCollider(60, 8);
                }
            },
            new Step
            {
                Name = "Ending combat permits pooling and releases its remote terrain requirement",
                Begin = () => FindActor(CombatId).CombatPinned = false,
                Ready = () => streamer.ActiveCount == 0 && !renderer.IsCellLoaded(60, 8),
                Verify = () => Assert(TryFindActor(CombatId) == null, "Unpinned distant actor stayed active.")
            },
            new Step
            {
                Name = "A moved actor reloads from saved position with combat pin cleared",
                Begin = () => MoveFocus(60, 10),
                Ready = () => TryFindActor(CombatId) != null,
                Verify = () =>
                {
                    var actor = FindActor(CombatId);
                    Assert(!actor.CombatPinned, "Pooled combat pin leaked into the next activation.");
                    Assert(Vector3.Distance(actor.transform.position, renderer.CellToWorldCenter(60, 10)) < .01f, "Pooled actor forgot its moved position.");
                }
            },
            new Step
            {
                Name = "Start new loading work before disabling the renderer",
                Begin = () => MoveFocus(20, 10),
                MinimumFrames = 1,
                Verify = () => Assert(renderer.enabled, "Renderer unexpectedly disabled before cancellation test.")
            },
            new Step
            {
                Name = "Disabled renderer has no active or late-spawned actors after real frames",
                Begin = () =>
                {
                    state.report.pendingAtDisable = streamer.PendingCount;
                    renderer.enabled = false;
                },
                MinimumFrames = 12,
                MinimumSeconds = .1f,
                Verify = () =>
                {
                    Assert(streamer.ActiveCount == 0 && streamer.PendingCount == 0, "Disable left active or pending actor work.");
                    Assert(ActiveActors().Count == 0, "An asynchronous actor appeared after disable.");
                    Assert(renderer.LoadedChunkCount == 0, "Disable retained loaded terrain chunks.");
                }
            },
            new Step
            {
                Name = "Re-enabling the same map preserves its runtime state",
                Begin = () => renderer.enabled = true,
                MinimumFrames = 1,
                Ready = () => TryFindActor(DamagedId) != null && renderer.IsCellLoaded(18, 8),
                Verify = () =>
                {
                    Assert(Mathf.Approximately(FindActor(DamagedId).Health, 75f), "Disable/enable reset HP.");
                    Assert(renderer.RuntimeState.GetCell(18, 8) == null && GroundTilemap().GetTile(new Vector3Int(18, 8, 0)) == null, "Disable/enable reset terrain state.");
                }
            },
            new Step
            {
                Name = "Map replacement cancels old requests and never emits late old actors",
                Begin = () =>
                {
                    state.report.pendingAtMapReplacement = streamer.PendingCount;
                    renderer.Map = alternateMap;
                },
                MinimumFrames = 16,
                MinimumSeconds = .15f,
                Ready = () => renderer.IsCellLoaded(20, 8),
                Verify = () =>
                {
                    Assert(renderer.RuntimeState.Map == alternateMap, "Map replacement retained the old session state.");
                    Assert(streamer.ActiveCount == 0 && streamer.PendingCount == 0 && ActiveActors().Count == 0, "Old-map actors survived or spawned after replacement.");
                    Assert(renderer.RuntimeState.GetCell(18, 8) == alternateGround, "Old-map cell override leaked into the new map.");
                    Assert(RenderedSource(18, 8) == alternateGround, "A stale old chunk overwrote the new map.");
                    Assert(sourceMap.GetCell(18, 8) == ground, "Source asset was mutated.");
                }
            },
            new Step
            {
                Name = "Cell-prefab actors appear once with authored capability and collision configuration",
                Begin = () => { renderer.Map = capabilityMap; MoveFocus(20, 10); },
                Ready = () => streamer.ActiveCount == 5 && renderer.IsCellLoaded(24, 10),
                Verify = () =>
                {
                    Assert(ActiveActors().Count == 5, "Cell prefabs were duplicated by Tilemap and the entity streamer.");
                    for (int x = 18; x <= 24; x += 2)
                    {
                        Assert(FindActor(CellEntityId(x)).transform.Find("Streaming Verification Prefab Marker") != null, "Cell actor did not come from the saved prefab.");
                        Vector3 actualScale = FindActor(CellEntityId(x)).transform.localScale;
                        Assert(Vector3.Distance(actualScale, new Vector3(1.5f, 1.5f, 1f)) < .001f,
                            "Cell prefab XY scale does not follow CellSize; actual=" + actualScale + ", expected=(1.5, 1.5, 1).");
                        AssertObjectCollider(x, 10, true);
                    }
                    Assert(Vector3.Distance(FindActor("spawn:scale-npc:0").transform.localScale, Vector3.one) < .001f, "NPC prefab inherited the tile CellSize scale.");
                }
            },
            new Step
            {
                Name = "Cell-prefab damage and ignition obey all four capability combinations",
                Begin = () =>
                {
                    for (int x = 18; x <= 24; x += 2)
                    {
                        var actor = FindActor(CellEntityId(x));
                        actor.TakeDamage(25f);
                        actor.Ignite();
                    }
                    FindActor(CellEntityId(20)).Kill();
                    FindActor(CellEntityId(24)).Kill();
                },
                Verify = () =>
                {
                    for (int x = 18; x <= 24; x += 2)
                    {
                        var actor = FindActor(CellEntityId(x));
                        bool damageAllowed = x == 18 || x == 22;
                        bool fireAllowed = x == 18 || x == 24;
                        Assert(Mathf.Approximately(actor.Health, damageAllowed ? 75f : 100f), "Damage ignored the capability at " + x);
                        Assert(actor.Ignited == fireAllowed && renderer.RuntimeState.IsCellIgnited(x, 10, MapLayer.Objects) == fireAllowed,
                            "Ignition ignored the capability or was not stored on the cell at " + x);
                        Assert(!actor.Destroyed && renderer.RuntimeState.GetCell(x, 10, MapLayer.Objects) != null, "An indestructible cell accepted Kill().");
                    }
                }
            },
            new Step
            {
                Name = "Destroying a cell prefab clears only that cell and collider and safely unbinds the returned actor",
                Begin = () =>
                {
                    releasedCellActor = FindActor(CellEntityId(18));
                    destroyedCellState = releasedCellActor.State;
                    releasedCellActor.TakeDamage(1000f);
                    // These calls deliberately use a reference retained by gameplay after synchronous return.
                    // They must become harmless and must not dereference a cleared State/owner binding.
                    releasedCellActor.TakeDamage(1f);
                    releasedCellActor.Kill();
                    releasedCellActor.Ignite();
                    releasedCellActor.MarkOpened();
                    releasedCellActor.CaptureState();
                },
                Ready = () => streamer.ActiveCount == 4 && TryFindActor(CellEntityId(18)) == null,
                Verify = () =>
                {
                    Assert(releasedCellActor != null && !releasedCellActor.gameObject.activeInHierarchy && releasedCellActor.State == null,
                        "Destroyed cell actor was not safely returned and unbound.");
                    Assert(destroyedCellState != null && destroyedCellState.Destroyed && Mathf.Approximately(destroyedCellState.Health, 0f), "Destroyed state was discarded or overwritten.");
                    Assert(renderer.RuntimeState.GetCell(18, 10, MapLayer.Objects) == null, "Destroyed cell remains in runtime data.");
                    Assert(renderer.GetTilemap(MapLayer.Objects, MapTileCollisionMode.Solid).GetTile(new Vector3Int(18, 10, 0)) == null,
                        "Destroyed cell remains in the tilemap.");
                    AssertObjectCollider(18, 10, false);
                    for (int x = 20; x <= 24; x += 2)
                    {
                        Assert(renderer.RuntimeState.GetCell(x, 10, MapLayer.Objects) == capabilityMap.GetCell(x, 10, MapLayer.Objects), "Neighboring cell changed.");
                        AssertObjectCollider(x, 10, true);
                        Assert(FindActor(CellEntityId(x)).State != null, "Neighboring actor lost its binding.");
                    }
                    Assert(capabilityMap.GetCell(18, 10, MapLayer.Objects) != null, "Destruction changed the source map asset.");
                    Assert(HashAsset(state.folder + "/CapabilityMap.asset") == state.capabilityMapHash, "Cell interaction changed the authored map file.");
                }
            },
            new Step
            {
                Name = "Cell-prefab capability state survives unloading without reviving the destroyed cell",
                Begin = () => MoveFocus(132, 10),
                Ready = () => streamer.ActiveCount == 0 && !renderer.IsCellLoaded(18, 10),
                Verify = () => Assert(renderer.RuntimeState.GetCell(18, 10, MapLayer.Objects) == null, "Unloading restored a destroyed cell.")
            },
            new Step
            {
                Name = "Returning restores surviving cell-prefab HP and fire flags with no duplicate or missing binding",
                Begin = () => MoveFocus(20, 10),
                Ready = () => streamer.ActiveCount == 4 && renderer.IsCellLoaded(18, 10),
                Verify = () =>
                {
                    Assert(TryFindActor(CellEntityId(18)) == null && renderer.RuntimeState.GetCell(18, 10, MapLayer.Objects) == null, "Destroyed prefab revived on return.");
                    Assert(Mathf.Approximately(FindActor(CellEntityId(22)).Health, 75f), "Surviving cell HP reset during pooling.");
                    Assert(FindActor(CellEntityId(24)).Ignited && renderer.RuntimeState.IsCellIgnited(24, 10, MapLayer.Objects), "Cell fire state reset during pooling.");
                    Assert(!FindActor(CellEntityId(20)).Ignited && Mathf.Approximately(FindActor(CellEntityId(20)).Health, 100f), "Protected cell inherited another pooled actor's state.");
                    AssertObjectCollider(18, 10, false);
                    Assert(ActiveActors().Count == 4, "Cell reload duplicated actors.");
                }
            },
            new Step
            {
                Name = "Observed real frames honor chunk and actor request budgets without duplicate entities",
                MinimumFrames = 1,
                Verify = () =>
                {
                    Assert(state.report.observedFrames > 20 && state.report.observedFixedTimeChanges > 0, "No meaningful actual Play/FixedUpdate frames were observed.");
                    Assert(state.report.framesWithChunkWork > 0, "No deferred chunk work was observed.");
                    Assert(state.report.maximumChunkWorkPerFrame <= 1, "A runtime frame exceeded the configured one-chunk budget: " + state.report.maximumChunkWorkPerFrame);
                    Assert(state.report.framesWithSpawnRequests > 0, "No real pool spawn requests were observed.");
                    Assert(state.report.maximumSpawnRequestsPerFrame <= 1, "A frame exceeded the one-actor-request budget: " + state.report.maximumSpawnRequestsPerFrame);
                    Assert(state.report.duplicateIdentityFrames == 0, "Duplicate live entity identities occurred in " + state.report.duplicateIdentityFrames + " observed frames.");
                    Assert(HashAsset(state.folder + "/Map.asset") == state.mapHash, "Authored map file changed during Play Mode.");
                    Assert(HashAsset(state.folder + "/AlternateMap.asset") == state.alternateMapHash, "Alternate map file changed during Play Mode.");
                    Assert(HashAsset(state.folder + "/CapabilityMap.asset") == state.capabilityMapHash, "Capability map file changed during Play Mode.");
                }
            }
        };
    }

    private static void VerifyStateJson()
    {
        savedJson = renderer.RuntimeState.ToJson();
        var restored = new GridMapRuntimeState(sourceMap);
        restored.LoadJson(savedJson);
        Assert(restored.GetCell(18, 8) == null && sourceMap.GetCell(18, 8) == ground, "JSON failed to restore an independent cell override.");
        Assert(Mathf.Approximately(restored.GetEntity(DamagedId, Vector3.zero, 100f).Health, 75f), "JSON lost HP.");
        MapEntityState chest = restored.GetEntity(ChestId, Vector3.zero, 100f);
        Assert(chest.Opened && chest.Ignited, "JSON lost interaction or fire state.");
        Assert(restored.GetEntity(PermanentId, Vector3.zero, 100f).Destroyed, "JSON lost permanent death.");
        string before = restored.ToJson();
        bool rejected = false;
        try { restored.LoadJson(new GridMapRuntimeState(alternateMap).ToJson()); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "JSON for another map was accepted.");
        Assert(restored.ToJson() == before, "Rejected JSON partially mutated existing state.");
    }

    private static void Update()
    {
        if (state == null || finishing) return;
        try
        {
            if (!state.timedOut && (DateTime.UtcNow - ParseUtc(state.startedUtc)).TotalSeconds > TimeoutSeconds)
            {
                state.timedOut = true;
                AddResult("Streaming verification completes within its batch timeout", false, "Timed out in " + state.stage + ".");
                LeavePlayMode();
                return;
            }
            if (state.stage == "Leaving")
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                else if ((DateTime.UtcNow - ParseUtc(state.leavingUtc)).TotalSeconds > 10d)
                {
                    AddResult("Leave Play Mode for cleanup", false, "Play Mode exit timed out.");
                    WriteReportAndExit();
                }
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (state.stage == "Entering") StartRuntimeSteps();
        }
        catch (Exception exception)
        {
            AddResult("Streaming verification driver", false, exception.ToString());
            LeavePlayMode();
        }
    }

    private static IEnumerator DriveRuntimeFrames()
    {
        // A native coroutine resumes after behaviour Update, so per-frame counters are
        // observed after real work. WaitForEndOfFrame is deliberately avoided in batchmode.
        // Disabling a MonoBehaviour does not stop its coroutine while its GameObject is active.
        while (state != null && state.stage == "Running" && !finishing && Application.isPlaying)
        {
            yield return null;
            if (state == null || state.stage != "Running" || finishing || !Application.isPlaying) yield break;
            try
            {
                if (lastFrame == Time.frameCount) continue;
                lastFrame = Time.frameCount;
                ObserveFrame();
                RunStep();
            }
            catch (Exception exception)
            {
                AddResult("Streaming Play frame driver", false, exception.ToString());
                LeavePlayMode();
            }
        }
    }

    private static void RunStep()
    {
        if (steps == null) throw new InvalidOperationException("Unexpected domain reload interrupted the runtime driver.");
        if (stepIndex >= steps.Count) { LeavePlayMode(); return; }
        Step step = steps[stepIndex];
        try
        {
            if (!stepStarted)
            {
                stepStarted = true;
                stepStartFrame = Time.frameCount;
                stepStartTime = Time.time;
                stepStartUtc = DateTime.UtcNow;
                step.Begin?.Invoke();
                return;
            }
            bool expired = (DateTime.UtcNow - stepStartUtc).TotalSeconds > step.Timeout;
            bool elapsed = Time.frameCount - stepStartFrame >= step.MinimumFrames && Time.time - stepStartTime >= step.MinimumSeconds;
            if (!expired && (!elapsed || (step.Ready != null && !step.Ready()))) return;
            Assert(!expired, "Timed out waiting for runtime behavior. " + RuntimeDiagnostic());
            step.Verify?.Invoke();
            AddResult(step.Name, true, null);
        }
        catch (Exception exception)
        {
            AddResult(step.Name, false, exception.ToString() + "\n" + RuntimeDiagnostic());
        }
        stepIndex++;
        stepStarted = false;
    }

    private static void ObserveFrame()
    {
        Report report = state.report;
        report.observedFrames++;
        if (Time.fixedTime > lastFixedTime) report.observedFixedTimeChanges++;
        lastFixedTime = Time.fixedTime;
        int chunkWork = renderer.LastChunksLoaded + renderer.LastChunksUnloaded;
        report.maximumChunkWorkPerFrame = Math.Max(report.maximumChunkWorkPerFrame, chunkWork);
        report.maximumSpawnRequestsPerFrame = Math.Max(report.maximumSpawnRequestsPerFrame, streamer.LastSpawnRequestsStarted);
        if (chunkWork > 0) report.framesWithChunkWork++;
        if (streamer.LastSpawnRequestsStarted > 0) report.framesWithSpawnRequests++;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var actor in ActiveActors())
            if (!ids.Add(actor.EntityId)) { report.duplicateIdentityFrames++; break; }
        if (report.frames.Count < 1200)
            report.frames.Add(new FrameObservation
            {
                frame = Time.frameCount, stage = steps != null && stepIndex < steps.Count ? steps[stepIndex].Name : state.stage,
                gameTime = Time.time, fixedTime = Time.fixedTime, loadedChunks = renderer.LoadedChunkCount,
                chunksLoaded = renderer.LastChunksLoaded, chunksUnloaded = renderer.LastChunksUnloaded,
                activeActors = streamer.ActiveCount, pendingActors = streamer.PendingCount,
                spawnRequestsStarted = streamer.LastSpawnRequestsStarted,
                actorsSpawned = streamer.LastActorsSpawned, actorsRecycled = streamer.LastActorsRecycled
            });
    }

    private static string RuntimeDiagnostic()
    {
        return renderer == null ? "Renderer is missing." : "frame=" + Time.frameCount + ", time=" + Time.time +
            ", chunks=" + renderer.LoadedChunkCount + ", active=" + (streamer == null ? -1 : streamer.ActiveCount) +
            ", pending=" + (streamer == null ? -1 : streamer.PendingCount) + ", focus=" + target.position;
    }

    private static void MoveFocus(int x, int y) { target.position = renderer.CellToWorldCenter(x, y); }
    private static Tilemap GroundTilemap() { return renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid); }
    private static MapTileType RenderedSource(int x, int y)
    {
        TileBase tile = GroundTilemap().GetTile(new Vector3Int(x, y, 0));
        return tile is MapRuntimeTile runtime ? runtime.Source : tile as MapTileType;
    }
    private static MapEntityState EntityState(string id) { return renderer.RuntimeState.GetEntity(id, Vector3.zero, 100f); }
    private static string CellEntityId(int x) { return "cell:Objects:" + x + ":10"; }

    private static void AssertObjectCollider(int x, int y, bool expected)
    {
        Physics2D.SyncTransforms();
        var tilemap = renderer.GetTilemap(MapLayer.Objects, MapTileCollisionMode.Solid);
        var collider = tilemap == null ? null : tilemap.GetComponent<CompositeCollider2D>();
        Assert(collider != null && tilemap.GetComponent<TilemapCollider2D>().usedByComposite, "Object solid composite collider is missing.");
        bool found = false;
        foreach (Collider2D hit in Physics2D.OverlapPointAll(renderer.CellToWorldCenter(x, y)))
            if (hit == collider) { found = true; break; }
        Assert(found == expected, "Object collision at (" + x + "," + y + ") expected " + expected + ", actual " + found);
    }

    private static void AssertGroundCollider(int x, int y)
    {
        Vector3 point = renderer.CellToWorldCenter(x, y);
        bool found = false;
        var expected = GroundTilemap().GetComponent<CompositeCollider2D>();
        foreach (Collider2D collider in Physics2D.OverlapPointAll(point))
            if (collider == expected) { found = true; break; }
        Assert(found, "No generated ground collider at cell (" + x + "," + y + ") world " + point);
    }

    private static List<MapStreamedEntity> ActiveActors()
    {
        var actors = new List<MapStreamedEntity>();
        foreach (var actor in Object.FindObjectsOfType<MapStreamedEntity>())
            if (actor != null && actor.gameObject.activeInHierarchy && actor.State != null && actor.gameObject.scene.IsValid()) actors.Add(actor);
        return actors;
    }

    private static MapStreamedEntity TryFindActor(string id)
    {
        foreach (var actor in ActiveActors()) if (actor.EntityId == id) return actor;
        return null;
    }

    private static MapStreamedEntity FindActor(string id)
    {
        var actor = TryFindActor(id);
        Assert(actor != null, "Expected active entity " + id);
        return actor;
    }

    private static int CountOriginalInstancesStillAlive()
    {
        int count = 0;
        foreach (int id in originalInstances) if (EditorUtility.InstanceIDToObject(id) != null) count++;
        return count;
    }

    private static GameObject FindRoot(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) if (root.name == name) return root;
        throw new InvalidOperationException("Temporary root was not found: " + name);
    }

    private static string HashAsset(string path)
    {
        using (SHA256 sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)))));
    }

    private static void LeavePlayMode()
    {
        if (state.stage != "Leaving") state.leavingUtc = DateTime.UtcNow.ToString("O");
        state.stage = "Leaving";
        SaveState();
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else EditorApplication.delayCall += Finish;
    }

    private static void Finish()
    {
        if (state == null || finishing || EditorApplication.isPlayingOrWillChangePlaymode) return;
        finishing = true;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Guid token;
            Assert(state.folder.StartsWith(FolderPrefix, StringComparison.Ordinal) && Guid.TryParseExact(state.folder.Substring(FolderPrefix.Length), "N", out token),
                "Refusing cleanup outside this test's unique temporary directory.");
            string assetRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string cleanupPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", state.folder));
            Assert(cleanupPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase), "Cleanup path is outside Assets.");
            if (state.temporaryFolderCreated && AssetDatabase.IsValidFolder(state.folder))
                Assert(AssetDatabase.DeleteAsset(state.folder), "Could not remove temporary verification assets.");
            state.report.temporaryAssetsCleaned = !AssetDatabase.IsValidFolder(state.folder);
            AddResult("Exit Play Mode and remove only this batch's temporary assets", state.report.temporaryAssetsCleaned, null);
        }
        catch (Exception exception) { AddResult("Clean up verification assets", false, exception.ToString()); }
        WriteReportAndExit();
    }

    private static void AddResult(string name, bool passed, string error)
    {
        state.report.results.Add(new Result { name = name, passed = passed, error = error });
        if (passed) state.report.passed++; else state.report.failed++;
        SaveState();
        if (passed) Debug.Log("Map2D streaming verification passed: " + name);
        else Debug.LogError("Map2D streaming verification failed: " + name + "\n" + error);
    }

    private static void SaveState() { SessionState.SetString(SessionKey, JsonUtility.ToJson(state)); }

    private static void WriteReportAndExit()
    {
        finishing = true;
        EditorApplication.update -= Update;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.delayCall -= Resume;
        EditorApplication.delayCall -= Finish;
        state.report.completedUtc = DateTime.UtcNow.ToString("O");
        int exitCode = state.report.failed == 0 ? 0 : 1;
        try
        {
            string directory = Path.GetDirectoryName(state.reportPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(state.reportPath, JsonUtility.ToJson(state.report, true));
            Debug.Log("Map2D streaming verification: " + state.report.passed + " passed, " + state.report.failed + " failed. " + state.reportPath);
        }
        catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
        SessionState.EraseString(SessionKey);
        GridMapBatchExit.Request(exitCode, state.reportPath);
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i] == "-mapStreamingReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/streaming-verification.json"));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

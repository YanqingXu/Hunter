using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Copy into an isolated validation project's Editor folder. Run Unity with
/// -batchmode -nographics -executeMethod GridMapPlayVerification.BeginBatch
/// -mapPlayReport <json path>, without -quit. Ordinary editor sessions do nothing.
/// </summary>
[InitializeOnLoad]
public static class GridMapPlayVerification
{
    private const string SessionKey = "BigWorld.Map2D.PlayVerification.State";
    private const string FolderPrefix = "Assets/__Map2DPlayVerification_";
    private const double TimeoutSeconds = 90d;
    private const int FramesToWait = 8;
    private const float PhysicsObservationSeconds = 5f;
    private const float PlatformTop = 4f;
    private const float ProbeHalfHeight = 0.4f;

    [Serializable]
    private sealed class Result
    {
        public string name;
        public bool passed;
        public string error;
    }

    [Serializable]
    private sealed class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public string completedUtc;
        public int passed;
        public int failed;
        public bool temporaryAssetsCleaned;
        public List<Result> results = new List<Result>();
        public List<PhysicsObservation> physics = new List<PhysicsObservation>();
    }

    [Serializable]
    private sealed class PhysicsObservation
    {
        public string collisionDetection;
        public float gravityY;
        public float initialY;
        public float initialVelocityY;
        public float maximumY;
        public float finalY;
        public float finalVelocityY;
        public float expectedRestingY;
        public float elapsedGameTime;
        public float elapsedFixedTime;
        public int observedFrames;
        public int observedFixedTimeChanges;
        public bool passedThroughFromBelow;
    }

    [Serializable]
    private sealed class State
    {
        public string stage;
        public string startedUtc;
        public string leavingUtc;
        public string folder;
        public string scenePath;
        public string hostName;
        public string markerName;
        public string reportPath;
        public int framesObserved;
        public int lastFrame = -1;
        public int oldInstanceId;
        public bool timedOut;
        public bool temporaryFolderCreated;
        public float physicsStartedTime;
        public float physicsStartedFixedTime;
        public float physicsLastFixedTime;
        public Report report;
    }

    private static State state;
    private static bool finishing;

    static GridMapPlayVerification()
    {
        // The batch entry point is the sole source of this session state.
        if (Application.isBatchMode && !string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty)))
            EditorApplication.delayCall += Resume;
    }

    public static void BeginBatch()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("This verification runs only in a dedicated Unity batch process.");
        if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty)))
            throw new InvalidOperationException("A Map2D Play Mode verification is already active in this process.");

        string token = Guid.NewGuid().ToString("N");
        state = new State
        {
            stage = "Setup",
            startedUtc = DateTime.UtcNow.ToString("O"),
            folder = FolderPrefix + token,
            hostName = "Map2D Play Verification " + token,
            markerName = "Map2D Prefab Proof " + token,
            reportPath = GetReportPath(),
            report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion }
        };
        state.scenePath = state.folder + "/PlayVerification.unity";
        SaveState();
        AttachCallbacks();
        try
        {
            PrepareScene();
            AddResult("Saved temporary scene has a cleared preview and intact map data", true, null);
            state.stage = "Entering";
            SaveState();
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            AddResult("Prepare isolated Play Mode verification", false, exception.ToString());
            LeavePlayMode();
        }
    }

    private static void PrepareScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("The verification process must begin in Edit Mode.");
        Assert(!AssetDatabase.IsValidFolder(state.folder), "Temporary asset folder must not pre-exist.");
        string folderGuid = AssetDatabase.CreateFolder("Assets", state.folder.Substring("Assets/".Length));
        Assert(!string.IsNullOrEmpty(folderGuid), "Could not create the temporary asset folder.");
        state.temporaryFolderCreated = true;
        SaveState();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject prefabSource = new GameObject("Map2D Runtime Object");
        GameObject proof = new GameObject(state.markerName);
        proof.transform.SetParent(prefabSource.transform, false);
        GameObject prefab;
        try
        {
            prefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, state.folder + "/RuntimeObject.prefab");
            Assert(prefab != null, "Temporary prefab was not saved.");
        }
        finally { Object.DestroyImmediate(prefabSource); }

        var ground = ScriptableObject.CreateInstance<MapTileType>();
        ground.Id = "play-verification-ground";
        ground.DisplayName = "Verification Ground";
        ground.Layer = MapLayer.Terrain;
        ground.Collision = MapTileCollisionMode.Solid;
        AssetDatabase.CreateAsset(ground, state.folder + "/Ground.asset");

        var objectType = ScriptableObject.CreateInstance<MapTileType>();
        objectType.Id = "play-verification-object";
        objectType.DisplayName = "Verification Object";
        objectType.Layer = MapLayer.Objects;
        objectType.Collision = MapTileCollisionMode.None;
        objectType.Prefab = prefab;
        AssetDatabase.CreateAsset(objectType, state.folder + "/Object.asset");

        var map = ScriptableObject.CreateInstance<GridMapAsset>();
        map.Initialize(4, 3, 1.5f);
        map.Origin = new Vector2(-2f, 3f);
        map.SetCell(1, 0, ground, MapLayer.Terrain);
        map.SetCell(1, 1, objectType, MapLayer.Objects);
        AssetDatabase.CreateAsset(map, state.folder + "/Map.asset");

        GameObject host = new GameObject(state.hostName);
        host.transform.position = new Vector3(7f, -5f, 0f);
        host.transform.localScale = new Vector3(1.25f, 1.25f, 1f);
        var renderer = host.AddComponent<GridMapRenderer>();
        renderer.Map = map;
        renderer.ClearPreview();
        foreach (Tilemap tilemap in renderer.GetComponentsInChildren<Tilemap>(true))
            Assert(tilemap.GetUsedTilesCount() == 0, "ClearPreview must empty every generated tilemap before saving.");
        Assert(map.GetCell(1, 0, MapLayer.Terrain) == ground, "Preview clearing must preserve terrain data.");
        Assert(map.GetCell(1, 1, MapLayer.Objects) == objectType, "Preview clearing must preserve object data.");
        PrepareOneWayPlatform();
        Assert(EditorSceneManager.SaveScene(scene, state.scenePath), "Could not save the temporary scene.");
    }

    private static void PrepareOneWayPlatform()
    {
        var type = ScriptableObject.CreateInstance<MapTileType>();
        type.Id = "play-verification-one-way";
        type.DisplayName = "Verification One-Way Platform";
        type.Layer = MapLayer.Terrain;
        type.Collision = MapTileCollisionMode.OneWay;
        AssetDatabase.CreateAsset(type, state.folder + "/OneWay.asset");
        var map = ScriptableObject.CreateInstance<GridMapAsset>();
        map.Initialize(8, 10);
        for (int x = 0; x < map.Width; x++) map.SetCell(x, 3, type);
        AssetDatabase.CreateAsset(map, state.folder + "/OneWayMap.asset");
        GameObject host = new GameObject(state.hostName + " OneWay");
        host.transform.position = new Vector3(30f, 0f, 0f);
        host.AddComponent<GridMapRenderer>().Map = map;
    }

    private static void Resume()
    {
        if (!Application.isBatchMode || finishing) return;
        string saved = SessionState.GetString(SessionKey, string.Empty);
        if (string.IsNullOrEmpty(saved)) return;
        state = JsonUtility.FromJson<State>(saved);
        AttachCallbacks();
        if (state.stage == "Entering" && EditorApplication.isPlaying)
            BeginFrameWait("Initial");
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
        if (change == PlayModeStateChange.EnteredPlayMode && state.stage == "Entering")
            BeginFrameWait("Initial");
        else if (change == PlayModeStateChange.EnteredEditMode && state.stage == "Leaving")
            EditorApplication.delayCall += Finish;
    }

    private static void BeginFrameWait(string stage)
    {
        state.stage = stage;
        state.framesObserved = 0;
        state.lastFrame = Time.frameCount;
        SaveState();
    }

    private static void Update()
    {
        if (state == null || finishing) return;
        try
        {
            double elapsed = (DateTime.UtcNow - ParseUtc(state.startedUtc)).TotalSeconds;
            if (!state.timedOut && elapsed > TimeoutSeconds)
            {
                state.timedOut = true;
                AddResult("Play Mode verification completes within 90 seconds", false, "Timed out in stage " + state.stage + ".");
                LeavePlayMode();
                return;
            }
            if (state.stage == "Leaving")
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                else if ((DateTime.UtcNow - ParseUtc(state.leavingUtc)).TotalSeconds > 10d)
                {
                    AddResult("Exit Play Mode for temporary asset cleanup", false, "Unity did not leave Play Mode within 10 seconds.");
                    WriteReportAndExit();
                }
                return;
            }
            if (!EditorApplication.isPlaying) return;
            if (state.stage == "Entering") BeginFrameWait("Initial");
            if (state.stage == "Physics")
            {
                ObserveOneWayPhysics();
                return;
            }
            if (state.stage != "Initial" && state.stage != "Rebuilding") return;
            if (state.lastFrame == Time.frameCount) return;
            state.lastFrame = Time.frameCount;
            state.framesObserved++;
            SaveState();
            if (state.framesObserved < FramesToWait) return;

            if (state.stage == "Initial") VerifyInitialRuntime();
            else VerifyAfterRebuild();
        }
        catch (Exception exception)
        {
            AddResult("Play Mode verification driver", false, exception.ToString());
            LeavePlayMode();
        }
    }

    private static void VerifyInitialRuntime()
    {
        Check("Entering Play Mode rebuilds all six tilemaps after saved ClearPreview", () =>
        {
            GridMapRenderer renderer = FindRenderer();
            Assert(renderer.GetComponentsInChildren<Tilemap>(true).Length == 6, "Expected exactly six generated Tilemaps.");
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            Assert(solid != null, "Solid terrain Tilemap is missing.");
            Assert(solid.GetTile(new Vector3Int(1, 0, 0)) == renderer.Map.GetCell(1, 0), "Saved ground cell was not rebuilt on entering Play Mode.");
            var collider = solid.GetComponent<TilemapCollider2D>();
            Assert(collider != null && collider.enabled, "Ground collider is missing or disabled.");
            Physics2D.SyncTransforms();
            Assert(Physics2D.OverlapPoint(renderer.CellToWorldCenter(1, 0)) == collider, "Ground collider does not occupy its expected world-space cell.");
        });
        Check("Object Tilemap instantiates its prefab at the cell world center", () =>
        {
            GridMapRenderer renderer = FindRenderer();
            GameObject instance = GetObjectInstance(renderer);
            Assert(instance != null, "Tilemap.GetInstantiatedObject returned null after eight runtime frames.");
            Assert(Vector3.Distance(instance.transform.position, renderer.CellToWorldCenter(1, 1)) < 0.001f,
                "Prefab position differs from CellToWorldCenter. Actual: " + instance.transform.position + ", expected: " + renderer.CellToWorldCenter(1, 1));
            Assert(CountProofMarkers() == 1, "Expected exactly one instantiated proof object.");
        });

        GridMapRenderer target = FindRenderer();
        GameObject previous = GetObjectInstance(target);
        state.oldInstanceId = previous != null ? previous.GetInstanceID() : 0;
        target.Rebuild();
        BeginFrameWait("Rebuilding");
    }

    private static void VerifyAfterRebuild()
    {
        Check("Runtime rebuild replaces the cell prefab without retaining duplicate instances", () =>
        {
            GridMapRenderer renderer = FindRenderer();
            GameObject instance = GetObjectInstance(renderer);
            Assert(instance != null, "Runtime rebuild lost the prefab instance.");
            Assert(Vector3.Distance(instance.transform.position, renderer.CellToWorldCenter(1, 1)) < 0.001f,
                "Rebuilt prefab is not centered in its cell.");
            Assert(CountProofMarkers() == 1, "Runtime rebuild left duplicate or missing prefab instances.");
            if (state.oldInstanceId != 0 && state.oldInstanceId != instance.GetInstanceID())
                Assert(EditorUtility.InstanceIDToObject(state.oldInstanceId) == null, "The replaced instance still exists after eight runtime frames.");
            Assert(renderer.GetComponentsInChildren<Tilemap>(true).Length == 6, "Runtime rebuild duplicated generated Tilemaps.");
        });
        BeginOneWayPhysics();
    }

    private static void BeginOneWayPhysics()
    {
        Assert(Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
            "One-way verification requires the project's normal automatic FixedUpdate physics mode.");
        Assert(Mathf.Abs(Physics2D.gravity.x) < 0.001f && Mathf.Abs(Physics2D.gravity.y + 9.81f) < 0.001f,
            "The controlled jump comparison requires gravity (0, -9.81). Actual: " + Physics2D.gravity);
        CreatePhysicsProbe(CollisionDetectionMode2D.Discrete, 33.5f);
        CreatePhysicsProbe(CollisionDetectionMode2D.Continuous, 36.5f);
        state.physicsStartedTime = Time.time;
        state.physicsStartedFixedTime = Time.fixedTime;
        state.physicsLastFixedTime = Time.fixedTime;
        state.lastFrame = Time.frameCount;
        state.stage = "Physics";
        Physics2D.SyncTransforms();
        SaveState();
    }

    private static void CreatePhysicsProbe(CollisionDetectionMode2D mode, float x)
    {
        GameObject probe = new GameObject(state.hostName + " Probe " + mode);
        probe.transform.position = new Vector3(x, 1.5f, 0f);
        var collider = probe.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one * 0.8f;
        var body = probe.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 1f;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = mode;
        body.interpolation = RigidbodyInterpolation2D.None;
        body.velocity = new Vector2(0f, 10f);
        state.report.physics.Add(new PhysicsObservation
        {
            collisionDetection = mode.ToString(),
            gravityY = Physics2D.gravity.y,
            initialY = 1.5f,
            initialVelocityY = 10f,
            maximumY = 1.5f,
            finalY = 1.5f,
            expectedRestingY = PlatformTop + ProbeHalfHeight
        });
    }

    private static void ObserveOneWayPhysics()
    {
        if (state.lastFrame == Time.frameCount) return;
        state.lastFrame = Time.frameCount;
        bool fixedTimeAdvanced = Time.fixedTime > state.physicsLastFixedTime;
        state.physicsLastFixedTime = Time.fixedTime;
        float gameElapsed = Time.time - state.physicsStartedTime;
        float fixedElapsed = Time.fixedTime - state.physicsStartedFixedTime;
        foreach (PhysicsObservation observation in state.report.physics)
        {
            Rigidbody2D body = FindPhysicsProbe(observation.collisionDetection);
            observation.maximumY = Mathf.Max(observation.maximumY, body.position.y);
            observation.finalY = body.position.y;
            observation.finalVelocityY = body.velocity.y;
            observation.elapsedGameTime = gameElapsed;
            observation.elapsedFixedTime = fixedElapsed;
            observation.observedFrames++;
            if (fixedTimeAdvanced) observation.observedFixedTimeChanges++;
            if (body.position.y - ProbeHalfHeight > PlatformTop + 0.01f)
                observation.passedThroughFromBelow = true;
        }
        SaveState();
        // No PhysicsScene2D.Simulate calls: both clocks must progress naturally.
        if (gameElapsed < PhysicsObservationSeconds || fixedElapsed < PhysicsObservationSeconds) return;

        foreach (PhysicsObservation observation in state.report.physics)
        {
            PhysicsObservation sample = observation;
            Check("One-way platform permits an upward jump then supports landing (" + sample.collisionDetection + ")", () =>
            {
                string diagnostic = JsonUtility.ToJson(sample);
                Assert(sample.observedFixedTimeChanges > 0, "No real fixed-step progression was observed. " + diagnostic);
                Assert(sample.passedThroughFromBelow,
                    "Body never fully crossed the platform from below. " + diagnostic);
                Assert(Mathf.Abs(sample.finalY - sample.expectedRestingY) <= 0.08f,
                    "Body did not come to rest on the platform's upper surface. " + diagnostic);
                Assert(Mathf.Abs(sample.finalVelocityY) <= 0.15f,
                    "Body still has substantial vertical speed after five seconds. " + diagnostic);
            });
            Debug.Log("Map2D one-way physics observation: " + JsonUtility.ToJson(sample));
        }
        LeavePlayMode();
    }

    private static Rigidbody2D FindPhysicsProbe(string mode)
    {
        string name = state.hostName + " Probe " + mode;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name)
            {
                var body = root.GetComponent<Rigidbody2D>();
                Assert(body != null, "Physics probe lost its Rigidbody2D: " + mode);
                return body;
            }
        throw new InvalidOperationException("One-way physics probe was not found: " + mode);
    }

    private static GridMapRenderer FindRenderer()
    {
        Scene scene = SceneManager.GetActiveScene();
        Assert(scene.path == state.scenePath, "Verification must run only in its saved temporary scene.");
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == state.hostName)
            {
                var renderer = root.GetComponent<GridMapRenderer>();
                Assert(renderer != null && renderer.Map != null, "Saved renderer or map reference did not survive the Play Mode transition.");
                return renderer;
            }
        throw new InvalidOperationException("Temporary verification renderer was not found.");
    }

    private static GameObject GetObjectInstance(GridMapRenderer renderer)
    {
        Tilemap objects = renderer.GetTilemap(MapLayer.Objects, MapTileCollisionMode.None);
        Assert(objects != null, "Object Tilemap is missing.");
        return objects.GetInstantiatedObject(new Vector3Int(1, 1, 0));
    }

    private static int CountProofMarkers()
    {
        int count = 0;
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == state.markerName) count++;
        return count;
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
            // Open an unsaved empty scene so the temporary scene can be deleted safely.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Guid folderId;
            Assert(state.folder.StartsWith(FolderPrefix, StringComparison.Ordinal) &&
                   Guid.TryParseExact(state.folder.Substring(FolderPrefix.Length), "N", out folderId),
                "Refusing cleanup outside the verification's unique temporary folder.");
            string assetRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string cleanupPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", state.folder));
            Assert(cleanupPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase), "Cleanup path is outside Assets.");
            if (state.temporaryFolderCreated && AssetDatabase.IsValidFolder(state.folder))
                Assert(AssetDatabase.DeleteAsset(state.folder), "Could not remove temporary verification assets.");
            state.report.temporaryAssetsCleaned = !AssetDatabase.IsValidFolder(state.folder);
            AddResult("Exited Play Mode and removed only temporary verification assets", state.report.temporaryAssetsCleaned, null);
        }
        catch (Exception exception)
        {
            AddResult("Clean up temporary verification assets", false, exception.ToString());
        }
        WriteReportAndExit();
    }

    private static void Check(string name, Action assertion)
    {
        try { assertion(); AddResult(name, true, null); }
        catch (Exception exception) { AddResult(name, false, exception.ToString()); }
    }

    private static void AddResult(string name, bool passed, string error)
    {
        state.report.results.Add(new Result { name = name, passed = passed, error = error });
        if (passed) state.report.passed++; else state.report.failed++;
        SaveState();
        if (passed) Debug.Log("Map2D Play verification passed: " + name);
        else Debug.LogError("Map2D Play verification failed: " + name + "\n" + error);
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
            Debug.Log("Map2D Play verification: " + state.report.passed + " passed, " + state.report.failed + " failed. " + state.reportPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exitCode = 1;
        }
        SessionState.EraseString(SessionKey);
        EditorApplication.Exit(exitCode);
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
            if (arguments[i] == "-mapPlayReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/play-verification.json"));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

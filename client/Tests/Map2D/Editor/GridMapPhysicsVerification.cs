using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Run from an isolated validation project's Editor folder with
/// -batchmode -executeMethod GridMapPhysicsVerification.RunBatch -mapPhysicsReport <json path>.
/// Uses a separate PhysicsScene2D and never saves or replaces an existing scene.
/// </summary>
public static class GridMapPhysicsVerification
{
    [Serializable]
    private sealed class Result
    {
        public string name;
        public bool passed;
        public string error;
        public float maximumHeight;
        public float finalHeight;
        public float finalVerticalSpeed;
        public float expectedRestingHeight;
        public bool crossedPlatformUpward;
        public string collisionDetection;
        public string platformKind;
        public bool explicitStaticBody;
        public bool colliderUsedByEffector;
        public bool effectorActive;
        public float effectorRotationalOffset;
        public bool effectorUsesColliderMask;
        public int effectorColliderMask;
    }

    [Serializable]
    private sealed class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public int passed;
        public int failed;
        public List<Result> results = new List<Result>();
    }

    public static void RunBatch()
    {
        Report report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        Vector2 previousGravity = Physics2D.gravity;
        SimulationMode2D previousSimulationMode = Physics2D.simulationMode;
        try
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Physics2D.simulationMode = SimulationMode2D.Script;
            RunCase(report, "Solid tiles stop a falling rigidbody", MapTileCollisionMode.Solid);
            RunCase(report, "One-way tiles allow upward passage then support landing", MapTileCollisionMode.OneWay);
            RunCase(report, "One-way tiles with discrete collision detection", MapTileCollisionMode.OneWay, CollisionDetectionMode2D.Discrete);
            RunCase(report, "One-way tiles with explicit static Rigidbody and continuous detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Continuous, true);
            RunCase(report, "One-way tiles with explicit static Rigidbody and discrete detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Discrete, true);
            RunCase(report, "Plain BoxCollider one-way platform with continuous detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Continuous, false, true);
            RunCase(report, "Plain BoxCollider one-way platform with discrete detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Discrete, false, true);
            RunCase(report, "Composite Tilemap one-way platform with continuous detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Continuous, true, false, true);
            RunCase(report, "Composite Tilemap one-way platform with discrete detection", MapTileCollisionMode.OneWay,
                CollisionDetectionMode2D.Discrete, true, false, true);
        }
        finally
        {
            Physics2D.gravity = previousGravity;
            Physics2D.simulationMode = previousSimulationMode;
            string reportPath = GetReportPath();
            string directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D physics verification: " + report.passed + " passed, " + report.failed + " failed. " + reportPath);
        }
        if (report.failed != 0)
            throw new InvalidOperationException("Map2D physics verification failed. See the JSON report.");
    }

    private static void RunCase(Report report, string name, MapTileCollisionMode mode,
        CollisionDetectionMode2D collisionDetection = CollisionDetectionMode2D.Continuous,
        bool explicitStaticBody = false, bool plainBoxPlatform = false, bool compositePlatform = false)
    {
        Result result = new Result
        {
            name = name,
            collisionDetection = collisionDetection.ToString(),
            platformKind = plainBoxPlatform ? "BoxCollider2D" : compositePlatform ? "CompositeCollider2D" : "TilemapCollider2D",
            explicitStaticBody = explicitStaticBody
        };
        report.results.Add(result);
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene scene = default(Scene);
        GridMapAsset map = null;
        MapTileType tile = null;
        try
        {
            scene = EditorSceneManager.NewPreviewScene();
            PhysicsScene2D physicsScene = scene.GetPhysicsScene2D();
            Require(physicsScene.IsValid(), "The isolated PhysicsScene2D must be valid.");
            Require(!physicsScene.Equals(Physics2D.defaultPhysicsScene), "Verification must not simulate the user's default physics scene.");

            map = ScriptableObject.CreateInstance<GridMapAsset>();
            map.Initialize(8, 10);
            tile = ScriptableObject.CreateInstance<MapTileType>();
            tile.Collision = mode;
            tile.Walkable = false;
            const int platformRow = 3;
            const float platformTop = platformRow + 1f;
            for (int x = 0; x < map.Width; x++) map.SetCell(x, platformRow, tile);

            GameObject mapObject = new GameObject("Physics Test Map");
            SceneManager.MoveGameObjectToScene(mapObject, scene);
            GridMapRenderer renderer = mapObject.AddComponent<GridMapRenderer>();
            renderer.Map = map;
            Tilemap tilemap = renderer.GetTilemap(MapLayer.Terrain, mode);
            Require(tilemap != null, "The collision layer must exist.");
            TilemapCollider2D mapCollider = tilemap.GetComponent<TilemapCollider2D>();
            Require(mapCollider != null && mapCollider.enabled, "The collision layer must contain an enabled TilemapCollider2D.");
            mapCollider.ProcessTilemapChanges();
            Require(mapCollider.shapeCount > 0, "TilemapCollider2D must contain actual collision shapes.");

            Collider2D platformCollider = mapCollider;
            PlatformEffector2D platformEffector = tilemap.GetComponent<PlatformEffector2D>();
            if (compositePlatform)
            {
                Rigidbody2D platformBody = tilemap.gameObject.AddComponent<Rigidbody2D>();
                platformBody.bodyType = RigidbodyType2D.Static;
                CompositeCollider2D composite = tilemap.gameObject.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Manual;
                mapCollider.usedByEffector = false;
                mapCollider.usedByComposite = true;
                mapCollider.ProcessTilemapChanges();
                composite.GenerateGeometry();
                composite.usedByEffector = true;
                platformCollider = composite;
                Require(composite.shapeCount > 0, "The comparison composite must contain collision shapes.");
            }
            if (plainBoxPlatform)
            {
                mapCollider.enabled = false;
                GameObject boxPlatform = new GameObject("Plain BoxCollider Comparison Platform");
                SceneManager.MoveGameObjectToScene(boxPlatform, scene);
                boxPlatform.transform.position = new Vector3(map.Width * 0.5f, platformRow + 0.5f, 0f);
                BoxCollider2D boxCollider = boxPlatform.AddComponent<BoxCollider2D>();
                boxCollider.size = new Vector2(map.Width, 1f);
                platformEffector = boxPlatform.AddComponent<PlatformEffector2D>();
                platformEffector.useOneWay = true;
                platformEffector.useOneWayGrouping = true;
                platformEffector.surfaceArc = 180f;
                boxCollider.usedByEffector = true;
                platformCollider = boxCollider;
            }
            if (explicitStaticBody)
            {
                Rigidbody2D platformBody = platformCollider.GetComponent<Rigidbody2D>();
                if (platformBody == null) platformBody = platformCollider.gameObject.AddComponent<Rigidbody2D>();
                platformBody.bodyType = RigidbodyType2D.Static;
            }
            result.colliderUsedByEffector = platformCollider.usedByEffector;
            result.effectorActive = platformEffector != null && platformEffector.isActiveAndEnabled;
            result.effectorRotationalOffset = platformEffector == null ? 0f : platformEffector.rotationalOffset;
            result.effectorUsesColliderMask = platformEffector != null && platformEffector.useColliderMask;
            result.effectorColliderMask = platformEffector == null ? 0 : platformEffector.colliderMask;

            GameObject bodyObject = new GameObject("Physics Test Body");
            SceneManager.MoveGameObjectToScene(bodyObject, scene);
            bodyObject.transform.position = new Vector3(3.5f, mode == MapTileCollisionMode.Solid ? 7f : 1.5f, 0f);
            Rigidbody2D body = bodyObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 1f;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = collisionDetection;
            BoxCollider2D bodyCollider = bodyObject.AddComponent<BoxCollider2D>();
            bodyCollider.size = new Vector2(0.8f, 0.8f);
            if (mode == MapTileCollisionMode.OneWay) body.velocity = new Vector2(0f, 10f);
            Physics2D.SyncTransforms();

            result.maximumHeight = body.position.y;
            const float timeStep = 1f / 60f;
            for (int step = 0; step < 300; step++)
            {
                Require(physicsScene.Simulate(timeStep), "The isolated physics simulation refused to advance.");
                float height = body.position.y;
                result.maximumHeight = Mathf.Max(result.maximumHeight, height);
                if (body.velocity.y > 0f && height - bodyCollider.size.y * 0.5f > platformTop + 0.01f)
                    result.crossedPlatformUpward = true;
            }

            result.expectedRestingHeight = platformTop + bodyCollider.size.y * 0.5f;
            result.finalHeight = body.position.y;
            result.finalVerticalSpeed = body.velocity.y;
            if (mode == MapTileCollisionMode.OneWay)
            {
                Require(result.crossedPlatformUpward,
                    "The body never fully crossed the platform upward; maximum center height was " + result.maximumHeight + ".");
                Require(result.maximumHeight > platformTop + 0.8f,
                    "The upward passage must be clear of the platform, not merely contact penetration.");
            }
            Require(Mathf.Abs(result.finalHeight - result.expectedRestingHeight) <= 0.08f,
                "The body did not settle on top of the map: expected center y=" + result.expectedRestingHeight + ", actual=" + result.finalHeight + ".");
            Require(Mathf.Abs(result.finalVerticalSpeed) <= 0.15f,
                "The body's vertical velocity did not settle: " + result.finalVerticalSpeed + ".");
            result.passed = true;
            report.passed++;
        }
        catch (Exception exception)
        {
            result.error = exception.ToString();
            report.failed++;
            Debug.LogError(name + "\n" + exception);
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            if (map != null) Object.DestroyImmediate(map);
            if (tile != null) Object.DestroyImmediate(tile);
        }
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapPhysicsReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/physics-verification.json"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

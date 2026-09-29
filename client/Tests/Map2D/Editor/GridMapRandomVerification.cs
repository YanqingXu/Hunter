using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Copy into an isolated validation project's Assets/.../Editor folder. Run Unity with
/// -batchmode -quit -executeMethod GridMapRandomVerification.RunBatch
/// -mapRandomReport <absolute JSON path>. No NUnit package or graphics device is required.
/// All data is temporary; no existing assets or user scenes are modified.
/// </summary>
public static class GridMapRandomVerification
{
    [Serializable]
    private sealed class CaseResult
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
        public int passed;
        public int failed;
        public List<CaseResult> results = new List<CaseResult>();
    }

    private sealed class Types
    {
        public MapTileType Ground;
        public MapTileType Barrel;
        public MapTileType Chest;
        public MapTileType Spawn;
    }

    private sealed class Snapshot
    {
        public readonly int Width;
        public readonly int Height;
        public readonly MapTileType[] Terrain;
        public readonly MapTileType[] Objects;

        public Snapshot(GridMapAsset map)
        {
            Width = map.Width;
            Height = map.Height;
            Terrain = new MapTileType[Width * Height];
            Objects = new MapTileType[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    Terrain[x + y * Width] = map.GetCell(x, y);
                    Objects[x + y * Width] = map.GetCell(x, y, MapLayer.Objects);
                }
        }

        public void AssertMatches(GridMapAsset map, string message)
        {
            Equal(Width, map.Width, message + " width");
            Equal(Height, map.Height, message + " height");
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    Same(Terrain[x + y * Width], map.GetCell(x, y), message + " terrain at " + x + "," + y);
                    Same(Objects[x + y * Width], map.GetCell(x, y, MapLayer.Objects), message + " objects at " + x + "," + y);
                }
        }

        public void AssertOutsideUnchanged(GridMapAsset map, RectInt region)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (region.Contains(new Vector2Int(x, y))) continue;
                    Same(Terrain[x + y * Width], map.GetCell(x, y), "Terrain outside the selected region must not change");
                    Same(Objects[x + y * Width], map.GetCell(x, y, MapLayer.Objects), "Objects outside the selected region must not change");
                }
        }

        public void AssertTerrainUnchanged(GridMapAsset map)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Same(Terrain[x + y * Width], map.GetCell(x, y), "Object-only generation must preserve every terrain cell");
        }

        public int ChangedCells(GridMapAsset map)
        {
            int changed = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (Terrain[x + y * Width] != map.GetCell(x, y)) changed++;
                    if (Objects[x + y * Width] != map.GetCell(x, y, MapLayer.Objects)) changed++;
                }
            return changed;
        }
    }

    private static readonly List<Object> TemporaryObjects = new List<Object>();
    private static Report report;

    public static void RunBatch()
    {
        report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        try
        {
            Check("Requested object quantities and zero are exact", VerifyExactCounts);
            Check("Unspecified random quantities reserve capacity for requested quantities", VerifyRandomCountReservation);
            Check("Terrain generation preserves both layers outside the region", VerifyRegionIsolation);
            Check("Every generated object has support and occupies a distinct empty cell", VerifyPlacement);
            Check("Equal seeds reproduce complete maps without consuming Unity Random", VerifyDeterminism);
            Check("Object-only mode preserves terrain and supports the lower external row", VerifyObjectsOnly);
            Check("Invalid regions, counts and type selections fail without modifying data", VerifyInvalidRequests);
            Check("Insufficient capacity fails before clearing existing objects", VerifyInsufficientCapacity);
            Check("Stale plans reject source, geometry and supporting-row changes atomically", VerifyStalePlans);
            Check("Unrelated edits outside the region remain valid and preserved", VerifyUnrelatedOutsideEdit);
            Check("The real generation window validates input and supports complete Undo/Redo", VerifyUndoRedo);
        }
        finally
        {
            foreach (Object temporary in TemporaryObjects)
            {
                if (temporary == null) continue;
                Undo.ClearUndo(temporary);
                Object.DestroyImmediate(temporary);
            }
            TemporaryObjects.Clear();
            Undo.IncrementCurrentGroup();
            string path = GetReportPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D random verification: " + report.passed + " passed, " + report.failed + " failed. " + path);
        }
        if (report.failed != 0)
            throw new InvalidOperationException("Map2D random verification failed. See the JSON report.");
    }

    private static void VerifyExactCounts()
    {
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 12, 4, 0);
        RectInt region = new RectInt(0, 1, 12, 3);
        GridMapRandomOptions options = Options(types, region, false);
        options.BarrelCount = 4;
        options.ChestCount = 3;
        GridMapRandomPlan plan = CreatePlan(map, options);
        Equal(4, plan.BarrelCount, "Plan reports the requested barrel count");
        Equal(3, plan.ChestCount, "Plan reports the requested chest count");
        plan.Apply(map);
        Equal(4, Count(map, region, types.Barrel), "Applied barrel quantity is exact");
        Equal(3, Count(map, region, types.Chest), "Applied chest quantity is exact");

        options.BarrelCount = 0;
        options.ChestCount = 0;
        plan = CreatePlan(map, options);
        Equal(0, plan.BarrelCount, "Explicit zero is not treated as unspecified");
        Equal(0, plan.ChestCount, "Explicit chest zero is not treated as unspecified");
        plan.Apply(map);
        Equal(0, CountObjects(map, region), "Explicit zero clears old regional objects without placing replacements");
    }

    private static void VerifyRandomCountReservation()
    {
        Types types = CreateTypes();
        RectInt region = new RectInt(0, 1, 12, 2);
        for (int seed = 0; seed < 32; seed++)
        {
            GridMapAsset map = FlatMap(types, 12, 3, 0);
            GridMapRandomOptions options = Options(types, region, false);
            options.Seed = seed;
            options.BarrelCount = 12;
            options.ChestCount = null;
            GridMapRandomPlan plan = CreatePlan(map, options);
            Equal(12, plan.BarrelCount, "An unspecified chest count cannot crowd out requested barrels");
            Equal(0, plan.ChestCount, "No capacity remains for random chests");
            plan.Apply(map);
            Equal(12, Count(map, region, types.Barrel), "All requested barrels fit");

            options.BarrelCount = null;
            options.ChestCount = 12;
            plan = CreatePlan(map, options);
            Equal(0, plan.BarrelCount, "No capacity remains for random barrels");
            Equal(12, plan.ChestCount, "Unspecified barrels cannot crowd out requested chests");
            plan.Apply(map);
            Equal(12, Count(map, region, types.Chest), "All requested chests fit");

            options.BarrelCount = null;
            options.ChestCount = null;
            plan = CreatePlan(map, options);
            True(plan.BarrelCount >= 0 && plan.ChestCount >= 0, "Random counts are nonnegative");
            True(plan.BarrelCount + plan.ChestCount + plan.SpawnCount <= 12, "Random counts respect available capacity");
            plan.Apply(map);
            Equal(plan.BarrelCount, Count(map, region, types.Barrel), "Random barrel quantity matches the plan");
            Equal(plan.ChestCount, Count(map, region, types.Chest), "Random chest quantity matches the plan");
        }
    }

    private static void VerifyRegionIsolation()
    {
        Types types = CreateTypes();
        GridMapAsset map = Map(32, 18);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if ((x + y) % 5 == 0) map.SetCell(x, y, types.Ground);
                if ((x + 2 * y) % 7 == 0) map.SetCell(x, y, types.Chest, MapLayer.Objects);
            }
        RectInt region = new RectInt(3, 2, 24, 12);
        GridMapRandomOptions options = Options(types, region, true);
        options.BarrelCount = 3;
        options.ChestCount = 4;
        options.PlaceSpawn = true;
        Snapshot before = new Snapshot(map);
        GridMapRandomPlan plan = CreatePlan(map, options);
        before.AssertMatches(map, "Planning alone is read-only");
        int changed = plan.Apply(map);
        before.AssertOutsideUnchanged(map, region);
        Equal(before.ChangedCells(map), changed, "Apply reports actual changed cells across both layers");
        Equal(3, Count(map, region, types.Barrel), "Old regional objects are replaced with exact barrel count");
        Equal(4, Count(map, region, types.Chest), "Old regional objects are replaced with exact chest count");
        Equal(1, Count(map, region, types.Spawn), "Optional spawn is placed once inside the region");
    }

    private static void VerifyPlacement()
    {
        Types types = CreateTypes();
        for (int seed = 100; seed < 116; seed++)
        {
            GridMapAsset map = Map(40, 22);
            RectInt region = new RectInt(2, 3, 34, 16);
            GridMapRandomOptions options = Options(types, region, true);
            options.Seed = seed;
            options.BarrelCount = 5;
            options.ChestCount = 5;
            options.PlaceSpawn = true;
            GridMapRandomPlan plan = CreatePlan(map, options);
            plan.Apply(map);
            Equal(11, CountObjects(map, region), "All object kinds occupy distinct cells");
            for (int y = region.yMin; y < region.yMax; y++)
                for (int x = region.xMin; x < region.xMax; x++)
                {
                    MapTileType obj = map.GetCell(x, y, MapLayer.Objects);
                    if (obj == null) continue;
                    Same(null, map.GetCell(x, y), "Objects do not intersect terrain cells");
                    True(y > 0 && map.GetCell(x, y - 1) != null && map.GetCell(x, y - 1).BlocksMovement,
                        "Every generated object has a supporting solid or one-way terrain cell beneath it");
                    True(obj == types.Barrel || obj == types.Chest || obj == types.Spawn, "Only configured object types are placed");
                }
        }
    }

    private static void VerifyDeterminism()
    {
        Types types = CreateTypes();
        GridMapAsset first = Map(30, 18);
        GridMapAsset second = Map(30, 18);
        GridMapRandomOptions options = Options(types, new RectInt(2, 2, 24, 13), true);
        options.Seed = int.MinValue;
        options.BarrelCount = null;
        options.ChestCount = null;
        options.PlaceSpawn = true;
        UnityEngine.Random.State original = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(73152);
            UnityEngine.Random.State before = UnityEngine.Random.state;
            float expectedNext = UnityEngine.Random.value;
            UnityEngine.Random.state = before;
            GridMapRandomPlan firstPlan = CreatePlan(first, options);
            firstPlan.Apply(first);
            GridMapRandomPlan secondPlan = CreatePlan(second, options);
            secondPlan.Apply(second);
            Equal(expectedNext, UnityEngine.Random.value, "Creating and applying plans does not consume UnityEngine.Random");
            new Snapshot(first).AssertMatches(second, "The same seed and options reproduce the complete map");
            Equal(firstPlan.BarrelCount, secondPlan.BarrelCount, "Random barrel counts are reproducible");
            Equal(firstPlan.ChestCount, secondPlan.ChestCount, "Random chest counts are reproducible");
        }
        finally { UnityEngine.Random.state = original; }
    }

    private static void VerifyObjectsOnly()
    {
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 16, 8, 1);
        map.SetCell(6, 5, types.Ground);
        map.SetCell(4, 2, types.Spawn, MapLayer.Objects);
        RectInt region = new RectInt(2, 2, 10, 4);
        GridMapRandomOptions options = Options(types, region, false);
        options.BarrelCount = 5;
        options.ChestCount = 4;
        options.PlaceSpawn = true;
        Snapshot before = new Snapshot(map);
        GridMapRandomPlan plan = CreatePlan(map, options);
        plan.Apply(map);
        before.AssertTerrainUnchanged(map);
        before.AssertOutsideUnchanged(map, region);
        Equal(5, Count(map, region, types.Barrel), "Object-only mode respects barrel count");
        Equal(4, Count(map, region, types.Chest), "Object-only mode respects chest count");
        Equal(1, Count(map, region, types.Spawn), "Object-only mode reserves a distinct spawn cell");
        for (int x = region.xMin; x < region.xMax; x++)
            True(map.GetCell(x, 2, MapLayer.Objects) != null, "Terrain just below the region can support regional objects");
    }

    private static void VerifyInvalidRequests()
    {
        int? parsed;
        string parseError;
        foreach (string text in new[] { null, "", "   " })
        {
            True(GridMapRandomWindow.TryParseCount(text, "数量", out parsed, out parseError), "Blank quantity text is accepted");
            True(!parsed.HasValue, "Blank quantity means random rather than zero");
        }
        True(GridMapRandomWindow.TryParseCount("0", "数量", out parsed, out parseError) && parsed == 0,
            "Literal zero remains an explicit zero");
        foreach (string text in new[] { "-1", "1.5", "2147483648", "abc" })
            True(!GridMapRandomWindow.TryParseCount(text, "数量", out parsed, out parseError), "Invalid quantity text is rejected");
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 12, 6, 0);
        RectInt valid = new RectInt(0, 1, 12, 4);
        RectInt[] invalidRegions =
        {
            new RectInt(-1, 1, 5, 3), new RectInt(0, -1, 5, 3), new RectInt(0, 1, 0, 3),
            new RectInt(0, 1, 5, 0), new RectInt(10, 1, 5, 3), new RectInt(0, 5, 5, 2),
            new RectInt(1, 1, int.MaxValue, 2)
        };
        foreach (RectInt region in invalidRegions) AssertRejected(map, Options(types, region, false), "Invalid region");
        GridMapRandomOptions options = Options(types, valid, false);
        options.BarrelCount = -1;
        AssertRejected(map, options, "Negative barrel count");
        options = Options(types, valid, false);
        options.ChestCount = -1;
        AssertRejected(map, options, "Negative chest count");
        options = Options(types, valid, false);
        options.BarrelCount = int.MaxValue;
        options.ChestCount = int.MaxValue;
        AssertRejected(map, options, "Count sum cannot overflow capacity validation");
        options = Options(types, new RectInt(0, 1, 12, 1), true);
        AssertRejected(map, options, "Terrain generation requires height for ground and empty space");
        options = Options(types, valid, true);
        options.Ground = types.Barrel;
        AssertRejected(map, options, "Ground must be a terrain type");
        options = Options(types, valid, false);
        options.Barrel = types.Ground;
        options.BarrelCount = 1;
        AssertRejected(map, options, "Barrel must be an object type");
        options = Options(types, valid, false);
        options.Chest = types.Ground;
        options.ChestCount = 1;
        AssertRejected(map, options, "Chest must be an object type");
        options = Options(types, valid, false);
        options.Spawn = types.Ground;
        options.PlaceSpawn = true;
        AssertRejected(map, options, "Spawn must be an object type");
        options = Options(types, valid, false);
        options.PlaceSpawn = true;
        options.Spawn = null;
        AssertRejected(map, options, "An enabled spawn requires a configured spawn type");
        options = Options(types, valid, false);
        options.Chest = types.Barrel;
        options.BarrelCount = 1;
        options.ChestCount = 1;
        AssertRejected(map, options, "Distinct exact object quantities require distinct type identities");
    }

    private static void VerifyInsufficientCapacity()
    {
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 12, 4, 0);
        map.SetCell(2, 1, types.Chest, MapLayer.Objects);
        map.SetCell(3, 1, types.Barrel, MapLayer.Objects);
        GridMapRandomOptions options = Options(types, new RectInt(0, 1, 12, 2), false);
        options.BarrelCount = 13;
        AssertRejected(map, options, "More requested objects than supporting cells");
        options.BarrelCount = 12;
        options.PlaceSpawn = true;
        AssertRejected(map, options, "Spawn also consumes capacity");
        GridMapAsset empty = Map(12, 4);
        options = Options(types, new RectInt(0, 0, 12, 4), false);
        options.ChestCount = 1;
        AssertRejected(empty, options, "Floating objects are rejected when there is no terrain support");
        options.ChestCount = 0;
        GridMapRandomPlan emptyPlan = CreatePlan(empty, options);
        Equal(0, emptyPlan.Apply(empty), "Zero requested objects can succeed without changing an empty region");
    }

    private static void VerifyStalePlans()
    {
        Types types = CreateTypes();
        RectInt region = new RectInt(2, 2, 10, 3);
        for (int scenario = 0; scenario < 8; scenario++)
        {
            Types activeTypes = CreateTypes();
            GridMapAsset map = FlatMap(activeTypes, 16, 8, 1);
            GridMapRandomOptions options = Options(activeTypes, region, false);
            options.BarrelCount = 3;
            options.ChestCount = 3;
            GridMapRandomPlan plan = CreatePlan(map, options);
            if (scenario == 0) map.SetCell(3, 2, activeTypes.Spawn, MapLayer.Objects);
            if (scenario == 1) map.SetCell(3, 3, activeTypes.Ground);
            if (scenario == 2) map.SetCell(3, 1, null);
            if (scenario == 3) map.Resize(17, 8);
            if (scenario == 4) map.CellSize = 2f;
            if (scenario == 5) map.Origin = new Vector2(5, -3);
            if (scenario == 6) activeTypes.Ground.Collision = MapTileCollisionMode.None;
            if (scenario == 7) activeTypes.Barrel.Layer = MapLayer.Terrain;
            Snapshot beforeApply = new Snapshot(map);
            ExpectInvalidOperation(() => plan.Apply(map), "A stale plan must reject source changes, scenario " + scenario);
            beforeApply.AssertMatches(map, "A stale-plan rejection makes no partial changes");
        }
        GridMapAsset source = FlatMap(types, 16, 8, 1);
        GridMapAsset other = FlatMap(types, 16, 8, 1);
        GridMapRandomPlan sourcePlan = CreatePlan(source, Options(types, region, false));
        Snapshot otherBefore = new Snapshot(other);
        ExpectInvalidOperation(() => sourcePlan.Apply(other), "A plan cannot be applied to a different map object");
        otherBefore.AssertMatches(other, "Rejecting a different map has no side effects");
    }

    private static void VerifyUnrelatedOutsideEdit()
    {
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 16, 8, 1);
        RectInt region = new RectInt(2, 2, 10, 3);
        GridMapRandomOptions options = Options(types, region, false);
        options.BarrelCount = 2;
        options.ChestCount = 3;
        GridMapRandomPlan plan = CreatePlan(map, options);
        map.SetCell(0, 6, types.Spawn, MapLayer.Objects);
        map.SetCell(15, 7, types.Ground);
        Snapshot beforeApply = new Snapshot(map);
        plan.Apply(map);
        beforeApply.AssertOutsideUnchanged(map, region);
        Equal(2, Count(map, region, types.Barrel), "Unrelated outside edits do not invalidate region-only plans");
        Equal(3, Count(map, region, types.Chest), "Requested counts survive unrelated outside edits");
    }

    private static void VerifyUndoRedo()
    {
        Types types = CreateTypes();
        GridMapAsset map = FlatMap(types, 28, 16, 0);
        RectInt region = new RectInt(2, 2, 22, 11);
        map.SetCell(4, 4, types.Chest, MapLayer.Objects);
        map.SetCell(5, 5, types.Ground);
        Snapshot original = new Snapshot(map);
        GridMapRandomOptions options = Options(types, region, true);
        options.BarrelCount = 3;
        options.ChestCount = 4;
        options.PlaceSpawn = true;
        GridMapRandomWindow window = ScriptableObject.CreateInstance<GridMapRandomWindow>();
        try
        {
            SetWindowField(window, "map", map);
            SetWindowField(window, "regionStart", region.position);
            SetWindowField(window, "regionSize", region.size);
            SetWindowField(window, "generateTerrain", true);
            SetWindowField(window, "placeSpawn", true);
            SetWindowField(window, "ground", types.Ground);
            SetWindowField(window, "barrel", types.Barrel);
            SetWindowField(window, "chest", types.Chest);
            SetWindowField(window, "spawn", types.Spawn);
            SetWindowField(window, "barrelCount", "3");
            SetWindowField(window, "chestCount", "4");
            SetWindowField(window, "seedText", "137");
            True(InvokeGenerate(window), "The actual window Generate action succeeds");
            Undo.FlushUndoRecordObjects();
            Equal(3, Count(map, region, types.Barrel), "The window applies exact barrel count");
            Equal(4, Count(map, region, types.Chest), "The window applies exact chest count");
            Equal(1, Count(map, region, types.Spawn), "The window applies its enabled spawn option");
            Snapshot generated = new Snapshot(map);
            True(original.ChangedCells(map) > 0, "The generation actually changed map data");
            Undo.PerformUndo();
            original.AssertMatches(map, "One Undo restores the complete original map, including both layers");
            Undo.PerformRedo();
            generated.AssertMatches(map, "One Redo restores the complete generated map");
            SetWindowField(window, "barrelCount", "1.5");
            True(!InvokeGenerate(window), "The window rejects invalid count input");
            generated.AssertMatches(map, "Rejected window input leaves the complete map unchanged");
            SetWindowField(window, "barrelCount", "3");
            SetWindowField(window, "seedText", "invalid seed");
            True(!InvokeGenerate(window), "The window rejects invalid seed input");
            generated.AssertMatches(map, "A rejected seed leaves the complete map unchanged");
        }
        finally
        {
            Object.DestroyImmediate(window);
            Undo.ClearUndo(map);
            Undo.IncrementCurrentGroup();
        }
    }

    private static void SetWindowField(GridMapRandomWindow window, string name, object value)
    {
        FieldInfo field = typeof(GridMapRandomWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(typeof(GridMapRandomWindow).FullName, name);
        field.SetValue(window, value);
    }

    private static bool InvokeGenerate(GridMapRandomWindow window)
    {
        MethodInfo method = typeof(GridMapRandomWindow).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException(typeof(GridMapRandomWindow).FullName, "Generate");
        return (bool)method.Invoke(window, null);
    }

    private static GridMapRandomPlan CreatePlan(GridMapAsset map, GridMapRandomOptions options)
    {
        GridMapRandomPlan plan;
        string error;
        True(GridMapRandomGenerator.TryCreatePlan(map, options, out plan, out error), "Planning should succeed: " + error);
        True(plan != null, "Successful planning returns a plan");
        return plan;
    }

    private static void AssertRejected(GridMapAsset map, GridMapRandomOptions options, string message)
    {
        Snapshot original = new Snapshot(map);
        GridMapRandomPlan plan;
        string error;
        True(!GridMapRandomGenerator.TryCreatePlan(map, options, out plan, out error), message + " must fail");
        True(plan == null, message + " must not return a usable plan");
        True(!string.IsNullOrWhiteSpace(error), message + " supplies an actionable validation error");
        original.AssertMatches(map, message + " does not alter either map layer");
    }

    private static GridMapRandomOptions Options(Types types, RectInt region, bool terrain)
    {
        return new GridMapRandomOptions
        {
            Region = region, Ground = types.Ground, Barrel = types.Barrel, Chest = types.Chest, Spawn = types.Spawn,
            GenerateTerrain = terrain, PlaceSpawn = false, BarrelCount = 0, ChestCount = 0, Seed = 137
        };
    }

    private static Types CreateTypes()
    {
        return new Types
        {
            Ground = Tile("random ground", MapLayer.Terrain, MapTileCollisionMode.Solid),
            Barrel = Tile("random barrel", MapLayer.Objects, MapTileCollisionMode.None),
            Chest = Tile("random chest", MapLayer.Objects, MapTileCollisionMode.None),
            Spawn = Tile("random spawn", MapLayer.Objects, MapTileCollisionMode.None)
        };
    }

    private static MapTileType Tile(string name, MapLayer layer, MapTileCollisionMode collision)
    {
        MapTileType type = Track(ScriptableObject.CreateInstance<MapTileType>());
        type.name = name;
        type.Layer = layer;
        type.Collision = collision;
        return type;
    }

    private static GridMapAsset Map(int width, int height)
    {
        GridMapAsset map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(width, height);
        return map;
    }

    private static GridMapAsset FlatMap(Types types, int width, int height, int floorY)
    {
        GridMapAsset map = Map(width, height);
        for (int x = 0; x < width; x++) map.SetCell(x, floorY, types.Ground);
        return map;
    }

    private static int Count(GridMapAsset map, RectInt region, MapTileType type)
    {
        int count = 0;
        for (int y = region.yMin; y < region.yMax; y++)
            for (int x = region.xMin; x < region.xMax; x++)
                if (map.GetCell(x, y, MapLayer.Objects) == type) count++;
        return count;
    }

    private static int CountObjects(GridMapAsset map, RectInt region)
    {
        int count = 0;
        for (int y = region.yMin; y < region.yMax; y++)
            for (int x = region.xMin; x < region.xMax; x++)
                if (map.GetCell(x, y, MapLayer.Objects) != null) count++;
        return count;
    }

    private static T Track<T>(T item) where T : Object
    {
        TemporaryObjects.Add(item);
        return item;
    }

    private static void Check(string name, Action action)
    {
        try
        {
            action();
            report.passed++;
            report.results.Add(new CaseResult { name = name, passed = true, error = string.Empty });
        }
        catch (Exception exception)
        {
            report.failed++;
            report.results.Add(new CaseResult { name = name, passed = false, error = exception.ToString() });
            Debug.LogError("Map2D random verification failed: " + name + "\n" + exception);
        }
    }

    private static void ExpectInvalidOperation(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapRandomReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/random-verification.json"));
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Same(Object expected, Object actual, string message)
    {
        if (expected != actual) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }
}

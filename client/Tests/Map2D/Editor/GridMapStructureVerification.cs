using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Run only in an isolated project copy. Copy to Assets/.../Editor and execute
/// GridMapStructureVerification.RunBatch with -mapStructureReport <absolute JSON path>.
/// No NUnit package or graphics device is required. Temporary assets are removed;
/// the default-template test restores its deliberate metadata edit before returning.
/// </summary>
public static class GridMapStructureVerification
{
    [Serializable] private sealed class Result { public string name; public bool passed; public string error; }
    [Serializable] private sealed class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public int passed;
        public int failed;
        public List<Result> results = new List<Result>();
    }

    private sealed class Tiles
    {
        public MapTileType Ground;
        public MapTileType Wall;
        public MapTileType Door;
        public MapTileType Chest;
    }

    private sealed class Snapshot
    {
        public readonly int Width;
        public readonly int Height;
        private readonly MapTileType[] terrain;
        private readonly MapTileType[] objects;
        public Snapshot(GridMapAsset map)
        {
            Width = map.Width;
            Height = map.Height;
            terrain = new MapTileType[Width * Height];
            objects = new MapTileType[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    terrain[x + y * Width] = map.GetCell(x, y);
                    objects[x + y * Width] = map.GetCell(x, y, MapLayer.Objects);
                }
        }
        public MapTileType Cell(int x, int y, MapLayer layer)
        {
            return (layer == MapLayer.Terrain ? terrain : objects)[x + y * Width];
        }
        public void AssertMatches(GridMapAsset map, string message)
        {
            Equal(Width, map.Width, message + " width");
            Equal(Height, map.Height, message + " height");
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                        Same(Cell(x, y, layer), map.GetCell(x, y, layer), message + " " + layer + " " + x + "," + y);
        }
        public void AssertOutside(GridMapAsset map, RectInt region)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (region.Contains(new Vector2Int(x, y))) continue;
                    Same(Cell(x, y, MapLayer.Terrain), map.GetCell(x, y), "Outside terrain remains unchanged");
                    Same(Cell(x, y, MapLayer.Objects), map.GetCell(x, y, MapLayer.Objects), "Outside objects remain unchanged");
                }
        }
        public int Changes(GridMapAsset map)
        {
            int changes = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (Cell(x, y, MapLayer.Terrain) != map.GetCell(x, y)) changes++;
                    if (Cell(x, y, MapLayer.Objects) != map.GetCell(x, y, MapLayer.Objects)) changes++;
                }
            return changes;
        }
    }

    private static readonly List<Object> Temporary = new List<Object>();
    private static Report report;
    private static string temporaryFolder;

    public static void RunBatch()
    {
        report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        temporaryFolder = "Assets/__Map2DStructureVerification_" + Guid.NewGuid().ToString("N");
        try
        {
            Check("Stamp copies both layers exactly and leaves the outside untouched", VerifyStamp);
            Check("Horizontal mirror preserves Y and mirrors both layers", VerifyMirror);
            Check("Empty-cell replacement is independent for each layer", VerifyEmptyCells);
            Check("Invalid placement rejects the whole stamp without editing either asset", VerifyAtomicFailure);
            Check("Using the target itself as the template reads a complete source snapshot", VerifyAliasedLayout);
            Check("Capturing a region creates independent data with shared tile definitions", VerifyCapture);
            Check("Captured template subassets survive save and actual unload/reload", VerifySavedCapture);
            Check("Saving a template layout as a map creates an independent map-only asset", VerifyMapCopyFromTemplate);
            Check("Three default templates and nine editable types initialize without overwriting edits", VerifyDefaults);
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(temporaryFolder)) AssetDatabase.DeleteAsset(temporaryFolder);
            foreach (Object item in Temporary)
            {
                if (item == null || EditorUtility.IsPersistent(item)) continue;
                Undo.ClearUndo(item);
                Object.DestroyImmediate(item);
            }
            Temporary.Clear();
            string path = GetReportPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D structure verification: " + report.passed + " passed, " + report.failed + " failed. " + path);
        }
        if (report.failed > 0) throw new InvalidOperationException("Map2D structure verification failed. See the JSON report.");
    }

    private static void VerifyStamp()
    {
        Tiles tiles = CreateTiles();
        MapStructureTemplate template = Template(tiles);
        GridMapAsset target = FilledMap(tiles, 8, 7);
        Vector2Int origin = new Vector2Int(2, 3);
        Snapshot before = new Snapshot(target);
        Snapshot source = new Snapshot(template.Layout);
        int changed = Place(target, template, origin, false, true);
        AssertStamped(target, template.Layout, origin, false);
        before.AssertOutside(target, new RectInt(origin, new Vector2Int(template.Width, template.Height)));
        Equal(before.Changes(target), changed, "Changed count includes only actual differences in both layers");
        source.AssertMatches(template.Layout, "Placement never edits the template");
        Equal(0, Place(target, template, origin, false, true), "Repeating an identical stamp reports no changes");
    }

    private static void VerifyMirror()
    {
        Tiles tiles = CreateTiles();
        MapStructureTemplate template = Template(tiles);
        GridMapAsset target = Map(8, 6);
        Snapshot source = new Snapshot(template.Layout);
        Vector2Int origin = new Vector2Int(3, 2);
        Place(target, template, origin, true, true);
        AssertStamped(target, template.Layout, origin, true);
        Same(tiles.Ground, target.GetCell(origin.x + 2, origin.y), "Leftmost source terrain appears at the right edge");
        Same(tiles.Chest, target.GetCell(origin.x, origin.y + 1, MapLayer.Objects), "Rightmost object mirrors without moving vertically");
        source.AssertMatches(template.Layout, "Mirroring does not modify source data");
    }

    private static void VerifyEmptyCells()
    {
        Tiles tiles = CreateTiles();
        MapStructureTemplate template = Template(tiles);
        GridMapAsset target = FilledMap(tiles, 7, 6);
        Vector2Int origin = new Vector2Int(2, 2);
        Snapshot before = new Snapshot(target);
        Place(target, template, origin, false, false);
        for (int y = 0; y < template.Height; y++)
            for (int x = 0; x < template.Width; x++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                {
                    MapTileType value = template.Layout.GetCell(x, y, layer);
                    MapTileType expected = value != null ? value : before.Cell(origin.x + x, origin.y + y, layer);
                    Same(expected, target.GetCell(origin.x + x, origin.y + y, layer), "Overlay leaves empty source cells unchanged per layer");
                }
        Place(target, template, origin, false, true);
        AssertStamped(target, template.Layout, origin, false);
        Same(null, target.GetCell(origin.x + 1, origin.y), "Replacing empty terrain clears the target ground");
        Same(tiles.Door, target.GetCell(origin.x + 1, origin.y, MapLayer.Objects), "The object at the same coordinate remains independently populated");
    }

    private static void VerifyAtomicFailure()
    {
        Tiles tiles = CreateTiles();
        GridMapAsset target = FilledMap(tiles, 5, 4);
        MapStructureTemplate template = Template(tiles);
        Snapshot before = new Snapshot(target);
        Snapshot source = new Snapshot(template.Layout);
        foreach (Vector2Int origin in new[]
        {
            new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(3, 0),
            new Vector2Int(0, 3), new Vector2Int(int.MaxValue, 0), new Vector2Int(0, int.MaxValue)
        })
        {
            int changed;
            string error;
            True(!GridMapStructureEditing.TryPlace(target, template, origin, true, true, out changed, out error), "An out-of-bounds template is rejected entirely");
            Equal(0, changed, "A rejected placement reports zero changes");
            True(!string.IsNullOrWhiteSpace(error), "A rejected placement provides a reason");
            before.AssertMatches(target, "Bounds failure is atomic");
            source.AssertMatches(template.Layout, "Bounds failure preserves the template");
        }
        int nullChanged;
        string nullError;
        True(!GridMapStructureEditing.TryPlace(target, null, Vector2Int.zero, false, true, out nullChanged, out nullError), "A missing template is rejected");
        MapStructureTemplate empty = Track(ScriptableObject.CreateInstance<MapStructureTemplate>());
        True(!GridMapStructureEditing.TryPlace(target, empty, Vector2Int.zero, false, true, out nullChanged, out nullError), "A missing layout is rejected");
        before.AssertMatches(target, "Missing template or layout never clears data");
    }

    private static void VerifyAliasedLayout()
    {
        Tiles tiles = CreateTiles();
        MapStructureTemplate template = Template(tiles);
        GridMapAsset map = template.Layout;
        Snapshot original = new Snapshot(map);
        Place(map, template, Vector2Int.zero, true, true);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                    Same(original.Cell(map.Width - 1 - x, y, layer), map.GetCell(x, y, layer),
                        "In-place mirroring uses the original full source, not partially written cells");
    }

    private static void VerifyCapture()
    {
        Tiles tiles = CreateTiles();
        GridMapAsset map = FilledMap(tiles, 6, 5);
        map.CellSize = 2.5f;
        map.Origin = new Vector2(-4, 7);
        map.SetCell(2, 2, tiles.Wall);
        map.SetCell(3, 1, tiles.Door, MapLayer.Objects);
        RectInt region = new RectInt(1, 1, 3, 2);
        GridMapAsset capture = Track(GridMapStructureEditing.Capture(map, region));
        Equal(3, capture.Width, "Capture width matches the selected region");
        Equal(2, capture.Height, "Capture height matches the selected region");
        Equal(2.5f, capture.CellSize, "Capture retains the cell size");
        Equal(Vector2.zero, capture.Origin, "Template capture uses local coordinates");
        for (int y = 0; y < region.height; y++)
            for (int x = 0; x < region.width; x++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                    Same(map.GetCell(region.x + x, region.y + y, layer), capture.GetCell(x, y, layer), "Capture copies both layers by object reference");
        Snapshot before = new Snapshot(map);
        capture.Clear();
        before.AssertMatches(map, "Editing captured data does not modify the source map");
        GridMapAsset second = Track(GridMapStructureEditing.Capture(map, region));
        Snapshot capturedBefore = new Snapshot(second);
        map.Clear();
        capturedBefore.AssertMatches(second, "Editing the source map does not modify an existing capture");
        ExpectArgument(() => GridMapStructureEditing.Capture(map, new RectInt(-1, 0, 2, 2)), "Negative capture region is rejected");
        ExpectArgument(() => GridMapStructureEditing.Capture(map, new RectInt(0, 0, 0, 2)), "Empty capture region is rejected");
        ExpectArgument(() => GridMapStructureEditing.Capture(map, new RectInt(5, 4, 2, 2)), "Out-of-bounds capture is rejected");
    }

    private static void VerifySavedCapture()
    {
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryFolder));
        Tiles tiles = CreateTiles();
        AssetDatabase.CreateAsset(tiles.Ground, temporaryFolder + "/Ground.asset");
        AssetDatabase.CreateAsset(tiles.Wall, temporaryFolder + "/Wall.asset");
        AssetDatabase.CreateAsset(tiles.Door, temporaryFolder + "/Door.asset");
        AssetDatabase.CreateAsset(tiles.Chest, temporaryFolder + "/Chest.asset");
        MapStructureTemplate source = Template(tiles);
        string path = temporaryFolder + "/CapturedHouse.asset";
        MapStructureTemplate saved = GridMapStructureAssets.CreateFromRegion(path, "自定义小屋", source.Layout, new RectInt(0, 0, 3, 2));
        True(saved != null && saved.Layout != null, "The asset helper creates a template and independent layout");
        True(saved.Layout != source.Layout, "The saved layout is a separate GridMapAsset");
        Equal(path, AssetDatabase.GetAssetPath(saved.Layout), "The layout is stored as a subasset of the template");
        True(GridMapStructureAssets.LoadAll().Contains(saved), "The library finds a custom template saved outside its default folder");
        Snapshot expected = new Snapshot(source.Layout);
        GridMapAsset savedLayout = saved.Layout;
        Resources.UnloadAsset(saved);
        if (savedLayout != null) Resources.UnloadAsset(savedLayout);
        MapStructureTemplate reloaded = AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(path);
        Equal("自定义小屋", reloaded.DisplayName, "Template name survives actual reload");
        expected.AssertMatches(reloaded.Layout, "Captured terrain and object references survive actual reload");
        source.Layout.Clear();
        expected.AssertMatches(reloaded.Layout, "Saved template remains independent of later source edits");
        bool rejected = false;
        try { GridMapStructureAssets.CreateFromRegion(path, "Overwrite attempt", source.Layout, new RectInt(0, 0, 3, 2)); }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is IOException) { rejected = true; }
        True(rejected, "Creating a template cannot overwrite an existing asset path");
        expected.AssertMatches(AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(path).Layout, "Rejected overwrite preserves existing template cells");
    }

    private static void VerifyMapCopyFromTemplate()
    {
        if (!AssetDatabase.IsValidFolder(temporaryFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryFolder));
        Tiles tiles = CreateTiles();
        AssetDatabase.CreateAsset(tiles.Ground, temporaryFolder + "/CopyGround.asset");
        AssetDatabase.CreateAsset(tiles.Wall, temporaryFolder + "/CopyWall.asset");
        AssetDatabase.CreateAsset(tiles.Door, temporaryFolder + "/CopyDoor.asset");
        AssetDatabase.CreateAsset(tiles.Chest, temporaryFolder + "/CopyChest.asset");
        MapStructureTemplate source = Template(tiles);
        source.Layout.CellSize = 1.75f;
        string templatePath = temporaryFolder + "/CopySourceHouse.asset";
        MapStructureTemplate saved = GridMapStructureAssets.CreateFromRegion(templatePath, "Copy source house", source.Layout, new RectInt(0, 0, 3, 2));
        saved.Layout.Origin = new Vector2(-3, 4);
        EditorUtility.SetDirty(saved.Layout);
        AssetDatabase.SaveAssetIfDirty(saved.Layout);
        Snapshot original = new Snapshot(saved.Layout);
        string originalName = saved.DisplayName;
        string copyPath = temporaryFolder + "/StandaloneMap.asset";
        GridMapAsset copy = GridMapAssets.CreateMapCopy(copyPath, saved.Layout);
        True(copy != null && copy != saved.Layout, "Save-as creates a separate GridMapAsset");
        True(AssetDatabase.LoadMainAssetAtPath(copyPath) is GridMapAsset, "The copied file's main asset is a map");
        True(!(AssetDatabase.LoadMainAssetAtPath(copyPath) is MapStructureTemplate), "Save-as does not clone the template container");
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(copyPath))
            True(!(asset is MapStructureTemplate), "No hidden template container is copied into the map file");
        original.AssertMatches(copy, "The standalone map retains both layers and dimensions");
        Equal(saved.Layout.CellSize, copy.CellSize, "Save-as preserves cell size");
        Equal(saved.Layout.Origin, copy.Origin, "Save-as preserves map origin");
        Resources.UnloadAsset(copy);
        copy = AssetDatabase.LoadAssetAtPath<GridMapAsset>(copyPath);
        original.AssertMatches(copy, "The standalone map persists independently through reload");
        Equal(1.75f, copy.CellSize, "Standalone cell size survives reload");
        Equal(new Vector2(-3, 4), copy.Origin, "Standalone origin survives reload");
        copy.Clear();
        original.AssertMatches(saved.Layout, "Editing the standalone copy never changes the source template");
        Equal(originalName, saved.DisplayName, "Save-as preserves source template metadata");
        Snapshot beforeRejectedCopy = new Snapshot(copy);
        bool rejected = false;
        try { GridMapAssets.CreateMapCopy(copyPath, saved.Layout); }
        catch (InvalidOperationException) { rejected = true; }
        True(rejected, "An existing map path cannot be overwritten");
        beforeRejectedCopy.AssertMatches(copy, "Rejected overwrite leaves the existing standalone map unchanged");
        original.AssertMatches(saved.Layout, "Rejected overwrite leaves the template unchanged");
    }

    private static void VerifyDefaults()
    {
        List<MapStructureTemplate> templates = GridMapStructureAssets.EnsureDefaults();
        Equal(3, templates.Count, "There are three built-in house templates");
        Vector2Int[] sizes = { new Vector2Int(9, 7), new Vector2Int(13, 11), new Vector2Int(15, 8) };
        for (int i = 0; i < templates.Count; i++)
        {
            True(templates[i] != null && templates[i].Layout != null, "Every built-in template has editable layout data");
            Equal(sizes[i], new Vector2Int(templates[i].Width, templates[i].Height), "Built-in template dimensions");
            True(templates[i].Layout.TileCount > 0, "Built-in templates contain actual cells");
        }
        string[] ids = { "Ground", "Spikes", "SpawnPoint", "ExplosiveBarrel", "TreasureChest", "Wall", "Window", "WoodenDoor", "Stairs" };
        MapTilePalette palette = AssetDatabase.LoadAssetAtPath<MapTilePalette>(GridMapAssets.PalettePath);
        foreach (string id in ids)
        {
            MapTileType type = palette.Types.Find(item => item != null && item.Id == id);
            True(type != null, "Requested editable tile type is available: " + id);
            True(type.PropertySchema != null, "Every type has an editable shared property schema");
            MapTilePropertyValue value;
            True(type.TryGetProperty(MapTilePropertyKeys.Destructible, MapTilePropertyKind.Boolean, out value), "Every type exposes destructible metadata");
            True(type.TryGetProperty(MapTilePropertyKeys.Flammable, MapTilePropertyKind.Boolean, out value), "Every type exposes flammable metadata");
        }
        MapStructureTemplate first = templates[0];
        string originalDescription = first.Description;
        Snapshot layoutBefore = new Snapshot(first.Layout);
        try
        {
            first.Description = "Verification custom description must survive EnsureDefaults";
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssetIfDirty(first);
            List<MapStructureTemplate> repeated = GridMapStructureAssets.EnsureDefaults();
            for (int i = 0; i < templates.Count; i++) Same(templates[i], repeated[i], "Repeated setup reuses existing template assets");
            Equal("Verification custom description must survive EnsureDefaults", repeated[0].Description, "Repeated setup preserves user-edited template metadata");
            layoutBefore.AssertMatches(repeated[0].Layout, "Repeated setup does not overwrite template layout data");
        }
        finally
        {
            first.Description = originalDescription;
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssetIfDirty(first);
        }
    }

    private static int Place(GridMapAsset map, MapStructureTemplate template, Vector2Int origin, bool mirror, bool replaceEmpty)
    {
        int changed;
        string error;
        True(GridMapStructureEditing.TryPlace(map, template, origin, mirror, replaceEmpty, out changed, out error), "Placement should succeed: " + error);
        return changed;
    }

    private static void AssertStamped(GridMapAsset target, GridMapAsset layout, Vector2Int origin, bool mirror)
    {
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                    Same(layout.GetCell(mirror ? layout.Width - 1 - x : x, y, layer), target.GetCell(origin.x + x, origin.y + y, layer), "Template layer is stamped exactly");
    }

    private static MapStructureTemplate Template(Tiles tiles)
    {
        MapStructureTemplate template = Track(ScriptableObject.CreateInstance<MapStructureTemplate>());
        template.DisplayName = "Temporary asymmetric structure";
        template.Layout = Map(3, 2);
        template.Layout.SetCell(0, 0, tiles.Ground);
        template.Layout.SetCell(2, 0, tiles.Wall);
        template.Layout.SetCell(0, 1, tiles.Wall);
        template.Layout.SetCell(1, 0, tiles.Door, MapLayer.Objects);
        template.Layout.SetCell(2, 1, tiles.Chest, MapLayer.Objects);
        return template;
    }

    private static GridMapAsset FilledMap(Tiles tiles, int width, int height)
    {
        GridMapAsset map = Map(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                map.SetCell(x, y, tiles.Ground);
                map.SetCell(x, y, tiles.Chest, MapLayer.Objects);
            }
        return map;
    }

    private static GridMapAsset Map(int width, int height)
    {
        GridMapAsset map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(width, height);
        return map;
    }

    private static Tiles CreateTiles()
    {
        return new Tiles
        {
            Ground = Tile("structure ground", MapLayer.Terrain), Wall = Tile("structure wall", MapLayer.Terrain),
            Door = Tile("structure door", MapLayer.Objects), Chest = Tile("structure chest", MapLayer.Objects)
        };
    }

    private static MapTileType Tile(string name, MapLayer layer)
    {
        MapTileType type = Track(ScriptableObject.CreateInstance<MapTileType>());
        type.name = name;
        type.Layer = layer;
        return type;
    }

    private static T Track<T>(T value) where T : Object { Temporary.Add(value); return value; }
    private static void Check(string name, Action action)
    {
        try { action(); report.passed++; report.results.Add(new Result { name = name, passed = true, error = string.Empty }); }
        catch (Exception exception)
        {
            report.failed++;
            report.results.Add(new Result { name = name, passed = false, error = exception.ToString() });
            Debug.LogError("Map2D structure verification failed: " + name + "\n" + exception);
        }
    }
    private static void ExpectArgument(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }
    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapStructureReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/structure-verification.json"));
    }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Same(Object expected, Object actual, string message)
    {
        if (expected != actual) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }
}

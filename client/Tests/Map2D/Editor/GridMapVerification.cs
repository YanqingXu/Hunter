using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Copy into an isolated validation project's Editor folder, then run Unity with
/// -batchmode -executeMethod GridMapVerification.RunBatch -mapEditorReport <json path>.
/// Does not require a test-framework package or modify a user scene.
/// </summary>
public static class GridMapVerification
{
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
        public int passed;
        public int failed;
        public List<Result> results = new List<Result>();
    }

    private static readonly List<Object> TemporaryObjects = new List<Object>();
    private static string temporaryAssetFolder;
    private static Report report;

    public static void RunBatch()
    {
        report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        temporaryAssetFolder = "Assets/__Map2DVerification_" + Guid.NewGuid().ToString("N");
        try
        {
            Check("Brush clips corners, counts only changes, and erases", VerifyBrush);
            Check("Brush clamps size and rejects extreme off-map coordinates", VerifyBrushLimits);
            Check("Line joins sparse samples in all octants", VerifyLine);
            Check("Line clips outside strokes and limits extreme coordinates", VerifyLineClipping);
            Check("Flood fill respects barriers and null identity", VerifyFill);
            Check("Flood fill is four-neighbor and handles maximum map", VerifyFillConnectivity);
            Check("Rectangle supports reversed corners, outline and clipping", VerifyRectangle);
            Check("Both layers paint, fill and erase independently", VerifyLayers);
            Check("Tile identity remains independent of display name and id", VerifyTileIdentity);
            Check("Resize preserves coordinates across both layers", VerifyResize);
            Check("Saving and reloading preserves dimensions and tile references", VerifySaveReload);
            Check("Renderer separates solid, one-way and decorative tiles", VerifyRenderer);
            Check("Renderer rebuild and cleanup are safe and idempotent", VerifyRendererRebuild);
            Check("World coordinate queries respect origin and host transform", VerifyWorldQueries);
            Check("A complete paint stroke undoes and redoes as one operation", VerifyStrokeUndoRedo);
            Check("Renderer creation Undo leaves no generated subtree", VerifyRendererCreationUndo);
            Check("Editor Save persists the map and referenced tile edits", VerifyEditorSave);
            Check("Default palette contains all five requested types and is repeatable", VerifyDefaults);
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(temporaryAssetFolder))
                AssetDatabase.DeleteAsset(temporaryAssetFolder);
            foreach (Object item in TemporaryObjects)
                if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
            TemporaryObjects.Clear();
            string reportPath = GetReportPath();
            string reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDirectory)) Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D verification: " + report.passed + " passed, " + report.failed + " failed. " + reportPath);
        }

        if (report.failed != 0)
            throw new InvalidOperationException("Map2D verification failed: " + report.failed + " case(s). See the JSON report.");
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            report.results.Add(new Result { name = name, passed = true });
            report.passed++;
        }
        catch (Exception exception)
        {
            report.results.Add(new Result { name = name, passed = false, error = exception.ToString() });
            report.failed++;
            Debug.LogError("Map2D verification failed: " + name + "\n" + exception);
        }
    }

    private static GridMapAsset Map(int width, int height)
    {
        GridMapAsset map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(width, height);
        return map;
    }

    private static MapTileType Tile(string name, MapLayer layer = MapLayer.Terrain,
        MapTileCollisionMode collision = MapTileCollisionMode.None)
    {
        MapTileType tile = Track(ScriptableObject.CreateInstance<MapTileType>());
        tile.name = name;
        tile.Id = name;
        tile.DisplayName = name;
        tile.Layer = layer;
        tile.Collision = collision;
        return tile;
    }

    private static T Track<T>(T item) where T : Object
    {
        TemporaryObjects.Add(item);
        return item;
    }

    private static void VerifyBrush()
    {
        GridMapAsset map = Map(5, 4);
        MapTileType tile = Tile("brush");
        Equal(4, GridMapEditing.PaintBrush(map, 0, 0, tile, 3), "Clipped bottom-left 3x3 brush");
        Equal(0, GridMapEditing.PaintBrush(map, 0, 0, tile, 3), "Identical paint is a no-op");
        Equal(4, Count(map), "Only in-bounds corner cells painted");
        Equal(4, GridMapEditing.PaintBrush(map, 4, 3, tile, 3), "Clipped top-right brush");
        Equal(8, Count(map), "Disjoint corner counts");
        Equal(4, GridMapEditing.PaintBrush(map, 0, 0, null, 3), "Null brush erases");
        Equal(4, Count(map), "Erase affects only painted corner");
        Equal(0, GridMapEditing.PaintBrush(null, 0, 0, tile, 1), "Missing map is ignored");
    }

    private static void VerifyBrushLimits()
    {
        GridMapAsset map = Map(64, 64);
        MapTileType tile = Tile("brush-limits");
        Equal(1024, GridMapEditing.PaintBrush(map, 32, 32, tile, int.MaxValue), "Maximum brush is 32x32");
        map.Clear();
        Equal(1, GridMapEditing.PaintBrush(map, 32, 32, tile, -2), "Minimum brush is one cell");
        Equal(0, GridMapEditing.PaintBrush(map, int.MaxValue, 0, tile, 32), "Positive coordinate cannot wrap");
        Equal(0, GridMapEditing.PaintBrush(map, int.MinValue, 0, tile, 32), "Negative coordinate cannot wrap");
        map.Clear();
        Equal(4, GridMapEditing.PaintBrush(map, 2, 2, tile, 2), "Even brush paints four cells");
        Same(tile, map.GetCell(2, 2), "Even brush anchor");
        Same(tile, map.GetCell(3, 3), "Even brush extends right/up");
        Same(null, map.GetCell(1, 1), "Even brush does not extend left/down");
    }

    private static void VerifyLine()
    {
        GridMapAsset map = Map(20, 20);
        MapTileType tile = Tile("line");
        Vector2Int center = new Vector2Int(10, 10);
        Vector2Int[] endpoints =
        {
            new Vector2Int(19, 13), new Vector2Int(13, 19), new Vector2Int(7, 19), new Vector2Int(1, 13),
            new Vector2Int(1, 7), new Vector2Int(7, 1), new Vector2Int(13, 1), new Vector2Int(19, 7)
        };
        foreach (Vector2Int end in endpoints)
        {
            map.Clear();
            int expected = Math.Max(Math.Abs(end.x - center.x), Math.Abs(end.y - center.y)) + 1;
            Equal(expected, GridMapEditing.PaintLine(map, center, end, tile, 1), "Line samples every major-axis cell");
            Same(tile, map.GetCell(center.x, center.y), "Line start");
            Same(tile, map.GetCell(end.x, end.y), "Line end");
            Equal(expected, ConnectedCount(map, center), "Line has no skipped drag cells");
        }
        map.Clear();
        Equal(10, GridMapEditing.PaintLine(map, new Vector2Int(2, 5), new Vector2Int(11, 5), tile, 1), "Horizontal stroke");
        Equal(0, GridMapEditing.PaintLine(map, new Vector2Int(11, 5), new Vector2Int(2, 5), tile, 1), "Repeated stroke counts no changes");
    }

    private static void VerifyLineClipping()
    {
        GridMapAsset map = Map(12, 8);
        MapTileType tile = Tile("line-clipping");
        Equal(12, GridMapEditing.PaintLine(map, new Vector2Int(int.MinValue, 3), new Vector2Int(int.MaxValue, 3), tile, 1),
            "Extreme crossing stroke is clipped to the map");
        Equal(0, GridMapEditing.PaintLine(map, new Vector2Int(-100, -100), new Vector2Int(-20, -10), tile, 1),
            "Completely outside segment is ignored");
        map.Clear();
        Equal(12, GridMapEditing.PaintLine(map, new Vector2Int(-10, -1), new Vector2Int(30, -1), tile, 3),
            "Brush can overlap the map while its center remains outside");
        for (int x = 0; x < map.Width; x++) Same(tile, map.GetCell(x, 0), "Clipped fringe reaches every boundary cell");
    }

    private static void VerifyFill()
    {
        GridMapAsset map = Map(7, 5);
        MapTileType wall = Tile("wall");
        MapTileType filled = Tile("filled");
        GridMapEditing.PaintLine(map, new Vector2Int(3, 0), new Vector2Int(3, 4), wall, 1);
        Equal(15, GridMapEditing.Fill(map, 0, 0, filled), "Fill does not cross full-height barrier");
        Same(null, map.GetCell(4, 2), "Other region remains empty");
        Same(wall, map.GetCell(3, 2), "Barrier identity retained");
        Equal(0, GridMapEditing.Fill(map, 0, 0, filled), "Same-type fill is a no-op");
        Equal(15, GridMapEditing.Fill(map, 0, 0, null), "Fill can erase a connected region");
        Equal(5, Count(map), "Only wall remains after erase");
        Equal(0, GridMapEditing.Fill(map, -1, 0, filled), "Outside fill is ignored");
    }

    private static void VerifyFillConnectivity()
    {
        GridMapAsset map = Map(3, 3);
        MapTileType wall = Tile("diagonal-wall");
        MapTileType filled = Tile("diagonal-fill");
        map.SetCell(1, 0, wall);
        map.SetCell(0, 1, wall);
        Equal(1, GridMapEditing.Fill(map, 0, 0, filled), "Diagonal cells are not four-neighbor connected");
        Same(null, map.GetCell(1, 1), "Diagonal gap remains empty");
        GridMapAsset large = Map(256, 256);
        Equal(65536, GridMapEditing.Fill(large, 128, 128, filled), "Maximum map fills without recursion or overflow");
        Equal(65536, Count(large), "Every large-map cell filled");
    }

    private static void VerifyRectangle()
    {
        GridMapAsset map = Map(6, 6);
        MapTileType tile = Tile("rectangle");
        Equal(12, GridMapEditing.PaintRectangle(map, new Vector2Int(4, 4), new Vector2Int(1, 2), tile), "Reversed corners are inclusive");
        Same(null, map.GetCell(0, 2), "Rectangle keeps exterior empty");
        map.Clear();
        Equal(12, GridMapEditing.PaintRectangle(map, new Vector2Int(1, 1), new Vector2Int(4, 4), tile, true), "Four-by-four outline perimeter");
        Same(null, map.GetCell(2, 2), "Outline leaves interior unchanged");
        map.Clear();
        Equal(0, GridMapEditing.PaintRectangle(map, new Vector2Int(-2, -2), new Vector2Int(9, 9), tile, true),
            "Clipping does not turn off-map outline edges into new map edges");
        Equal(4, GridMapEditing.PaintRectangle(map, new Vector2Int(-2, -2), new Vector2Int(1, 1), tile), "Filled rectangle clips to map");
        Equal(0, GridMapEditing.PaintRectangle(map, new Vector2Int(8, 8), new Vector2Int(9, 9), tile), "Outside rectangle ignored");
    }

    private static void VerifyLayers()
    {
        GridMapAsset map = Map(4, 3);
        MapTileType ground = Tile("ground");
        MapTileType chest = Tile("chest", MapLayer.Objects);
        GridMapEditing.PaintRectangle(map, Vector2Int.zero, new Vector2Int(3, 2), ground);
        Equal(1, GridMapEditing.PaintBrush(map, 1, 1, chest, 1, MapLayer.Objects), "Object placed above ground");
        Same(ground, map.GetCell(1, 1), "Object paint retains terrain");
        Same(chest, map.GetCell(1, 1, MapLayer.Objects), "Object identity stored separately");
        Equal(11, GridMapEditing.Fill(map, 0, 0, chest, MapLayer.Objects), "Object fill sees only object layer");
        Equal(12, Count(map), "Object fill preserves every terrain tile");
        Equal(12, Count(map, MapLayer.Objects), "Object layer can fill independently");
        Equal(1, GridMapEditing.PaintBrush(map, 1, 1, null, 1, MapLayer.Objects), "Object erase");
        Same(ground, map.GetCell(1, 1), "Object erase retains terrain");
        map.Clear(MapLayer.Objects);
        Equal(0, Count(map, MapLayer.Objects), "Layer clear removes only objects");
        Equal(12, Count(map), "Layer clear preserves terrain");
        map.Clear();
        Equal(0, Count(map), "Full clear removes terrain");
        Equal(0, Count(map, MapLayer.Objects), "Full clear removes objects");
    }

    private static void VerifyResize()
    {
        GridMapAsset map = Map(3, 2);
        MapTileType ground = Tile("resize-ground");
        MapTileType chest = Tile("resize-chest", MapLayer.Objects);
        map.SetCell(2, 1, ground);
        map.SetCell(1, 1, chest, MapLayer.Objects);
        map.Resize(5, 4);
        Same(ground, map.GetCell(2, 1), "Growing width retains terrain coordinates");
        Same(chest, map.GetCell(1, 1, MapLayer.Objects), "Growing width retains object coordinates");
        Same(null, map.GetCell(0, 1), "Old flattened index does not leak into new position");
        Same(null, map.GetCell(4, 3), "New cells are empty");
        map.Resize(2, 2);
        Equal(0, Count(map), "Shrinking discards terrain outside new bounds");
        Same(chest, map.GetCell(1, 1, MapLayer.Objects), "Shrinking retains in-bounds object");
        map.Resize(4, 4, false);
        Equal(0, Count(map, MapLayer.Objects), "Resize without preserve clears objects");
        Equal(0, Count(map), "Resize without preserve clears terrain");
    }

    private static void VerifyTileIdentity()
    {
        GridMapAsset map = Map(3, 1);
        MapTileType first = Tile("same-id");
        MapTileType second = Tile("same-id");
        MapTileType replacement = Tile("replacement");
        map.SetCell(0, 0, first);
        map.SetCell(1, 0, second);
        map.SetCell(2, 0, first);
        Equal(1, GridMapEditing.Fill(map, 0, 0, replacement), "Different assets with identical names/ids are distinct types");
        Same(second, map.GetCell(1, 0), "Fill retains different asset identity");
        Same(first, map.GetCell(2, 0), "Fill does not cross different asset");
    }

    private static void VerifySaveReload()
    {
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryAssetFolder));
        GridMapAsset map = Map(7, 4);
        MapTileType ground = Tile("saved-ground", MapLayer.Terrain, MapTileCollisionMode.Solid);
        MapTileType chest = Tile("saved-chest", MapLayer.Objects);
        ground.Tags.Add("terrain");
        chest.Tags.Add("interactable");
        string groundPath = temporaryAssetFolder + "/Ground.asset";
        string chestPath = temporaryAssetFolder + "/Chest.asset";
        string mapPath = temporaryAssetFolder + "/Map.asset";
        AssetDatabase.CreateAsset(ground, groundPath);
        AssetDatabase.CreateAsset(chest, chestPath);
        map.CellSize = 1.5f;
        map.Origin = new Vector2(-4, -2);
        map.SetCell(6, 3, ground);
        map.SetCell(6, 3, chest, MapLayer.Objects);
        AssetDatabase.CreateAsset(map, mapPath);
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(ground);
        EditorUtility.SetDirty(chest);
        AssetDatabase.SaveAssets();
        Resources.UnloadAsset(map);
        Resources.UnloadAsset(ground);
        Resources.UnloadAsset(chest);
        GridMapAsset reloaded = AssetDatabase.LoadAssetAtPath<GridMapAsset>(mapPath);
        True(reloaded != null, "Map reload succeeds");
        Equal(7, reloaded.Width, "Width persists");
        Equal(4, reloaded.Height, "Height persists");
        True(Mathf.Approximately(1.5f, reloaded.CellSize), "Cell size persists");
        Equal(new Vector2(-4, -2), reloaded.Origin, "Origin persists");
        Same(AssetDatabase.LoadAssetAtPath<MapTileType>(groundPath), reloaded.GetCell(6, 3), "Terrain reference persists");
        Same(AssetDatabase.LoadAssetAtPath<MapTileType>(chestPath), reloaded.GetCell(6, 3, MapLayer.Objects), "Object reference persists");
        Same(null, reloaded.GetCell(5, 3), "Empty cell remains empty");
        True(reloaded.GetCell(6, 3).Tags.Contains("terrain"), "Custom tags persist");
    }

    private static void VerifyRenderer()
    {
        GridMapAsset map = Map(4, 3);
        map.CellSize = 2f;
        MapTileType ground = Tile("render-solid", MapLayer.Terrain, MapTileCollisionMode.Solid);
        MapTileType platform = Tile("render-platform", MapLayer.Terrain, MapTileCollisionMode.OneWay);
        MapTileType marker = Tile("render-marker", MapLayer.Objects, MapTileCollisionMode.None);
        map.SetCell(0, 0, ground);
        map.SetCell(1, 0, platform);
        map.SetCell(0, 0, marker, MapLayer.Objects);
        WithPreviewRenderer(map, renderer =>
        {
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            Tilemap oneWay = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.OneWay);
            Tilemap objects = renderer.GetTilemap(MapLayer.Objects, MapTileCollisionMode.None);
            True(solid != null && oneWay != null && objects != null, "Expected tilemaps exist");
            Same(ground, solid.GetTile(Vector3Int.zero), "Solid tile rendered in solid tilemap");
            Same(platform, oneWay.GetTile(new Vector3Int(1, 0, 0)), "Platform tile rendered in one-way tilemap");
            Same(marker, objects.GetTile(Vector3Int.zero), "Object coexists above solid terrain");
            Equal(UnityEngine.Tilemaps.Tile.ColliderType.Grid, solid.GetColliderType(Vector3Int.zero), "Solid tile supplies full-cell collision geometry");
            Equal(UnityEngine.Tilemaps.Tile.ColliderType.Grid, oneWay.GetColliderType(new Vector3Int(1, 0, 0)), "One-way tile supplies full-cell collision geometry");
            Equal(UnityEngine.Tilemaps.Tile.ColliderType.None, objects.GetColliderType(Vector3Int.zero), "Marker tile supplies no collision geometry");
            True(solid.GetSprite(Vector3Int.zero) != null, "Unassigned art uses a visible fallback sprite");
            True(solid.GetComponent<TilemapCollider2D>() != null, "Solid tiles have a collider");
            True(!solid.GetComponent<TilemapCollider2D>().usedByEffector, "Solid collider is not one-way");
            True(oneWay.GetComponent<TilemapCollider2D>() != null, "One-way tiles have a collider");
            True(oneWay.GetComponent<TilemapCollider2D>().usedByEffector, "One-way collider uses its effector");
            True(oneWay.GetComponent<PlatformEffector2D>() != null && oneWay.GetComponent<PlatformEffector2D>().useOneWay,
                "Platform effector enables one-way contact");
            True(objects.GetComponent<TilemapCollider2D>() == null || !objects.GetComponent<TilemapCollider2D>().enabled,
                "Non-collision object tiles do not add collisions");
            True(Mathf.Approximately(2f, Vector3.Distance(solid.GetCellCenterWorld(Vector3Int.zero),
                solid.GetCellCenterWorld(new Vector3Int(1, 0, 0)))), "Renderer respects map cell size");
            Equal(new Vector2Int(1, 0), renderer.WorldToCell(solid.GetCellCenterWorld(new Vector3Int(1, 0, 0))),
                "World position maps back to map coordinates");
        });
        Same(ground, map.GetCell(0, 0), "Preview cleanup does not alter terrain data");
        Same(marker, map.GetCell(0, 0, MapLayer.Objects), "Preview cleanup does not alter object data");
    }

    private static void VerifyRendererRebuild()
    {
        GridMapAsset map = Map(3, 2);
        MapTileType tile = Tile("render-rebuild", MapLayer.Terrain, MapTileCollisionMode.Solid);
        map.SetCell(2, 1, tile);
        WithPreviewRenderer(map, renderer =>
        {
            renderer.Rebuild();
            int firstCount = renderer.GetComponentsInChildren<Tilemap>(true).Length;
            renderer.Rebuild();
            Equal(firstCount, renderer.GetComponentsInChildren<Tilemap>(true).Length, "Rebuild does not duplicate tilemaps");
            Equal(6, firstCount, "One tilemap per layer and collision mode");
            map.SetCell(2, 1, null);
            map.SetCell(0, 0, tile);
            renderer.Rebuild();
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            Same(null, solid.GetTile(new Vector3Int(2, 1, 0)), "Rebuild removes stale tiles");
            Same(tile, solid.GetTile(Vector3Int.zero), "Rebuild adds new tile");
            renderer.ClearPreview();
            renderer.ClearPreview();
            foreach (Tilemap tilemap in renderer.GetComponentsInChildren<Tilemap>(true))
                Equal(0, tilemap.GetUsedTilesCount(), "Preview cleanup empties all tilemaps and is idempotent");
            Same(tile, map.GetCell(0, 0), "Preview cleanup preserves source data");
        });
    }

    private static void VerifyWorldQueries()
    {
        GridMapAsset map = Map(4, 3);
        map.CellSize = 1.75f;
        map.Origin = new Vector2(-2f, 3f);
        MapTileType solid = Tile("world-solid", MapLayer.Terrain, MapTileCollisionMode.Solid);
        MapTileType objectType = Tile("world-object", MapLayer.Objects);
        objectType.Walkable = false;
        map.SetCell(1, 1, solid);
        map.SetCell(2, 1, objectType, MapLayer.Objects);
        WithPreviewRenderer(map, renderer =>
        {
            renderer.transform.position = new Vector3(10f, -8f, 0f);
            renderer.transform.rotation = Quaternion.Euler(0, 0, 20f);
            renderer.transform.localScale = new Vector3(2f, 0.5f, 1f);
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    Equal(new Vector2Int(x, y), renderer.WorldToCell(renderer.CellToWorldCenter(x, y)),
                        "Coordinate roundtrip includes origin, size, position, rotation and scale");
            MapTileType read;
            True(renderer.TryGetCell(renderer.CellToWorldCenter(1, 1), out read), "World query finds ground");
            Same(solid, read, "World query returns source asset identity");
            True(renderer.TryGetCell(renderer.CellToWorldCenter(2, 1), out read, MapLayer.Objects), "Object query uses requested layer");
            Same(objectType, read, "Object world query identity");
            True(!renderer.IsWalkable(renderer.CellToWorldCenter(1, 1)), "Solid terrain blocks movement queries");
            True(!renderer.IsWalkable(renderer.CellToWorldCenter(2, 1)), "Object walkability is checked independently");
            True(renderer.IsWalkable(renderer.CellToWorldCenter(0, 0)), "Empty in-bounds cells allow movement");
            True(!renderer.IsWalkable(renderer.CellToWorldCenter(-1, 0)), "Out-of-bounds world position is not walkable");
        });
    }

    private static void VerifyStrokeUndoRedo()
    {
        GridMapAsset map = Map(8, 4);
        MapTileType terrain = Tile("undo-terrain");
        MapTileType marker = Tile("undo-marker", MapLayer.Objects);
        map.SetCell(2, 1, marker, MapLayer.Objects);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        try
        {
            // The window records one complete asset before a stroke, then all drag
            // samples share this group. Verify the same contract over sparse samples.
            Undo.RegisterCompleteObjectUndo(map, "Map2D verification stroke");
            GridMapEditing.PaintBrush(map, 1, 1, terrain, 1);
            GridMapEditing.PaintLine(map, new Vector2Int(1, 1), new Vector2Int(5, 1), terrain, 1);
            GridMapEditing.PaintLine(map, new Vector2Int(5, 1), new Vector2Int(5, 3), terrain, 1);
            EditorUtility.SetDirty(map);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Equal(7, Count(map), "All drag samples painted");
            Undo.PerformUndo();
            Equal(0, Count(map), "One undo removes the entire stroke");
            Same(marker, map.GetCell(2, 1, MapLayer.Objects), "Undo preserves preexisting object layer");
            Undo.PerformRedo();
            Equal(7, Count(map), "One redo restores the entire stroke");
            Same(terrain, map.GetCell(5, 3), "Redo retains final sample");
            Same(marker, map.GetCell(2, 1, MapLayer.Objects), "Redo preserves preexisting object layer");
        }
        finally
        {
            Undo.ClearUndo(map);
            Undo.IncrementCurrentGroup();
        }
    }

    private static void VerifyRendererCreationUndo()
    {
        GridMapAsset map = Map(4, 3);
        MapTileType ground = Tile("undo-renderer-ground", MapLayer.Terrain, MapTileCollisionMode.Solid);
        map.SetCell(1, 1, ground);
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject host = new GameObject("Map2D verification undo renderer");
            SceneManager.MoveGameObjectToScene(host, scene);
            GridMapRenderer renderer = host.AddComponent<GridMapRenderer>();
            GameObject userChild = new GameObject("User-owned child");
            userChild.transform.SetParent(host.transform, false);
            // Set the backing field without triggering Map's automatic Rebuild. The
            // existing renderer is deliberately empty when the user starts generating.
            SetPrivateField(renderer, "map", map);
            Equal(0, host.GetComponentsInChildren<Tilemap>(true).Length, "Existing renderer starts empty");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(host, "Map2D verification generate");
            renderer.Rebuild(true);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Equal(6, host.GetComponentsInChildren<Tilemap>(true).Length, "Generation creates all six tilemaps");
            True(renderer.HasGeneratedPreview, "Renderer reports its complete generated preview");
            Undo.PerformUndo();
            True(host != null && renderer != null, "Undo preserves the preexisting renderer");
            Equal(0, host.GetComponentsInChildren<Tilemap>(true).Length, "Undo removes all new tilemaps");
            Equal(1, host.transform.childCount, "Undo removes the generated Grid root too");
            True(!renderer.HasGeneratedPreview, "Undo clears the renderer's generated-preview state");
            True(!(bool)GetPrivateField(renderer, "rebuildQueued"), "Undo cancels delayed rebuilding of deleted children");
            True(userChild != null && userChild.transform.parent == host.transform, "Undo preserves user-owned children");
            Undo.PerformRedo();
            Equal(6, host.GetComponentsInChildren<Tilemap>(true).Length, "Redo restores all generated layers");
            True(renderer.HasGeneratedPreview, "Redo restores the complete generated-preview state");
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            True(solid != null, "Redo restores renderer-to-tilemap references");
            Same(ground, solid.GetTile(new Vector3Int(1, 1, 0)), "Redo restores rendered tile data");
            Tilemap oneWay = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.OneWay);
            True(oneWay != null, "Redo restores the one-way tilemap reference");
            TilemapCollider2D oneWayCollider = oneWay.GetComponent<TilemapCollider2D>();
            True(oneWayCollider != null && oneWayCollider.usedByEffector, "Redo restores the collider's effector connection");
            PlatformEffector2D effector = oneWay.GetComponent<PlatformEffector2D>();
            True(effector != null && effector.useOneWay, "Redo restores one-way platform behavior");
            True(Mathf.Approximately(180f, effector.surfaceArc), "Redo restores the platform surface arc");
            Same(ground, map.GetCell(1, 1), "Scene Undo/Redo leaves map data unchanged");
            Undo.ClearUndo(host);
            Undo.ClearUndo(renderer);
        }
        finally
        {
            Undo.IncrementCurrentGroup();
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void VerifyEditorSave()
    {
        if (!AssetDatabase.IsValidFolder(temporaryAssetFolder))
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryAssetFolder));
        GridMapAsset map = Map(4, 3);
        MapTileType type = Tile("window-save-type");
        MapTilePalette palette = Track(ScriptableObject.CreateInstance<MapTilePalette>());
        palette.Types.Add(type);
        string mapPath = temporaryAssetFolder + "/WindowMap.asset";
        string typePath = temporaryAssetFolder + "/WindowType.asset";
        AssetDatabase.CreateAsset(type, typePath);
        AssetDatabase.CreateAsset(palette, temporaryAssetFolder + "/WindowPalette.asset");
        AssetDatabase.CreateAsset(map, mapPath);
        AssetDatabase.SaveAssets();
        GridMapEditorWindow window = ScriptableObject.CreateInstance<GridMapEditorWindow>();
        try
        {
            SetPrivateField(window, "map", map);
            SetPrivateField(window, "palette", palette);
            SetPrivateField(window, "selectedType", type);
            map.SetCell(3, 2, type);
            type.DisplayName = "保存测试类型";
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(type);
            InvokePrivate(window, "SaveMap");
            True(!EditorUtility.IsDirty(map), "Window Save flushes map changes");
            True(!EditorUtility.IsDirty(type), "Window Save flushes referenced type changes");
            SetPrivateField(window, "map", null);
            SetPrivateField(window, "palette", null);
            SetPrivateField(window, "selectedType", null);
            Resources.UnloadAsset(map);
            Resources.UnloadAsset(palette);
            Resources.UnloadAsset(type);
            GridMapAsset savedMap = AssetDatabase.LoadAssetAtPath<GridMapAsset>(mapPath);
            MapTileType savedType = AssetDatabase.LoadAssetAtPath<MapTileType>(typePath);
            Same(savedType, savedMap.GetCell(3, 2), "Window Save persists edited map cells");
            Equal("保存测试类型", savedType.DisplayName, "Window Save persists type properties");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void VerifyDefaults()
    {
        MapTilePalette palette = GridMapAssets.EnsureDefaults();
        string[] ids = { "Ground", "Spikes", "SpawnPoint", "ExplosiveBarrel", "TreasureChest" };
        string[] names = { "地面", "尖刺", "出生点", "炸药桶", "宝箱" };
        List<MapTileType> expected = new List<MapTileType>();
        for (int i = 0; i < ids.Length; i++)
        {
            MapTileType type = palette.Types.Find(item => item != null && item.Id == ids[i]);
            True(type != null, "Requested default type exists: " + names[i]);
            Equal(names[i], type.DisplayName, "Default Chinese name");
            Equal(i == 0 ? MapLayer.Terrain : MapLayer.Objects, type.Layer, "Default layer separation");
            expected.Add(type);
        }
        Equal(MapTileCollisionMode.Solid, expected[0].Collision, "Ground provides solid collision");
        GridMapAsset example = AssetDatabase.LoadAssetAtPath<GridMapAsset>(GridMapAssets.ExamplePath);
        True(example != null, "Example map exists");
        True(example.GetTileCount(MapLayer.Terrain) > 0 && example.GetTileCount(MapLayer.Objects) > 0,
            "Example demonstrates terrain and objects");
        int typeCount = palette.Types.Count;
        MapTilePalette repeated = GridMapAssets.EnsureDefaults();
        Same(palette, repeated, "Ensuring defaults reuses the palette");
        Equal(typeCount, repeated.Types.Count, "Repeated default setup does not duplicate types");
        Same(example, AssetDatabase.LoadAssetAtPath<GridMapAsset>(GridMapAssets.ExamplePath), "Repeated setup reuses the example map");
        for (int i = 0; i < ids.Length; i++)
            Same(expected[i], repeated.Types.Find(item => item != null && item.Id == ids[i]), "Repeated setup preserves type asset identity");
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    private static object GetPrivateField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return field.GetValue(target);
    }

    private static void InvokePrivate(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
        method.Invoke(target, null);
    }

    private static void WithPreviewRenderer(GridMapAsset map, Action<GridMapRenderer> assertion)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject host = new GameObject("Map2D verification preview");
            SceneManager.MoveGameObjectToScene(host, scene);
            GridMapRenderer renderer = host.AddComponent<GridMapRenderer>();
            renderer.Map = map;
            renderer.Rebuild();
            assertion(renderer);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static int Count(GridMapAsset map, MapLayer layer = MapLayer.Terrain)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.GetCell(x, y, layer) != null) count++;
        return count;
    }

    private static int ConnectedCount(GridMapAsset map, Vector2Int start)
    {
        // Bresenham lines are connected across all eight neighbors, including diagonals.
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> pending = new Queue<Vector2Int>();
        pending.Enqueue(start);
        visited.Add(start);
        while (pending.Count > 0)
        {
            Vector2Int cell = pending.Dequeue();
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    Vector2Int next = cell + new Vector2Int(x, y);
                    if (!map.Contains(next.x, next.y) || map.GetCell(next.x, next.y) == null || !visited.Add(next)) continue;
                    pending.Enqueue(next);
                }
        }
        return visited.Count;
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapEditorReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/verification.json"));
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

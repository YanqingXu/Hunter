using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Exercises the actual EditorWindow OnGUI through Show and SendEvent. Run in an
/// isolated validation project with -executeMethod GridMapWindowVerification.RunBatch
/// and optionally -mapWindowReport <json path>. An unavailable native GUI is reported
/// as skipped; direct editing calls are never substituted for the UI interactions.
/// </summary>
public static class GridMapWindowVerification
{
    [Serializable]
    private sealed class CaseResult
    {
        public string name;
        public string status;
        public string detail;
    }

    [Serializable]
    private sealed class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public bool batchMode;
        public bool onGuiEventsDispatched;
        public string windowSize;
        public string contentWorldBounds;
        public string mouseInputOffset;
        public string mouseInputOffsetSource;
        public List<string> inputCalibration = new List<string>();
        public int passed;
        public int failed;
        public int skipped;
        public List<CaseResult> results = new List<CaseResult>();
        public List<string> guiErrors = new List<string>();
    }

    private sealed class GuiUnavailableException : NotSupportedException
    {
        public GuiUnavailableException(string message) : base(message) { }
    }

    private static GridMapEditorWindow window;
    private static GridMapAsset map;
    private static MapTilePalette palette;
    private static MapTileType ground;
    private static MapTileType marker;
    private static MapTileType mutableType;
    private static Report report;
    private static Vector2 mouseInputOffset;
    private static readonly List<Object> TemporaryObjects = new List<Object>();

    public static void RunBatch()
    {
        RunBatchInternal(false);
    }

    /// <summary>Only the new spawn window and canvas placement paths; uses -mapWindowReport.</summary>
    public static void RunSpawnBatch()
    {
        RunBatchInternal(true);
    }

    private static void RunBatchInternal(bool spawnOnly)
    {
        report = new Report
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            batchMode = Application.isBatchMode
        };
        mouseInputOffset = Vector2.zero;
        EditorWindow previousFocus = EditorWindow.focusedWindow;
        Dictionary<int, bool> originalSceneDirty = CaptureSceneDirtyState();
        Application.logMessageReceived += CaptureGuiError;
        try
        {
            CreateTemporaryWindow();
            ProbeNativeGui();
            if (!spawnOnly)
            {
                Check("Window Layout and Repaint run without GUI exceptions", VerifyDraw);
                Check("Terrain canvas drag uses the correct cell coordinates", VerifyTerrainStroke);
                Check("Object canvas drag preserves terrain at the same coordinates", VerifyObjectStroke);
                Check("Right-button erasure affects only the selected layer", VerifyRightErase);
                Check("A type moved externally to Objects cannot overwrite Terrain", VerifyChangedTypeLayer);
                Check("Rectangle drag commits its complete area only on mouse release", VerifyRectangleCommit);
                Check("Clicking a house template places both layers and supports whole-house Undo/Redo", VerifyStructureClick);
                Check("Native template drag and drop places a house and rejects out-of-bounds drops", VerifyStructureDragDrop);
                Check("Random generation window draws native GUI and exposes its region without editing the map", VerifyRandomWindowDraw);
            }
            Check("Spawn configuration window draws native GUI without changing its map", VerifySpawnWindowDraw);
            Check("Native spawn marker placement preserves tiles and supports stable-identity Undo/Redo", VerifySpawnPlacement);
            if (!spawnOnly) Check("Window interactions leave existing scenes unchanged", () => VerifySceneDirtyState(originalSceneDirty));
        }
        catch (GuiUnavailableException exception)
        {
            report.skipped++;
            report.results.Add(new CaseResult
            {
                name = "Native EditorWindow event dispatch",
                status = "skipped",
                detail = exception.Message
            });
        }
        catch (Exception exception)
        {
            AddFailure("Window setup and native GUI dispatch", exception);
        }
        finally
        {
            try
            {
                if (map != null) Undo.ClearUndo(map);
                if (window != null) window.Close();
                if (window != null) Object.DestroyImmediate(window);
                window = null;
                foreach (Object temporary in TemporaryObjects)
                    if (temporary != null) Object.DestroyImmediate(temporary);
                TemporaryObjects.Clear();
                if (previousFocus != null) previousFocus.Focus();
                if (!spawnOnly) VerifySceneDirtyState(originalSceneDirty);
            }
            catch (Exception exception)
            {
                AddFailure("Temporary window cleanup", exception);
            }
            finally
            {
                Application.logMessageReceived -= CaptureGuiError;
                if (report.guiErrors.Count > 0 && report.failed == 0)
                    AddFailure("GUI error log", new InvalidOperationException(report.guiErrors[0]));
                string path = GetReportPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("Map2D window verification: " + report.passed + " passed, " + report.failed + " failed, " +
                    report.skipped + " skipped. " + path);
            }
        }

        if (report.failed > 0)
            throw new InvalidOperationException("Map2D window verification failed. See the JSON report.");
    }

    private static void CreateTemporaryWindow()
    {
        map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(20, 12);
        ground = CreateType("UI ground", MapLayer.Terrain);
        marker = CreateType("UI object", MapLayer.Objects);
        mutableType = CreateType("UI externally changed type", MapLayer.Terrain);
        MapTilePropertySchema schema = Track(ScriptableObject.CreateInstance<MapTilePropertySchema>());
        schema.EnsureBuiltInDefinitions();
        ground.PropertySchema = schema;
        marker.PropertySchema = schema;
        mutableType.PropertySchema = schema;
        palette = Track(ScriptableObject.CreateInstance<MapTilePalette>());
        palette.Types.Add(ground);
        palette.Types.Add(marker);
        palette.Types.Add(mutableType);
        window = ScriptableObject.CreateInstance<GridMapEditorWindow>();
        SetField("map", map);
        SetField("palette", palette);
        SetField("selectedType", ground);
        SetField("layer", MapLayer.Terrain);
        SetField("pan", new Vector2(24, 24));
        SetField("pixels", 25f);
        SetField("fitPending", false);
        SetField("brushSize", 1);
        SetField("updatePreview", false);
        SetTool("Brush");
        window.Show();
        window.position = new Rect(80, 80, 1200, 700);
        window.Focus();
        report.windowSize = window.position.width + " x " + window.position.height;
    }

    private static void ProbeNativeGui()
    {
        int errorsBefore = report.guiErrors.Count;
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
        ThrowIfNewGuiErrors(errorsBefore);
        Vector2Int firstPoint = new Vector2Int(2, 2);
        Vector2Int firstObserved;
        if (!TryReadHoveredCell(firstPoint, out firstObserved))
            throw new GuiUnavailableException("EditorWindow.Show and SendEvent did not dispatch the canvas MouseMove into OnGUI. " +
                "This Unity execution environment cannot verify window interaction; run this entry point with an interactive Unity editor.");
        ThrowIfNewGuiErrors(errorsBefore);
        report.onGuiEventsDispatched = true;

        // SendEvent targets the native GUIView, which can include the host's tab bar.
        // Prefer the actual content element's position over assuming that bar's height.
        Rect contentBounds = window.rootVisualElement.worldBound;
        report.contentWorldBounds = contentBounds.ToString();
        Vector2 contentOffset = contentBounds.position;
        bool finite = !float.IsNaN(contentOffset.x) && !float.IsNaN(contentOffset.y) &&
            !float.IsInfinity(contentOffset.x) && !float.IsInfinity(contentOffset.y);
        if (finite)
        {
            mouseInputOffset = contentOffset;
            if (ProbeMatches(firstPoint) && ProbeMatches(new Vector2Int(15, 9)))
            {
                report.mouseInputOffsetSource = "EditorWindow.rootVisualElement.worldBound.position";
            }
        }

        if (string.IsNullOrEmpty(report.mouseInputOffsetSource))
        {
            // Some Unity hosts report (0,0) even though GUIView subtracts tab chrome.
            // Derive a stable translation from a real MouseMove at a cell center; all
            // later events remain real native input, and distant probes must agree.
            float pixels = (float)GetField("pixels");
            mouseInputOffset = new Vector2((firstPoint.x - firstObserved.x) * pixels,
                (firstObserved.y - firstPoint.y) * pixels);
            report.mouseInputOffsetSource = "MouseMove cell-center calibration (fixed translation verified at distant points)";
        }

        report.mouseInputOffset = mouseInputOffset.ToString("F2");
        Vector2Int[] calibrationPoints = { firstPoint, new Vector2Int(15, 9), new Vector2Int(7, 6) };
        foreach (Vector2Int expected in calibrationPoints)
        {
            Vector2Int observed;
            True(TryReadHoveredCell(expected, out observed), "Calibrated native mouse event must reach the canvas");
            report.inputCalibration.Add("Expected " + expected + ", observed " + observed);
            Equal(expected, observed, "Native input translation must be consistent across distant canvas points");
        }
        ThrowIfNewGuiErrors(errorsBefore);
    }

    private static bool ProbeMatches(Vector2Int expected)
    {
        Vector2Int observed;
        return TryReadHoveredCell(expected, out observed) && observed == expected;
    }

    private static bool TryReadHoveredCell(Vector2Int intendedCell, out Vector2Int observedCell)
    {
        const string pending = "Map2D UI event probe pending";
        observedCell = default(Vector2Int);
        SetField("status", pending);
        SendMouse(EventType.MouseMove, intendedCell, 0);
        string status = (string)GetField("status");
        if (status == pending || string.IsNullOrEmpty(status) || status[0] != '(') return false;
        int comma = status.IndexOf(',');
        int closing = status.IndexOf(')');
        int x;
        int y;
        if (comma < 0 || closing <= comma || !int.TryParse(status.Substring(1, comma - 1), out x) ||
            !int.TryParse(status.Substring(comma + 1, closing - comma - 1), out y)) return false;
        observedCell = new Vector2Int(x, y);
        return true;
    }

    private static void VerifyDraw()
    {
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
        Equal(0, Count(MapLayer.Terrain), "Drawing the window does not paint terrain");
        Equal(0, Count(MapLayer.Objects), "Drawing the window does not paint objects");
    }

    private static void VerifyTerrainStroke()
    {
        Select(ground, MapLayer.Terrain);
        Drag(new Vector2Int(2, 2), new Vector2Int(7, 2));
        Equal(6, Count(MapLayer.Terrain), "Sparse drag paints every intervening terrain cell");
        for (int x = 2; x <= 7; x++) Same(ground, map.GetCell(x, 2), "Terrain drag coordinate");
        Same(null, map.GetCell(2, 3), "Vertical coordinates are not inverted or offset");
        Equal(0, Count(MapLayer.Objects), "Terrain stroke leaves object layer empty");
        True(!(bool)GetField("painting"), "Mouse release ends the paint stroke");
    }

    private static void VerifyObjectStroke()
    {
        Select(marker, MapLayer.Objects);
        Drag(new Vector2Int(2, 2), new Vector2Int(4, 2));
        Equal(3, Count(MapLayer.Objects), "Object drag paints requested cells");
        Equal(6, Count(MapLayer.Terrain), "Object drag preserves terrain");
        for (int x = 2; x <= 4; x++)
        {
            Same(marker, map.GetCell(x, 2, MapLayer.Objects), "Object coordinate");
            Same(ground, map.GetCell(x, 2), "Ground beneath object remains unchanged");
        }
    }

    private static void VerifyRightErase()
    {
        Select(marker, MapLayer.Objects);
        Click(new Vector2Int(3, 2), 1);
        Same(null, map.GetCell(3, 2, MapLayer.Objects), "Right button erases the active object cell");
        Same(ground, map.GetCell(3, 2), "Right button preserves the underlying terrain");
        Equal(2, Count(MapLayer.Objects), "Right button erases one cell only");
        Same(marker, map.GetCell(2, 2, MapLayer.Objects), "Neighboring object remains");
    }

    private static void VerifyChangedTypeLayer()
    {
        Select(mutableType, MapLayer.Terrain);
        mutableType.Layer = MapLayer.Objects;
        Click(new Vector2Int(6, 2), 0);
        Same(ground, map.GetCell(6, 2), "A selected type's external layer change cannot overwrite ground");
        Same(mutableType, map.GetCell(6, 2, MapLayer.Objects), "Changed type paints its new layer");
        Equal(MapLayer.Objects, (MapLayer)GetField("layer"), "Window switches to the type's actual layer");
    }

    private static void VerifyRectangleCommit()
    {
        Select(ground, MapLayer.Terrain);
        SetTool("Rectangle");
        SetField("rectangleOutline", false);
        SendGuiEvent(EventType.Layout);
        Vector2Int start = new Vector2Int(9, 4);
        Vector2Int end = new Vector2Int(12, 6);
        SendMouse(EventType.MouseDown, start, 0);
        SendMouse(EventType.MouseDrag, end, 0, CellPixel(end) - CellPixel(start));
        Same(null, map.GetCell(start.x, start.y), "Rectangle has not committed on mouse drag");
        Same(null, map.GetCell(end.x, end.y), "Rectangle endpoint remains unchanged before release");
        SendGuiEvent(EventType.Repaint);
        SendMouse(EventType.MouseUp, end, 0);
        for (int y = 4; y <= 6; y++)
            for (int x = 9; x <= 12; x++) Same(ground, map.GetCell(x, y), "Rectangle fills every inclusive coordinate on release");
        Equal(18, Count(MapLayer.Terrain), "Rectangle contributes its twelve-cell area");
        Same(null, map.GetCell(8, 4), "Rectangle does not spill outside the chosen area");
        True(!(bool)GetField("painting"), "Rectangle mouse release ends the stroke");
    }

    private static void VerifyRandomWindowDraw()
    {
        GridMapRandomWindow randomWindow = null;
        int terrainBefore = Count(MapLayer.Terrain);
        int objectsBefore = Count(MapLayer.Objects);
        var randomGround = CreateType("Random UI ground", MapLayer.Terrain);
        randomGround.Collision = MapTileCollisionMode.Solid;
        var barrel = CreateType("Random UI barrel", MapLayer.Objects);
        var chest = CreateType("Random UI chest", MapLayer.Objects);
        var spawn = CreateType("Random UI spawn", MapLayer.Objects);
        randomGround.PropertySchema = ground.PropertySchema;
        barrel.PropertySchema = ground.PropertySchema;
        chest.PropertySchema = ground.PropertySchema;
        spawn.PropertySchema = ground.PropertySchema;
        var expectedRegion = new RectInt(2, 1, 14, 9);
        try
        {
            // Do not call Open: it intentionally resolves and creates project defaults.
            randomWindow = ScriptableObject.CreateInstance<GridMapRandomWindow>();
            SetRandomField(randomWindow, "map", map);
            SetRandomField(randomWindow, "regionStart", expectedRegion.position);
            SetRandomField(randomWindow, "regionSize", expectedRegion.size);
            SetRandomField(randomWindow, "ground", randomGround);
            SetRandomField(randomWindow, "barrel", barrel);
            SetRandomField(randomWindow, "chest", chest);
            SetRandomField(randomWindow, "spawn", spawn);
            SetRandomField(randomWindow, "generateTerrain", true);
            SetRandomField(randomWindow, "placeSpawn", true);
            SetRandomField(randomWindow, "barrelCount", "");
            SetRandomField(randomWindow, "chestCount", "3");
            SetRandomField(randomWindow, "seedText", "12345");
            randomWindow.Show();
            randomWindow.position = new Rect(120, 100, 600, 900);
            randomWindow.Focus();
            randomWindow.SendEvent(new Event { type = EventType.Layout });
            randomWindow.SendEvent(new Event { type = EventType.Repaint });

            RectInt actualRegion;
            True(GridMapRandomWindow.TryGetRegion(map, out actualRegion), "The shown random window exposes its region to the map canvas");
            Equal(expectedRegion, actualRegion, "Region overlay uses the configured origin and dimensions");
            window.Focus();
            SendGuiEvent(EventType.Layout);
            SendGuiEvent(EventType.Repaint);

            SetRandomField(randomWindow, "generateTerrain", false);
            SetRandomField(randomWindow, "placeSpawn", false);
            randomWindow.Focus();
            randomWindow.SendEvent(new Event { type = EventType.Layout });
            randomWindow.SendEvent(new Event { type = EventType.Repaint });
            Equal(terrainBefore, Count(MapLayer.Terrain), "Drawing either generation mode does not change terrain");
            Equal(objectsBefore, Count(MapLayer.Objects), "Drawing either generation mode does not change objects");
        }
        finally
        {
            if (randomWindow != null) randomWindow.Close();
            if (randomWindow != null) Object.DestroyImmediate(randomWindow);
            if (window != null) window.Focus();
        }
        RectInt closedRegion;
        True(!GridMapRandomWindow.TryGetRegion(map, out closedRegion), "Closing the random window clears its region overlay");
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
    }

    private static void VerifySpawnWindowDraw()
    {
        GridMapSpawnWindow spawnWindow = null;
        var source = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        source.Initialize(24, 16);
        source.Spawns.Add(new MapSpawnDefinition
        {
            DisplayName = "UI spawn", Cell = new Vector2Int(3, 4), Count = 3,
            Spacing = 2f, Kind = MapSpawnKind.Npc, MaxHealth = 75f, RespawnSeconds = 5f
        });
        string before = JsonUtility.ToJson(source);
        try
        {
            spawnWindow = ScriptableObject.CreateInstance<GridMapSpawnWindow>();
            FieldInfo field = typeof(GridMapSpawnWindow).GetField("map", BindingFlags.Instance | BindingFlags.NonPublic);
            True(field != null, "Spawn window map field exists");
            field.SetValue(spawnWindow, source);
            spawnWindow.Show();
            spawnWindow.position = new Rect(140, 100, 600, 900);
            spawnWindow.Focus();
            spawnWindow.SendEvent(new Event { type = EventType.Layout });
            spawnWindow.SendEvent(new Event { type = EventType.Repaint });
            Equal(before, JsonUtility.ToJson(source), "Drawing the populated spawn configuration does not mutate data");
            field.SetValue(spawnWindow, null);
            spawnWindow.SendEvent(new Event { type = EventType.Layout });
            spawnWindow.SendEvent(new Event { type = EventType.Repaint });
        }
        finally
        {
            if (spawnWindow != null) spawnWindow.Close();
            if (spawnWindow != null) Object.DestroyImmediate(spawnWindow);
            if (window != null) window.Focus();
        }
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
    }

    private static void VerifySpawnPlacement()
    {
        var definition = new MapSpawnDefinition { DisplayName = "Native placement", Cell = new Vector2Int(1, 1), Count = 2 };
        string id = definition.Id;
        map.Spawns.Add(definition);
        MapTileType[] before = CaptureCells(map);
        Vector2Int destination = new Vector2Int(7, 5);
        try
        {
            SetField("structurePlacement", false);
            SetField("placingSpawnId", id);
            Undo.IncrementCurrentGroup();
            SendGuiEvent(EventType.Layout);
            SendGuiEvent(EventType.Repaint);
            Click(destination, 0);
            Undo.FlushUndoRecordObjects();
            Equal(destination, map.Spawns.Find(spawn => spawn.Id == id).Cell, "Native canvas click moves the selected spawn marker");
            True(string.IsNullOrEmpty((string)GetField("placingSpawnId")), "Placement ends after a successful click");
            AssertCells(map, before, "Spawn placement never paints or erases either tile layer");
            Undo.PerformUndo();
            Equal(new Vector2Int(1, 1), map.Spawns.Find(spawn => spawn.Id == id).Cell, "Undo restores the original spawn position and identity");
            Undo.PerformRedo();
            Equal(destination, map.Spawns.Find(spawn => spawn.Id == id).Cell, "Redo restores the position using the same identity");
            SetField("placingSpawnId", id);
            Click(new Vector2Int(8, 6), 1);
            True(string.IsNullOrEmpty((string)GetField("placingSpawnId")), "Right-click cancels spawn placement");
            Equal(destination, map.Spawns.Find(spawn => spawn.Id == id).Cell, "Cancellation does not move the spawn");
            AssertCells(map, before, "Cancelling spawn placement preserves tiles");
        }
        finally
        {
            SetField("placingSpawnId", null);
            map.Spawns.RemoveAll(spawn => spawn != null && spawn.Id == id);
            Undo.ClearUndo(map);
            SendGuiEvent(EventType.Layout);
            SendGuiEvent(EventType.Repaint);
        }
    }

    private static void VerifyStructureClick()
    {
        MapStructureTemplate template = CreateStructureTemplate();
        MapTileType[] original = CaptureCells(map);
        MapTileType[] source = CaptureCells(template.Layout);
        Vector2Int origin = new Vector2Int(0, 8);
        try
        {
            ConfigureStructureLibrary(template, true);
            Click(origin, 0);
            Undo.FlushUndoRecordObjects();
            AssertStructureArea(template, origin, false);
            AssertCells(template.Layout, source, "Click placement does not change the template");
            MapTileType[] placed = CaptureCells(map);
            Undo.PerformUndo();
            AssertCells(map, original, "One Undo removes the complete house and restores previous map contents");
            Undo.PerformRedo();
            AssertCells(map, placed, "One Redo restores both layers of the complete house");
        }
        finally { ResetStructureLibrary(); }
    }

    private static void VerifyStructureDragDrop()
    {
        MapStructureTemplate template = CreateStructureTemplate();
        MapTileType[] source = CaptureCells(template.Layout);
        Object[] previousDragObjects = DragAndDrop.objectReferences;
        try
        {
            ConfigureStructureLibrary(template, false);
            Vector2Int origin = new Vector2Int(15, 5);
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { template };
            SendMouse(EventType.DragUpdated, origin, 0);
            Equal(DragAndDropVisualMode.Copy, DragAndDrop.visualMode, "The real canvas accepts the template drag payload");
            SendMouse(EventType.DragPerform, origin, 0);
            SendGuiEvent(EventType.Layout);
            SendGuiEvent(EventType.Repaint);
            AssertStructureArea(template, origin, false);
            Equal(0, DragAndDrop.objectReferences.Length, "The accepted native drop consumes its payload");
            AssertCells(template.Layout, source, "Native drag placement preserves template data");

            MapTileType[] beforeRejectedDrop = CaptureCells(map);
            Vector2Int invalidOrigin = new Vector2Int(map.Width - 1, map.Height - 1);
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { template };
            SendMouse(EventType.DragUpdated, invalidOrigin, 0);
            Equal(DragAndDropVisualMode.Rejected, DragAndDrop.visualMode, "An out-of-bounds native drag shows rejection");
            SendMouse(EventType.DragPerform, invalidOrigin, 0);
            AssertCells(map, beforeRejectedDrop, "An out-of-bounds drop cannot partially place a house");
            AssertCells(template.Layout, source, "A rejected drop leaves its source unchanged");
        }
        finally
        {
            DragAndDrop.objectReferences = previousDragObjects;
            ResetStructureLibrary();
        }
    }

    private static MapStructureTemplate CreateStructureTemplate()
    {
        MapStructureTemplate template = Track(ScriptableObject.CreateInstance<MapStructureTemplate>());
        template.DisplayName = "Temporary UI house";
        GridMapAsset layout = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        layout.Initialize(3, 3);
        for (int x = 0; x < 3; x++) layout.SetCell(x, 0, ground);
        layout.SetCell(0, 2, ground);
        layout.SetCell(2, 2, ground);
        layout.SetCell(1, 1, marker, MapLayer.Objects);
        layout.SetCell(2, 1, mutableType, MapLayer.Objects);
        template.Layout = layout;
        return template;
    }

    private static void ConfigureStructureLibrary(MapStructureTemplate template, bool placement)
    {
        SetTool("Brush");
        SetField("selectedStructure", template);
        SetField("structurePlacement", placement);
        SetField("structureFlipX", false);
        SetField("structureReplaceEmpty", true);
        SetField("structureLibraryTab", 1);
        SetField("structures", new List<MapStructureTemplate> { template });
        window.Focus();
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
    }

    private static void ResetStructureLibrary()
    {
        SetField("structurePlacement", false);
        SetField("selectedStructure", null);
        SetField("structureLibraryTab", 0);
        SetField("structures", null);
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
    }

    private static MapTileType[] CaptureCells(GridMapAsset asset)
    {
        MapTileType[] cells = new MapTileType[asset.CellCount * 2];
        for (int y = 0; y < asset.Height; y++)
            for (int x = 0; x < asset.Width; x++)
            {
                int index = x + y * asset.Width;
                cells[index] = asset.GetCell(x, y);
                cells[index + asset.CellCount] = asset.GetCell(x, y, MapLayer.Objects);
            }
        return cells;
    }

    private static void AssertCells(GridMapAsset asset, MapTileType[] cells, string message)
    {
        for (int y = 0; y < asset.Height; y++)
            for (int x = 0; x < asset.Width; x++)
            {
                int index = x + y * asset.Width;
                Same(cells[index], asset.GetCell(x, y), message + " (terrain)");
                Same(cells[index + asset.CellCount], asset.GetCell(x, y, MapLayer.Objects), message + " (objects)");
            }
    }

    private static void AssertStructureArea(MapStructureTemplate template, Vector2Int origin, bool flipX)
    {
        for (int y = 0; y < template.Height; y++)
            for (int x = 0; x < template.Width; x++)
            {
                int sourceX = flipX ? template.Width - 1 - x : x;
                Same(template.Layout.GetCell(sourceX, y), map.GetCell(origin.x + x, origin.y + y), "Native placement copies exact terrain coordinates");
                Same(template.Layout.GetCell(sourceX, y, MapLayer.Objects), map.GetCell(origin.x + x, origin.y + y, MapLayer.Objects),
                    "Native placement copies exact object coordinates");
            }
    }

    private static void SetRandomField(GridMapRandomWindow target, string name, object value)
    {
        FieldInfo field = typeof(GridMapRandomWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(typeof(GridMapRandomWindow).FullName, name);
        field.SetValue(target, value);
    }

    private static void Select(MapTileType type, MapLayer targetLayer)
    {
        SetField("selectedType", type);
        SetField("layer", targetLayer);
        SetTool("Brush");
        SendGuiEvent(EventType.Layout);
        SendGuiEvent(EventType.Repaint);
    }

    private static void Click(Vector2Int cell, int button)
    {
        SendMouse(EventType.MouseDown, cell, button);
        SendMouse(EventType.MouseUp, cell, button);
        SendGuiEvent(EventType.Repaint);
    }

    private static void Drag(Vector2Int from, Vector2Int to)
    {
        SendMouse(EventType.MouseDown, from, 0);
        SendMouse(EventType.MouseDrag, to, 0, CellPixel(to) - CellPixel(from));
        SendMouse(EventType.MouseUp, to, 0);
        SendGuiEvent(EventType.Repaint);
    }

    private static Vector2 CellPixel(Vector2Int cell)
    {
        Vector2 pan = (Vector2)GetField("pan");
        float pixels = (float)GetField("pixels");
        // Window.DrawCanvas starts at (213, 48). Its local Y increases downwards.
        return new Vector2(213f + pan.x + (cell.x + 0.5f) * pixels,
            48f + pan.y + (map.Height - cell.y - 0.5f) * pixels) + mouseInputOffset;
    }

    private static void SendMouse(EventType eventType, Vector2Int cell, int button, Vector2 delta = default(Vector2))
    {
        window.SendEvent(new Event
        {
            type = eventType,
            mousePosition = CellPixel(cell),
            button = button,
            clickCount = 1,
            delta = delta
        });
    }

    private static void SendGuiEvent(EventType type)
    {
        window.SendEvent(new Event { type = type });
    }

    private static int Count(MapLayer targetLayer)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.GetCell(x, y, targetLayer) != null) count++;
        return count;
    }

    private static void Check(string name, Action action)
    {
        int errorsBefore = report.guiErrors.Count;
        try
        {
            action();
            ThrowIfNewGuiErrors(errorsBefore);
            report.passed++;
            report.results.Add(new CaseResult { name = name, status = "passed", detail = string.Empty });
        }
        catch (Exception exception)
        {
            AddFailure(name, exception);
        }
    }

    private static void AddFailure(string name, Exception exception)
    {
        report.failed++;
        report.results.Add(new CaseResult { name = name, status = "failed", detail = exception.ToString() });
    }

    private static void CaptureGuiError(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            report.guiErrors.Add(condition + "\n" + stackTrace);
    }

    private static void ThrowIfNewGuiErrors(int previousCount)
    {
        if (report.guiErrors.Count > previousCount)
            throw new InvalidOperationException("Unity logged an error during native window interaction: " + report.guiErrors[previousCount]);
    }

    private static Dictionary<int, bool> CaptureSceneDirtyState()
    {
        Dictionary<int, bool> states = new Dictionary<int, bool>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            states.Add(scene.handle, scene.isDirty);
        }
        return states;
    }

    private static void VerifySceneDirtyState(Dictionary<int, bool> expected)
    {
        Equal(expected.Count, SceneManager.sceneCount, "Window verification does not create or close scenes");
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            True(expected.ContainsKey(scene.handle), "Existing scene identity remains unchanged");
            Equal(expected[scene.handle], scene.isDirty, "Existing scene dirty state remains unchanged");
        }
    }

    private static MapTileType CreateType(string name, MapLayer targetLayer)
    {
        MapTileType type = Track(ScriptableObject.CreateInstance<MapTileType>());
        type.name = name;
        type.DisplayName = name;
        type.Layer = targetLayer;
        type.Description = "Temporary UI verification type";
        return type;
    }

    private static T Track<T>(T item) where T : Object
    {
        TemporaryObjects.Add(item);
        return item;
    }

    private static void SetTool(string name)
    {
        FieldInfo field = Field("tool");
        field.SetValue(window, Enum.Parse(field.FieldType, name));
    }

    private static void SetField(string name, object value) { Field(name).SetValue(window, value); }
    private static object GetField(string name) { return Field(name).GetValue(window); }

    private static FieldInfo Field(string name)
    {
        FieldInfo field = typeof(GridMapEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(typeof(GridMapEditorWindow).FullName, name);
        return field;
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapWindowReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/window-verification.json"));
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

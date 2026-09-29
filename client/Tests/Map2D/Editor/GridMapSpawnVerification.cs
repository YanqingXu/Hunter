using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Focused Edit Mode checks in an isolated Unity batch project, without NUnit.
/// -batchmode -nographics -quit -executeMethod GridMapSpawnVerification.RunBatch
/// -mapSpawnReport <json path>. Only this run's unique asset folder is created/removed.
/// Native window drawing and actual canvas placement Undo are covered separately by
/// GridMapWindowVerification.RunBatch, using -mapWindowReport.
/// </summary>
public static class GridMapSpawnVerification
{
    private const string FolderPrefix = "Assets/__Map2DSpawnVerification_";
    [Serializable] private sealed class Result { public string name; public bool passed; public string error; }
    [Serializable] private sealed class Report
    {
        public string generatedUtc;
        public string unityVersion;
        public int passed;
        public int failed;
        public bool temporaryAssetsCleaned;
        public List<Result> results = new List<Result>();
    }

    private static string folder;
    private static Report report;
    private static readonly List<Object> temporary = new List<Object>();
    private static GridMapAsset source;
    private static MapTileType ground;
    private static GameObject prefab;

    public static void RunBatch()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run this verification in an isolated Edit Mode batch process.");
        report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        folder = FolderPrefix + Guid.NewGuid().ToString("N");
        temporary.Clear();
        try
        {
            Prepare();
            Check("Spawn definition cloning separates identity and mutable configuration", VerifyDefinitionClone);
            Check("Map Save As gives a new identity and independent spawn records while sharing resource assets", VerifyMapCopy);
            Check("Serialized spawn settings support Undo and Redo without changing stable identity", VerifySpawnUndo);
            Check("Spawn validation repairs duplicate and missing identities without changing valid identities", VerifySpawnIdentityRepair);
            Check("Project asset duplication repairs map identity while moves and transient clones keep it", VerifyProjectCopyAndMove);
            Check("Copied template layouts use distinct persistent identities including multiple subassets", VerifySubassetIdentity);
            Check("Session JSON accepts repeated references to one tile type and rejects another map", VerifySessionIdentity);
            Check("Duplicate and empty tile identifiers reject save and load without partial state changes", VerifyAmbiguousTileIds);
            Check("Build scene stripping removes only streamed preview tiles and preserves authored data, legacy maps, and user nodes", VerifyBuildPreviewStripping);
        }
        catch (Exception exception) { AddResult("Prepare focused editor verification", false, exception.ToString()); }
        finally
        {
            try
            {
                if (source != null) Undo.ClearUndo(source);
                foreach (Object item in temporary) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
                temporary.Clear();
                Guid token;
                Assert(folder.StartsWith(FolderPrefix, StringComparison.Ordinal) && Guid.TryParseExact(folder.Substring(FolderPrefix.Length), "N", out token),
                    "Refusing cleanup outside this run's unique folder.");
                string root = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
                Assert(path.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Cleanup path escaped Assets.");
                if (AssetDatabase.IsValidFolder(folder)) Assert(AssetDatabase.DeleteAsset(folder), "Could not remove temporary assets.");
                report.temporaryAssetsCleaned = !AssetDatabase.IsValidFolder(folder);
                AddResult("Remove only this run's temporary assets", report.temporaryAssetsCleaned, null);
            }
            catch (Exception exception) { AddResult("Clean up focused editor verification", false, exception.ToString()); }
            string reportPath = GetReportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D spawn/identity verification: " + report.passed + " passed, " + report.failed + " failed. " + reportPath);
        }
        if (report.failed > 0) throw new InvalidOperationException("Focused map spawn verification failed; see the JSON report.");
    }

    private static void Prepare()
    {
        Assert(!AssetDatabase.IsValidFolder(folder), "Temporary folder already exists.");
        Assert(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7))), "Could not create temporary folder.");
        var prefabSource = new GameObject("Spawn Verification Prefab");
        try { prefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, folder + "/Actor.prefab"); }
        finally { Object.DestroyImmediate(prefabSource); }
        Assert(prefab != null, "Could not save temporary prefab.");
        ground = ScriptableObject.CreateInstance<MapTileType>();
        ground.Id = "focused-ground";
        ground.Layer = MapLayer.Terrain;
        ground.Collision = MapTileCollisionMode.Solid;
        AssetDatabase.CreateAsset(ground, folder + "/Ground.asset");
        source = ScriptableObject.CreateInstance<GridMapAsset>();
        source.Initialize(16, 12, 1.5f);
        source.Origin = new Vector2(-2f, 3f);
        source.SetCell(1, 1, ground);
        source.SetCell(2, 1, ground);
        source.Spawns.Add(NewDefinition());
        string beforeRegistration = source.Id;
        AssetDatabase.CreateAsset(source, folder + "/Source.asset");
        GridMapAssetIdentity.EnsureIdentity(source);
        Assert(source.Id == beforeRegistration, "First persistent registration must preserve the original map identity.");
    }

    private static MapSpawnDefinition NewDefinition()
    {
        return new MapSpawnDefinition
        {
            DisplayName = "NPC patrol", Prefab = prefab, Cell = new Vector2Int(3, 4), Count = 3,
            Spacing = 2.5f, Kind = MapSpawnKind.Npc, MaxHealth = 75f, RespawnSeconds = 8f, Enabled = true
        };
    }

    private static void VerifyDefinitionClone()
    {
        MapSpawnDefinition original = NewDefinition();
        MapSpawnDefinition copy = original.Clone();
        Assert(!ReferenceEquals(original, copy), "Clone returned the original object.");
        Assert(!string.IsNullOrEmpty(copy.Id) && copy.Id != original.Id, "A copied spawn must have its own identity.");
        Assert(copy.Prefab == original.Prefab && copy.Cell == original.Cell && copy.Count == original.Count &&
            Mathf.Approximately(copy.Spacing, original.Spacing) && copy.Kind == original.Kind &&
            Mathf.Approximately(copy.MaxHealth, original.MaxHealth) && Mathf.Approximately(copy.RespawnSeconds, original.RespawnSeconds) && copy.Enabled,
            "Clone lost authoring configuration or changed the shared prefab.");
        copy.Cell = new Vector2Int(8, 9); copy.Count = 1; copy.DisplayName = "Independent"; copy.Enabled = false;
        Assert(original.Cell == new Vector2Int(3, 4) && original.Count == 3 && original.DisplayName == "NPC patrol" && original.Enabled,
            "Changing the cloned definition mutated the original.");
        Assert(original.Clone(false).Id == original.Id, "Explicit identity-preserving clone changed the ID.");
    }

    private static void VerifyMapCopy()
    {
        string sourceId = source.Id;
        string sourceSpawnId = source.Spawns[0].Id;
        string path = folder + "/SaveAs.asset";
        GridMapAsset copy = GridMapAssets.CreateMapCopy(path, source);
        GridMapAssetIdentity.EnsureIdentity(copy);
        Assert(copy.Id != sourceId && source.Id == sourceId, "Save As failed to create an independent map identity.");
        Assert(copy.Width == source.Width && copy.Height == source.Height && copy.CellSize == source.CellSize && copy.Origin == source.Origin,
            "Save As lost map geometry.");
        Assert(copy.GetCell(1, 1) == ground && copy.Spawns.Count == 1 && copy.Spawns[0].Prefab == prefab, "Save As lost shared resource references.");
        Assert(!ReferenceEquals(copy.Spawns, source.Spawns) && !ReferenceEquals(copy.Spawns[0], source.Spawns[0]), "Spawn records are shared between independent maps.");
        // Spawn identities are scoped by the newly independent map, so their stable local IDs can stay intact.
        Assert(copy.Spawns[0].Id == sourceSpawnId, "Map copy unexpectedly changed a spawn's local identity.");
        copy.Spawns[0].Count = 7;
        copy.Spawns[0].Cell = new Vector2Int(9, 2);
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        Assert(source.Spawns[0].Count == 3 && source.Spawns[0].Cell == new Vector2Int(3, 4), "Editing copied spawn records mutated the source map.");
        string copyId = copy.Id;
        Resources.UnloadAsset(copy);
        copy = AssetDatabase.LoadAssetAtPath<GridMapAsset>(path);
        Assert(copy.Id == copyId && copy.Spawns[0].Id == sourceSpawnId && copy.Spawns[0].Count == 7, "Identity or spawn changes did not survive a real asset reload.");
        string before = File.ReadAllText(AssetFile(path));
        Throws<InvalidOperationException>(() => GridMapAssets.CreateMapCopy(path, source));
        Assert(File.ReadAllText(AssetFile(path)) == before, "Rejected overwrite changed an existing map file.");
    }

    private static void VerifySpawnUndo()
    {
        Undo.ClearUndo(source);
        string id = source.Spawns[0].Id;
        Vector2Int cell = source.Spawns[0].Cell;
        int count = source.Spawns[0].Count;
        Undo.IncrementCurrentGroup();
        var data = new SerializedObject(source);
        data.Update();
        SerializedProperty spawn = data.FindProperty("spawns").GetArrayElementAtIndex(0);
        spawn.FindPropertyRelative("count").intValue = 6;
        spawn.FindPropertyRelative("cell").vector2IntValue = new Vector2Int(8, 6);
        Assert(data.ApplyModifiedProperties(), "Serialized spawn edit was not applied.");
        Undo.FlushUndoRecordObjects();
        Assert(source.Spawns[0].Count == 6 && source.Spawns[0].Cell == new Vector2Int(8, 6), "Serialized editing did not change the spawn.");
        Undo.PerformUndo();
        Assert(source.Spawns[0].Id == id && source.Spawns[0].Count == count && source.Spawns[0].Cell == cell, "Undo did not restore spawn values and stable ID.");
        Undo.PerformRedo();
        Assert(source.Spawns[0].Id == id && source.Spawns[0].Count == 6 && source.Spawns[0].Cell == new Vector2Int(8, 6), "Redo lost spawn identity or edited values.");
        Undo.PerformUndo();
        Undo.ClearUndo(source);
        EditorUtility.SetDirty(source);
        AssetDatabase.SaveAssetIfDirty(source);
    }

    private static void VerifySpawnIdentityRepair()
    {
        var map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(8, 8);
        var first = NewDefinition();
        var duplicate = first.Clone(false);
        var missing = NewDefinition(); missing.Id = null;
        map.Spawns.Add(first); map.Spawns.Add(duplicate); map.Spawns.Add(missing);
        string firstId = first.Id;
        map.ValidateSpawns();
        var ids = new HashSet<string>();
        foreach (var spawn in map.Spawns) Assert(!string.IsNullOrWhiteSpace(spawn.Id) && ids.Add(spawn.Id), "Validation left a missing or duplicate spawn ID.");
        Assert(first.Id == firstId, "Repair unnecessarily changed an already valid identity.");
        string before = JsonUtility.ToJson(map);
        map.ValidateSpawns();
        Assert(JsonUtility.ToJson(map) == before, "Validating twice changed stable identities.");
    }

    private static void VerifyProjectCopyAndMove()
    {
        string sourceId = source.Id;
        string path = folder + "/ProjectDuplicate.asset";
        Assert(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path), "Could not duplicate map using AssetDatabase.CopyAsset.");
        var copy = AssetDatabase.LoadAssetAtPath<GridMapAsset>(path);
        GridMapAssetIdentity.EnsureIdentity(copy);
        Assert(copy.Id != sourceId && source.Id == sourceId, "Project duplicate reused the source map identity.");
        string copyId = copy.Id;
        Assert(!GridMapAssetIdentity.EnsureIdentity(copy), "A second identity synchronization should be a no-op.");
        string moved = folder + "/RenamedDuplicate.asset";
        Assert(string.IsNullOrEmpty(AssetDatabase.MoveAsset(path, moved)), "Could not rename temporary copied map.");
        GridMapAssetIdentity.EnsureIdentity(copy);
        Assert(copy.Id == copyId, "Moving or renaming changed the map identity.");
        var transient = Track(Object.Instantiate(source));
        string transientId = transient.Id;
        Assert(!GridMapAssetIdentity.EnsureIdentity(transient) && transient.Id == transientId && transientId == sourceId,
            "Transient runtime clones must not receive persistent-asset identity changes.");
        string sourceJson = new GridMapRuntimeState(source).ToJson();
        Throws<ArgumentException>(() => new GridMapRuntimeState(copy).LoadJson(sourceJson));
    }

    private static void VerifySubassetIdentity()
    {
        var template = ScriptableObject.CreateInstance<MapStructureTemplate>();
        string path = folder + "/Template.asset";
        AssetDatabase.CreateAsset(template, path);
        var layout = Object.Instantiate(source);
        layout.name = "FirstLayout";
        AssetDatabase.AddObjectToAsset(layout, template);
        template.Layout = layout;
        GridMapAssetIdentity.EnsureIdentity(layout, false);
        Assert(layout.Id != source.Id, "Persistent layout copied from a map kept the source identity.");
        var second = Object.Instantiate(layout);
        second.name = "SecondLayout";
        AssetDatabase.AddObjectToAsset(second, template);
        GridMapAssetIdentity.EnsureIdentity(second, false);
        Assert(second.Id != layout.Id, "Two layout subassets with different local file IDs share one identity.");
        EditorUtility.SetDirty(template);
        AssetDatabase.SaveAssetIfDirty(template);
        string layoutId = layout.Id;
        string duplicatePath = folder + "/TemplateDuplicate.asset";
        Assert(AssetDatabase.CopyAsset(path, duplicatePath), "Could not duplicate the template asset.");
        var copy = AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(duplicatePath);
        Assert(copy != null && copy.Layout != null, "Copied template lost its layout subasset.");
        GridMapAssetIdentity.EnsureIdentity(copy.Layout);
        Assert(copy.Layout.Id != layoutId && layout.Id == layoutId, "Copied template layout reused the source layout identity.");
        string copiedLayoutId = copy.Layout.Id;
        Assert(string.IsNullOrEmpty(AssetDatabase.MoveAsset(duplicatePath, folder + "/MovedTemplate.asset")), "Could not move copied template.");
        GridMapAssetIdentity.EnsureIdentity(copy.Layout);
        Assert(copy.Layout.Id == copiedLayoutId, "Moving a template changed its layout identity.");
    }

    private static void VerifySessionIdentity()
    {
        var state = new GridMapRuntimeState(source);
        state.SetCell(1, 1, null);
        state.GetEntity("spawn:" + source.Spawns[0].Id + ":0", new Vector3(2, 3, 0), 75f).Health = 42f;
        string json = state.ToJson(); // Ground is referenced repeatedly in the source; this is valid.
        var restored = new GridMapRuntimeState(source);
        restored.LoadJson(json, new[] { ground, ground });
        Assert(restored.GetCell(1, 1) == null && restored.GetCell(2, 1) == ground, "Repeated references to the same type were rejected or restored incorrectly.");
        Assert(source.GetCell(1, 1) == ground, "Session restore changed authored cells.");
        var otherMap = Track(Object.Instantiate(source)); otherMap.NewIdentity();
        string before = restored.ToJson();
        Throws<ArgumentException>(() => restored.LoadJson(new GridMapRuntimeState(otherMap).ToJson()));
        Assert(restored.ToJson() == before, "Wrong-map rejection partially changed existing session state.");
    }

    private static void VerifyAmbiguousTileIds()
    {
        var map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(4, 3);
        var first = Track(ScriptableObject.CreateInstance<MapTileType>()); first.Id = "type-a";
        var second = Track(ScriptableObject.CreateInstance<MapTileType>()); second.Id = "type-b";
        map.SetCell(0, 0, first); map.SetCell(1, 0, second); map.SetCell(2, 0, first);
        var state = new GridMapRuntimeState(map);
        state.SetCell(0, 0, null);
        MapEntityState actor = state.GetEntity("stable", new Vector3(2, 2, 0), 100f); actor.Health = 37f;
        string valid = state.ToJson();
        int restoredEvents = 0;
        state.StateRestored += () => restoredEvents++;
        foreach (string invalidId in new[] { "type-a", "", "   " })
        {
            second.Id = invalidId;
            Throws<InvalidOperationException>(() => state.ToJson());
            Throws<InvalidOperationException>(() => state.LoadJson(valid));
            Assert(state.GetCell(0, 0) == null && state.GetCell(1, 0) == second &&
                ReferenceEquals(state.GetEntity("stable", Vector3.zero, 100f), actor) && actor.Health == 37f && restoredEvents == 0,
                "A rejected type identifier changed cells, entities, or fired StateRestored.");
            second.Id = "type-b";
            Assert(state.ToJson() == valid, "Rejected save/load altered the serialized session after identifiers were repaired.");
        }
        var extra = Track(ScriptableObject.CreateInstance<MapTileType>()); extra.Id = first.Id;
        Throws<InvalidOperationException>(() => state.LoadJson(valid, new[] { extra }));
        Assert(state.ToJson() == valid && restoredEvents == 0, "Ambiguous additional type lookup partially replaced session state.");
        state.LoadJson(valid, new[] { first, first, second });
        Assert(restoredEvents == 1 && state.GetCell(0, 0) == null, "Repeated references to one type should load successfully.");
    }

    private static void VerifyBuildPreviewStripping()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var streaming = CreateSceneRenderer(scene, "Streaming build preview", true);
            var legacy = CreateSceneRenderer(scene, "Legacy build preview", false);
            Tilemap streamedGround = streaming.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            Tilemap legacyGround = legacy.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            Assert(streamedGround != null && streamedGround.GetUsedTilesCount() > 0, "Streaming preview was not built for the stripping test.");
            Assert(legacyGround != null && legacyGround.GetUsedTilesCount() > 0, "Legacy preview was not built for the stripping test.");
            var userGrid = new GameObject("User-owned grid", typeof(Grid));
            SceneManager.MoveGameObjectToScene(userGrid, scene);
            userGrid.transform.SetParent(streaming.transform, false);
            var user = new GameObject("User-owned tilemap", typeof(Tilemap), typeof(TilemapRenderer));
            SceneManager.MoveGameObjectToScene(user, scene);
            user.transform.SetParent(userGrid.transform, false);
            var userTiles = user.GetComponent<Tilemap>();
            userTiles.SetTile(Vector3Int.zero, ground);
            var userChild = new GameObject("User-owned gameplay node");
            SceneManager.MoveGameObjectToScene(userChild, scene);
            userChild.transform.SetParent(streaming.transform, false);
            int streamingNodes = streaming.GetComponentsInChildren<Transform>(true).Length;
            string sourceBefore = EditorJsonUtility.ToJson(source);
            string sourceFileBefore = File.ReadAllText(AssetFile(AssetDatabase.GetAssetPath(source)));
            int processed = GridMapBuildSceneProcessor.StripScenePreview(scene);
            Assert(processed == 6, "Expected only the six owned streamed Tilemaps to be processed; actual=" + processed);
            foreach (MapGeneratedPart part in streaming.GetComponentsInChildren<MapGeneratedPart>(true))
            {
                Tilemap tilemap = part.GetComponent<Tilemap>();
                if (tilemap == null) continue;
                Assert(tilemap.GetUsedTilesCount() == 0, "An owned streamed Tilemap still contains preview tiles.");
                var collider = tilemap.GetComponent<TilemapCollider2D>();
                Assert(collider == null || !collider.hasTilemapChanges, "Collider changes were not committed during stripping.");
            }
            Assert(legacyGround.GetTile(new Vector3Int(1, 1, 0)) == ground, "Legacy non-streaming preview was stripped.");
            Assert(userTiles != null && userTiles.GetTile(Vector3Int.zero) == ground && userChild != null,
                "User-authored nodes or tiles were removed.");
            Assert(streaming.GetComponentsInChildren<Transform>(true).Length == streamingNodes && streaming.IsPreviewCleared,
                "Stripping changed scene hierarchy or failed to suppress queued preview rebuilds.");
            Assert(EditorJsonUtility.ToJson(source) == sourceBefore && File.ReadAllText(AssetFile(AssetDatabase.GetAssetPath(source))) == sourceFileBefore,
                "Build preparation changed the map source asset.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static GridMapRenderer CreateSceneRenderer(Scene scene, string name, bool streaming)
    {
        var host = new GameObject(name);
        SceneManager.MoveGameObjectToScene(host, scene);
        var result = host.AddComponent<GridMapRenderer>();
        result.StreamingEnabled = streaming;
        result.Map = source;
        return result;
    }

    private static T Track<T>(T item) where T : Object { temporary.Add(item); return item; }
    private static string AssetFile(string path) { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)); }
    private static void Check(string name, Action action)
    {
        try { action(); AddResult(name, true, null); }
        catch (Exception exception) { AddResult(name, false, exception.ToString()); }
    }
    private static void AddResult(string name, bool passed, string error)
    {
        report.results.Add(new Result { name = name, passed = passed, error = error });
        if (passed) report.passed++; else report.failed++;
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string GetReportPath()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-mapSpawnReport") return Path.GetFullPath(args[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/Streaming/spawn-editor.json"));
    }
}

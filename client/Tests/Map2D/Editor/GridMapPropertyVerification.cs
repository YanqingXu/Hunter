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
/// Copy this file into an isolated validation project's Assets/.../Editor folder,
/// alongside the current Map2D product scripts. No NUnit package is required.
/// Run Unity with -batchmode -quit -executeMethod GridMapPropertyVerification.RunBatch
/// -mapPropertyReport <absolute JSON path>. No graphics device is required.
/// All test schemas, types and maps are temporary; existing assets and scenes are not edited.
/// </summary>
public static class GridMapPropertyVerification
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

    private static readonly List<Object> TemporaryObjects = new List<Object>();
    private static string temporaryAssetFolder;
    private static Report report;

    public static void RunBatch()
    {
        report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        temporaryAssetFolder = "Assets/__Map2DPropertyVerification_" + Guid.NewGuid().ToString("N");
        try
        {
            Check("Built-in properties are false by default and initialize without duplication", VerifyBuiltIns);
            Check("All four property kinds resolve schema defaults with typed access", VerifySchemaDefaults);
            Check("False, zero and empty text are explicit per-type overrides", VerifyExplicitEmptyOverrides);
            Check("Destructible and flammable flags are independent type metadata", VerifyBuiltInOverrides);
            Check("Missing schemas and removed definitions ignore orphaned overrides", VerifyMissingDefinitions);
            Check("Changing a definition kind ignores the previous kind's override", VerifyKindChanges);
            Check("Default changes reach unmodified types while preserving overrides", VerifyChangedDefaults);
            Check("Keys and duplicate entries follow deterministic identity rules", VerifyKeyResolution);
            Check("Override helpers copy inputs, reject invalid edits and reset to defaults", VerifyOverrideHelpers);
            Check("Editor-added definitions reach existing types and reject duplicate or invalid keys", VerifyEditorAddDefinition);
            Check("Editor SaveType persists schema and type edits through actual asset reload", VerifySaveReload);
            Check("Window Save without a map persists the selected type and its schema", VerifyWindowSaveWithoutMap);
            Check("Per-type property edits undo and redo without changing other types", VerifyTypeUndoRedo);
            Check("Schema edits undo and redo including definition kind changes", VerifySchemaUndoRedo);
        }
        finally
        {
            try
            {
                foreach (Object temporary in TemporaryObjects)
                    if (temporary != null) Undo.ClearUndo(temporary);
                if (AssetDatabase.IsValidFolder(temporaryAssetFolder)) AssetDatabase.DeleteAsset(temporaryAssetFolder);
                foreach (Object temporary in TemporaryObjects)
                    if (temporary != null && !EditorUtility.IsPersistent(temporary)) Object.DestroyImmediate(temporary);
                TemporaryObjects.Clear();
                Undo.IncrementCurrentGroup();
            }
            catch (Exception exception)
            {
                report.failed++;
                report.results.Add(new CaseResult { name = "Temporary asset cleanup", passed = false, error = exception.ToString() });
            }
            string path = GetReportPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Map2D property verification: " + report.passed + " passed, " + report.failed + " failed. " + path);
        }
        if (report.failed != 0)
            throw new InvalidOperationException("Map2D property verification failed. See the JSON report.");
    }

    private static void VerifyBuiltIns()
    {
        MapTilePropertySchema schema = Schema();
        Equal(0, schema.Definitions.Count, "Creating a schema does not silently populate asset data");
        True(schema.EnsureBuiltInDefinitions(), "First explicit initialization adds missing built-ins");
        Equal(2, schema.Definitions.Count, "Exactly two built-in definitions are added");
        Equal("destructible", MapTilePropertyKeys.Destructible, "Destructible key remains stable");
        Equal("flammable", MapTilePropertyKeys.Flammable, "Flammable key remains stable");
        foreach (string key in new[] { MapTilePropertyKeys.Destructible, MapTilePropertyKeys.Flammable })
        {
            MapTilePropertyDefinition definition;
            True(schema.TryGetDefinition(key, out definition), "Built-in key is discoverable: " + key);
            Equal(MapTilePropertyKind.Boolean, definition.Kind, "Built-ins are Boolean");
            True(!definition.BoolValue, "Built-ins default to false");
        }
        True(!schema.EnsureBuiltInDefinitions(), "Repeated built-in initialization reports no changes");
        Equal(2, schema.Definitions.Count, "Repeated initialization creates no duplicates");
        MapTilePropertyDefinition destructible;
        schema.TryGetDefinition(MapTilePropertyKeys.Destructible, out destructible);
        destructible.BoolValue = true;
        True(!schema.EnsureBuiltInDefinitions(), "Existing configured definitions are preserved");
        True(destructible.BoolValue, "Ensuring built-ins does not reset a user's chosen default");
    }

    private static void VerifySchemaDefaults()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType tile = Type("schema defaults", schema);
        True(tile.GetBool("enabled"), "Boolean schema default");
        Equal(12, tile.GetInt("durability"), "Integer schema default");
        Equal(2.5f, tile.GetNumber("weight"), "Number schema default");
        Equal("wood", tile.GetText("material"), "Text schema default");
        MapTilePropertyValue value;
        True(tile.TryGetProperty("durability", out value), "Untyped access resolves a definition");
        Equal(MapTilePropertyKind.Integer, value.Kind, "Untyped access reports the actual kind");
        True(tile.TryGetProperty("durability", MapTilePropertyKind.Integer, out value), "Typed access succeeds for the matching kind");
        Equal(12, value.IntValue, "Typed access uses the definition's value");
        True(!tile.TryGetProperty("durability", MapTilePropertyKind.Text, out value), "Wrong-kind access fails without converting values");
        Equal("fallback", tile.GetText("durability", "fallback"), "Wrong-kind getter uses its explicit fallback");
    }

    private static void VerifyExplicitEmptyOverrides()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType first = Type("overridden type", schema);
        MapTileType second = Type("unmodified type", schema);
        AddEmptyOverrides(first);
        True(!first.GetBool("enabled", true), "False overrides a true default");
        Equal(0, first.GetInt("durability", 999), "Zero overrides a nonzero integer default");
        Equal(0f, first.GetNumber("weight", 999f), "Zero overrides a nonzero number default");
        Equal(string.Empty, first.GetText("material", "fallback"), "Empty text overrides a nonempty default");
        True(second.GetBool("enabled"), "A second type keeps its Boolean default");
        Equal(12, second.GetInt("durability"), "A second type keeps its integer default");
        Equal(2.5f, second.GetNumber("weight"), "A second type keeps its number default");
        Equal("wood", second.GetText("material"), "A second type keeps its text default");
        Equal(0, second.PropertyOverrides.Count, "Reading defaults does not materialize overrides on another type");
    }

    private static void VerifyBuiltInOverrides()
    {
        MapTilePropertySchema schema = Schema();
        schema.EnsureBuiltInDefinitions();
        MapTileType barrel = Type("barrel metadata", schema);
        MapTileType ground = Type("ground metadata", schema);
        barrel.Collision = MapTileCollisionMode.Solid;
        barrel.Layer = MapLayer.Objects;
        barrel.PropertyOverrides.Add(Value(MapTilePropertyKeys.Destructible, MapTilePropertyKind.Boolean, boolValue: true));
        barrel.PropertyOverrides.Add(Value(MapTilePropertyKeys.Flammable, MapTilePropertyKind.Boolean, boolValue: true));
        True(barrel.CanBeDestroyed && barrel.CanBeIgnited, "Convenience flags reflect this type's overrides");
        True(!ground.CanBeDestroyed && !ground.CanBeIgnited, "Other types retain false defaults");
        Equal(MapTileCollisionMode.Solid, barrel.Collision, "Metadata does not change collision behavior");
        Equal(MapLayer.Objects, barrel.Layer, "Metadata does not change the map layer");
        Same(null, barrel.Prefab, "Metadata does not attach gameplay prefabs");
        barrel.PropertyOverrides.RemoveAt(0);
        True(!barrel.CanBeDestroyed && barrel.CanBeIgnited, "Resetting one property leaves the other independent");
    }

    private static void VerifyMissingDefinitions()
    {
        MapTileType legacy = Type("legacy type", null);
        legacy.PropertyOverrides.Add(Value("enabled", MapTilePropertyKind.Boolean, boolValue: true));
        legacy.PropertyOverrides.Add(Value(MapTilePropertyKeys.Destructible, MapTilePropertyKind.Boolean, boolValue: true));
        MapTilePropertyValue value;
        True(!legacy.TryGetProperty("enabled", out value), "A missing schema does not activate an orphaned override");
        True(!legacy.GetBool("enabled"), "Missing schema uses Boolean fallback");
        Equal(19, legacy.GetInt("durability", 19), "Missing schema uses integer fallback");
        Equal(3.5f, legacy.GetNumber("weight", 3.5f), "Missing schema uses number fallback");
        Equal("legacy fallback", legacy.GetText("material", "legacy fallback"), "Missing schema uses text fallback");
        True(!legacy.CanBeDestroyed && !legacy.CanBeIgnited, "Legacy types safely default the built-in flags to false");

        MapTilePropertySchema schema = StandardSchema();
        legacy.PropertySchema = schema;
        legacy.PropertyOverrides.Add(Value("durability", MapTilePropertyKind.Integer, intValue: 3));
        Equal(3, legacy.GetInt("durability"), "Defined override resolves before removal");
        schema.Definitions.RemoveAll(definition => definition != null && definition.Key == "durability");
        True(!legacy.TryGetProperty("durability", out value), "Removing a definition deactivates its old override");
        Equal(71, legacy.GetInt("durability", 71), "Removed definitions use the caller's fallback");
        True(!legacy.TryGetProperty("not-defined", out value), "Unknown keys fail cleanly");
    }

    private static void VerifyKindChanges()
    {
        MapTilePropertySchema schema = Schema();
        MapTilePropertyDefinition definition = Definition("dynamic", MapTilePropertyKind.Boolean, boolValue: true);
        schema.Definitions.Add(definition);
        MapTileType type = Type("kind change", schema);
        type.PropertyOverrides.Add(Value("dynamic", MapTilePropertyKind.Boolean, boolValue: false));
        True(!type.GetBool("dynamic", true), "Initial Boolean override is active");
        definition.Kind = MapTilePropertyKind.Integer;
        definition.IntValue = 23;
        Equal(23, type.GetInt("dynamic"), "Changing to Integer ignores the old Boolean override");
        True(type.GetBool("dynamic", true), "Old Boolean getter uses its fallback after a kind change");
        type.PropertyOverrides.Add(Value("dynamic", MapTilePropertyKind.Integer, intValue: 0));
        Equal(0, type.GetInt("dynamic", 99), "A matching new-kind override is accepted");
        definition.Kind = MapTilePropertyKind.Text;
        definition.TextValue = "new default";
        Equal("new default", type.GetText("dynamic"), "Changing to Text ignores all stale numeric and Boolean values");
        Equal(99, type.GetInt("dynamic", 99), "Stale numeric access does not leak through");
    }

    private static void VerifyChangedDefaults()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType overridden = Type("kept override", schema);
        MapTileType firstDefault = Type("default A", schema);
        MapTileType secondDefault = Type("default B", schema);
        overridden.PropertyOverrides.Add(Value("durability", MapTilePropertyKind.Integer, intValue: 0));
        MapTilePropertyDefinition definition;
        schema.TryGetDefinition("durability", out definition);
        definition.IntValue = 34;
        Equal(34, firstDefault.GetInt("durability"), "An existing unmodified type follows changed schema defaults");
        Equal(34, secondDefault.GetInt("durability"), "Shared defaults reach every unmodified type");
        Equal(0, overridden.GetInt("durability"), "An explicit zero remains independent of default changes");
        overridden.PropertyOverrides.Clear();
        Equal(34, overridden.GetInt("durability"), "Resetting an override restores inheritance from the current default");
    }

    private static void VerifyKeyResolution()
    {
        MapTilePropertyDefinition newDefinition = new MapTilePropertyDefinition();
        MapTilePropertyDefinition anotherDefinition = new MapTilePropertyDefinition();
        string originalKey = newDefinition.Key;
        True(!string.IsNullOrWhiteSpace(originalKey) && originalKey != anotherDefinition.Key, "New definitions receive independent stable keys");
        newDefinition.DisplayName = "重命名显示名称";
        Equal(originalKey, newDefinition.Key, "Changing a display name does not change property identity");
        MapTilePropertySchema schema = Schema();
        schema.Definitions.Add(Definition("code", MapTilePropertyKind.Integer, intValue: 4));
        schema.Definitions.Add(Definition("code", MapTilePropertyKind.Integer, intValue: 8));
        schema.Definitions.Add(Definition("Code", MapTilePropertyKind.Text, textValue: "capitalized"));
        MapTileType type = Type("key identity", schema);
        Equal(4, type.GetInt("code"), "Duplicate definitions resolve to the first definition");
        Equal("capitalized", type.GetText("Code"), "Keys compare case sensitively");
        Equal("missing", type.GetText("CODE", "missing"), "A differently cased unknown key does not alias a definition");
        type.PropertyOverrides.Add(Value("code", MapTilePropertyKind.Integer, intValue: 10));
        type.PropertyOverrides.Add(Value("code", MapTilePropertyKind.Integer, intValue: 0));
        type.PropertyOverrides.Add(Value("code", MapTilePropertyKind.Text, textValue: "obsolete kind"));
        Equal(0, type.GetInt("code", 99), "The last matching-kind override wins; stale kinds are ignored");
    }

    private static void VerifyOverrideHelpers()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType type = Type("override helper", schema);
        MapTilePropertyValue initial = Value("durability", MapTilePropertyKind.Integer, intValue: 0);
        True(type.SetPropertyOverride(initial), "An explicit zero can be set through the public helper");
        initial.IntValue = 99;
        Equal(0, type.GetInt("durability", 71), "The helper copies values instead of retaining a caller-owned mutable object");
        True(!type.SetPropertyOverride(Value("durability", MapTilePropertyKind.Integer, intValue: 0)), "Setting the same value is a no-op");
        Equal(1, type.PropertyOverrides.Count, "Repeated writes do not create duplicate records");
        True(!type.SetPropertyOverride(Value("durability", MapTilePropertyKind.Text, textValue: "wrong kind")), "Wrong-kind writes are rejected");
        True(!type.SetPropertyOverride(Value("unknown", MapTilePropertyKind.Integer, intValue: 8)), "Undefined property writes are rejected");
        Equal(0, type.GetInt("durability", 71), "Rejected writes leave the current value intact");
        type.PropertyOverrides.Add(Value("durability", MapTilePropertyKind.Text, textValue: "stale duplicate"));
        type.PropertyOverrides.Add(Value("durability", MapTilePropertyKind.Integer, intValue: 5));
        type.SetPropertyOverride(Value("material", MapTilePropertyKind.Text, textValue: "stone"));
        True(type.SetPropertyOverride(Value("durability", MapTilePropertyKind.Integer, intValue: 3)), "Writing a property cleans up legacy duplicate records");
        Equal(2, type.PropertyOverrides.Count, "Exactly one record remains per configured key");
        Equal("stone", type.GetText("material"), "Cleaning one key preserves unrelated property overrides");
        True(type.RemovePropertyOverride("durability"), "Resetting removes the override");
        Equal(12, type.GetInt("durability"), "Resetting restores the schema default");
        True(!type.RemovePropertyOverride("durability"), "Resetting an already inherited property is a no-op");
        schema.Definitions.RemoveAll(definition => definition != null && definition.Key == "material");
        True(type.RemovePropertyOverride("material"), "The reset helper can clean an orphaned override");
        Equal(0, type.PropertyOverrides.Count, "Orphan cleanup removes the obsolete record");
    }

    private static void VerifySaveReload()
    {
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryAssetFolder));
        MapTilePropertySchema schema = StandardSchema();
        schema.EnsureBuiltInDefinitions();
        MapTileType first = Type("persisted explicit values", schema);
        MapTileType second = Type("persisted defaults", schema);
        second.Layer = MapLayer.Objects;
        GridMapAsset map = Track(ScriptableObject.CreateInstance<GridMapAsset>());
        map.Initialize(3, 2);
        map.SetCell(1, 1, first);
        map.SetCell(1, 1, second, MapLayer.Objects);
        string schemaPath = temporaryAssetFolder + "/Schema.asset";
        string firstPath = temporaryAssetFolder + "/TypeA.asset";
        string secondPath = temporaryAssetFolder + "/TypeB.asset";
        string mapPath = temporaryAssetFolder + "/Map.asset";
        AssetDatabase.CreateAsset(schema, schemaPath);
        AssetDatabase.CreateAsset(first, firstPath);
        AssetDatabase.CreateAsset(second, secondPath);
        AssetDatabase.CreateAsset(map, mapPath);
        EditorUtility.SetDirty(schema);
        EditorUtility.SetDirty(first);
        EditorUtility.SetDirty(second);
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();

        // Exercise the actual editor save action with edits made after all baseline
        // assets were written. A later SaveAssets call must not mask a missed schema save.
        AddEmptyOverrides(first);
        first.SetPropertyOverride(Value(MapTilePropertyKeys.Flammable, MapTilePropertyKind.Boolean, boolValue: true));
        MapTilePropertyDefinition definition;
        schema.TryGetDefinition("weight", out definition);
        definition.NumberValue = 7.25f;
        schema.TryGetDefinition("material", out definition);
        definition.TextValue = "saved schema default";
        EditorUtility.SetDirty(first);
        EditorUtility.SetDirty(schema);
        GridMapPropertyAssets.SaveType(first);
        True(!EditorUtility.IsDirty(first), "Editor SaveType flushes the modified type asset");
        True(!EditorUtility.IsDirty(schema), "Editor SaveType also flushes the modified shared schema");
        Resources.UnloadAsset(map);
        Resources.UnloadAsset(first);
        Resources.UnloadAsset(second);
        Resources.UnloadAsset(schema);
        GridMapAsset reloadedMap = AssetDatabase.LoadAssetAtPath<GridMapAsset>(mapPath);
        MapTilePropertySchema reloadedSchema = AssetDatabase.LoadAssetAtPath<MapTilePropertySchema>(schemaPath);
        MapTileType reloadedFirst = reloadedMap.GetCell(1, 1);
        MapTileType reloadedSecond = reloadedMap.GetCell(1, 1, MapLayer.Objects);
        Same(reloadedSchema, reloadedFirst.PropertySchema, "Terrain type retains its schema asset reference");
        Same(reloadedSchema, reloadedSecond.PropertySchema, "Object type shares the same schema after reload");
        True(!reloadedFirst.GetBool("enabled", true), "Explicit false survives actual reload");
        Equal(0, reloadedFirst.GetInt("durability", 99), "Explicit integer zero survives actual reload");
        Equal(0f, reloadedFirst.GetNumber("weight", 99f), "Explicit number zero survives actual reload");
        Equal(string.Empty, reloadedFirst.GetText("material", "fallback"), "Explicit empty text survives actual reload");
        True(reloadedFirst.CanBeIgnited && !reloadedFirst.CanBeDestroyed, "Built-in overrides survive actual reload");
        True(reloadedSecond.GetBool("enabled"), "Unmodified type retains its Boolean schema default");
        Equal(12, reloadedSecond.GetInt("durability"), "Unmodified type retains its integer schema default");
        Equal(7.25f, reloadedSecond.GetNumber("weight"), "Editor SaveType persists the modified number schema default");
        Equal("saved schema default", reloadedSecond.GetText("material"), "Editor SaveType persists the modified text schema default");
    }

    private static void VerifyEditorAddDefinition()
    {
        MapTilePropertySchema schema = Schema();
        MapTileType terrain = Type("existing terrain type", schema);
        MapTileType objects = Type("existing object type", schema);
        objects.Layer = MapLayer.Objects;
        string[] keys = { "canFreeze", "maxHealth", "burnDuration", "lootName" };
        MapTilePropertyKind[] kinds =
        {
            MapTilePropertyKind.Boolean, MapTilePropertyKind.Integer,
            MapTilePropertyKind.Number, MapTilePropertyKind.Text
        };
        Undo.IncrementCurrentGroup();
        for (int i = 0; i < keys.Length; i++)
        {
            Equal(null, MapTilePropertySchemaGUI.ValidateNew(schema, keys[i], "Shared property " + i), "Valid keys are accepted");
            MapTilePropertySchemaGUI.AddDefinition(schema, keys[i], " Shared property " + i + " ", kinds[i]);
            MapTilePropertyDefinition definition;
            True(schema.TryGetDefinition(keys[i], out definition), "Editor addition creates a definition");
            Equal("Shared property " + i, definition.DisplayName, "Editor addition trims the display name");
            MapTilePropertyValue value;
            True(terrain.TryGetProperty(keys[i], kinds[i], out value), "A preexisting terrain type immediately sees the added definition");
            True(objects.TryGetProperty(keys[i], kinds[i], out value), "A preexisting object type immediately sees the added definition");
            True(MapTilePropertySchemaGUI.ValidateNew(schema, keys[i], "Duplicate") != null, "Duplicate validation reports an error");
            ExpectArgumentException(() => MapTilePropertySchemaGUI.AddDefinition(schema, keys[i], "Duplicate", kinds[i]),
                "The add action itself rejects duplicate keys");
        }
        foreach (string invalid in new[] { "", "1health", "has space", "invalid-key", "属性", "trailingNewline\n" })
        {
            True(MapTilePropertySchemaGUI.ValidateNew(schema, invalid, "Invalid key") != null, "Invalid key is rejected: " + invalid);
            ExpectArgumentException(() => MapTilePropertySchemaGUI.AddDefinition(schema, invalid, "Invalid key", MapTilePropertyKind.Boolean),
                "Invalid input cannot bypass the add action's validation");
        }
        True(MapTilePropertySchemaGUI.ValidateNew(schema, "validKey", "   ") != null, "An empty display name is rejected");
        ExpectArgumentException(() => MapTilePropertySchemaGUI.AddDefinition(schema, "validKey", "   ", MapTilePropertyKind.Boolean),
            "The add action rejects blank names");
        Equal(4, schema.Definitions.Count, "Rejected additions leave the schema unchanged");
        Equal(0, terrain.PropertyOverrides.Count, "Adding shared definitions does not create per-type overrides");
        Equal(0, objects.PropertyOverrides.Count, "Adding shared definitions leaves other type overrides untouched");
        Undo.FlushUndoRecordObjects();
        Undo.ClearUndo(schema);
        Undo.IncrementCurrentGroup();
    }

    private static void VerifyWindowSaveWithoutMap()
    {
        if (!AssetDatabase.IsValidFolder(temporaryAssetFolder))
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryAssetFolder));
        MapTilePropertySchema schema = StandardSchema();
        MapTileType type = Type("window save without map", schema);
        string schemaPath = temporaryAssetFolder + "/WindowOnlySchema.asset";
        string typePath = temporaryAssetFolder + "/WindowOnlyType.asset";
        AssetDatabase.CreateAsset(schema, schemaPath);
        AssetDatabase.CreateAsset(type, typePath);
        AssetDatabase.SaveAssets();
        type.SetPropertyOverride(Value("enabled", MapTilePropertyKind.Boolean, boolValue: false));
        MapTilePropertyDefinition definition;
        schema.TryGetDefinition("material", out definition);
        definition.TextValue = "saved without a map";
        EditorUtility.SetDirty(type);
        EditorUtility.SetDirty(schema);
        GridMapEditorWindow window = ScriptableObject.CreateInstance<GridMapEditorWindow>();
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo mapField = typeof(GridMapEditorWindow).GetField("map", flags);
            FieldInfo typeField = typeof(GridMapEditorWindow).GetField("selectedType", flags);
            MethodInfo save = typeof(GridMapEditorWindow).GetMethod("SaveMap", flags);
            True(mapField != null && typeField != null && save != null, "Window save entry point and test state are available");
            mapField.SetValue(window, null);
            typeField.SetValue(window, type);
            save.Invoke(window, null);
            True(!EditorUtility.IsDirty(type), "Window Save without a map flushes the selected type");
            True(!EditorUtility.IsDirty(schema), "Window Save without a map flushes the selected type's schema");
            typeField.SetValue(window, null);
            Resources.UnloadAsset(type);
            Resources.UnloadAsset(schema);
            MapTileType reloaded = AssetDatabase.LoadAssetAtPath<MapTileType>(typePath);
            MapTilePropertySchema reloadedSchema = AssetDatabase.LoadAssetAtPath<MapTilePropertySchema>(schemaPath);
            Same(reloadedSchema, reloaded.PropertySchema, "Window Save preserves the schema reference without a map");
            True(!reloaded.GetBool("enabled", true), "Window Save without a map persists an explicit false override");
            Equal("saved without a map", reloaded.GetText("material"), "Window Save without a map persists a shared schema edit");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void VerifyTypeUndoRedo()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType first = Type("undo type A", schema);
        MapTileType second = Type("undo type B", schema);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.RegisterCompleteObjectUndo(first, "Map2D property verification type edit");
        AddEmptyOverrides(first);
        EditorUtility.SetDirty(first);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        True(!first.GetBool("enabled", true), "Before undo, explicit false is active");
        Undo.PerformUndo();
        Equal(0, first.PropertyOverrides.Count, "Undo removes all added override records");
        True(first.GetBool("enabled"), "Undo restores inheritance from schema defaults");
        Equal(12, first.GetInt("durability"), "Undo restores integer default");
        Undo.PerformRedo();
        Equal(4, first.PropertyOverrides.Count, "Redo restores the complete override edit");
        True(!first.GetBool("enabled", true), "Redo restores explicit false");
        Equal(0, first.GetInt("durability", 99), "Redo restores explicit integer zero");
        Equal(0f, first.GetNumber("weight", 99f), "Redo restores explicit number zero");
        Equal(string.Empty, first.GetText("material", "fallback"), "Redo restores explicit empty text");
        True(second.GetBool("enabled") && second.PropertyOverrides.Count == 0, "Type Undo/Redo leaves other types unchanged");
        Undo.ClearUndo(first);
        Undo.IncrementCurrentGroup();
    }

    private static void VerifySchemaUndoRedo()
    {
        MapTilePropertySchema schema = StandardSchema();
        MapTileType type = Type("schema undo dependent", schema);
        type.PropertyOverrides.Add(Value("durability", MapTilePropertyKind.Integer, intValue: 0));
        MapTilePropertyDefinition definition;
        schema.TryGetDefinition("durability", out definition);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.RegisterCompleteObjectUndo(schema, "Map2D property verification schema edit");
        definition.Kind = MapTilePropertyKind.Text;
        definition.TextValue = "changed schema";
        schema.Definitions.Add(Definition("new property", MapTilePropertyKind.Boolean, boolValue: true));
        EditorUtility.SetDirty(schema);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        Equal("changed schema", type.GetText("durability"), "Changed schema ignores an old-kind override");
        True(type.GetBool("new property"), "New definition is visible to an already existing type");
        Undo.PerformUndo();
        Equal(0, type.GetInt("durability", 99), "Undo restores the original kind and its matching explicit zero");
        MapTilePropertyValue value;
        True(!type.TryGetProperty("new property", out value), "Undo removes the added schema definition");
        Undo.PerformRedo();
        Equal("changed schema", type.GetText("durability"), "Redo restores the new default and kind");
        Equal(99, type.GetInt("durability", 99), "Redo ignores the old integer override again");
        True(type.GetBool("new property"), "Redo restores the added schema definition");
        Equal(1, type.PropertyOverrides.Count, "Schema Undo/Redo does not rewrite per-type override records");
        Undo.ClearUndo(schema);
        Undo.IncrementCurrentGroup();
    }

    private static MapTilePropertySchema Schema()
    {
        return Track(ScriptableObject.CreateInstance<MapTilePropertySchema>());
    }

    private static MapTilePropertySchema StandardSchema()
    {
        MapTilePropertySchema schema = Schema();
        schema.Definitions.Add(Definition("enabled", MapTilePropertyKind.Boolean, boolValue: true));
        schema.Definitions.Add(Definition("durability", MapTilePropertyKind.Integer, intValue: 12));
        schema.Definitions.Add(Definition("weight", MapTilePropertyKind.Number, numberValue: 2.5f));
        schema.Definitions.Add(Definition("material", MapTilePropertyKind.Text, textValue: "wood"));
        return schema;
    }

    private static MapTileType Type(string name, MapTilePropertySchema schema)
    {
        MapTileType type = Track(ScriptableObject.CreateInstance<MapTileType>());
        type.name = name;
        type.DisplayName = name;
        type.PropertySchema = schema;
        return type;
    }

    private static MapTilePropertyDefinition Definition(string key, MapTilePropertyKind kind,
        bool boolValue = false, int intValue = 0, float numberValue = 0f, string textValue = "")
    {
        return new MapTilePropertyDefinition
        {
            Key = key, DisplayName = key, Kind = kind,
            BoolValue = boolValue, IntValue = intValue, NumberValue = numberValue, TextValue = textValue
        };
    }

    private static MapTilePropertyValue Value(string key, MapTilePropertyKind kind,
        bool boolValue = false, int intValue = 0, float numberValue = 0f, string textValue = "")
    {
        return new MapTilePropertyValue
        {
            Key = key, Kind = kind,
            BoolValue = boolValue, IntValue = intValue, NumberValue = numberValue, TextValue = textValue
        };
    }

    private static void AddEmptyOverrides(MapTileType type)
    {
        True(type.SetPropertyOverride(Value("enabled", MapTilePropertyKind.Boolean, boolValue: false)), "Set explicit Boolean false");
        True(type.SetPropertyOverride(Value("durability", MapTilePropertyKind.Integer, intValue: 0)), "Set explicit integer zero");
        True(type.SetPropertyOverride(Value("weight", MapTilePropertyKind.Number, numberValue: 0f)), "Set explicit number zero");
        True(type.SetPropertyOverride(Value("material", MapTilePropertyKind.Text, textValue: string.Empty)), "Set explicit empty text");
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
            Debug.LogError("Map2D property verification failed: " + name + "\n" + exception);
        }
    }

    private static string GetReportPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapPropertyReport") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/property-verification.json"));
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectArgumentException(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
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

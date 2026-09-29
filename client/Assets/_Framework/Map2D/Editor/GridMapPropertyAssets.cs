using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Connects existing and newly created tile types to shared property definitions.</summary>
    public static class GridMapPropertyAssets
    {
        public const string SchemaPath = GridMapAssets.Root + "/TileProperties.asset";

        public static MapTilePropertySchema EnsureDefaultSchema()
        {
            var schema = AssetDatabase.LoadAssetAtPath<MapTilePropertySchema>(SchemaPath);
            if (schema == null)
            {
                GridMapAssets.EnsureFolder(GridMapAssets.Root);
                schema = ScriptableObject.CreateInstance<MapTilePropertySchema>();
                schema.EnsureBuiltInDefinitions();
                AssetDatabase.CreateAsset(schema, SchemaPath);
            }
            else if (schema.EnsureBuiltInDefinitions()) EditorUtility.SetDirty(schema);
            AssetDatabase.SaveAssetIfDirty(schema);
            return schema;
        }

        public static MapTilePropertySchema EnsureTypeSchema(MapTileType type)
        {
            if (type.PropertySchema != null) return type.PropertySchema;
            type.PropertySchema = EnsureDefaultSchema();
            EditorUtility.SetDirty(type);
            return type.PropertySchema;
        }

        public static void ConnectExistingTypes()
        {
            var schema = EnsureDefaultSchema();
            foreach (string guid in AssetDatabase.FindAssets("t:MapTileType", new[] { "Assets" }))
            {
                var type = AssetDatabase.LoadAssetAtPath<MapTileType>(AssetDatabase.GUIDToAssetPath(guid));
                if (type == null || type.PropertySchema != null) continue;
                type.PropertySchema = schema;
                EditorUtility.SetDirty(type);
                AssetDatabase.SaveAssetIfDirty(type);
            }
        }

        public static void SaveType(MapTileType type)
        {
            if (type == null) return;
            if (EditorUtility.IsPersistent(type)) AssetDatabase.SaveAssetIfDirty(type);
            if (type.PropertySchema != null && EditorUtility.IsPersistent(type.PropertySchema))
                AssetDatabase.SaveAssetIfDirty(type.PropertySchema);
        }
    }
}

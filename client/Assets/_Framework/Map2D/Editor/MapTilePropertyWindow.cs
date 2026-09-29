using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed class MapTilePropertyWindow : EditorWindow
    {
        [SerializeField] private MapTilePropertySchema schema;
        private Vector2 scroll;
        private readonly MapTilePropertySchemaGUI fields = new MapTilePropertySchemaGUI();

        [MenuItem("Tools/BigWorld/地图/管理公共格子属性")]
        public static void OpenDefault()
        {
            GridMapPropertyAssets.ConnectExistingTypes();
            Open(GridMapPropertyAssets.EnsureDefaultSchema());
        }

        public static void Open(MapTilePropertySchema value)
        {
            var window = GetWindow<MapTilePropertyWindow>();
            window.titleContent = new GUIContent("公共格子属性");
            window.minSize = new Vector2(430, 400);
            window.Save();
            window.schema = value;
            window.Show();
        }

        private void OnEnable() { Undo.undoRedoPerformed += Repaint; }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) Save();
        }

        private void Save()
        {
            if (schema != null && EditorUtility.IsPersistent(schema)) AssetDatabase.SaveAssetIfDirty(schema);
        }

        private void OnGUI()
        {
            if (schema == null)
            {
                if (GUILayout.Button("打开公共属性配置")) schema = GridMapPropertyAssets.EnsureDefaultSchema();
                return;
            }
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.keyCode == KeyCode.S) { Save(); evt.Use(); }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("公共格子属性", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(60))) Save();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            fields.Draw(schema);
            EditorGUILayout.EndScrollView();
        }
    }

    [CustomEditor(typeof(MapTilePropertySchema))]
    public sealed class MapTilePropertySchemaInspector : UnityEditor.Editor
    {
        private readonly MapTilePropertySchemaGUI fields = new MapTilePropertySchemaGUI();
        public override void OnInspectorGUI() { fields.Draw((MapTilePropertySchema)target); }
    }

    public sealed class MapTilePropertySchemaGUI
    {
        private static readonly string[] KindNames = { "开关（是 / 否）", "整数", "小数", "文本" };
        private string newKey = "";
        private string newName = "";
        private MapTilePropertyKind newKind;

        public void Draw(MapTilePropertySchema schema)
        {
            EditorGUILayout.HelpBox("新增属性会出现在所有使用此配置的格子类型中。修改默认值会影响未单独设置该属性的类型；已有覆盖值保持不变。", MessageType.Info);
            int removeIndex = -1;
            for (int i = 0; i < schema.Definitions.Count; i++)
            {
                var definition = schema.Definitions[i];
                if (definition == null) continue;
                bool builtIn = definition.Key == MapTilePropertyKeys.Destructible || definition.Key == MapTilePropertyKeys.Flammable;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(definition.Key, EditorStyles.miniBoldLabel);
                        if (builtIn) GUILayout.Label("内置", EditorStyles.miniLabel, GUILayout.Width(32));
                        else if (GUILayout.Button("删除", GUILayout.Width(45))) removeIndex = i;
                    }
                    EditorGUI.BeginChangeCheck();
                    string label = EditorGUILayout.TextField("显示名称", definition.DisplayName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(schema, "修改属性名称"); definition.DisplayName = label; EditorUtility.SetDirty(schema);
                    }
                    MapTilePropertyKind kind;
                    using (new EditorGUI.DisabledScope(builtIn))
                        kind = (MapTilePropertyKind)EditorGUILayout.Popup("值类型", (int)definition.Kind, KindNames);
                    if (kind != definition.Kind && EditorUtility.DisplayDialog("修改属性值类型", "旧类型的单独设置会保留，但暂不生效。格子类型将使用新类型的默认值；改回原类型后旧设置可重新生效。", "修改", "取消"))
                    {
                        Undo.RecordObject(schema, "修改属性值类型"); definition.Kind = kind; EditorUtility.SetDirty(schema);
                    }
                    var value = definition.Clone();
                    EditorGUI.BeginChangeCheck();
                    MapTilePropertyGUI.DrawValue(new GUIContent("默认值"), value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(schema, "修改公共属性默认值");
                        definition.BoolValue = value.BoolValue; definition.IntValue = value.IntValue;
                        definition.NumberValue = value.NumberValue; definition.TextValue = value.TextValue;
                        EditorUtility.SetDirty(schema);
                    }
                }
            }
            if (removeIndex >= 0 && EditorUtility.DisplayDialog("删除公共属性", "所有类型将不再显示或读取此属性。已有覆盖数据保留；撤销删除可恢复。", "删除", "取消"))
            {
                Undo.RecordObject(schema, "删除公共属性"); schema.Definitions.RemoveAt(removeIndex); EditorUtility.SetDirty(schema);
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("新增属性", EditorStyles.boldLabel);
            newName = EditorGUILayout.TextField("显示名称", newName);
            newKey = EditorGUILayout.TextField(new GUIContent("属性标识", "用于代码读取，创建后保持不变，例如 hitPoints、burnDuration"), newKey);
            newKind = (MapTilePropertyKind)EditorGUILayout.Popup("值类型", (int)newKind, KindNames);
            string error = ValidateNew(schema, newKey, newName);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.None);
            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("＋ 添加到所有格子类型", GUILayout.Height(28)))
                {
                    AddDefinition(schema, newKey, newName, newKind);
                    newKey = ""; newName = ""; GUI.FocusControl(null);
                }
            }
        }

        public static string ValidateNew(MapTilePropertySchema schema, string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return "填写属性名称，例如：可被冰冻、生命值。";
            if (string.IsNullOrEmpty(key) || !Regex.IsMatch(key, @"\A[A-Za-z][A-Za-z0-9_]*\z")) return "标识须以英文字母开头，后续可用字母、数字和下划线。";
            if (schema.TryGetDefinition(key, out _)) return "此属性标识已存在，请换一个。";
            return null;
        }

        public static void AddDefinition(MapTilePropertySchema schema, string key, string displayName, MapTilePropertyKind kind)
        {
            string error = ValidateNew(schema, key, displayName);
            if (error != null) throw new ArgumentException(error);
            Undo.RecordObject(schema, "新增公共格子属性");
            schema.Definitions.Add(new MapTilePropertyDefinition { Key = key, DisplayName = displayName.Trim(), Kind = kind });
            EditorUtility.SetDirty(schema);
        }
    }
}

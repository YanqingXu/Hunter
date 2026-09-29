using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public static class MapTilePropertyGUI
    {
        public static void Draw(MapTileType type)
        {
            var schema = GridMapPropertyAssets.EnsureTypeSchema(type);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("格子属性", EditorStyles.boldLabel);
            float oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(oldWidth, 120f);
            foreach (var definition in schema.Definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.Key)) continue;
                if (!type.TryGetProperty(definition.Key, out var effective)) continue;
                var value = effective.Clone();
                bool overridden = type.PropertyOverrides.Exists(item => item != null && item.Key == definition.Key && item.Kind == definition.Kind);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    DrawValue(new GUIContent(definition.DisplayName, definition.Key + (overridden ? "：此类型单独设置" : "：使用公共默认值")), value);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(type, "修改格子属性");
                        type.SetPropertyOverride(value);
                        EditorUtility.SetDirty(type);
                    }
                    using (new EditorGUI.DisabledScope(!overridden))
                    {
                        if (GUILayout.Button(new GUIContent(overridden ? "恢复" : "默认", "删除此类型的覆盖值，重新使用公共默认值"), GUILayout.Width(40)))
                        {
                            Undo.RecordObject(type, "恢复格子属性默认值");
                            type.RemovePropertyOverride(definition.Key);
                            EditorUtility.SetDirty(type);
                        }
                    }
                }
            }
            EditorGUIUtility.labelWidth = oldWidth;
            if (GUILayout.Button("管理公共属性（新增 / 修改）")) MapTilePropertyWindow.Open(schema);
            EditorGUILayout.HelpBox("所有类型共用属性清单；这里修改当前类型的值，点“恢复”后跟随公共默认值。", MessageType.None);
        }

        internal static void DrawValue(GUIContent label, MapTilePropertyValue value)
        {
            switch (value.Kind)
            {
                case MapTilePropertyKind.Boolean: value.BoolValue = EditorGUILayout.Toggle(label, value.BoolValue); break;
                case MapTilePropertyKind.Integer: value.IntValue = EditorGUILayout.IntField(label, value.IntValue); break;
                case MapTilePropertyKind.Number: value.NumberValue = EditorGUILayout.FloatField(label, value.NumberValue); break;
                case MapTilePropertyKind.Text: value.TextValue = EditorGUILayout.TextField(label, value.TextValue); break;
            }
        }
    }
}

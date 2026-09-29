using System;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed class GridMapStructureCaptureWindow : EditorWindow
    {
        [SerializeField] private GridMapAsset source;
        [SerializeField] private Vector2Int start;
        [SerializeField] private Vector2Int size;
        [SerializeField] private string houseName = "自定义房屋";
        private string error;

        public static void Open(GridMapAsset map)
        {
            var window = GetWindow<GridMapStructureCaptureWindow>(true, "保存房屋模板");
            window.minSize = new Vector2(410, 300);
            window.source = map; window.start = Vector2Int.zero;
            window.size = map == null ? Vector2Int.one : new Vector2Int(map.Width, map.Height);
            window.error = null; window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("把选定矩形的地形和物件保存为可重复摆放的房屋。原地图保持不变。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("源地图", source, typeof(GridMapAsset), false);
            houseName = EditorGUILayout.TextField("房屋名称", houseName);
            start = EditorGUILayout.Vector2IntField("左下角 X / Y", start);
            size = EditorGUILayout.Vector2IntField("宽度 / 高度", size);
            if (source != null && GUILayout.Button("使用整张地图")) { start = Vector2Int.zero; size = new Vector2Int(source.Width, source.Height); }
            using (new EditorGUI.DisabledScope(source == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("保存为新房屋模板…", GUILayout.Height(30))) SaveTemplate();
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void SaveTemplate()
        {
            if (string.IsNullOrWhiteSpace(houseName)) { error = "请填写房屋名称。"; return; }
            if (start.x < 0 || start.y < 0 || size.x <= 0 || size.y <= 0 ||
                (long)start.x + size.x > source.Width || (long)start.y + size.y > source.Height)
            { error = "请选择完整位于地图内的非空矩形。"; return; }
            GridMapAssets.EnsureFolder(GridMapStructureAssets.Folder);
            string path = EditorUtility.SaveFilePanelInProject("保存房屋模板", "NewHouse", "asset", "选择房屋模板保存位置", GridMapStructureAssets.Folder);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var template = GridMapStructureAssets.CreateFromRegion(path, houseName.Trim(), source, new RectInt(start, size));
                GridMapEditorWindow.SelectStructure(template); Selection.activeObject = template; Close();
            }
            catch (Exception exception) { error = exception.Message; }
        }
    }

    [CustomEditor(typeof(MapStructureTemplate))]
    public sealed class MapStructureTemplateInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("房屋名称"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("说明"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("layout"), new GUIContent("格子布局"));
            serializedObject.ApplyModifiedProperties();
            var template = (MapStructureTemplate)target;
            EditorGUILayout.LabelField("占用格子", template.Width + " × " + template.Height);
            using (new EditorGUI.DisabledScope(template.Layout == null))
            {
                if (GUILayout.Button("编辑房屋格子")) GridMapEditorWindow.EditStructure(template);
            }
            EditorGUILayout.HelpBox("将模板从 Project 拖到地图编辑器画布即可放置。放置后是独立格子；之后修改模板不会自动修改地图里已放置的房子。", MessageType.Info);
        }
    }
}

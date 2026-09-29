using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed class GridMapSpawnWindow : EditorWindow
    {
        [SerializeField] private GridMapAsset map;
        [SerializeField] private int selected;
        private Vector2 scroll;

        [MenuItem("Tools/BigWorld/地图/NPC 与怪物生成点")]
        private static void OpenSelected() { Open(Selection.activeObject as GridMapAsset); }

        public static void Open(GridMapAsset target)
        {
            var window = GetWindow<GridMapSpawnWindow>();
            window.map = target; window.selected = 0; window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("NPC / 怪物生成点"); minSize = new Vector2(410, 490);
            Undo.undoRedoPerformed += OnUndo;
        }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; }
        private void OnUndo() { Repaint(); GridMapEditorWindow.RepaintMapWindows(); }

        private void OnGUI()
        {
            var nextMap = (GridMapAsset)EditorGUILayout.ObjectField("地图", map, typeof(GridMapAsset), false);
            if (nextMap != map) { map = nextMap; selected = 0; }
            if (map == null) { EditorGUILayout.HelpBox("先选择地图，或从地图编辑器顶部打开此窗口。", MessageType.Info); return; }
            if (AssetDatabase.IsSubAsset(map))
            {
                EditorGUILayout.HelpBox("房屋模板保存格子布局。请先把房屋放到地图上，再为地图配置 NPC 和怪物生成点。", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("生成点只保存配置。游戏中靠近时生成，远离后回收，状态会保留。角色的移动、战斗行为由其预制体提供。", MessageType.None);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("新增生成点"))
                    {
                        Undo.RecordObject(map, "新增角色生成点");
                        map.Spawns.Add(new MapSpawnDefinition { DisplayName = "生成点 " + (map.Spawns.Count + 1), Cell = new Vector2Int(1, 1) });
                        selected = map.Spawns.Count - 1; Changed();
                    }
                    using (new EditorGUI.DisabledScope(map.Spawns.Count == 0))
                    {
                        if (GUILayout.Button("复制"))
                        {
                            selected = Mathf.Clamp(selected, 0, map.Spawns.Count - 1);
                            Undo.RecordObject(map, "复制角色生成点");
                            var copy = map.Spawns[selected].Clone(); copy.DisplayName += " 副本";
                            map.Spawns.Add(copy); selected = map.Spawns.Count - 1; Changed();
                        }
                        if (GUILayout.Button("删除"))
                        {
                            selected = Mathf.Clamp(selected, 0, map.Spawns.Count - 1);
                            Undo.RecordObject(map, "删除角色生成点"); map.Spawns.RemoveAt(selected);
                            selected = Mathf.Max(0, selected - 1); Changed();
                        }
                    }
                }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (map.Spawns.Count > 0)
                {
                    selected = Mathf.Clamp(selected, 0, map.Spawns.Count - 1);
                    string[] labels = map.Spawns.ConvertAll(s => s == null ? "无效生成点" : (s.Enabled ? "" : "[停用] ") + s.DisplayName).ToArray();
                    selected = EditorGUILayout.Popup("选择生成点", selected, labels);
                    var data = new SerializedObject(map);
                    data.Update();
                    SerializedProperty spawn = data.FindProperty("spawns").GetArrayElementAtIndex(selected);
                    Draw(spawn, "enabled", "启用"); Draw(spawn, "displayName", "名称");
                    SerializedProperty kind = spawn.FindPropertyRelative("kind");
                    kind.enumValueIndex = EditorGUILayout.Popup("类别", kind.enumValueIndex, new[] { "NPC", "怪物", "交互物体" });
                    Draw(spawn, "prefab", "角色 / 物体预制体"); Draw(spawn, "cell", "起点格子 X / Y");
                    Draw(spawn, "count", "数量"); Draw(spawn, "spacing", "水平间距（格）");
                    Draw(spawn, "maxHealth", "初始生命值"); Draw(spawn, "respawnSeconds", "死亡后刷新秒数（0 不刷新）");
                    if (data.ApplyModifiedProperties()) { map.ValidateSpawns(); Changed(); }
                    MapSpawnDefinition definition = map.Spawns[selected];
                    if (definition != null)
                    {
                        if (definition.Prefab == null) EditorGUILayout.HelpBox("拖入一个预制体后才会生成，可使用导入的角色动画预制体。", MessageType.Warning);
                        if (!map.Contains(definition.Cell.x, definition.Cell.y) || definition.Cell.x + (definition.Count - 1) * definition.Spacing >= map.Width)
                            EditorGUILayout.HelpBox("部分位置超出地图，超出的角色不会生成。请调整起点、数量或间距。", MessageType.Warning);
                        if (GUILayout.Button("到地图画布上选择位置", GUILayout.Height(30))) GridMapEditorWindow.BeginSpawnPlacement(map, definition.Id);
                    }
                }
                else EditorGUILayout.HelpBox("点击“新增生成点”，设置预制体和数量。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("保存生成点", GUILayout.Height(28)))
                {
                    map.ValidateSpawns(); EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
                }
            }
            if (Application.isPlaying) EditorGUILayout.HelpBox("停止游戏后可修改生成点配置。", MessageType.Info);
        }

        private void Changed() { EditorUtility.SetDirty(map); GridMapEditorWindow.RepaintMapWindows(); }
        private static void Draw(SerializedProperty parent, string name, string label)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name), new GUIContent(label), true);
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    [CustomEditor(typeof(GridMapAsset))]
    public sealed class GridMapAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var map = (GridMapAsset)target;
            EditorGUILayout.LabelField("横版格子地图", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("尺寸", map.Width + " × " + map.Height);
            EditorGUILayout.LabelField("每格尺寸", map.CellSize.ToString("0.###"));
            EditorGUILayout.LabelField("地形 / 物件", map.GetTileCount(MapLayer.Terrain) + " / " + map.GetTileCount(MapLayer.Objects));
            EditorGUILayout.LabelField("角色生成点", map.Spawns.Count.ToString());
            EditorGUILayout.HelpBox("双击此资产，或点击下方按钮，在地图编辑器中绘制与调整大小。", MessageType.Info);
            if (GUILayout.Button("打开 2D 地图编辑器", GUILayout.Height(30))) GridMapEditorWindow.OpenMap(map);
            if (GUILayout.Button("配置 NPC / 怪物生成点")) GridMapSpawnWindow.Open(map);
        }
    }

    [CustomEditor(typeof(MapTileType))]
    public sealed class MapTileTypeInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            GridMapPropertyAssets.EnsureTypeSchema((MapTileType)target);
            serializedObject.Update();
            Draw("displayName", "类型名称");
            var layer = serializedObject.FindProperty("layer");
            layer.enumValueIndex = EditorGUILayout.Popup("默认绘制层", layer.enumValueIndex, new[] { "地形", "物件" });
            Draw("sprite", "显示精灵"); Draw("tint", "显示颜色"); Draw("prefab", "运行时预制体");
            var collision = serializedObject.FindProperty("collision");
            collision.enumValueIndex = EditorGUILayout.Popup("格子碰撞", collision.enumValueIndex, new[] { "无碰撞", "实体", "单向平台" });
            Draw("description", "说明"); Draw("tags", "业务标签");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("扩展属性", EditorStyles.boldLabel);
            Draw("id", "类型标识"); Draw("walkable", "通行查询允许通过"); Draw("movementCost", "移动代价");
            serializedObject.ApplyModifiedProperties();
            MapTilePropertyGUI.Draw((MapTileType)target);
            EditorGUILayout.HelpBox("格子碰撞由地图生成，预制体可另有自己的碰撞组件。伤害、爆炸、掉落等由预制体的游戏脚本实现。修改默认层不会自动迁移地图中已绘制的格子。", MessageType.Info);
        }

        private void Draw(string property, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label), true);
        }
    }

    [CustomEditor(typeof(MapTilePalette))]
    public sealed class MapTilePaletteInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("types"), new GUIContent("可绘制的类型"), true);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("地图保存类型资产引用。把类型从面板移除不会删除地图中的格子；删除类型资产则会使引用丢失。", MessageType.Info);
            if (GUILayout.Button("打开地图编辑器")) GridMapEditorWindow.Open();
        }
    }

    [CustomEditor(typeof(GridMapRenderer))]
    public sealed class GridMapRendererInspector : UnityEditor.Editor
    {
        private string stateMessage;
        public override void OnInspectorGUI()
        {
            var renderer = (GridMapRenderer)target;
            var nextMap = (GridMapAsset)EditorGUILayout.ObjectField("地图资产", renderer.Map, typeof(GridMapAsset), false);
            if (nextMap != renderer.Map)
            {
                Undo.RegisterFullObjectHierarchyUndo(renderer.gameObject, "更换地图资产");
                renderer.SetMap(nextMap, true); MarkScene(renderer);
            }
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sortingLayerName"), new GUIContent("排序层名称"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sortingOrder"), new GUIContent("基础排序序号"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("游戏运行时分区加载", EditorStyles.boldLabel);
            DrawStreaming("streamingEnabled", "启用附近区域加载");
            DrawStreaming("loadingTarget", "跟随对象（玩家）");
            DrawStreaming("chunkSize", "每块边长（格）");
            DrawStreaming("loadingRadiusCells", "预加载半径 X / Y（格）");
            DrawStreaming("unloadPaddingCells", "卸载缓冲距离（格）");
            DrawStreaming("useCameraViewport", "同时覆盖主相机画面");
            DrawStreaming("chunksPerFrame", "每帧最多处理区块数");
            DrawStreaming("streamingBudgetMilliseconds", "每帧加载时间预算（毫秒）");
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("编辑时预览整图，运行时只生成附近区块。请指定玩家为跟随对象；留空时寻找 Player 标签对象，再使用主相机。对象创建会分帧处理，瞬间传送后应等待落点区块就绪再恢复移动。", MessageType.Info);
            if (Application.isPlaying) EditorGUILayout.LabelField("当前已加载区块", renderer.LoadedChunkCount.ToString());
            if (Application.isPlaying && renderer.RuntimeState != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("导出运行存档"))
                    {
                        string path = EditorUtility.SaveFilePanel("保存当前地图运行状态", "", renderer.Map.name + "_state", "json");
                        if (!string.IsNullOrEmpty(path))
                        {
                            try { System.IO.File.WriteAllText(path, renderer.SaveRuntimeState()); stateMessage = "已保存运行状态。"; }
                            catch (System.Exception error) { stateMessage = "保存失败：" + error.Message; }
                        }
                    }
                    if (GUILayout.Button("载入运行存档"))
                    {
                        string path = EditorUtility.OpenFilePanel("载入当前地图运行状态", "", "json");
                        if (!string.IsNullOrEmpty(path))
                        {
                            try { renderer.LoadRuntimeState(System.IO.File.ReadAllText(path)); stateMessage = "已载入运行状态。"; }
                            catch (System.Exception error) { stateMessage = "载入失败：" + error.Message; }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(stateMessage)) EditorGUILayout.HelpBox(stateMessage, MessageType.Info);
            }
            if (renderer.GetComponent<GridMapEntityStreamer>() == null && GUILayout.Button("添加角色 / 物件加载设置"))
            {
                Undo.AddComponent<GridMapEntityStreamer>(renderer.gameObject); MarkScene(renderer);
            }
            using (new EditorGUI.DisabledScope(renderer.Map == null))
            {
                if (GUILayout.Button("编辑地图")) GridMapEditorWindow.OpenMap(renderer.Map);
                if (GUILayout.Button("重新生成地图"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(renderer.gameObject, "重新生成地图");
                    renderer.Rebuild(true); MarkScene(renderer);
                }
            }
            if (GUILayout.Button("清空场景预览（保留地图数据）"))
            {
                Undo.RegisterFullObjectHierarchyUndo(renderer.gameObject, "清空地图预览");
                renderer.ClearPreview(); MarkScene(renderer);
            }
            EditorGUILayout.HelpBox("在 XY 平面生成。默认色块每格一单位，自定义精灵请按一单位一格设置 Pixels Per Unit。物件层显示在地形层前方。", MessageType.None);
        }

        private static void MarkScene(GridMapRenderer renderer)
        {
            EditorUtility.SetDirty(renderer);
            if (!Application.isPlaying && renderer.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
        }

        private void DrawStreaming(string property, string label)
        {
            SerializedProperty field = serializedObject.FindProperty(property);
            if (field != null) EditorGUILayout.PropertyField(field, new GUIContent(label), true);
        }
    }

    [CustomEditor(typeof(GridMapEntityStreamer))]
    public sealed class GridMapEntityStreamerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Draw("loadDistance", "角色激活距离"); Draw("unloadDistance", "角色回收距离");
            Draw("maxActors", "同时活动物体上限"); Draw("spawnRequestsPerFrame", "每帧最多生成请求");
            Draw("maxIdlePerPrefab", "每种物体最多缓存"); Draw("failedSpawnRetrySeconds", "生成失败重试间隔");
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("距离按世界单位计算，回收距离应大于激活距离。复用现有对象池；追击中的角色可以保持激活。NPC 与怪物的 AI 由角色预制体脚本提供。", MessageType.None);
            var renderer = ((GridMapEntityStreamer)target).GetComponent<GridMapRenderer>();
            if (renderer != null && renderer.Map != null && GUILayout.Button("配置 NPC / 怪物生成点")) GridMapSpawnWindow.Open(renderer.Map);
        }
        private void Draw(string property, string label)
        {
            SerializedProperty field = serializedObject.FindProperty(property);
            if (field != null) EditorGUILayout.PropertyField(field, new GUIContent(label), true);
        }
    }
}

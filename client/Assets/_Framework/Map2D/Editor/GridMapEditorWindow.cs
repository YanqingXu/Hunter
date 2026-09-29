using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed partial class GridMapEditorWindow : EditorWindow
    {
        private enum Tool { Brush, Eraser, Rectangle, Fill, Picker, Pan }
        private static readonly string[] ToolNames = { "画笔 B", "橡皮 E", "矩形 R", "填充 F", "吸管 I", "手形 H" };
        private static readonly string[] LayerNames = { "地形", "物件" };
        private static readonly string[] CollisionNames = { "无碰撞", "实体", "单向平台" };
        [SerializeField] private GridMapAsset map;
        [SerializeField] private MapTilePalette palette;
        [SerializeField] private MapTileType selectedType;
        [SerializeField] private GridMapRenderer preview;
        [SerializeField] private MapLayer layer;
        [SerializeField] private Tool tool;
        [SerializeField] private int brushSize = 1;
        [SerializeField] private float pixels = 25f;
        [SerializeField] private Vector2 pan = new Vector2(24, 24);
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool showOtherLayer = true;
        [SerializeField] private bool rectangleOutline;
        [SerializeField] private bool updatePreview = true;
        private Vector2 paletteScroll;
        private Vector2 settingsScroll;
        private Vector2Int lastCell;
        private Vector2Int rectangleStart;
        private Vector2Int rectangleEnd;
        private MapTileType strokeType;
        private MapLayer strokeLayer;
        private bool painting;
        private bool panning;
        private bool strokeChanged;
        private int undoGroup;
        private int desiredWidth = 40;
        private int desiredHeight = 22;
        private float desiredCellSize = 1f;
        private bool fitPending;
        private string status = "左键绘制 · 右键擦除 · 中键平移 · 滚轮缩放";
        private GUIStyle badgeStyle;
        private GUIContent[] toolContents;
        private readonly Dictionary<Sprite, Texture> spritePreviews = new Dictionary<Sprite, Texture>();

        [MenuItem("Tools/BigWorld/2D 地图编辑器", false, 10)]
        [MenuItem("Window/BigWorld/2D Map Editor")]
        public static void Open()
        {
            var window = GetWindow<GridMapEditorWindow>();
            window.titleContent = new GUIContent("2D 地图编辑器");
            window.minSize = new Vector2(960, 600);
            GridMapPropertyAssets.ConnectExistingTypes();
            if (window.palette == null) window.palette = GridMapAssets.EnsureDefaults();
            GridMapStructureAssets.EnsureDefaults();
            window.ReloadStructures();
            if (window.map == null) window.SetMap(AssetDatabase.LoadAssetAtPath<GridMapAsset>(GridMapAssets.ExamplePath));
            window.SelectLayer(window.layer);
            window.Show();
        }

        public static void OpenMap(GridMapAsset asset)
        {
            Open();
            GetWindow<GridMapEditorWindow>().SetMap(asset);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is GridMapAsset asset)) return false;
            OpenMap(asset);
            return true;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("2D 地图编辑器");
            minSize = new Vector2(960, 600);
            wantsMouseMove = true;
            toolContents = Array.ConvertAll(ToolNames, name => new GUIContent(name));
            toolContents[(int)Tool.Pan] = new GUIContent("手形 H", EditorGUIUtility.IconContent("ViewToolMove").image,
                "按住鼠标左键拖动画布；H 切换手形，B 返回画笔");
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnStructureAssetsChanged;
            if (map != null) ReadDimensions();
        }

        private void OnDisable()
        {
            FinishStroke();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnStructureAssetsChanged;
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) SaveMap();
        }

        private void OnLostFocus() { FinishStroke(); structureDragCandidate = null; }

        private void OnUndoRedo()
        {
            if (map != null) ReadDimensions();
            RefreshPreview();
            Repaint();
        }

        private void SetMap(GridMapAsset value)
        {
            FinishStroke();
            SaveMap();
            CancelStructurePlacement();
            map = value;
            preview = null;
            if (map != null) ReadDimensions();
            fitPending = true;
            Repaint();
        }

        private void ReadDimensions()
        {
            desiredWidth = map.Width;
            desiredHeight = map.Height;
            desiredCellSize = map.CellSize;
        }

        private void SelectLayer(MapLayer value)
        {
            FinishStroke();
            CancelStructurePlacement();
            layer = value;
            if (selectedType == null || selectedType.Layer != layer)
            {
                GridMapPropertyAssets.SaveType(selectedType);
                selectedType = palette == null ? null : palette.Types.Find(t => t != null && t.Layer == layer);
            }
            Repaint();
        }

        private void OnGUI()
        {
            spritePreviews.Clear();
            HandleKeyboard();
            DrawToolbar();
            const float leftWidth = 212;
            const float rightWidth = 252;
            var left = new Rect(0, 48, leftWidth, position.height - 74);
            var right = new Rect(position.width - rightWidth, 48, rightWidth, position.height - 74);
            var canvas = new Rect(leftWidth + 1, 48, position.width - leftWidth - rightWidth - 2, position.height - 74);
            DrawPalette(left);
            DrawSettings(right);
            DrawCanvas(canvas);
            string hint = SpawnPlacement ? "左键选择生成点位置 · 右键 / Esc 取消 · 中键拖动画布" : tool == Tool.Pan && !structurePlacement
                ? "手形工具：左键拖动画布 · 滚轮缩放 · B 返回画笔"
                : status;
            GUI.Label(new Rect(8, position.height - 23, position.width - 16, 20), hint, EditorStyles.miniLabel);
        }

        private void DrawToolbar()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
            {
                if (GUILayout.Button("新建地图", EditorStyles.toolbarButton, GUILayout.Width(72))) NewMap();
                var next = (GridMapAsset)EditorGUILayout.ObjectField(map, typeof(GridMapAsset), false, GUILayout.MinWidth(120));
                if (next != map) SetMap(next);
                if (map == editingStructureMap && structureReturnMap != null && GUILayout.Button("返回地图", EditorStyles.toolbarButton, GUILayout.Width(70))) SetMap(structureReturnMap);
                if (GUILayout.Button(map != null && EditorUtility.IsDirty(map) ? "保存 *" : "保存", EditorStyles.toolbarButton, GUILayout.Width(54))) SaveMap();
                using (new EditorGUI.DisabledScope(map == null))
                {
                    if (GUILayout.Button("NPC / 怪物…", EditorStyles.toolbarButton, GUILayout.Width(88))) { FinishStroke(); GridMapSpawnWindow.Open(map); }
                    if (GUILayout.Button("另存为", EditorStyles.toolbarButton, GUILayout.Width(60))) SaveCopy();
                    if (GridMapRandomWindow.IsEnabled && GUILayout.Button("随机生成…", EditorStyles.toolbarButton, GUILayout.Width(80))) { FinishStroke(); GridMapRandomWindow.Open(map, palette); }
                    if (GUILayout.Button("生成到场景", EditorStyles.toolbarButton, GUILayout.Width(90))) GeneratePreview();
                }
            }
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
            {
                int next = GUILayout.Toolbar(structurePlacement || SpawnPlacement ? -1 : (int)tool, toolContents, EditorStyles.toolbarButton, GUILayout.Width(450));
                if (next >= 0 && ((Tool)next != tool || structurePlacement || SpawnPlacement)) { FinishStroke(); CancelStructurePlacement(); tool = (Tool)next; }
                GUILayout.Space(8);
                GUILayout.Label("笔刷", GUILayout.Width(28));
                brushSize = EditorGUILayout.IntSlider(brushSize, 1, 16, GUILayout.MinWidth(100), GUILayout.MaxWidth(210));
                if (tool == Tool.Rectangle) rectangleOutline = GUILayout.Toggle(rectangleOutline, "空心", EditorStyles.toolbarButton, GUILayout.Width(42));
                GUILayout.FlexibleSpace();
                showGrid = GUILayout.Toggle(showGrid, "网格", EditorStyles.toolbarButton, GUILayout.Width(45));
                if (GUILayout.Button("适应画布", EditorStyles.toolbarButton, GUILayout.Width(68))) fitPending = true;
            }
        }

        private void DrawPalette(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            int nextTab = GUILayout.Toolbar(structureLibraryTab, new[] { "格子类型", "房屋模板" });
            if (nextTab != structureLibraryTab) { FinishStroke(); CancelStructurePlacement(); structureLibraryTab = nextTab; }
            if (structureLibraryTab == 1) { DrawStructurePalette(); GUILayout.EndArea(); return; }
            GUILayout.Label("格子类型", EditorStyles.boldLabel);
            var nextPalette = (MapTilePalette)EditorGUILayout.ObjectField(palette, typeof(MapTilePalette), false);
            if (nextPalette != palette) { SaveMap(); palette = nextPalette; selectedType = null; SelectLayer(layer); }
            var nextLayer = (MapLayer)GUILayout.Toolbar((int)layer, LayerNames);
            if (nextLayer != layer) SelectLayer(nextLayer);
            showOtherLayer = EditorGUILayout.ToggleLeft("显示另一层", showOtherLayer);
            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);
            if (palette != null)
            {
                foreach (var type in palette.Types)
                {
                    if (type == null || type.Layer != layer) continue;
                    Rect row = EditorGUILayout.GetControlRect(false, 48);
                    if (selectedType == type) EditorGUI.DrawRect(row, new Color(.20f, .38f, .52f, .65f));
                    DrawType(new Rect(row.x + 5, row.y + 7, 33, 33), type, true);
                    GUI.Label(new Rect(row.x + 45, row.y + 5, row.width - 48, 20), NameOf(type), EditorStyles.boldLabel);
                    GUI.Label(new Rect(row.x + 45, row.y + 26, row.width - 48, 18), CollisionNames[(int)type.Collision], EditorStyles.miniLabel);
                    if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) && Event.current.button == 0)
                    {
                        FinishStroke(); CancelStructurePlacement(); GridMapPropertyAssets.SaveType(selectedType); selectedType = type; tool = Tool.Brush; GUI.FocusControl(null);
                        Event.current.Use(); Repaint();
                    }
                }
            }
            else EditorGUILayout.HelpBox("选择一个类型面板，或恢复默认面板。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("＋ 新增格子类型")) CreateType();
            if (GUILayout.Button("编辑类型面板")) { Selection.activeObject = palette; EditorGUIUtility.PingObject(palette); }
            if (GUILayout.Button("使用默认类型面板")) { SaveMap(); palette = GridMapAssets.EnsureDefaults(); selectedType = null; SelectLayer(layer); }
            GUILayout.Space(6);
            EditorGUILayout.HelpBox("地形与物件独立绘制。\n右键只擦除当前层。\n可多次放置出生点标记。", MessageType.None);
            GUILayout.EndArea();
        }

        private void DrawSettings(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
            GUILayout.Label("当前类型", EditorStyles.boldLabel);
            var nextType = (MapTileType)EditorGUILayout.ObjectField(selectedType, typeof(MapTileType), false);
            if (nextType != selectedType)
            {
                FinishStroke(); CancelStructurePlacement(); GridMapPropertyAssets.SaveType(selectedType); selectedType = nextType;
                if (selectedType != null) { layer = selectedType.Layer; tool = Tool.Brush; }
            }
            if (selectedType != null)
            {
                EditorGUI.BeginChangeCheck();
                string displayName = EditorGUILayout.TextField("名称", selectedType.DisplayName);
                Color tint = EditorGUILayout.ColorField("显示颜色", selectedType.Tint);
                var sprite = (Sprite)EditorGUILayout.ObjectField("精灵", selectedType.Sprite, typeof(Sprite), false);
                var prefab = (GameObject)EditorGUILayout.ObjectField("运行时预制体", selectedType.Prefab, typeof(GameObject), false);
                var collision = (MapTileCollisionMode)EditorGUILayout.Popup("格子碰撞", (int)selectedType.Collision, CollisionNames);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(selectedType, "修改格子类型");
                    selectedType.DisplayName = displayName; selectedType.Tint = tint;
                    selectedType.Sprite = sprite; selectedType.Prefab = prefab; selectedType.Collision = collision;
                    selectedType.Walkable = collision == MapTileCollisionMode.None;
                    EditorUtility.SetDirty(selectedType); RefreshPreview();
                }
                EditorGUILayout.HelpBox(selectedType.Description ?? "", MessageType.None);
                MapTilePropertyGUI.Draw(selectedType);
                EditorGUILayout.HelpBox("类型设置会影响所有引用它的地图。预制体在运行时生成，伤害、爆炸和开箱逻辑由预制体负责。", MessageType.Info);
                if (GUILayout.Button("在 Inspector 中编辑完整类型")) Selection.activeObject = selectedType;
            }
            GUILayout.Space(12);
            GUILayout.Label("地图设置", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(map == null))
            {
                desiredWidth = Mathf.Clamp(EditorGUILayout.IntField("宽度（格）", desiredWidth), 1, 256);
                desiredHeight = Mathf.Clamp(EditorGUILayout.IntField("高度（格）", desiredHeight), 1, 256);
                desiredCellSize = Mathf.Clamp(EditorGUILayout.FloatField("每格尺寸", desiredCellSize), .01f, 100f);
                if (GUILayout.Button("应用尺寸（保留重叠区域）")) ResizeMap();
                if (map != null)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector2 origin = EditorGUILayout.Vector2Field("地图原点（左下角）", map.Origin);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(map, "修改地图原点"); map.Origin = origin;
                        EditorUtility.SetDirty(map); RefreshPreview();
                    }
                    EditorGUILayout.LabelField("已绘制", "地形 " + map.GetTileCount(MapLayer.Terrain) + " / 物件 " + map.GetTileCount(MapLayer.Objects));
                }
                if (GUILayout.Button("清空当前层…")) ClearLayer();
            }
            GUILayout.Space(12);
            GUILayout.Label("场景预览", EditorStyles.boldLabel);
            preview = (GridMapRenderer)EditorGUILayout.ObjectField(preview, typeof(GridMapRenderer), true);
            if (preview != null)
            {
                var nextTarget = (Transform)EditorGUILayout.ObjectField("跟随对象（玩家）", preview.LoadingTarget, typeof(Transform), true);
                if (nextTarget != preview.LoadingTarget)
                {
                    Undo.RecordObject(preview, "设置地图加载跟随对象"); preview.LoadingTarget = nextTarget;
                    EditorUtility.SetDirty(preview);
                    if (!Application.isPlaying && preview.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(preview.gameObject.scene);
                }
                if (GUILayout.Button("查看分区运行设置")) Selection.activeGameObject = preview.gameObject;
            }
            updatePreview = EditorGUILayout.ToggleLeft("完成笔画后更新已关联的预览", updatePreview);
            if (preview != null && preview.Map != map)
                EditorGUILayout.HelpBox("预览关联了另一张地图。点击“生成到场景”可改为当前地图。", MessageType.Warning);
            EditorGUILayout.HelpBox("地图资产与场景分别保存。预览对象可挂在自己的场景中；进入运行模式时按地图数据构建。", MessageType.None);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCanvas(Rect viewport)
        {
            EditorGUI.DrawRect(viewport, new Color(.095f, .105f, .125f));
            if (map == null)
            {
                GUI.Label(viewport, "新建或选择地图开始编辑", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
                return;
            }
            if (fitPending && viewport.width > 0)
            {
                pixels = Mathf.Clamp(Mathf.Min((viewport.width - 48) / map.Width, (viewport.height - 48) / map.Height), 4, 64);
                pan = new Vector2((viewport.width - map.Width * pixels) / 2, (viewport.height - map.Height * pixels) / 2);
                fitPending = false;
            }
            int controlId = GUIUtility.GetControlID("BigWorldMapCanvas".GetHashCode(), FocusType.Passive, viewport);
            GUI.BeginGroup(viewport);
            Rect local = new Rect(0, 0, viewport.width, viewport.height);
            Event evt = Event.current;
            Vector2 mouse = evt.mousePosition;
            Vector2Int cell = PixelToCell(mouse);
            HandleCanvasInput(evt, local, cell, controlId);
            if (panning || (tool == Tool.Pan && !structurePlacement && !SpawnPlacement))
                EditorGUIUtility.AddCursorRect(local, MouseCursor.Pan, controlId);
            int minX = Mathf.Clamp(Mathf.FloorToInt(-pan.x / pixels), 0, map.Width);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((local.width - pan.x) / pixels), 0, map.Width);
            int minRow = Mathf.Clamp(Mathf.FloorToInt(-pan.y / pixels), 0, map.Height);
            int maxRow = Mathf.Clamp(Mathf.CeilToInt((local.height - pan.y) / pixels), 0, map.Height);
            if (evt.type == EventType.Repaint)
            {
                for (int row = minRow; row < maxRow; row++)
                for (int x = minX; x < maxX; x++)
                {
                    int y = map.Height - 1 - row;
                    Rect tileRect = CellRect(x, y);
                    EditorGUI.DrawRect(tileRect, ((x + y) & 1) == 0 ? new Color(.15f, .17f, .20f) : new Color(.17f, .19f, .22f));
                    if (showOtherLayer || layer == MapLayer.Terrain) DrawType(tileRect, map.GetCell(x, y), false);
                    if (showOtherLayer || layer == MapLayer.Objects)
                    {
                        MapTileType item = map.GetCell(x, y, MapLayer.Objects);
                        float inset = item != null && item.Sprite != null ? 0 : pixels * .13f;
                        Rect objectRect = new Rect(tileRect.x + inset, tileRect.y + inset, tileRect.width - inset * 2, tileRect.height - inset * 2);
                        DrawType(objectRect, item, true);
                    }
                }
                if (showGrid && pixels >= 8)
                {
                    Color line = new Color(0, 0, 0, .25f);
                    for (int x = minX; x <= maxX; x++) EditorGUI.DrawRect(new Rect(pan.x + x * pixels, pan.y + minRow * pixels, 1, (maxRow - minRow) * pixels), line);
                    for (int row = minRow; row <= maxRow; row++) EditorGUI.DrawRect(new Rect(pan.x + minX * pixels, pan.y + row * pixels, (maxX - minX) * pixels, 1), line);
                }
                DrawBorder(new Rect(pan.x, pan.y, map.Width * pixels, map.Height * pixels), new Color(.45f, .57f, .66f));
                if (GridMapRandomWindow.TryGetRegion(map, out var generationRegion))
                {
                    Rect regionRect = CellRect(generationRegion.xMin, generationRegion.yMax - 1);
                    regionRect.size = new Vector2(generationRegion.width * pixels, generationRegion.height * pixels);
                    EditorGUI.DrawRect(regionRect, new Color(1f, .7f, .15f, .08f));
                    DrawBorder(regionRect, new Color(1f, .7f, .15f));
                }
                if (DrawStructureGhost(local, cell)) { }
                else if (painting && tool == Tool.Rectangle)
                {
                    int x = Mathf.Min(rectangleStart.x, rectangleEnd.x), y = Mathf.Max(rectangleStart.y, rectangleEnd.y);
                    Rect highlight = CellRect(x, y);
                    highlight.width = (Mathf.Abs(rectangleStart.x - rectangleEnd.x) + 1) * pixels;
                    highlight.height = (Mathf.Abs(rectangleStart.y - rectangleEnd.y) + 1) * pixels;
                    EditorGUI.DrawRect(highlight, new Color(.25f, .75f, 1f, .2f)); DrawBorder(highlight, Color.cyan);
                }
                else if (local.Contains(mouse) && map.Contains(cell.x, cell.y) && !panning && tool != Tool.Pan && !SpawnPlacement)
                {
                    int size = tool == Tool.Brush || tool == Tool.Eraser ? brushSize : 1;
                    int offset = (size - 1) / 2;
                    Rect highlight = CellRect(cell.x - offset, cell.y - offset + size - 1);
                    highlight.size = Vector2.one * (pixels * size);
                    DrawBorder(highlight, tool == Tool.Eraser ? new Color(1f, .4f, .4f) : Color.white);
                }
                DrawSpawnMarkers(local, cell);
            }
            if (local.Contains(mouse) && map.Contains(cell.x, cell.y) && evt.type == EventType.MouseMove)
                status = "(" + cell.x + ", " + cell.y + ")  地形：" + NameOf(map.GetCell(cell.x, cell.y)) + "  物件：" + NameOf(map.GetCell(cell.x, cell.y, MapLayer.Objects)) + "    左键绘制 · 右键擦除 · 中键平移 · 滚轮缩放";
            GUI.EndGroup();
            if (evt.type == EventType.MouseMove) Repaint();
        }

        private void HandleCanvasInput(Event evt, Rect viewport, Vector2Int cell, int controlId)
        {
            // Complete captured gestures before a house placement can consume their events.
            if (panning && evt.type == EventType.MouseDrag) { pan += evt.delta; evt.Use(); Repaint(); return; }
            if (evt.rawType == EventType.MouseUp && (painting || panning))
            {
                if (painting && tool == Tool.Rectangle)
                    strokeChanged |= GridMapEditing.PaintRectangle(map, rectangleStart, rectangleEnd, strokeType, rectangleOutline, strokeLayer) > 0;
                FinishStroke(); evt.Use(); Repaint(); return;
            }
            if (HandleSpawnInput(evt, viewport, cell)) return;
            if (HandleStructureInput(evt, viewport, cell)) return;
            bool inside = viewport.Contains(evt.mousePosition);
            if (evt.type == EventType.ScrollWheel && inside)
            {
                float next = Mathf.Clamp(pixels * Mathf.Pow(1.13f, -evt.delta.y), 4, 96);
                pan = evt.mousePosition - (evt.mousePosition - pan) * (next / pixels);
                pixels = next; evt.Use(); Repaint(); return;
            }
            if (evt.type == EventType.MouseDown && inside && (evt.button == 2 || (evt.button == 0 && (evt.alt || tool == Tool.Pan))))
            {
                FinishStroke(); GUI.FocusControl(null); panning = true; GUIUtility.hotControl = controlId; evt.Use(); Repaint(); return;
            }
            if (tool == Tool.Pan) return;
            if (evt.type == EventType.MouseDown && inside && map.Contains(cell.x, cell.y) && (evt.button == 0 || evt.button == 1))
            {
                GUI.FocusControl(null);
                if (tool == Tool.Picker && evt.button == 0)
                {
                    GridMapPropertyAssets.SaveType(selectedType);
                    selectedType = map.GetCell(cell.x, cell.y, layer); tool = selectedType == null ? Tool.Eraser : Tool.Brush;
                    evt.Use(); Repaint(); return;
                }
                strokeType = evt.button == 1 || tool == Tool.Eraser ? null : selectedType;
                if (strokeType == null && evt.button == 0 && tool != Tool.Eraser)
                {
                    status = "请先选择当前层的格子类型；擦除请用橡皮或右键。"; evt.Use(); return;
                }
                // A type can change its default layer in another Inspector while this window remains open.
                strokeLayer = strokeType != null ? strokeType.Layer : layer;
                layer = strokeLayer;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(map, "绘制 2D 地图");
                painting = true; strokeChanged = false; GUIUtility.hotControl = controlId;
                rectangleStart = rectangleEnd = lastCell = cell;
                if (tool == Tool.Fill && evt.button == 0)
                {
                    strokeChanged = GridMapEditing.Fill(map, cell.x, cell.y, strokeType, strokeLayer) > 0;
                    FinishStroke();
                }
                else if (tool != Tool.Rectangle)
                    strokeChanged = GridMapEditing.PaintBrush(map, cell.x, cell.y, strokeType, brushSize, strokeLayer) > 0;
                evt.Use(); Repaint(); return;
            }
            if (painting && evt.type == EventType.MouseDrag)
            {
                if (tool == Tool.Rectangle)
                    rectangleEnd = new Vector2Int(Mathf.Clamp(cell.x, 0, map.Width - 1), Mathf.Clamp(cell.y, 0, map.Height - 1));
                else if (inside && map.Contains(cell.x, cell.y))
                {
                    strokeChanged |= GridMapEditing.PaintLine(map, lastCell, cell, strokeType, brushSize, strokeLayer) > 0;
                    lastCell = cell;
                }
                evt.Use(); Repaint();
            }
        }

        private void FinishStroke()
        {
            if (painting)
            {
                if (strokeChanged && map != null) { EditorUtility.SetDirty(map); RefreshPreview(); }
                Undo.CollapseUndoOperations(undoGroup);
            }
            if (painting || panning) GUIUtility.hotControl = 0;
            painting = panning = strokeChanged = false;
        }

        private Vector2Int PixelToCell(Vector2 pixel)
        {
            return new Vector2Int(Mathf.FloorToInt((pixel.x - pan.x) / pixels), map.Height - 1 - Mathf.FloorToInt((pixel.y - pan.y) / pixels));
        }

        private Rect CellRect(int x, int y)
        {
            return new Rect(pan.x + x * pixels, pan.y + (map.Height - 1 - y) * pixels, pixels, pixels);
        }

        private void DrawType(Rect rect, MapTileType type, bool label)
        {
            if (type == null) return;
            if (type.Sprite != null)
            {
                if (DrawSprite(rect, type.Sprite, type.Tint)) return;
                if (!spritePreviews.TryGetValue(type.Sprite, out Texture texture))
                {
                    texture = AssetPreview.GetAssetPreview(type.Sprite) ?? AssetPreview.GetMiniThumbnail(type.Sprite);
                    spritePreviews[type.Sprite] = texture;
                }
                if (texture != null)
                {
                    Color previous = GUI.color; GUI.color = type.Tint;
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true); GUI.color = previous;
                }
                if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
            }
            else
            {
                EditorGUI.DrawRect(rect, type.Tint);
                if (!label || rect.width < 15) return;
                if (badgeStyle == null) badgeStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                badgeStyle.normal.textColor = new Color(.08f, .09f, .12f);
                badgeStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.width * .5f), 10, 21);
                string name = NameOf(type);
                GUI.Label(rect, name.Length > 0 ? name.Substring(0, 1) : "?", badgeStyle);
            }
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static bool DrawSprite(Rect rect, Sprite sprite, Color tint)
        {
            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) return false;
            Rect source;
            try { source = sprite.textureRect; }
            catch (UnityException) { return false; }
            if (source.width <= 0 || source.height <= 0 || sprite.texture == null) return false;
            float scale = Mathf.Min(rect.width / source.width, rect.height / source.height);
            var destination = new Rect(rect.center.x - source.width * scale * .5f, rect.center.y - source.height * scale * .5f, source.width * scale, source.height * scale);
            var uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height);
            Color previous = GUI.color; GUI.color = tint;
            GUI.DrawTextureWithTexCoords(destination, sprite.texture, uv, true); GUI.color = previous;
            return true;
        }

        private static string NameOf(MapTileType type) { return type == null ? "空" : string.IsNullOrWhiteSpace(type.DisplayName) ? type.name : type.DisplayName; }

        private void HandleKeyboard()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (evt.keyCode == KeyCode.Escape && SpawnPlacement) { CancelSpawnPlacement(); evt.Use(); Repaint(); return; }
            if (evt.keyCode == KeyCode.Escape && structurePlacement) { CancelStructurePlacement(); evt.Use(); Repaint(); return; }
            if ((evt.control || evt.command) && evt.keyCode == KeyCode.S) { FinishStroke(); SaveMap(); evt.Use(); return; }
            if ((evt.control || evt.command) && evt.keyCode == KeyCode.Z)
            {
                FinishStroke(); if (evt.shift) Undo.PerformRedo(); else Undo.PerformUndo(); evt.Use(); return;
            }
            if ((evt.control || evt.command) && evt.keyCode == KeyCode.Y) { FinishStroke(); Undo.PerformRedo(); evt.Use(); return; }
            if (evt.control || evt.command || evt.alt) return;
            Tool? next = null;
            if (evt.keyCode == KeyCode.B) next = Tool.Brush;
            if (evt.keyCode == KeyCode.E) next = Tool.Eraser;
            if (evt.keyCode == KeyCode.R) next = Tool.Rectangle;
            if (evt.keyCode == KeyCode.F) next = Tool.Fill;
            if (evt.keyCode == KeyCode.I) next = Tool.Picker;
            if (evt.keyCode == KeyCode.H) next = Tool.Pan;
            if (next.HasValue) { FinishStroke(); CancelStructurePlacement(); tool = next.Value; evt.Use(); Repaint(); }
        }

        private void SaveMap()
        {
            if (map != null && EditorUtility.IsPersistent(map)) AssetDatabase.SaveAssetIfDirty(map);
            if (palette != null)
            {
                if (EditorUtility.IsPersistent(palette)) AssetDatabase.SaveAssetIfDirty(palette);
                foreach (var type in palette.Types) GridMapPropertyAssets.SaveType(type);
            }
            GridMapPropertyAssets.SaveType(selectedType);
            status = map != null && EditorUtility.IsPersistent(map) ? "已保存：" + AssetDatabase.GetAssetPath(map) : "已保存类型和公共属性配置";
        }

        private void NewMap()
        {
            FinishStroke(); GridMapAssets.EnsureFolder(GridMapAssets.Root);
            string path = EditorUtility.SaveFilePanelInProject("新建地图", "NewMap", "asset", "选择地图保存位置", GridMapAssets.Root);
            if (string.IsNullOrEmpty(path)) return;
            if (System.IO.File.Exists(path)) { status = "文件已存在，请使用新的地图文件名。"; return; }
            SetMap(GridMapAssets.CreateMap(path, desiredWidth, desiredHeight, desiredCellSize));
            status = "新地图已创建，可从右侧调整大小。";
        }

        private void SaveCopy()
        {
            FinishStroke(); if (map == null) return;
            string path = EditorUtility.SaveFilePanelInProject("地图另存为", map.name + "_Copy", "asset", "选择新地图位置", GridMapAssets.Root);
            if (string.IsNullOrEmpty(path)) return;
            if (path == AssetDatabase.GetAssetPath(map)) { SaveMap(); return; }
            SaveMap();
            try { SetMap(GridMapAssets.CreateMapCopy(path, map)); }
            catch (Exception exception) { status = "另存为失败：" + exception.Message; return; }
            status = "已创建地图副本；两张地图仍共享类型配置。";
        }

        private void CreateType()
        {
            FinishStroke(); GridMapAssets.EnsureFolder(GridMapAssets.TypesFolder);
            string path = EditorUtility.SaveFilePanelInProject("新增格子类型", "NewTileType", "asset", "选择类型保存位置", GridMapAssets.TypesFolder);
            if (string.IsNullOrEmpty(path)) return;
            if (System.IO.File.Exists(path)) { status = "文件已存在，请使用新的类型文件名。"; return; }
            var type = CreateInstance<MapTileType>(); type.Id = Guid.NewGuid().ToString("N");
            type.DisplayName = "新类型"; type.Layer = layer; type.Tint = Color.white;
            type.PropertySchema = GridMapPropertyAssets.EnsureDefaultSchema();
            AssetDatabase.CreateAsset(type, path);
            if (palette == null) palette = GridMapAssets.EnsureDefaults();
            Undo.RecordObject(palette, "添加格子类型"); palette.Types.Add(type); EditorUtility.SetDirty(palette);
            GridMapPropertyAssets.SaveType(selectedType); selectedType = type; tool = Tool.Brush;
            AssetDatabase.SaveAssetIfDirty(palette); AssetDatabase.SaveAssetIfDirty(type); Selection.activeObject = type;
        }

        private void ResizeMap()
        {
            FinishStroke(); if (map == null) return;
            if ((desiredWidth < map.Width || desiredHeight < map.Height) &&
                !EditorUtility.DisplayDialog("缩小地图", "超出新边界的两层格子会被裁剪，可以撤销。", "调整", "取消")) return;
            Undo.RegisterCompleteObjectUndo(map, "调整地图尺寸");
            map.Resize(desiredWidth, desiredHeight); map.CellSize = desiredCellSize;
            EditorUtility.SetDirty(map); fitPending = true; RefreshPreview();
        }

        private void ClearLayer()
        {
            FinishStroke(); if (map == null) return;
            if (!EditorUtility.DisplayDialog("清空当前层", "仅清空“" + LayerNames[(int)layer] + "”层，可以撤销。", "清空", "取消")) return;
            Undo.RegisterCompleteObjectUndo(map, "清空地图层"); map.Clear(layer); EditorUtility.SetDirty(map); RefreshPreview();
        }

        private void GeneratePreview()
        {
            FinishStroke(); if (map == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { status = "请停止运行后生成场景预览。"; return; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            if (preview == null)
            {
                foreach (var renderer in FindObjectsOfType<GridMapRenderer>())
                    if (renderer.Map == map && renderer.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) { preview = renderer; break; }
            }
            if (preview == null)
            {
                var root = new GameObject("Map_" + map.name); Undo.RegisterCreatedObjectUndo(root, "生成 2D 地图");
                preview = Undo.AddComponent<GridMapRenderer>(root);
            }
            Undo.RegisterFullObjectHierarchyUndo(preview.gameObject, "生成 2D 地图");
            if (preview.GetComponent<GridMapEntityStreamer>() == null) Undo.AddComponent<GridMapEntityStreamer>(preview.gameObject);
            preview.SetMap(map, true); EditorUtility.SetDirty(preview);
            EditorSceneManager.MarkSceneDirty(preview.gameObject.scene); Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = preview.gameObject;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = true;
                Vector3 center = preview.transform.TransformPoint(new Vector3(map.Origin.x + map.Width * map.CellSize * .5f, map.Origin.y + map.Height * map.CellSize * .5f, 0));
                SceneView.lastActiveSceneView.Frame(new Bounds(center, new Vector3(map.Width * map.CellSize, map.Height * map.CellSize, 1)), false);
            }
            status = "地图已生成到当前场景；请单独保存场景。";
        }

        public static void RepaintMapWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<GridMapEditorWindow>()) window.Repaint();
        }

        public static void NotifyMapChanged(GridMapAsset changedMap, string message)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<GridMapEditorWindow>())
            {
                if (window.map != changedMap) continue;
                window.FinishStroke(); window.status = message; window.RefreshPreview(); window.Repaint();
            }
        }

        private void RefreshPreview()
        {
            if (!updatePreview || preview == null || preview.Map != map || !preview.HasGeneratedPreview || preview.IsPreviewCleared || EditorApplication.isPlayingOrWillChangePlaymode) return;
            preview.Rebuild(); EditorSceneManager.MarkSceneDirty(preview.gameObject.scene); SceneView.RepaintAll();
        }
    }
}

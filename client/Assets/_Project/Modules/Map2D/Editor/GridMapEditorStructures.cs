using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed partial class GridMapEditorWindow
    {
        [SerializeField] private int structureLibraryTab;
        [SerializeField] private MapStructureTemplate selectedStructure;
        [SerializeField] private bool structurePlacement;
        [SerializeField] private bool structureFlipX;
        [SerializeField] private bool structureReplaceEmpty = true;
        [SerializeField] private GridMapAsset structureReturnMap;
        [SerializeField] private GridMapAsset editingStructureMap;
        private List<MapStructureTemplate> structures;
        private Vector2 structureScroll;
        private MapStructureTemplate structureDragCandidate;
        private Vector2 structureDragStart;

        private void OnStructureAssetsChanged() { structures = null; Repaint(); }
        private void ReloadStructures() { structures = GridMapStructureAssets.LoadAll(); Repaint(); }

        private void CancelStructurePlacement()
        {
            CancelSpawnPlacement();
            structurePlacement = false;
            structureDragCandidate = null;
        }

        private void DrawStructurePalette()
        {
            if (Event.current.rawType == EventType.MouseUp) structureDragCandidate = null;
            if (structures == null) ReloadStructures();
            EditorGUILayout.HelpBox("拖房子到画布，或选中后点击放置。鼠标位置是房屋左下角。", MessageType.None);
            structureScroll = EditorGUILayout.BeginScrollView(structureScroll);
            foreach (var template in structures)
            {
                if (template == null) continue;
                Rect row = EditorGUILayout.GetControlRect(false, 86);
                if (selectedStructure == template) EditorGUI.DrawRect(row, new Color(.2f, .38f, .52f, .65f));
                DrawStructureThumbnail(new Rect(row.x + 4, row.y + 5, 100, 76), template);
                GUI.Label(new Rect(row.x + 110, row.y + 6, row.width - 112, 36),
                    new GUIContent(template.DisplayName, template.Description), new GUIStyle(EditorStyles.boldLabel) { wordWrap = true });
                GUI.Label(new Rect(row.x + 110, row.y + 43, row.width - 112, 18), template.Width + " × " + template.Height, EditorStyles.miniLabel);
                GUI.Label(new Rect(row.x + 110, row.y + 62, row.width - 112, 18), "拖放到地图", EditorStyles.miniLabel);
                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition))
                {
                    FinishStroke(); CancelSpawnPlacement(); selectedStructure = template; structurePlacement = true;
                    structureDragCandidate = template; structureDragStart = evt.mousePosition;
                    GUI.FocusControl(null); evt.Use(); Repaint();
                }
                else if (evt.type == EventType.MouseDrag && evt.button == 0 && structureDragCandidate == template &&
                    (evt.mousePosition - structureDragStart).sqrMagnitude > 16f)
                {
                    DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new Object[] { template };
                    DragAndDrop.StartDrag("放置 " + template.DisplayName); structureDragCandidate = null;
                    evt.Use();
                }
            }
            if (structures.Count == 0) EditorGUILayout.HelpBox("暂无房屋模板，可以从地图区域保存。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            structureFlipX = EditorGUILayout.ToggleLeft("水平镜像", structureFlipX);
            structureReplaceEmpty = EditorGUILayout.ToggleLeft("覆盖房屋范围内的空白格", structureReplaceEmpty);
            EditorGUILayout.HelpBox(structureReplaceEmpty ? "放置会替换整片范围的两层内容，保留房间内部空隙。可整次撤销。" : "仅粘贴模板里非空的格子，空白处保留原地图。", MessageType.None);
            using (new EditorGUI.DisabledScope(selectedStructure == null || selectedStructure.Layout == null))
            {
                if (GUILayout.Button("编辑选中房屋")) OpenStructureEditor(selectedStructure);
            }
            if (structurePlacement && GUILayout.Button("结束放置（Esc / 右键）")) CancelStructurePlacement();
            using (new EditorGUI.DisabledScope(map == null))
            {
                if (GUILayout.Button("从地图区域保存房屋…")) { FinishStroke(); CancelStructurePlacement(); GridMapStructureCaptureWindow.Open(map); }
            }
            if (GUILayout.Button("刷新房屋库")) ReloadStructures();
        }

        private void DrawStructureThumbnail(Rect area, MapStructureTemplate template)
        {
            EditorGUI.DrawRect(area, new Color(.09f, .11f, .14f));
            if (template.Layout == null) return;
            float size = Mathf.Min(area.width / template.Width, area.height / template.Height);
            Vector2 offset = area.center - new Vector2(template.Width * size, template.Height * size) * .5f;
            for (int y = 0; y < template.Height; y++)
                for (int x = 0; x < template.Width; x++)
                {
                    Rect cell = new Rect(offset.x + x * size, offset.y + (template.Height - 1 - y) * size, size, size);
                    DrawType(cell, template.Layout.GetCell(x, y), false);
                    DrawType(cell, template.Layout.GetCell(x, y, MapLayer.Objects), false);
                }
        }

        private static MapStructureTemplate DraggedStructure()
        {
            MapStructureTemplate result = null;
            foreach (Object item in DragAndDrop.objectReferences)
            {
                if (!(item is MapStructureTemplate template)) continue;
                if (result != null) return null;
                result = template;
            }
            return result;
        }

        private bool CanPlaceStructure(MapStructureTemplate template, Vector2Int origin)
        {
            return map != null && template != null && template.Layout != null && template.Layout.TileCount > 0 &&
                !EditorApplication.isPlayingOrWillChangePlaymode && origin.x >= 0 && origin.y >= 0 &&
                (long)origin.x + template.Width <= map.Width && (long)origin.y + template.Height <= map.Height;
        }

        private bool PlaceStructure(MapStructureTemplate template, Vector2Int origin)
        {
            FinishStroke();
            if (!CanPlaceStructure(template, origin))
            {
                status = "房屋需要完整放在地图范围内，且模板不能为空；请在停止运行后放置。";
                return false;
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(map, "放置房屋 " + template.DisplayName);
            if (!GridMapStructureEditing.TryPlace(map, template, origin, structureFlipX, structureReplaceEmpty, out int changed, out string error))
            {
                status = error; return false;
            }
            Undo.CollapseUndoOperations(group);
            if (changed > 0) { EditorUtility.SetDirty(map); RefreshPreview(); }
            selectedStructure = template;
            status = "已放置“" + template.DisplayName + "”，可继续放置；Esc 或右键结束，Ctrl+Z 撤销。";
            Repaint(); return true;
        }

        private bool HandleStructureInput(Event evt, Rect viewport, Vector2Int cell)
        {
            bool inside = viewport.Contains(evt.mousePosition);
            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && inside)
            {
                var template = DraggedStructure();
                if (template == null) return false;
                bool allowed = CanPlaceStructure(template, cell);
                DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (evt.type == EventType.DragPerform && allowed)
                {
                    DragAndDrop.AcceptDrag();
                    PlaceStructure(template, cell); structurePlacement = true;
                    DragAndDrop.objectReferences = new Object[0];
                }
                evt.Use(); Repaint(); return true;
            }
            if (evt.type == EventType.DragExited) { Repaint(); return false; }
            if (!structurePlacement || !inside || evt.alt) return false;
            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                CancelStructurePlacement(); evt.Use(); Repaint(); return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                PlaceStructure(selectedStructure, cell); evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0) { evt.Use(); return true; }
            return false;
        }

        private bool DrawStructureGhost(Rect viewport, Vector2Int origin)
        {
            if (!viewport.Contains(Event.current.mousePosition)) return false;
            var template = DraggedStructure();
            if (template == null && structurePlacement) template = selectedStructure;
            if (template == null || template.Layout == null) return false;
            bool valid = CanPlaceStructure(template, origin);
            Rect bounds = CellRect(origin.x, origin.y + template.Height - 1);
            bounds.size = new Vector2(template.Width * pixels, template.Height * pixels);
            EditorGUI.DrawRect(bounds, valid ? new Color(.25f, .85f, .55f, .16f) : new Color(1f, .25f, .2f, .2f));
            for (int y = 0; y < template.Height; y++)
                for (int x = 0; x < template.Width; x++)
                {
                    int fromX = structureFlipX ? template.Width - 1 - x : x;
                    Rect cell = CellRect(origin.x + x, origin.y + y);
                    if (!cell.Overlaps(viewport)) continue;
                    DrawType(cell, template.Layout.GetCell(fromX, y), false);
                    DrawType(cell, template.Layout.GetCell(fromX, y, MapLayer.Objects), false);
                }
            DrawBorder(bounds, valid ? new Color(.3f, 1f, .65f) : new Color(1f, .35f, .3f));
            return true;
        }

        private void OpenStructureEditor(MapStructureTemplate template)
        {
            if (template == null || template.Layout == null) return;
            if (map != editingStructureMap) structureReturnMap = map;
            editingStructureMap = template.Layout;
            SetMap(template.Layout); structureLibraryTab = 0;
            status = "正在编辑房屋“" + template.DisplayName + "”。保存后用于下次放置，地图里已放置的房屋保持独立。";
        }

        public static void EditStructure(MapStructureTemplate template)
        {
            Open(); GetWindow<GridMapEditorWindow>().OpenStructureEditor(template);
        }

        public static void SelectStructure(MapStructureTemplate template)
        {
            var window = GetWindow<GridMapEditorWindow>();
            window.ReloadStructures(); window.selectedStructure = template;
            window.structureLibraryTab = 1; window.structurePlacement = true; window.Repaint();
        }
    }
}

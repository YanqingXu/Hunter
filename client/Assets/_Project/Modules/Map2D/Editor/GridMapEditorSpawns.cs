using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed partial class GridMapEditorWindow
    {
        [SerializeField] private string placingSpawnId;
        private bool SpawnPlacement { get { return !string.IsNullOrEmpty(placingSpawnId); } }

        public static void BeginSpawnPlacement(GridMapAsset target, string id)
        {
            OpenMap(target);
            var window = GetWindow<GridMapEditorWindow>();
            window.FinishStroke(); window.CancelStructurePlacement();
            window.placingSpawnId = id;
            window.status = "左键选择生成点位置 · 右键 / Esc 取消 · 中键拖动画布";
            window.Focus(); window.Repaint();
        }

        private void CancelSpawnPlacement() { placingSpawnId = null; }

        private bool HandleSpawnInput(Event evt, Rect viewport, Vector2Int cell)
        {
            if (!SpawnPlacement || !viewport.Contains(evt.mousePosition) || evt.alt) return false;
            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                CancelSpawnPlacement(); evt.Use(); Repaint(); return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                var definition = map.Spawns.Find(s => s != null && s.Id == placingSpawnId);
                if (definition == null) { CancelSpawnPlacement(); return true; }
                if (map.Contains(cell.x, cell.y))
                {
                    Undo.RecordObject(map, "移动角色生成点"); definition.Cell = cell;
                    EditorUtility.SetDirty(map); status = "已设置生成点：" + definition.DisplayName + " (" + cell.x + ", " + cell.y + ")";
                    CancelSpawnPlacement();
                    foreach (var window in Resources.FindObjectsOfTypeAll<GridMapSpawnWindow>()) window.Repaint();
                }
                else status = "请选择地图内的位置。";
                evt.Use(); Repaint(); return true;
            }
            return false;
        }

        private void DrawSpawnMarkers(Rect viewport, Vector2Int hovered)
        {
            foreach (var definition in map.Spawns)
            {
                if (definition == null || !definition.Enabled) continue;
                Vector2Int anchor = definition.Id == placingSpawnId && viewport.Contains(Event.current.mousePosition) ? hovered : definition.Cell;
                if (!map.Contains(anchor.x, anchor.y)) continue;
                Rect rect = CellRect(anchor.x, anchor.y);
                if (!viewport.Overlaps(rect)) continue;
                Color color = definition.Kind == MapSpawnKind.Monster ? new Color(1f, .4f, .3f) : new Color(.3f, 1f, .7f);
                EditorGUI.DrawRect(new Rect(rect.x + rect.width * .35f, rect.y + 2, rect.width * .3f, rect.height - 4), color);
                GUI.Label(new Rect(rect.x + 3, rect.y - 18, 150, 18), definition.DisplayName + " ×" + definition.Count, EditorStyles.whiteMiniLabel);
            }
        }
    }
}

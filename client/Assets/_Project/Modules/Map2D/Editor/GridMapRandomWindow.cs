using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    public sealed class GridMapRandomWindow : EditorWindow
    {
        // Retain the generator while keeping its editor entry points disabled by default.
        internal static bool IsEnabled
        {
            get
            {
#if BIGWORLD_MAP_RANDOM_GENERATION
                return true;
#else
                return false;
#endif
            }
        }

        [SerializeField] private GridMapAsset map;
        [SerializeField] private Vector2Int regionStart;
        [SerializeField] private Vector2Int regionSize = new Vector2Int(20, 12);
        [SerializeField] private bool generateTerrain = true;
        [SerializeField] private bool placeSpawn = true;
        [SerializeField] private MapTileType ground;
        [SerializeField] private MapTileType barrel;
        [SerializeField] private MapTileType chest;
        [SerializeField] private MapTileType spawn;
        [SerializeField] private string barrelCount = "";
        [SerializeField] private string chestCount = "";
        [SerializeField] private string seedText = "";
        private Vector2 scroll;
        private string message;
        private bool messageIsError;
        private int? lastSeed;
        private static GridMapRandomWindow active;

#if BIGWORLD_MAP_RANDOM_GENERATION
        [MenuItem("Tools/BigWorld/地图/随机生成地图")]
#endif
        public static void OpenFromSelection() { Open(Selection.activeObject as GridMapAsset, null); }

        public static void Open(GridMapAsset selectedMap, MapTilePalette palette)
        {
            if (!IsEnabled) return;
            var window = GetWindow<GridMapRandomWindow>();
            window.titleContent = new GUIContent("随机地图生成");
            window.minSize = new Vector2(440, 580);
            if (window.map != selectedMap) window.SetMap(selectedMap);
            if (palette == null) palette = GridMapAssets.EnsureDefaults();
            window.ground = FindType(palette, "Ground", "ground");
            window.barrel = FindType(palette, "ExplosiveBarrel", "explosive");
            window.chest = FindType(palette, "TreasureChest", "chest");
            window.spawn = FindType(palette, "SpawnPoint", "spawn");
            if (window.spawn == null) window.placeSpawn = false;
            window.Show();
            GridMapEditorWindow.RepaintMapWindows();
        }

        private static MapTileType FindType(MapTilePalette palette, string id, string tag)
        {
            if (palette == null) return null;
            return palette.Types.Find(type => type != null && type.Id == id)
                ?? palette.Types.Find(type => type != null && type.HasTag(tag));
        }

        private void OnEnable()
        {
            active = this;
            titleContent = new GUIContent("随机地图生成");
            minSize = new Vector2(440, 580);
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            if (active == this) active = null;
            GridMapEditorWindow.RepaintMapWindows();
        }

        private void SetMap(GridMapAsset value)
        {
            map = value;
            UseWholeMap();
            message = null;
        }

        private void UseWholeMap()
        {
            regionStart = Vector2Int.zero;
            if (map != null) regionSize = new Vector2Int(map.Width, map.Height);
            GridMapEditorWindow.RepaintMapWindows();
        }

        public static bool TryGetRegion(GridMapAsset target, out RectInt region)
        {
            region = default;
            if (!IsEnabled || active == null || active.map != target || target == null) return false;
            if (active.regionStart.x < 0 || active.regionStart.y < 0 || active.regionSize.x <= 0 || active.regionSize.y <= 0 ||
                (long)active.regionStart.x + active.regionSize.x > target.Width || (long)active.regionStart.y + active.regionSize.y > target.Height) return false;
            region = new RectInt(active.regionStart, active.regionSize);
            return true;
        }

        private void OnGUI()
        {
            if (!IsEnabled)
            {
                EditorGUILayout.HelpBox("随机地图功能已暂时停用。", MessageType.Info);
                if (GUILayout.Button("关闭窗口")) Close();
                return;
            }
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.keyCode == KeyCode.S)
            {
                Save(); evt.Use();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            var selectedMap = (GridMapAsset)EditorGUILayout.ObjectField("地图", map, typeof(GridMapAsset), false);
            if (selectedMap != map) SetMap(selectedMap);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("生成区域（格子坐标）", EditorStyles.boldLabel);
            regionStart = EditorGUILayout.Vector2IntField("左下角 X / Y", regionStart);
            regionSize = EditorGUILayout.Vector2IntField("宽度 / 高度", regionSize);
            using (new EditorGUI.DisabledScope(map == null))
            {
                if (GUILayout.Button("使用整张地图")) UseWholeMap();
            }
            if (map != null) EditorGUILayout.LabelField("地图范围", "X：0～" + (map.Width - 1) + "，Y：0～" + (map.Height - 1));

            EditorGUILayout.Space(8);
            generateTerrain = EditorGUILayout.ToggleLeft("生成起伏地面和悬空平台", generateTerrain);
            using (new EditorGUI.DisabledScope(!generateTerrain))
                ground = (MapTileType)EditorGUILayout.ObjectField("地面类型", ground, typeof(MapTileType), false);
            if (!generateTerrain) EditorGUILayout.HelpBox("仅重新摆放区域内物件，保留现有地形。", MessageType.None);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("物件数量", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("数量留空＝随机，填 0＝不生成，填写非负整数＝生成后的准确数量。物件会放在有地形支撑的空格上。", MessageType.None);
            barrel = (MapTileType)EditorGUILayout.ObjectField("炸药桶类型", barrel, typeof(MapTileType), false);
            barrelCount = EditorGUILayout.TextField("炸药桶数量（可留空）", barrelCount);
            chest = (MapTileType)EditorGUILayout.ObjectField("宝箱类型", chest, typeof(MapTileType), false);
            chestCount = EditorGUILayout.TextField("宝箱数量（可留空）", chestCount);
            placeSpawn = EditorGUILayout.ToggleLeft("生成 1 个出生点", placeSpawn);
            using (new EditorGUI.DisabledScope(!placeSpawn))
                spawn = (MapTileType)EditorGUILayout.ObjectField("出生点类型", spawn, typeof(MapTileType), false);

            EditorGUILayout.Space(8);
            seedText = EditorGUILayout.TextField(new GUIContent("随机种子（可留空）", "留空时每次生成新种子；填写相同种子、相同地图和配置可复现结果。"), seedText);
            if (lastSeed.HasValue && GUILayout.Button("使用上次种子：" + lastSeed.Value)) seedText = lastSeed.Value.ToString(CultureInfo.InvariantCulture);
            if (EditorGUI.EndChangeCheck()) { message = null; GridMapEditorWindow.RepaintMapWindows(); }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(generateTerrain
                ? "生成会替换区域内两层的全部格子，包括原有尖刺和自定义物件。区域外保持原样；Ctrl+Z 可整次撤销。"
                : "生成会替换区域内全部物件，包括原有尖刺和自定义物件。地形和区域外保持原样；Ctrl+Z 可整次撤销。", MessageType.Info);
            using (new EditorGUI.DisabledScope(map == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("随机生成并替换区域", GUILayout.Height(34))) Generate();
                if (GUILayout.Button("保存地图")) Save();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.HelpBox("请停止运行后编辑地图。", MessageType.None);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageIsError ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        public static bool TryParseCount(string text, string label, out int? value, out string error)
        {
            value = null; error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
            {
                error = label + "请填写非负整数，或留空使用随机数量。";
                return false;
            }
            value = parsed;
            return true;
        }

        private bool Generate()
        {
            if (!IsEnabled) return false;
            messageIsError = true;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { message = "请停止运行后编辑地图。"; return false; }
            if (!TryParseCount(barrelCount, "炸药桶数量", out var barrels, out message) ||
                !TryParseCount(chestCount, "宝箱数量", out var chests, out message)) return false;
            int seed;
            if (string.IsNullOrWhiteSpace(seedText)) seed = Guid.NewGuid().GetHashCode();
            else if (!int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                message = "随机种子请填写整数，或留空自动选择。"; return false;
            }
            var options = new GridMapRandomOptions
            {
                Region = new RectInt(regionStart, regionSize), Ground = ground, Barrel = barrel, Chest = chest, Spawn = spawn,
                GenerateTerrain = generateTerrain, PlaceSpawn = placeSpawn, BarrelCount = barrels, ChestCount = chests, Seed = seed
            };
            if (!GridMapRandomGenerator.TryCreatePlan(map, options, out var plan, out message)) return false;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("随机生成地图区域");
            Undo.RegisterCompleteObjectUndo(map, "随机生成地图区域");
            try { plan.Apply(map); }
            catch (InvalidOperationException exception) { message = exception.Message; return false; }
            EditorUtility.SetDirty(map);
            Undo.CollapseUndoOperations(group);
            lastSeed = seed;
            messageIsError = false;
            message = "已生成：炸药桶 " + plan.BarrelCount + "，宝箱 " + plan.ChestCount + "，出生点 " + plan.SpawnCount +
                "。种子：" + seed + "。可继续手工编辑，完成后保存地图。";
            GridMapEditorWindow.NotifyMapChanged(map, message);
            return true;
        }

        private void Save()
        {
            if (map == null || !EditorUtility.IsPersistent(map)) return;
            AssetDatabase.SaveAssetIfDirty(map);
            GridMapPropertyAssets.SaveType(ground); GridMapPropertyAssets.SaveType(barrel);
            GridMapPropertyAssets.SaveType(chest); GridMapPropertyAssets.SaveType(spawn);
            messageIsError = false; message = "地图已保存。";
        }
    }
}

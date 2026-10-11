using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Gameplay;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using BigWorld.YouYou2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace BigWorld.Editor
{
    /// <summary>Builds an independent SSW level inspired by the supplied branching route map.</summary>
    public static class HunterReferenceMapBuilder
    {
        public const string ScenePath = HunterReferenceMapLayout.Root + "/Scenes/ReferenceCitadel.unity";
        private const string RequestPath = "Library/HunterReferenceMap.request";
        private const string ResultPath = "Library/HunterReferenceMap.result.json";
        private const string SourceScene = "Assets/_Project/Game/SSWMaps/Scenes/ForestOutpost.unity";
        private static readonly Color Outside = new Color(.035f, .055f, .095f);

        [Serializable] private sealed class Report
        {
            public bool success;
            public string error;
            public string generatedUtc;
            public string map;
            public string scene;
            public List<string> checks = new List<string>();
            public List<string> previews = new List<string>();
        }

        [InitializeOnLoadMethod]
        private static void Schedule() => EditorApplication.delayCall += CheckRequest;

        private static void CheckRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += CheckRequest; return; }
            if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string stamp = File.GetLastWriteTimeUtc(RequestPath).Ticks.ToString();
            if (SessionState.GetString("Hunter.Reference.Request", "") == stamp) return;
            SessionState.SetString("Hunter.Reference.Request", stamp);
            if (Build()) { File.Delete(RequestPath); Open(); }
        }

        [MenuItem("Tools/BigWorld/SSW地图资源/参考地图/生成并打开 %#F10", false, 30)]
        public static void Generate() { if (Build()) Open(); }

        [MenuItem("Tools/BigWorld/SSW地图资源/参考地图/打开王城地下迷宫", false, 31)]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            GridMapRenderer renderer = Find<GridMapRenderer>(scene);
            GridMapEditorWindow.OpenMap(renderer.Map);
            var window = EditorWindow.GetWindow<GridMapEditorWindow>();
            var settings = new SerializedObject(window);
            settings.FindProperty("palette").objectReferenceValue = HunterReferenceMapLayout.LoadPalette();
            settings.FindProperty("selectedType").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = renderer.gameObject;
            if (SceneView.lastActiveSceneView)
            {
                SceneView.lastActiveSceneView.in2DMode = true;
                SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(96, 72, 0), new Vector3(194, 146, 1)), false);
            }
            Debug.Log("HUNTER_REFERENCE_MAP_OPEN: " + scene.path);
        }

        public static void GenerateAndValidate()
        { if (!Build()) throw new InvalidOperationException("参考地图生成失败，见 " + ResultPath); }

        private static bool Build()
        {
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), map = HunterReferenceMapLayout.MapPath, scene = ScenePath };
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = default;
            bool ownsScene = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Play Mode。");
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException("保留当前场景，已取消生成。");
                if (string.IsNullOrEmpty(previous.path) && !previous.isDirty)
                { previous = EditorSceneManager.OpenScene(SourceScene); }
                GridMapAsset map = HunterReferenceMapLayout.EnsureMap();
                ValidateMap(map, report);
                Folder(HunterReferenceMapLayout.Root + "/Scenes");
                Folder(HunterReferenceMapLayout.Root + "/Previews");
                bool create = !File.Exists(ScenePath);
                if (create && !AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new IOException("无法创建独立场景。");
                scene = SceneManager.GetSceneByPath(ScenePath);
                ownsScene = !scene.IsValid() || !scene.isLoaded;
                if (ownsScene) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                if (create) ConfigureScene(scene, map);
                bool annotationsChanged = SetAnnotations(scene, false);
                ValidateScene(scene, map, report);
                if ((create || annotationsChanged) && !EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("无法保存独立场景。");
                ExportPreviews(scene, report);
                // Preview-only annotations and isolation layers have been restored.
                // Persist their inactive state so opening the finished level needs no save prompt.
                if (scene.isDirty && !EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("无法保存预览复原后的场景。");
                AssetDatabase.SaveAssets();
                report.success = true;
                Debug.Log("HUNTER_REFERENCE_MAP_READY: " + report.checks.Count + " checks; " + ScenePath);
            }
            catch (Exception error)
            { report.error = error.ToString(); Debug.LogError("HUNTER_REFERENCE_MAP_FAILED: " + error.Message); }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (ownsScene && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                File.WriteAllText(ResultPath, JsonUtility.ToJson(report, true));
            }
            return report.success;
        }

        private static void ConfigureScene(Scene scene, GridMapAsset map)
        {
            GridMapRenderer renderer = Find<GridMapRenderer>(scene);
            PlayerController2D player = Find<PlayerController2D>(scene);
            LevelSession2D level = Find<LevelSession2D>(scene);
            if (!renderer || !player || !level) throw new InvalidOperationException("原关卡运行组件不完整。");
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name.StartsWith("Backdrop") || root.GetComponent<Checkpoint2D>()) Object.DestroyImmediate(root);
            renderer.gameObject.name = "World - 王城地下迷宫";
            renderer.SetMap(map);
            renderer.LoadingTarget = player.transform;
            renderer.LoadingRadiusCells = new Vector2Int(24, 18);
            player.transform.position = Feet(HunterReferenceMapLayout.StartCell);
            level.StartPoint.position = player.transform.position;
            level.RequiredKills = 3;
            level.FallDeathY = -4;
            level.ExitPoint.position = Feet(HunterReferenceMapLayout.ExitCell);
            level.ExitPoint.name = "Exit - 荒废植物园";
            Camera camera = Find<Camera>(scene);
            camera.backgroundColor = Outside;
            camera.transform.position = player.transform.position + new Vector3(7, 3.5f, -10);
            Sprite pixel = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Game/Art/Environment/WhitePixel.png");
            for (int i = 0; i < HunterReferenceMapLayout.CheckpointCells.Length; i++)
                Checkpoint(HunterReferenceMapLayout.CheckpointCells[i], i + 1, pixel);
            Backdrop(scene, pixel);
        }

        private static void Backdrop(Scene scene, Sprite pixel)
        {
            var root = new GameObject("Backdrop - reference rooms, no collision");
            var grid = root.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            var tileObject = new GameObject("Room brick background");
            tileObject.transform.SetParent(root.transform, false);
            var tilemap = tileObject.AddComponent<Tilemap>();
            tileObject.AddComponent<TilemapRenderer>().sortingOrder = -40;
            Color[] tints = { new Color(.46f, .57f, .86f), new Color(.39f, .51f, .60f), new Color(.40f, .66f, .46f) };
            Tile[] tiles = new Tile[3];
            Folder(HunterReferenceMapLayout.Root + "/Data/BackdropTiles");
            for (int i = 0; i < tiles.Length; i++)
            {
                string path = HunterReferenceMapLayout.Root + "/Data/BackdropTiles/Room" + i + ".asset";
                tiles[i] = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (!tiles[i])
                {
                    tiles[i] = ScriptableObject.CreateInstance<Tile>();
                    tiles[i].sprite = HunterReferenceMapLayout.SourceSprite(i == 1 ? "MainLevBuild_841" : "MainLevBuild_840");
                    tiles[i].color = tints[i]; tiles[i].colliderType = Tile.ColliderType.None;
                    AssetDatabase.CreateAsset(tiles[i], path);
                }
            }
            bool[,] inside = HunterReferenceMapLayout.CreateInteriorMask();
            for (int y = 0; y < HunterReferenceMapLayout.Height; y++)
                for (int x = 0; x < HunterReferenceMapLayout.Width; x++)
                {
                    if (!inside[x, y]) continue;
                    int theme = 0;
                    foreach (var region in HunterReferenceMapLayout.Regions)
                        if (region.Rect.Contains(new Vector2Int(x, y))) theme = region.Theme;
                    tilemap.SetTile(new Vector3Int(x, y, 0), tiles[Mathf.Clamp(theme, 0, 2)]);
                }
            foreach (var region in HunterReferenceMapLayout.Regions)
            {
                RectInt r = region.Rect;
                if (r.width >= 16 && r.height >= 7 && region.Theme == 0)
                {
                    Decoration(root.transform, "Stone pillar - " + region.Name, "Pillar", new Vector2(r.x + 3, r.y), .72f, new Color(.55f, .61f, .73f), -22);
                    if (r.width > 24) Decoration(root.transform, "Stone pillar - " + region.Name, "Pillar", new Vector2(r.xMax - 4, r.y), .72f, new Color(.55f, .61f, .73f), -22);
                }
                if (r.width >= 18 && r.height >= 5)
                    for (int x = r.x + 5; x < r.xMax - 3; x += 12) Torch(root.transform, pixel, new Vector2(x, r.y + 2.2f));
            }
            Decoration(root.transform, "城墙入口石门", "Arch", Feet(HunterReferenceMapLayout.StartCell), 1.1f, new Color(.7f, .8f, .9f), -15);
            Decoration(root.transform, "植物园出口石门", "Arch", Feet(HunterReferenceMapLayout.ExitCell), 1.1f, new Color(.68f, .9f, .73f), -15);
            Decoration(root.transform, "Garden tree", "TreeSmall", new Vector2(115, 32), .7f, new Color(.62f, .83f, .63f), -20);
            foreach (int x in new[] { 106, 118, 125 }) Decoration(root.transform, "Garden bush", "Bush", new Vector2(x, 32), .7f, new Color(.68f, .86f, .63f), -16);
            foreach (Vector2Int cell in HunterReferenceMapLayout.CheckpointCells)
                Decoration(root.transform, "Checkpoint arch", "Arch", Feet(cell), .85f, new Color(.60f, .70f, .85f), -18);
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Game/UI/Fonts/NotoSansCJKsc-Regular.otf");
            Label(root.transform, font, "城墙入口", new Vector2(30, 79));
            Label(root.transform, font, "王城长厅", new Vector2(92, 140));
            Label(root.transform, font, "地下宝库", new Vector2(35, 69));
            Label(root.transform, font, "折返高塔", new Vector2(159, 116));
            Label(root.transform, font, "荒废植物园", new Vector2(119, 42));
        }

        private static void Decoration(Transform parent, string name, string key, Vector2 position, float scale, Color color, int order)
        {
            Sprite sprite = HunterSswMaterials.Sprite(key);
            var item = new GameObject(name); item.transform.SetParent(parent, false);
            item.transform.localPosition = new Vector3(position.x, position.y - sprite.bounds.min.y * scale, 0);
            item.transform.localScale = Vector3.one * scale;
            var visual = item.AddComponent<SpriteRenderer>(); visual.sprite = sprite; visual.color = color; visual.sortingOrder = order;
        }

        private static SpriteRenderer Rect(Transform parent, Sprite pixel, string name, Vector2 position, Vector2 size, Color color, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(parent, false);
            item.transform.localPosition = position; item.transform.localScale = new Vector3(size.x, size.y, 1);
            var visual = item.AddComponent<SpriteRenderer>(); visual.sprite = pixel; visual.color = color; visual.sortingOrder = order;
            return visual;
        }

        private static void Torch(Transform parent, Sprite pixel, Vector2 position)
        {
            Rect(parent, pixel, "Torch glow", position, new Vector2(1.3f, 1.5f), new Color(1, .53f, .16f, .055f), -32);
            Rect(parent, pixel, "Torch bracket", position + new Vector2(0, -.3f), new Vector2(.12f, .45f), new Color(.40f, .28f, .18f), -12);
            Rect(parent, pixel, "Amber flame", position, new Vector2(.19f, .32f), new Color(1, .48f, .08f), -11);
            Rect(parent, pixel, "Flame core", position + new Vector2(0, -.04f), new Vector2(.09f, .16f), new Color(1, .86f, .38f), -10);
        }

        private static void Label(Transform parent, Font font, string value, Vector2 position)
        {
            if (!font) return;
            var item = new GameObject("Area label - " + value); item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            var text = item.AddComponent<TextMesh>(); text.text = value; text.font = font;
            text.fontSize = 64; text.characterSize = .37f; text.anchor = TextAnchor.MiddleCenter;
            text.color = new Color(.69f, .78f, .86f);
            var renderer = item.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material; renderer.sortingOrder = 25;
            item.SetActive(false);
        }

        private static void Checkpoint(Vector2Int cell, int index, Sprite pixel)
        {
            var item = new GameObject("Checkpoint " + index); item.transform.position = Feet(cell);
            var box = item.AddComponent<BoxCollider2D>(); box.isTrigger = true;
            box.size = new Vector2(1.6f, 2.5f); box.offset = new Vector2(0, 1.25f);
            var checkpoint = item.AddComponent<Checkpoint2D>(); checkpoint.Order = index;
            checkpoint.DisplayName = new[] { "中央枢纽", "上层长廊", "地下宝库", "植物园" }[Mathf.Min(index - 1, 3)];
            var marker = new GameObject("Respawn point"); marker.transform.SetParent(item.transform, false); checkpoint.SpawnPoint = marker.transform;
            Rect(item.transform, pixel, "Pole", new Vector2(0, 1), new Vector2(.09f, 2), new Color(.42f, .59f, .58f), 17);
            checkpoint.Flag = Rect(item.transform, pixel, "Green flag", new Vector2(.32f, 1.75f), new Vector2(.64f, .43f), new Color(.25f, .83f, .57f), 18);
        }

        private static void ValidateMap(GridMapAsset map, Report report)
        {
            report.checks.AddRange(HunterReferenceMapLayout.ValidateDesign(map));
            Check(report, map && map.Width == 192 && map.Height == 144, "192 × 144 独立分支地图");
            var ids = new HashSet<string>();
            foreach (MapTileType type in HunterReferenceMapLayout.LoadPalette().Types)
            {
                Check(report, type && type.Sprite && ids.Add(type.Id), "类型引用及唯一ID: " + (type ? type.name : "null"));
                if (type.Collision != MapTileCollisionMode.None)
                    Check(report, Vector2.Distance(type.Sprite.bounds.size, Vector2.one) < .01f, "碰撞精灵 1 × 1: " + type.name);
            }
            var points = new List<Vector2Int> { HunterReferenceMapLayout.StartCell, HunterReferenceMapLayout.ExitCell };
            points.AddRange(HunterReferenceMapLayout.GuardCells); points.AddRange(HunterReferenceMapLayout.CheckpointCells);
            foreach (Vector2Int point in points)
            {
                MapTileType support = map.GetCell(point.x, point.y - 1);
                bool clear = support && support.Collision != MapTileCollisionMode.None;
                for (int y = point.y; y <= point.y + 1; y++)
                {
                    MapTileType cell = map.GetCell(point.x, y);
                    if (cell && cell.Collision == MapTileCollisionMode.Solid) clear = false;
                }
                Check(report, clear, "关键落点支撑和净空: " + point);
            }
            Check(report, map.Spawns.Count == 3, "三个独立守卫生成点");
            for (int i = 0; i < map.Spawns.Count; i++)
                Check(report, map.Spawns[i].Prefab && map.Spawns[i].Id == "trial-guard-" + (i + 1), "守卫通关关联: " + map.Spawns[i].Id);
        }

        private static void ValidateScene(Scene scene, GridMapAsset map, Report report)
        {
            GridMapRenderer renderer = Find<GridMapRenderer>(scene);
            PlayerController2D player = Find<PlayerController2D>(scene);
            WorldSession2D world = Find<WorldSession2D>(scene);
            LevelSession2D level = Find<LevelSession2D>(scene);
            Check(report, renderer && renderer.Map == map && renderer.HasGeneratedPreview, "完整地图与可编辑 Tilemap 预览");
            Check(report, player && world && level && level.World == world && player.World == world && world.Map == renderer &&
                level.Player == player && level.StartPoint && level.ExitPoint && level.RequiredKills == 3 && Find<GameServices2D>(scene), "现有游戏服务、玩家、相机和关卡引用");
            Check(report, renderer.StreamingEnabled && renderer.GetComponent<GridMapEntityStreamer>(), "复用区块和怪物流式加载");
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            var collider = solid.GetComponent<CompositeCollider2D>();
            Physics2D.SyncTransforms();
            Check(report, collider && collider.pathCount > 0, "实体墙体合并碰撞");
            foreach (Vector2Int point in new[] { HunterReferenceMapLayout.StartCell, HunterReferenceMapLayout.ExitCell })
                Check(report, collider.OverlapPoint(new Vector2(point.x + .5f, point.y - .2f)), "出生/出口实际碰撞: " + point);
            Tilemap platforms = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.OneWay);
            var effector = platforms.GetComponent<PlatformEffector2D>();
            Check(report, effector && effector.useOneWay && platforms.GetComponent<TilemapCollider2D>().usedByEffector, "竖井单向跳台");
        }

        private static void ExportPreviews(Scene scene, Report report)
        {
            var layers = new Dictionary<GameObject, int>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                { layers.Add(child.gameObject, child.gameObject.layer); child.gameObject.layer = 31; }
            var item = new GameObject("Temporary reference preview camera");
            var camera = item.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.backgroundColor = Outside; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 31; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            try
            {
                SetAnnotations(scene, true);
                Render(camera, "overview", new Vector2(96, 72), 72, 3072, 2304, report);
                SetAnnotations(scene, false);
                Render(camera, "entrance", (Vector2)HunterReferenceMapLayout.StartCell + new Vector2(8, 3.6f), 5.625f, 1280, 720, report);
                Render(camera, "hub", new Vector2(92, 88), 7.5f, 1440, 900, report);
            }
            finally
            {
                SetAnnotations(scene, false);
                Object.DestroyImmediate(item);
                foreach (var pair in layers) if (pair.Key) pair.Key.layer = pair.Value;
            }
        }

        private static void Render(Camera camera, string suffix, Vector2 center, float halfHeight, int width, int height, Report report)
        {
            string path = HunterReferenceMapLayout.Root + "/Previews/ReferenceCitadel_" + suffix + ".png";
            RenderTexture old = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            target.filterMode = FilterMode.Point;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            camera.transform.position = new Vector3(center.x, center.y, -10); camera.orthographicSize = halfHeight;
            camera.aspect = (float)width / height; camera.targetTexture = target;
            try
            {
                RenderTexture.active = target; GL.Clear(true, true, Outside); camera.Render();
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096;
                importer.SaveAndReimport(); report.previews.Add(path);
            }
            finally
            { camera.targetTexture = null; RenderTexture.active = old; Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(target); }
        }

        private static Vector3 Feet(Vector2Int point) => new Vector3(point.x + .5f, point.y + .05f, 0);
        private static bool SetAnnotations(Scene scene, bool active)
        {
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name.StartsWith("Area label - ") && child.gameObject.activeSelf != active)
                    { child.gameObject.SetActive(active); changed = true; }
            if (changed) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }
        private static T Find<T>(Scene scene) where T : Component
        { foreach (GameObject root in scene.GetRootGameObjects()) { T value = root.GetComponentInChildren<T>(true); if (value) return value; } return null; }
        private static void Check(Report report, bool passed, string value)
        { if (!passed) throw new InvalidOperationException(value); report.checks.Add(value); }
        private static void Folder(string path) => GridMapAssets.EnsureFolder(path);
    }
}

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
    /// <summary>Creates a separate playable SSW map pack without replacing authored game assets.</summary>
    public static class HunterSswMapPackBuilder
    {
        private const string RequestPath = "Library/HunterSswMapBuild.request";
        private const string ResultPath = "Library/HunterSswMapBuild.result.json";
        private const string SceneFolder = HunterSswMapLayouts.Root + "/Scenes";
        private const string PreviewFolder = HunterSswMapLayouts.Root + "/Previews";
        private const string PlayerPath = "Assets/_Project/Game/Prefabs/Player.prefab";
        private const string CatalogPath = "Assets/_Project/Game/Data/Config/GameAssets.asset";
        private const string PixelPath = "Assets/_Project/Game/Art/Environment/WhitePixel.png";
        private static readonly string[] MapNames = { "ForestOutpost", "CavernPass", "RuinedKeep" };
        private static readonly string[] DisplayNames = { "林地哨站", "地下洞窟", "废弃要塞" };
        private static readonly Color[] SkyColors = {
            new Color(.075f, .15f, .19f), new Color(.07f, .075f, .12f), new Color(.17f, .13f, .19f) };

        [Serializable] private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] private sealed class BuildReport
        {
            public bool success;
            public string generatedUtc;
            public string unityVersion;
            public string error;
            public string[] maps;
            public string[] scenes;
            public List<string> previews = new List<string>();
            public List<CheckResult> checks = new List<CheckResult>();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleRequest()
        {
            EditorApplication.delayCall += CheckRequestWhenReady;
        }

        private static void CheckRequestWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.update -= WaitForImport;
                EditorApplication.update += WaitForImport;
                return;
            }
            if (!File.Exists(RequestPath)) return;
            string stamp = File.GetLastWriteTimeUtc(RequestPath).Ticks.ToString();
            const string key = "Hunter.SswMapBuild.RequestStamp";
            // Leave a failed request for inspection, but never retry it on every domain reload.
            if (SessionState.GetString(key, string.Empty) == stamp) return;
            SessionState.SetString(key, stamp);
            if (BuildMissing(File.ReadAllText(RequestPath).Trim() == "refresh-previews"))
            {
                File.Delete(RequestPath);
                OpenCreatedScene(0);
            }
        }

        private static void WaitForImport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitForImport;
            CheckRequestWhenReady();
        }

        [MenuItem("Tools/BigWorld/SSW地图资源/生成缺失资源", false, 1)]
        public static void GenerateMissing()
        {
            if (BuildMissing()) OpenCreatedScene(0);
        }

        [MenuItem("Tools/BigWorld/SSW地图资源/更新预览图", false, 2)]
        public static void RefreshPreviews()
        {
            if (BuildMissing(true)) OpenCreatedScene(0);
        }

        /// <summary>Headless entry point for validating the resource pack in an independent Unity project.</summary>
        public static void GenerateAndValidate()
        {
            if (!BuildMissing()) throw new InvalidOperationException("SSW 地图生成失败，详情见 " + ResultPath);
        }

        [MenuItem("Tools/BigWorld/SSW地图资源/打开林地哨站", false, 11)]
        public static void OpenForest() => OpenMapScene(0);

        [MenuItem("Tools/BigWorld/SSW地图资源/打开地下洞窟", false, 12)]
        public static void OpenCavern() => OpenMapScene(1);

        [MenuItem("Tools/BigWorld/SSW地图资源/打开废弃要塞", false, 13)]
        public static void OpenKeep() => OpenMapScene(2);

        private static void OpenMapScene(int index)
        {
            if (!File.Exists(ScenePath(index)) && !BuildMissing()) return;
            OpenCreatedScene(index);
        }

        private static void OpenCreatedScene(int index)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(ScenePath(index));
            GridMapRenderer renderer = Find<GridMapRenderer>(scene);
            if (!renderer || !renderer.Map) throw new InvalidOperationException("场景缺少地图引用：" + ScenePath(index));
            HunterSswMapLayouts.OpenEditor(renderer.Map);
            Selection.activeGameObject = renderer.gameObject;
            if (SceneView.lastActiveSceneView)
            {
                SceneView.lastActiveSceneView.in2DMode = true;
                SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(13, 7, 0), new Vector3(28, 14, 1)), false);
            }
            Debug.Log("HUNTER_SSW_MAP_EDITOR_OPEN: " + renderer.Map.name);
        }

        private static bool BuildMissing(bool refreshPreviews = false)
        {
            var report = new BuildReport {
                generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                maps = new string[3], scenes = new string[3] };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请先停止 Play Mode，再生成地图资源。");
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new OperationCanceledException("已取消生成，未切换当前场景。");
                // Unity refuses additive scene creation while its initial, unchanged
                // Untitled scene is open. Load a saved project scene before staging
                // the new scenes; never discard a modified or authored scene here.
                Scene active = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                    EditorSceneManager.OpenScene(BorderTrialBuilder.ScenePath, OpenSceneMode.Single);
                GridMapAsset[] maps = HunterSswMapLayouts.EnsureMaps();
                if (maps == null || maps.Length != 3) throw new InvalidOperationException("地图布局应包含三个地图资产。");
                MapTilePalette palette = HunterSswMapLayouts.LoadPalette();
                ValidatePalette(palette, report);
                GameAssetCatalog catalog = Required<GameAssetCatalog>(CatalogPath);
                Check(report, "原框架运行配置", catalog.FrameworkSettings != null, CatalogPath);
                GameObject playerPrefab = Required<GameObject>(PlayerPath);
                Sprite pixel = Required<Sprite>(PixelPath);
                GridMapAssets.EnsureFolder(SceneFolder);
                GridMapAssets.EnsureFolder(PreviewFolder);
                for (int i = 0; i < maps.Length; i++)
                {
                    if (!maps[i]) throw new InvalidOperationException("地图资产引用为空：" + MapNames[i]);
                    report.maps[i] = AssetDatabase.GetAssetPath(maps[i]);
                    report.scenes[i] = ScenePath(i);
                    ValidateMap(maps[i], palette, report);
                    CreateOrValidateScene(i, maps[i], catalog, playerPrefab, pixel, report, refreshPreviews);
                }
                AssetDatabase.SaveAssets();
                report.success = true;
                Debug.Log("HUNTER_SSW_MAPS_READY: 3 maps, 3 playable scenes; " + ResultPath);
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogError("HUNTER_SSW_MAPS_FAILED: " + exception.Message);
            }
            finally
            {
                Directory.CreateDirectory("Library");
                File.WriteAllText(ResultPath, JsonUtility.ToJson(report, true));
            }
            return report.success;
        }

        private static string ScenePath(int index) => SceneFolder + "/" + MapNames[index] + ".unity";

        private static T Required<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("缺少游戏资源：" + path);
            return asset;
        }

        private static void Check(BuildReport report, string name, bool passed, string detail)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            if (!passed) throw new InvalidOperationException(name + "：" + detail);
        }

        private static void ValidatePalette(MapTilePalette palette, BuildReport report)
        {
            Check(report, "SSW类型面板", palette && palette.Types.Count > 0, "类型面板不能为空。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapTileType type in palette.Types)
            {
                Check(report, "类型精灵", type && type.Sprite, type ? type.name : "空类型引用");
                Check(report, "类型标识唯一", !string.IsNullOrWhiteSpace(type.Id) && ids.Add(type.Id), type.name + " / " + type.Id);
                if (type.Collision != MapTileCollisionMode.None)
                {
                    Vector2 size = type.Sprite.bounds.size;
                    Check(report, "碰撞图块比例", Mathf.Abs(size.x - 1) < .01f && Mathf.Abs(size.y - 1) < .01f,
                        type.name + " 应为 1 × 1 世界单位，实际 " + size);
                }
            }
        }

        private static void ValidateMap(GridMapAsset map, MapTilePalette palette, BuildReport report)
        {
            Check(report, map.name + "尺寸", map.Width == 96 && map.Height == 24 && Mathf.Approximately(map.CellSize, 1), "96 × 24，1 单位/格");
            var usable = new HashSet<MapTileType>(palette.Types);
            int count = 0;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                    {
                        MapTileType type = map.GetCell(x, y, layer);
                        if (!type) continue;
                        count++;
                        if (!usable.Contains(type) || !type.Sprite || type.Layer != layer)
                            throw new InvalidOperationException(map.name + " " + layer + " " + x + "," + y + " 类型不在专用面板、精灵缺失或层配置不一致。");
                    }
            Check(report, map.name + "地图引用", count > 0, count + " 个可编辑格子，精灵及所属层有效");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int guards = 0;
            foreach (MapSpawnDefinition spawn in map.Spawns)
            {
                if (spawn == null || !spawn.Enabled) continue;
                Check(report, map.name + "生成点标识", !string.IsNullOrWhiteSpace(spawn.Id) && ids.Add(spawn.Id), spawn.Id);
                Check(report, map.name + "生成点预制体", spawn.Prefab != null && map.Contains(spawn.Cell.x, spawn.Cell.y), spawn.DisplayName);
                if (spawn.Kind != MapSpawnKind.Monster) continue;
                Check(report, map.name + "守卫通关关联", spawn.Id.StartsWith("trial-guard-", StringComparison.Ordinal) && spawn.Count == 1,
                    "沿用现有关卡的守卫击杀计数：" + spawn.Id);
                guards += spawn.Count;
                ValidateStandingSpace(map, spawn.Cell.x, spawn.Cell.y, "守卫 " + spawn.Id, report);
            }
            Check(report, map.name + "守卫数量", guards == 3, "3 名守卫，与 RequiredKills 一致");
            foreach (int x in new[] { 4, 31, 61, 91 })
                ValidateStandingSpace(map, x, HunterSswMapLayouts.GroundTop(map, x), "出生/检查点/出口 x=" + x, report);
            MapTileType start = map.GetCell(4, 4, MapLayer.Objects);
            MapTileType exit = map.GetCell(91, 4, MapLayer.Objects);
            Check(report, map.name + "出生标记", start && start.HasTag("spawn"), "物件格 (4,4) 应带 spawn 标签");
            Check(report, map.name + "出口标记", exit && exit.HasTag("exit"), "物件格 (91,4) 应带 exit 标签");
        }

        private static void ValidateStandingSpace(GridMapAsset map, int x, int y, string label, BuildReport report)
        {
            MapTileType support = map.GetCell(x, y - 1);
            bool valid = map.Contains(x, y + 1) && support && support.Collision == MapTileCollisionMode.Solid;
            for (int row = y; row <= y + 1 && valid; row++)
                foreach (MapLayer layer in new[] { MapLayer.Terrain, MapLayer.Objects })
                {
                    MapTileType type = map.GetCell(x, row, layer);
                    if (type && type.Collision == MapTileCollisionMode.Solid) valid = false;
                }
            Check(report, map.name + "站立净空", valid, label + " 脚下实体支撑且上方两格净空 (" + x + "," + y + ")");
        }

        private static void CreateOrValidateScene(int index, GridMapAsset map, GameAssetCatalog catalog,
            GameObject playerPrefab, Sprite pixel, BuildReport report, bool refreshPreviews)
        {
            Scene previous = SceneManager.GetActiveScene();
            string path = ScenePath(index);
            bool create = !File.Exists(path);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool ownsScene = !scene.IsValid() || !scene.isLoaded;
            if (ownsScene)
                scene = create ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
                    : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                if (create) BuildScene(scene, index, map, catalog, playerPrefab, pixel);
                ValidateScene(scene, map, report);
                if (create && !EditorSceneManager.SaveScene(scene, path)) throw new IOException("无法保存新场景：" + path);
                ExportMissingPreviews(scene, map, index, report, refreshPreviews);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (ownsScene) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void BuildScene(Scene scene, int index, GridMapAsset map, GameAssetCatalog catalog,
            GameObject playerPrefab, Sprite pixel)
        {
            new GameObject("Game Services").AddComponent<GameServices2D>().Assets = catalog;
            GameObject playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            PlayerController2D player = playerObject.GetComponent<PlayerController2D>();
            if (!player) throw new InvalidOperationException("Player.prefab 缺少 PlayerController2D。");
            playerObject.name = "Player";
            player.transform.position = StandPosition(map, 4);

            var mapObject = new GameObject("World - " + DisplayNames[index]);
            var renderer = mapObject.AddComponent<GridMapRenderer>();
            renderer.ChunkSize = 16;
            renderer.LoadingRadiusCells = new Vector2Int(24, 14);
            renderer.UnloadPaddingCells = 8;
            renderer.LoadingTarget = player.transform;
            var streamer = mapObject.AddComponent<GridMapEntityStreamer>();
            streamer.LoadDistance = 24;
            streamer.UnloadDistance = 32;
            renderer.SetMap(map);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.transform.position = new Vector3(9, 7, -10);
            camera.backgroundColor = SkyColors[index];
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<AudioListener>();
            var follow = camera.gameObject.AddComponent<CameraFollow2D>();
            follow.Target = player.transform;
            follow.Map = renderer;
            follow.OrthographicSize = 5;

            var world = new GameObject("World Session").AddComponent<WorldSession2D>();
            world.Map = renderer;
            world.Player = player.transform;
            world.HudResourceKey = "ui.game";
            player.World = world;

            var level = new GameObject("Level Session").AddComponent<LevelSession2D>();
            level.World = world;
            level.Player = player;
            level.FollowCamera = follow;
            level.RequiredKills = 3;
            level.FallDeathY = -4;
            level.StartPoint = Marker("Start Point", StandPosition(map, 4));
            MakeCheckpoint(map, pixel, 31, 1, index == 1 ? "洞口营地" : "第一营地");
            MakeCheckpoint(map, pixel, 61, 2, index == 2 ? "城门前哨" : "第二前哨");
            level.ExitPoint = MakeExit(map, pixel);
            MakeBackdrop(map, index, pixel);
        }

        private static Vector3 StandPosition(GridMapAsset map, int x)
            => new Vector3(x + .5f, HunterSswMapLayouts.GroundTop(map, x) + .05f, 0);

        private static Transform Marker(string name, Vector3 position)
        {
            var marker = new GameObject(name);
            marker.transform.position = position;
            return marker.transform;
        }

        private static SpriteRenderer Rect(Sprite pixel, string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = pixel;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void MakeCheckpoint(GridMapAsset map, Sprite pixel, int x, int order, string displayName)
        {
            var root = new GameObject("Checkpoint " + order + " - " + displayName);
            root.transform.position = new Vector3(x + .5f, HunterSswMapLayouts.GroundTop(map, x), 0);
            var trigger = root.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.6f, 2.5f);
            trigger.offset = new Vector2(0, 1.25f);
            var checkpoint = root.AddComponent<Checkpoint2D>();
            checkpoint.Order = order;
            checkpoint.DisplayName = displayName;
            checkpoint.SpawnPoint = Marker("Respawn " + order, StandPosition(map, x));
            checkpoint.SpawnPoint.SetParent(root.transform, true);
            Rect(pixel, "Pole", root.transform, new Vector2(0, 1), new Vector2(.08f, 2), new Color(.6f, .65f, .65f), 17);
            checkpoint.Flag = Rect(pixel, "Flag", root.transform, new Vector2(.35f, 1.75f), new Vector2(.7f, .45f), new Color(.45f, .55f, .65f), 18);
        }

        private static Transform MakeExit(GridMapAsset map, Sprite pixel)
        {
            var root = new GameObject("Exit Gate - clear 3 guards");
            root.transform.position = new Vector3(91.5f, HunterSswMapLayouts.GroundTop(map, 91), 0);
            var trigger = root.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.8f, 3);
            trigger.offset = new Vector2(0, 1.5f);
            var gate = root.AddComponent<ExitGate2D>();
            Rect(pixel, "Left Post", root.transform, new Vector2(-.85f, 1.5f), new Vector2(.15f, 3), new Color(.45f, .6f, .65f), 17);
            Rect(pixel, "Right Post", root.transform, new Vector2(.85f, 1.5f), new Vector2(.15f, 3), new Color(.45f, .6f, .65f), 17);
            gate.Indicator = Rect(pixel, "Gate Light", root.transform, new Vector2(0, 3), new Vector2(1.9f, .18f), new Color(1, .45f, .2f), 18);
            return root.transform;
        }

        private static void MakeBackdrop(GridMapAsset map, int theme, Sprite pixel)
        {
            var root = new GameObject("Backdrop - decorative, no collision").transform;
            Rect(pixel, "Distant Background", root, new Vector2(48, 12), new Vector2(96, 24), SkyColors[theme], -100);
            if (theme == 0)
            {
                for (int i = 0; i < 11; i++)
                    Decoration(root, "Distant Tree " + i, "TreeLarge", i * 9 + 1, 3.5f, 2.3f + (i % 3) * .25f,
                        new Color(.12f, .28f, .28f), -50);
                foreach (int x in new[] { 1, 17, 35, 49, 73, 95 })
                    Decoration(root, "Tree " + x, x % 2 == 0 ? "TreeSmall" : "TreeLarge", x,
                        HunterSswMapLayouts.GroundTop(map, x) - .1f, 1.25f, Color.white, -8);
                foreach (int x in new[] { 9, 21, 33, 46, 66, 81, 94 })
                    Decoration(root, "Bush " + x, "Bush", x, HunterSswMapLayouts.GroundTop(map, x), 1, Color.white, -7);
            }
            else if (theme == 1)
            {
                for (int i = 0; i < 13; i++)
                    Decoration(root, "Cave Silhouette " + i, "CaveRock", i * 8 - 1, 2.5f, 3.5f,
                        new Color(.22f, .21f, .31f), -45);
                foreach (int x in new[] { 9, 20, 37, 54, 70, 82, 95 })
                    Decoration(root, "Cave Rock " + x, "CaveRock", x, HunterSswMapLayouts.GroundTop(map, x) - .1f,
                        1.2f, new Color(.6f, .57f, .7f), -8);
            }
            else
            {
                foreach (int x in new[] { 6, 20, 36, 51, 69, 84, 95 })
                {
                    Decoration(root, "Distant Pillar " + x, "Pillar", x - 2, 3.5f, 2.7f,
                        new Color(.27f, .23f, .3f), -45);
                    Decoration(root, "Pillar " + x, "Pillar", x, HunterSswMapLayouts.GroundTop(map, x), 1.3f,
                        new Color(.78f, .74f, .78f), -8);
                }
                foreach (int x in new[] { 17, 48, 78 })
                    Decoration(root, "Ruined Arch " + x, "Arch", x, 4, 1.5f, new Color(.5f, .45f, .53f), -20);
            }
            foreach (int x in new[] { 7, 29, 44, 63, 86 })
                Decoration(root, "Loose Rock " + x, theme == 1 ? "RockLarge" : "RockSmall", x,
                    HunterSswMapLayouts.GroundTop(map, x), 1, Color.white, -6);
        }

        private static void Decoration(Transform parent, string name, string key, float x, float bottom, float scale, Color tint, int order)
        {
            Sprite sprite = HunterSswMapLayouts.Decoration(key);
            if (!sprite) return;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(x, bottom - sprite.bounds.min.y * scale, 0);
            root.transform.localScale = new Vector3(scale, scale, 1);
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingOrder = order;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component) return component;
            }
            return null;
        }

        private static void ValidateScene(Scene scene, GridMapAsset map, BuildReport report)
        {
            GridMapRenderer renderer = Find<GridMapRenderer>(scene);
            PlayerController2D player = Find<PlayerController2D>(scene);
            WorldSession2D world = Find<WorldSession2D>(scene);
            LevelSession2D level = Find<LevelSession2D>(scene);
            CameraFollow2D follow = Find<CameraFollow2D>(scene);
            GameServices2D services = Find<GameServices2D>(scene);
            Check(report, map.name + "场景地图", renderer && renderer.Map == map && renderer.HasGeneratedPreview,
                "场景绑定对应地图且保留可编辑的完整预览");
            Check(report, map.name + "关卡引用", player && world && level && follow && services &&
                world.Map == renderer && world.Player == player.transform && player.World == world &&
                level.World == world && level.Player == player && level.FollowCamera == follow &&
                level.StartPoint && level.ExitPoint && follow.Target == player.transform && follow.Map == renderer &&
                renderer.LoadingTarget == player.transform && level.RequiredKills == 3 && services.Assets && services.Assets.FrameworkSettings,
                "服务、玩家、世界、相机、出入口与 3 守卫通关条件引用完整");
            Check(report, map.name + "流式加载", renderer.StreamingEnabled && renderer.GetComponent<GridMapEntityStreamer>(),
                "沿用地形区块与守卫对象池流式加载");
            Tilemap solid = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.Solid);
            CompositeCollider2D composite = solid.GetComponent<CompositeCollider2D>();
            Rigidbody2D body = solid.GetComponent<Rigidbody2D>();
            Physics2D.SyncTransforms();
            Check(report, map.name + "实体碰撞生成", composite && composite.pathCount > 0 && body && body.bodyType == RigidbodyType2D.Static,
                "实际 Tilemap 合并碰撞具有非空几何和静态刚体");
            foreach (int x in new[] { 4, 31, 61, 91 })
            {
                Vector2 point = new Vector2(x + .5f, HunterSswMapLayouts.GroundTop(map, x) - .25f);
                Check(report, map.name + "落点实际碰撞", composite.OverlapPoint(point), "出生/检查点/出口脚下碰撞 x=" + x);
            }
            Tilemap oneWay = renderer.GetTilemap(MapLayer.Terrain, MapTileCollisionMode.OneWay);
            PlatformEffector2D effector = oneWay.GetComponent<PlatformEffector2D>();
            TilemapCollider2D oneWayCollider = oneWay.GetComponent<TilemapCollider2D>();
            Check(report, map.name + "单向平台配置", effector && effector.useOneWay && oneWayCollider && oneWayCollider.usedByEffector,
                "单向平台保留 PlatformEffector2D 向上穿过/向下落地规则");
        }

        private static void ExportMissingPreviews(Scene scene, GridMapAsset map, int index, BuildReport report, bool force)
        {
            string overview = PreviewFolder + "/" + MapNames[index] + "_overview.png";
            string start = PreviewFolder + "/" + MapNames[index] + "_start.png";
            if (!force && File.Exists(overview) && File.Exists(start)) { report.previews.Add(overview); report.previews.Add(start); return; }
            var layers = new Dictionary<GameObject, int>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add(transform.gameObject, transform.gameObject.layer);
                    transform.gameObject.layer = 31;
                }
            var cameraObject = new GameObject("SSW temporary preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SkyColors[index];
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            try
            {
                // Clear color and depth explicitly on each render; a reused RT must never retain ghost pixels.
                if (force || !File.Exists(overview)) RenderPng(camera, overview, new Vector3(48, 12, -10), 12, 3072, 768);
                if (force || !File.Exists(start)) RenderPng(camera, start, new Vector3(12, 7.5f, -10), 6, 1280, 640);
                report.previews.Add(overview);
                report.previews.Add(start);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                foreach (var pair in layers) if (pair.Key) pair.Key.layer = pair.Value;
            }
        }

        private static void RenderPng(Camera camera, string path, Vector3 position, float halfHeight, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            target.filterMode = FilterMode.Point;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            camera.transform.position = position;
            camera.orthographicSize = halfHeight;
            camera.aspect = (float)width / height;
            camera.targetTexture = target;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, camera.backgroundColor);
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.filterMode = FilterMode.Point;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = 4096;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}

using System;
using System.IO;
using BigWorld.Map2D;
using Cinemachine;
using SkillEditorKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BigWorld.YouYou2D.Editor
{
    public static class Framework2DDemoBuilder
    {
        public const string ScenePath = "Assets/_Examples/Framework2D/Scenes/Framework2DDemo.unity";
        public const string CatalogPath = "Assets/_Examples/Framework2D/Data/Config/GameAssets.asset";
        public const string MapPath = "Assets/_Examples/Framework2D/Data/Maps/Framework2DExample.asset";
        private const string Prefabs = "Assets/_Examples/Framework2D/Prefabs";
        private const string Skills = "Assets/_Examples/Framework2D/Data/Skills";
        private const string Source = "Assets/SkillEditorKit/Samples/TwoD";

        [MenuItem("Tools/BigWorld/2D 框架/打开运行示例")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/BigWorld/2D 框架/创建示例资源（不覆盖）")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Folder(Prefabs); Folder(Skills); Folder("Assets/_Examples/Framework2D/Data/Config");
            Folder("Assets/_Examples/Framework2D/Scenes"); Folder("Assets/_Examples/Framework2D/Data/Maps");
            var shoot = CopySkill("Shoot2D");
            var melee = CopySkill("Melee2D");
            var sprite = Required<Sprite>(Source + "/SoldierIdle.png");
            var target = MakeTarget(sprite);
            var player = MakePlayer(sprite, shoot, melee);
            var hud = MakeHud();
            var map = MakeMap(target);
            var catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<GameAssetCatalog>();
                catalog.Entries = new[]
                {
                    Entry("ui.demo", hud), Entry("skill.shoot", shoot), Entry("skill.melee", melee),
                    Entry("world.demo", map), Entry("actor.player", player), Entry("actor.target", target)
                };
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (!File.Exists(ScenePath)) MakeScene(catalog, map, player);
            AssetDatabase.SaveAssets();
            Debug.Log("BIGWORLD_FRAMEWORK2D_DEMO_CREATED: " + ScenePath);
        }

        private static GameAssetCatalog.Entry Entry(string key, Object asset) => new GameAssetCatalog.Entry { Key = key, Asset = asset };

        [MenuItem("Tools/BigWorld/2D 框架/更新示例相机绑定")]
        public static void UpgradeDemoCamera()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                WorldSession2D world = null;
                CameraFollow2D follow = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!world) world = root.GetComponentInChildren<WorldSession2D>(true);
                    if (!follow) follow = root.GetComponentInChildren<CameraFollow2D>(true);
                }
                if (!world || !follow) throw new InvalidOperationException("示例缺少世界或相机组件。");
                if (!follow.GetComponent<CinemachineBrain>()) follow.gameObject.AddComponent<CinemachineBrain>();
                follow.Target = world.Player;
                follow.Map = world.Map;
                EditorUtility.SetDirty(follow);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("无法保存示例相机绑定。");
                Debug.Log("BIGWORLD_CINEMACHINE2D_CONFIGURED");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }
        private static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?
            AssetDatabase.LoadAssetAtPath<T>(path) : throw new FileNotFoundException("缺少示例依赖：" + path);
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static SkillClip CopySkill(string name)
        {
            string path = Skills + "/" + name + ".asset";
            if (!AssetDatabase.LoadAssetAtPath<SkillClip>(path) && !AssetDatabase.CopyAsset(Source + "/" + name + ".asset", path))
                throw new IOException("无法复制 2D 技能：" + name);
            return Required<SkillClip>(path);
        }

        private static GameObject MakeTarget(Sprite sprite)
        {
            string path = Prefabs + "/StreamedTarget2D.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) return prefab;
            var root = new GameObject("StreamedTarget2D");
            try
            {
                root.layer = 30;
                var visual = root.AddComponent<SpriteRenderer>(); visual.sprite = sprite;
                visual.color = new Color(1, .42f, .36f); visual.sortingOrder = 20; visual.flipX = true;
                var shape = root.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.65f, 1.45f); shape.offset = new Vector2(0, .75f);
                var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 2;
                body.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                root.AddComponent<MapStreamedEntity>();
                var actor = root.AddComponent<SkillActor2D>(); actor.Team = 2;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject MakePlayer(Sprite sprite, SkillClip shoot, SkillClip melee)
        {
            string path = Prefabs + "/FrameworkPlayer2D.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) return prefab;
            var root = new GameObject("FrameworkPlayer2D");
            try
            {
                root.tag = "Player"; root.layer = 30;
                root.AddComponent<Animator>().applyRootMotion = false;
                root.AddComponent<SkillAnimationPlayer>();
                root.AddComponent<SkillPlayer>().attackDetectionLayer = 1 << 30;
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 25;
                root.AddComponent<SkillFacing2D>().Sprite = renderer;
                var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 2;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                var shape = root.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.65f, 1.45f); shape.offset = new Vector2(0, .75f);
                root.AddComponent<SkillActor2D>().Team = 1;
                var input = root.AddComponent<PlatformerDemoInput2D>(); input.Shoot = shoot; input.Melee = melee;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject MakeHud()
        {
            string path = Prefabs + "/FrameworkHud2D.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) return prefab;
            var root = new GameObject("FrameworkHud2D", typeof(RectTransform), typeof(FrameworkDemoHud2D));
            try { return PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { Object.DestroyImmediate(root); }
        }

        private static GridMapAsset MakeMap(GameObject target)
        {
            var map = AssetDatabase.LoadAssetAtPath<GridMapAsset>(MapPath);
            if (map) return map;
            map = ScriptableObject.CreateInstance<GridMapAsset>(); map.Initialize(128, 24);
            var ground = Required<MapTileType>("Assets/_Game/Data/Maps/TileTypes/Ground.asset");
            var platform = Required<MapTileType>("Assets/_Game/Data/Maps/TileTypes/Stairs.asset");
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < 2; y++) map.SetCell(x, y, ground);
            for (int x = 12; x < 18; x++) map.SetCell(x, 5, platform);
            for (int x = 22; x < 28; x++) map.SetCell(x, 7, platform);
            foreach (int x in new[] { 9, 19, 38, 70, 105 })
                map.Spawns.Add(new MapSpawnDefinition
                {
                    Id = "demo-target-" + x, DisplayName = "测试目标 " + x,
                    Prefab = target, Cell = new Vector2Int(x, 2), Kind = MapSpawnKind.Monster,
                    MaxHealth = 50, RespawnSeconds = 5
                });
            AssetDatabase.CreateAsset(map, MapPath);
            return map;
        }

        private static void MakeScene(GameAssetCatalog catalog, GridMapAsset map, GameObject prefab)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                new GameObject("Game Services 2D").AddComponent<GameServices2D>().Assets = catalog;
                var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                player.transform.position = new Vector3(4.5f, 2.05f, 0);
                var mapObject = new GameObject("World - Framework2DExample");
                var renderer = mapObject.AddComponent<GridMapRenderer>();
                renderer.ChunkSize = 16; renderer.LoadingRadiusCells = new Vector2Int(20, 12);
                renderer.UnloadPaddingCells = 8; renderer.LoadingTarget = player.transform;
                var streamer = mapObject.AddComponent<GridMapEntityStreamer>();
                streamer.LoadDistance = 20; streamer.UnloadDistance = 28;
                renderer.SetMap(map);
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.orthographic = true; camera.orthographicSize = 5;
                camera.transform.position = new Vector3(6.5f, 4, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .08f, .13f);
                camera.gameObject.AddComponent<AudioListener>();
                var follow = camera.gameObject.AddComponent<CameraFollow2D>();
                follow.Target = player.transform;
                follow.Map = renderer;
                var world = new GameObject("World Session 2D").AddComponent<WorldSession2D>();
                world.Map = renderer; world.Player = player.transform; world.HudResourceKey = "ui.demo";
                player.GetComponent<PlatformerDemoInput2D>().World = world;
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("无法保存示例场景。");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

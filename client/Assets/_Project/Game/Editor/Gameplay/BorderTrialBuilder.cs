using System;
using System.IO;
using BigWorld.Gameplay;
using BigWorld.Map2D;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BigWorld.Editor
{
    /// <summary>Creates editable game assets once; rerunning preserves existing authored content.</summary>
    public static class BorderTrialBuilder
    {
        public const string ScenePath = "Assets/_Project/Game/Scenes/BorderTrial.unity";
        public const string MapPath = "Assets/_Project/Game/Data/Maps/BorderTrial.asset";
        public const string CatalogPath = "Assets/_Project/Game/Data/Config/GameAssets.asset";
        private const string Source = "Assets/_Project/Modules/SkillEditorKit/Samples/TwoD";
        private const string Art = "Assets/_Project/Game/Art/Characters";
        private const string Prefabs = "Assets/_Project/Game/Prefabs";

        [MenuItem("Tools/BigWorld/正式关卡/打开边境试炼", false, 1)]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/BigWorld/正式关卡/创建缺失资源并设为打包入口", false, 2)]
        public static void CreateAndConfigure()
        {
            Create();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("BIGWORLD_BORDER_TRIAL_CONFIGURED");
        }

        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string path in new[] { Art, Prefabs, "Assets/_Project/Game/Scenes", "Assets/_Project/Game/Data/Skills",
                "Assets/_Project/Game/Data/Config", "Assets/_Project/Game/Animations", "Assets/_Project/Game/Art/Environment", "Assets/_Project/Game/Data/Physics" }) Folder(path);
            Copy(Source + "/SoldierIdle.png", Art + "/SoldierIdle.png");
            Copy(Source + "/SoldierFire.png", Art + "/SoldierFire.png");
            Copy(Source + "/Bullet.png", Art + "/Bullet.png");
            Sprite idle = Required<Sprite>(Art + "/SoldierIdle.png");
            Sprite fire = Required<Sprite>(Art + "/SoldierFire.png");
            Sprite pixel = MakePixel();
            var shotAnimation = MakeAnimation(idle, fire);
            var bullet = MakeBullet();
            var shoot = MakeSkill("Shoot2D", shotAnimation, bullet);
            var melee = MakeSkill("Melee2D", shotAnimation, bullet);
            var material = MakePhysicsMaterial();
            var player = MakePlayer(idle, shoot, melee, material);
            var enemy = MakeEnemy(idle, material);
            var map = MakeMap(enemy);
            var hud = MakeHud();
            var catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<GameAssetCatalog>();
                catalog.Entries = new[] { Entry("ui.game", hud), Entry("world.trial", map), Entry("actor.player", player),
                    Entry("actor.guard", enemy), Entry("skill.shoot", shoot), Entry("skill.melee", melee) };
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (!File.Exists(ScenePath)) MakeScene(catalog, map, player, pixel);
            AssetDatabase.SaveAssets();
        }

        private static GameAssetCatalog.Entry Entry(string key, Object value) => new GameAssetCatalog.Entry { Key = key, Asset = value };
        private static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?
            AssetDatabase.LoadAssetAtPath<T>(path) : throw new FileNotFoundException("缺少关卡资源：" + path);
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void Copy(string source, string destination)
        {
            if (!File.Exists(destination) && !AssetDatabase.CopyAsset(source, destination)) throw new IOException("复制资源失败：" + source);
        }

        private static Sprite MakePixel()
        {
            const string path = "Assets/_Project/Game/Art/Environment/WhitePixel.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.SetPixel(0, 0, Color.white); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 1; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return Required<Sprite>(path);
        }

        private static AnimationClip MakeAnimation(Sprite idle, Sprite fire)
        {
            const string path = "Assets/_Project/Game/Animations/Shoot2D.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip) return clip;
            clip = new AnimationClip { frameRate = 30 };
            var binding = EditorCurveBinding.PPtrCurve("Visual", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[] {
                new ObjectReferenceKeyframe { time = 0, value = idle }, new ObjectReferenceKeyframe { time = .1f, value = fire },
                new ObjectReferenceKeyframe { time = .2f, value = idle }, new ObjectReferenceKeyframe { time = .3f, value = idle } });
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static GameObject MakeBullet()
        {
            string path = Prefabs + "/Bullet2D.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing) return existing;
            Copy(Source + "/Bullet2D.prefab", path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.GetComponentInChildren<SpriteRenderer>().sprite = Required<Sprite>(Art + "/Bullet.png");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return Required<GameObject>(path);
        }

        private static SkillClip MakeSkill(string name, AnimationClip animation, GameObject bullet)
        {
            string path = "Assets/_Project/Game/Data/Skills/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<SkillClip>(path);
            if (existing) return existing;
            Copy(Source + "/" + name + ".asset", path);
            var clip = Required<SkillClip>(path);
            foreach (var entry in SkillTrackModel.Read(clip))
            {
                if (entry.Data is SkillAnimationEvent animate) animate.AnimationClip = animation;
                if (entry.Data is SkillProjectileEvent projectile) projectile.Prefab = bullet;
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static PhysicsMaterial2D MakePhysicsMaterial()
        {
            const string path = "Assets/_Project/Game/Data/Physics/ActorMovement.physicsMaterial2D";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material) return material;
            material = new PhysicsMaterial2D("ActorMovement") { friction = 0, bounciness = 0 };
            AssetDatabase.CreateAsset(material, path); return material;
        }

        private static GameObject Actor(string name, Sprite sprite, PhysicsMaterial2D material, Color tint)
        {
            var root = new GameObject(name); root.layer = 30;
            var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 2.5f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(.65f, 1.45f); collider.offset = new Vector2(0, .75f); collider.sharedMaterial = material;
            var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = tint; renderer.sortingOrder = 25;
            root.AddComponent<SkillFacing2D>().Sprite = renderer;
            return root;
        }

        private static GameObject MakePlayer(Sprite sprite, SkillClip shoot, SkillClip melee, PhysicsMaterial2D material)
        {
            string path = Prefabs + "/Player.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var root = Actor("Player", sprite, material, Color.white);
            try
            {
                root.tag = "Player";
                root.AddComponent<Animator>().applyRootMotion = false;
                root.AddComponent<SkillAnimationPlayer>();
                root.AddComponent<SkillPlayer>().attackDetectionLayer = 1 << 30;
                var actor = root.AddComponent<SkillActor2D>(); actor.Team = 1; actor.BaseAttack = 25; actor.MaxHealth = 100; actor.InvulnerabilitySeconds = .65f;
                var controller = root.AddComponent<PlayerController2D>(); controller.Shoot = shoot; controller.Melee = melee; controller.JumpSpeed = 11;
                var visual = root.AddComponent<PlayerVisual2D>(); visual.Sprite = root.GetComponent<SkillFacing2D>().Sprite; visual.Visual = visual.Sprite.transform;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject MakeEnemy(Sprite sprite, PhysicsMaterial2D material)
        {
            string path = Prefabs + "/PatrolGuard.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var root = Actor("PatrolGuard", sprite, material, new Color(1, .4f, .35f));
            try
            {
                root.AddComponent<MapStreamedEntity>();
                root.AddComponent<SkillActor2D>().Team = 2;
                root.AddComponent<PatrolEnemy2D>();
                root.GetComponent<SkillFacing2D>().SetFacing(-1);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject MakeHud()
        {
            string path = Prefabs + "/GameHud.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var root = new GameObject("GameHud", typeof(RectTransform));
            try
            {
                root.AddComponent<GameHud2D>().UIFont = Required<Font>("Assets/_Project/Game/UI/Fonts/NotoSansCJKsc-Regular.otf");
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GridMapAsset MakeMap(GameObject guard)
        {
            var map = AssetDatabase.LoadAssetAtPath<GridMapAsset>(MapPath); if (map) return map;
            map = ScriptableObject.CreateInstance<GridMapAsset>(); map.Initialize(80, 18);
            var ground = Required<MapTileType>("Assets/_Project/Game/Data/Maps/TileTypes/Ground.asset");
            for (int x = 0; x < 80; x++)
            {
                if ((x >= 24 && x <= 26) || (x >= 50 && x <= 52)) continue;
                for (int y = 0; y < 2; y++) map.SetCell(x, y, ground);
            }
            for (int y = 2; y < 7; y++) { map.SetCell(0, y, ground); map.SetCell(79, y, ground); }
            // Optional raised routes; the two broken sections can also be crossed with a running jump.
            foreach (int x in new[] { 18, 19, 20, 44, 45, 46 }) map.SetCell(x, 3, ground);
            int[] positions = { 14, 40, 65 };
            for (int i = 0; i < positions.Length; i++) map.Spawns.Add(new MapSpawnDefinition {
                Id = "trial-guard-" + (i + 1), DisplayName = "边境守卫 " + (i + 1), Prefab = guard,
                Cell = new Vector2Int(positions[i], 2), Kind = MapSpawnKind.Monster, MaxHealth = 50, RespawnSeconds = 0 });
            AssetDatabase.CreateAsset(map, MapPath); return map;
        }

        private static void MakeScene(GameAssetCatalog catalog, GridMapAsset map, GameObject playerPrefab, Sprite pixel)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                new GameObject("Game Services").AddComponent<GameServices2D>().Assets = catalog;
                var player = ((GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene)).GetComponent<PlayerController2D>();
                player.transform.position = new Vector3(4.5f, 2.05f, 0);
                var mapObject = new GameObject("World - Border Trial");
                var renderer = mapObject.AddComponent<GridMapRenderer>();
                renderer.ChunkSize = 16; renderer.LoadingRadiusCells = new Vector2Int(22, 12); renderer.UnloadPaddingCells = 8;
                renderer.LoadingTarget = player.transform;
                var streamer = mapObject.AddComponent<GridMapEntityStreamer>(); streamer.LoadDistance = 22; streamer.UnloadDistance = 30;
                renderer.SetMap(map);
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.orthographic = true; camera.orthographicSize = 5; camera.transform.position = new Vector3(8.9f, 5, -10);
                camera.backgroundColor = new Color(.06f, .095f, .16f); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.gameObject.AddComponent<AudioListener>();
                var follow = camera.gameObject.AddComponent<CameraFollow2D>(); follow.Target = player.transform; follow.Map = renderer;
                var world = new GameObject("World Session").AddComponent<WorldSession2D>();
                world.Map = renderer; world.Player = player.transform; world.HudResourceKey = "ui.game";
                player.World = world;
                var session = new GameObject("Level Session").AddComponent<LevelSession2D>();
                session.World = world; session.Player = player; session.FollowCamera = follow;
                session.StartPoint = Marker("Start Point", new Vector3(4.5f, 2.05f));
                MakeCheckpoint(pixel, 31, 1, "断桥营地");
                MakeCheckpoint(pixel, 57, 2, "边境前哨");
                var exit = new GameObject("Exit Gate"); exit.transform.position = new Vector3(74, 2, 0);
                var gateCollider = exit.AddComponent<BoxCollider2D>(); gateCollider.isTrigger = true; gateCollider.size = new Vector2(1.8f, 3); gateCollider.offset = new Vector2(0, 1.5f);
                var gate = exit.AddComponent<ExitGate2D>();
                Rect(pixel, "Left Post", exit.transform, new Vector2(-.85f, 1.5f), new Vector2(.2f, 3), new Color(.35f, .5f, .6f), 12);
                Rect(pixel, "Right Post", exit.transform, new Vector2(.85f, 1.5f), new Vector2(.2f, 3), new Color(.35f, .5f, .6f), 12);
                gate.Indicator = Rect(pixel, "Gate Light", exit.transform, new Vector2(0, 3), new Vector2(1.9f, .25f), new Color(1, .45f, .2f), 12);
                session.ExitPoint = exit.transform;
                MakeBackdrop(pixel);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("无法保存正式关卡。");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Transform Marker(string name, Vector3 position)
        { var marker = new GameObject(name); marker.transform.position = position; return marker.transform; }
        private static SpriteRenderer Rect(Sprite sprite, string name, Transform parent, Vector2 position, Vector2 size, Color color, int sorting)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false);
            child.transform.localPosition = position; child.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = sorting;
            return renderer;
        }
        private static void MakeCheckpoint(Sprite pixel, float x, int order, string name)
        {
            var root = new GameObject("Checkpoint " + order + " - " + name); root.transform.position = new Vector3(x, 2, 0);
            var collider = root.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = new Vector2(1.8f, 2.5f); collider.offset = new Vector2(0, 1.25f);
            var checkpoint = root.AddComponent<Checkpoint2D>(); checkpoint.Order = order; checkpoint.DisplayName = name;
            checkpoint.SpawnPoint = Marker("Respawn " + order, new Vector3(x - 1.5f, 2.05f)); checkpoint.SpawnPoint.SetParent(root.transform, true);
            Rect(pixel, "Pole", root.transform, new Vector2(0, 1), new Vector2(.1f, 2), new Color(.7f, .8f, .85f), 10);
            checkpoint.Flag = Rect(pixel, "Flag", root.transform, new Vector2(.4f, 1.7f), new Vector2(.8f, .55f), new Color(.45f, .55f, .65f), 11);
        }
        private static void MakeBackdrop(Sprite pixel)
        {
            var root = new GameObject("Backdrop").transform;
            Rect(pixel, "Horizon", root, new Vector2(40, 3.5f), new Vector2(80, 7), new Color(.08f, .15f, .23f), -40);
            for (int i = 0; i < 20; i++)
            {
                float height = 2.5f + (i * 7 % 9) * .45f;
                Rect(pixel, "Distant Ruin " + i, root, new Vector2(i * 4 + 1, 2 + height / 2), new Vector2(2.6f, height), new Color(.10f, .19f, .27f), -30);
                Rect(pixel, "Signal " + i, root, new Vector2(i * 4 + 1.5f, 3.5f + height / 2), new Vector2(.12f, .35f), new Color(.17f, .35f, .4f), -29);
            }
        }
    }
}

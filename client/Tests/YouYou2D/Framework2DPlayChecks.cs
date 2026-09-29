using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using BigWorld.Pooling.Unity;
using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.SceneManagement;
using YouYou;

public sealed class Framework2DPlayChecks : MonoBehaviour
{
    [Serializable] private sealed class Report { public string unityVersion; public int passed; public List<string> checks = new List<string>(); public string failure; }
    private readonly Report report = new Report();
    private string failure;
    private float deadline;
    private bool finished;
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/YouYouFramework/Adaptation2D"));

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(Output);
        Application.logMessageReceived += OnLog;
        deadline = Time.realtimeSinceStartup + 300;
        var stack = new Stack<IEnumerator>();
        stack.Push(Checks());
        while (stack.Count > 0 && !finished)
        {
            object wait = null;
            try
            {
                if (failure != null) throw new Exception(failure);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Integration checks timed out.");
                var current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                wait = current.Current;
            }
            catch (Exception e)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(e.ToString()); yield break;
            }
            yield return wait;
        }
        if (!finished) Finish(null);
    }

    private void Update()
    {
        if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
            Finish("Integration checks exceeded 300 seconds while waiting for a frame or phase.");
    }

    private IEnumerator Checks()
    {
        var world = FindObjectOfType<WorldSession2D>();
        yield return Until(() => GameServices2D.Instance && GameServices2D.Instance.IsReady, 60, "Original framework content bootstrap");
        yield return Until(() => world && world.IsReady, 60, "Initial world terrain and native HUD readiness");
        var services = GameServices2D.Instance;
        var driver = services.Pools;
        var player = world.Player.GetComponent<SkillActor2D>();
        var input = player.GetComponent<PlatformerDemoInput2D>(); input.ReadKeyboard = false;
        var body = player.GetComponent<Rigidbody2D>();
        var map = world.Map;
        var streamer = map.GetComponent<GridMapEntityStreamer>();
        string targetId = "spawn:demo-target-9:0";
        MapStreamedEntity target;
        while (!streamer.TryGetActor(targetId, out target)) yield return null;
        Check(services.Framework && services.Framework == GameEntry.Instance && services.Framework.IsInitialized && services.IsReady &&
            !services.Framework.AutoLaunchProcedure && map.LoadingTarget == player.transform, "Boot, original framework services and player-driven map streaming");
        Check(driver && driver.Service.State == BigWorld.Pooling.PoolState.Running && FindObjectsOfType<PoolDriver>().Length == 1, "Map and framework host share one pool driver");
        Check(FindObjectsOfType<GameEntry>().Length == 1 && FindObjectOfType<FrameworkDemoHud2D>(), "Native UI manager loads the framework HUD with a single original entry");
        object[] originalManagers = NativeManagers();
        Check(Array.TrueForAll(originalManagers, manager => manager != null) &&
            GameEntry.DataTable.AlreadyLoadTable.Count >= 17, "All 18 original managers, C# session data and original tables are ready");
        Check(GameEntry.DataTable.Sys_UIFormList.GetEntity(9001).HasValue && GameEntry.DataTable.Sys_UIFormList.GetEntity(9002).HasValue &&
            GameEntry.DataTable.Sys_UIFormList.GetEntity(UIFormId.UI_Dialog).HasValue, "Original FlatBuffers UI table retains course rows and appends both 2D forms");
        var nativeHud = FindObjectOfType<FrameworkDemoHud2D>();
        string hudPath = services.Assets.GetAssetPath("ui.demo");
        var indexedHud = GameEntry.Resource.ResourceLoaderManager.GetAssetEntity(AssetCategory.UIPrefab, hudPath);
        Check(indexedHud != null && indexedHud.AssetFullName == hudPath && nativeHud.UIFormId == 9002 &&
            nativeHud.transform.IsChildOf(services.Framework.UIRootRectTransform) && ReferenceEquals(nativeHud.UserData, world),
            "Demo HUD uses the original resource index, UI form 9002 and native UI hierarchy");
        yield return ReporterResourceChecks();
        yield return new WaitForSeconds(.5f);
        Check(input.IsGrounded() && Mathf.Abs(body.position.y - 2) < .12f, "Rigidbody2D lands on streamed TilemapCollider2D");
        var initialPosition = body.position;
        input.SetInput(1, false); yield return new WaitForSeconds(.25f); input.SetInput(0, false);
        Check(body.position.x > initialPosition.x + .6f && Mathf.Abs(player.transform.position.z) < .001f, "XY horizontal movement without Z drift");
        input.SetInput(-1, false); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); input.SetInput(0, false);
        Check(player.GetComponent<SkillFacing2D>().Direction == -1, "Left-facing skill orientation");
        input.SetInput(0, true); yield return new WaitForSeconds(.15f);
        Check(body.position.y > initialPosition.y + .3f, "Grounded jump uses 2D physics");
        yield return new WaitForSeconds(1.3f);
        Move(body, new Vector2(4.5f, 2.05f)); body.simulated = true;
        player.GetComponent<SkillFacing2D>().SetFacing(1);
        yield return new WaitForSeconds(.25f);
        var hud = FindObjectOfType<FrameworkDemoHud2D>();
        Check(hud.Seconds > 0, "HUD uses framework timer");
        Check(player.TryPlay(input.Shoot), "2D skill cast accepted");
        Check(!player.TryPlay(input.Shoot), "Duplicate active cast rejected");
        float hitDeadline = Time.time + 3;
        while (target.Health == 50 && Time.time < hitDeadline) yield return null;
        Check(target.Health == 40 && hud.HitCount == 1, "Real projectile hit updates streamed health and framework event exactly once");
        var not2D = ScriptableObject.CreateInstance<SkillClip>(); not2D.Space = SkillSpace.ThreeD;
        Check(!player.TryPlay(not2D), "3D skill rejected by the 2D adapter"); Destroy(not2D);
        yield return new WaitForSeconds(.4f);
        services.SetPaused(true);
        int seconds = hud.Seconds; var pausedPosition = body.position;
        Check(!player.TryPlay(input.Shoot), "Pause blocks new gameplay casts");
        yield return new WaitForSecondsRealtime(.25f);
        Check(hud.Seconds == seconds && Vector2.Distance(body.position, pausedPosition) < .001f, "Pause freezes framework timer and player physics");
        services.SetPaused(false);
        string saved = map.SaveRuntimeState();
        int oldInstance = target.GetInstanceID();
        Move(body, new Vector2(85, 2.1f));
        while (streamer.TryGetActor(targetId, out _)) yield return null;
        Check(map.RuntimeState != null, "Leaving range returns the target to the project pool");
        Move(body, new Vector2(4.5f, 2.1f));
        while (!streamer.TryGetActor(targetId, out target)) yield return null;
        Check(target.Health == 40 && target.GetInstanceID() == oldInstance, "Pool reuse preserves map entity health");
        target.GetComponent<SkillActor2D>().BeHit(new SkillHitData { attackValue = 100, soure = player });
        while (streamer.TryGetActor(targetId, out _)) yield return null;
        Check(hud.HitCount == 2, "Lethal hit emits one event and recycles the target");
        while (!streamer.TryGetActor(targetId, out target)) yield return null;
        Check(target.Health == 50, "Map respawn restores spawn-defined health");
        map.LoadRuntimeState(saved);
        while (!streamer.TryGetActor(targetId, out target)) yield return null;
        Check(target.Health == 40, "Existing map JSON restore remains compatible with the adapter");
        body.simulated = true;
        var duplicate = new GameObject("Duplicate bootstrap"); duplicate.SetActive(false);
        duplicate.AddComponent<GameServices2D>().Assets = services.Assets; duplicate.SetActive(true);
        yield return null; yield return null;
        Check(GameServices2D.Instance == services && FindObjectsOfType<GameEntry>().Length == 1 && FindObjectsOfType<PoolDriver>().Length == 1,
            "Duplicate bootstrap does not create extra services");
        var cameraChecks = CameraChecks(world, input, body);
        while (cameraChecks.MoveNext()) yield return cameraChecks.Current;
        Capture(Camera.main);
        var eventList = GameEntry.Event.CommonEvent;
        int staleCalls = 0;
        eventList.AddEventListener(65002, _ => staleCalls++);
        var retainedNode = eventList.dic[65002].First;
        var shutdown = services.ShutdownAsync();
        Check(ReferenceEquals(shutdown, services.ShutdownAsync()), "Repeated shutdown shares one pending C# task");
        while (!shutdown.IsCompleted) yield return null;
        Check(retainedNode.Value == null && eventList.dic.Count == 0 && staleCalls == 0,
            "Shutdown releases retained C# event nodes and subscriptions");
        if (shutdown.IsFaulted) throw shutdown.Exception;
        yield return null; yield return null;
        Check(!GameServices2D.Instance && FindObjectsOfType<PoolDriver>().Length == 0 && !GameEntry.Instance &&
            Array.TrueForAll(NativeManagers(), manager => manager == null),
            "Async shutdown drains map pools and clears all original managers before destroying services");
#if UNITY_EDITOR
        var reload = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/__Framework2DChecks/WithExistingDriver.unity", new LoadSceneParameters(LoadSceneMode.Single));
        while (!reload.isDone) yield return null;
#endif
        world = FindObjectOfType<WorldSession2D>();
        yield return Until(() => GameServices2D.Instance && GameServices2D.Instance.IsReady, 60, "Second original framework content bootstrap");
        yield return Until(() => world && world.IsReady, 60, "Second world terrain and native HUD readiness");
        Check(GameServices2D.Instance && GameEntry.Instance && FindObjectsOfType<PoolDriver>().Length == 1, "A second scene session boots cleanly after shutdown");
        var restartedManagers = NativeManagers();
        bool freshManagers = true;
        for (int i = 0; i < originalManagers.Length; i++)
            freshManagers &= restartedManagers[i] != null && !ReferenceEquals(originalManagers[i], restartedManagers[i]);
        Check(freshManagers && FindObjectOfType<FrameworkDemoHud2D>().UIFormId == 9002,
            "A second native session creates 18 fresh managers and reloads C# session data and its HUD");
        var existingDriver = GameServices2D.Instance.Pools;
        Check(existingDriver.name == "Existing Project Pool Driver", "A scene-authored pool driver initializes first and is reused");
        var finish = GameServices2D.Instance.ShutdownAsync();
        while (!finish.IsCompleted) yield return null;
        if (finish.IsFaulted) throw finish.Exception;
        Check(existingDriver && existingDriver.Service.State == BigWorld.Pooling.PoolState.Running, "Shutdown preserves an externally owned pool driver");
        var externalClose = existingDriver.ShutdownAsync();
        while (!externalClose.IsCompleted) yield return null;
        if (externalClose.IsFaulted) throw externalClose.Exception;
    }

    private IEnumerator ReporterResourceChecks()
    {
        const string iconPath = "Assets/Download/Reporter/ReporterRes/log_icon.png";
        const string skinPath = "Assets/Download/Reporter/ReporterRes/reporterScrollerSkin.guiskin";
        var loader = GameEntry.Resource.ResourceLoaderManager;
        var pool = GameEntry.Pool.AssetPool[AssetCategory.Reporter];
        ResourceEntity icon = null, skin = null;
        bool validTypes = false;
        int iconReferences = 0, skinReferences = 0;
        try
        {
            loader.LoadMainAsset(AssetCategory.Reporter, iconPath, resource => icon = resource);
            yield return Until(() => icon != null, 20, "Original Reporter icon bundle load");
            loader.LoadMainAsset(AssetCategory.Reporter, skinPath, resource => skin = resource);
            yield return Until(() => skin != null, 20, "Original Reporter GUISkin bundle load");
            validTypes = icon.Target is Texture2D && skin.Target is GUISkin &&
                icon.Category == AssetCategory.Reporter && skin.Category == AssetCategory.Reporter &&
                icon.ResourceName == iconPath && skin.ResourceName == skinPath;
            iconReferences = icon.ReferenceCount;
            skinReferences = skin.ReferenceCount;
        }
        finally
        {
            // Return only this test's two main-asset references; the original pools own eviction/unloading.
            if (icon != null) pool.Unspawn(iconPath);
            if (skin != null) pool.Unspawn(skinPath);
        }
        Check(validTypes && iconReferences > 0 && skinReferences > 0 &&
            icon.ReferenceCount == iconReferences - 1 && skin.ReferenceCount == skinReferences - 1,
            "Original Reporter resources load real Texture2D and GUISkin through bundles and return their asset references");
    }

    private static object[] NativeManagers() => new object[]
    {
        GameEntry.Logger, GameEntry.Event, GameEntry.Time, GameEntry.Fsm, GameEntry.Procedure,
        GameEntry.DataTable, GameEntry.Socket, GameEntry.Http, GameEntry.Data, GameEntry.Localization,
        GameEntry.Pool, GameEntry.Scene, GameEntry.Resource, GameEntry.Download, GameEntry.UI,
        GameEntry.Audio, GameEntry.Input, GameEntry.Task
    };

    private static IEnumerator Until(Func<bool> ready, float seconds, string step)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            if (GameServices2D.Instance && !string.IsNullOrEmpty(GameServices2D.Instance.InitializationError))
                throw new InvalidOperationException("Original framework initialization failed: " + GameServices2D.Instance.InitializationError);
            if (Time.realtimeSinceStartup > timeout) throw new TimeoutException(step);
            yield return null;
        }
    }

    private IEnumerator CameraChecks(WorldSession2D world, PlatformerDemoInput2D input, Rigidbody2D body)
    {
        var camera = Camera.main;
        var follow = camera ? camera.GetComponent<CameraFollow2D>() : null;
        Check(camera && camera.orthographic && follow && follow.Target == world.Player && follow.Map == world.Map &&
            follow.Brain && follow.VirtualCamera && follow.Brain.IsLive(follow.VirtualCamera),
            "Cinemachine brain drives the bound orthographic 2D follow camera");

        var facing = body.GetComponent<SkillFacing2D>();
        var savedPosition = body.position;
        var savedVelocity = body.velocity;
        var savedSimulated = body.simulated;
        var savedInputEnabled = input.enabled;
        var savedDirection = facing.Direction;
        var savedFollowSprite = facing.FollowSprite;
        var savedFlip = facing.Sprite && facing.Sprite.flipX;
        var savedPaused = GameServices2D.Instance.IsPaused;
        var map = world.Map;
        var mapAsset = map.Map;
        var localMin = mapAsset.Origin;
        var localMax = localMin + new Vector2(mapAsset.Width, mapAsset.Height) * mapAsset.CellSize;
        Vector2 center = map.transform.TransformPoint((localMin + localMax) * .5f);
        try
        {
            input.SetInput(0, false);
            input.enabled = false;
            Move(body, center);
            facing.SetFacing(1);
            follow.Snap();
            yield return null; yield return null;
            var rightPosition = camera.transform.position;
            Check(rightPosition.x > body.position.x + follow.LookAheadDistance * .5f,
                "Facing right gives horizontal look-ahead in the map interior");

            facing.SetFacing(-1);
            yield return new WaitForSeconds(1.5f);
            Check(camera.transform.position.x < body.position.x - follow.LookAheadDistance * .5f &&
                rightPosition.x - camera.transform.position.x > follow.LookAheadDistance,
                "Facing left smoothly switches look-ahead to the opposite side");

            facing.SetFacing(1);
            follow.Snap();
            yield return null; yield return null;
            float beforeHopY = camera.transform.position.y;
            Move(body, center + Vector2.up * .5f);
            yield return new WaitForSeconds(.3f);
            Check(Mathf.Abs(camera.transform.position.y - beforeHopY) < .06f,
                "A half-cell hop inside the vertical dead zone does not bob the camera");

            Vector2 destination = map.transform.TransformPoint(new Vector2(Mathf.Lerp(localMin.x, localMax.x, .75f), (localMin.y + localMax.y) * .5f));
            Move(body, destination);
            follow.Snap();
            yield return null; yield return null;
            Check(Mathf.Abs(camera.transform.position.x - destination.x - follow.LookAheadDistance) < .15f &&
                Mathf.Abs(camera.transform.position.y - destination.y - follow.VerticalOffset) < .15f,
                "Explicit camera Snap resets damping immediately after a distant teleport");

            var corners = new[]
            {
                new Vector2(localMin.x, localMin.y), new Vector2(localMax.x, localMin.y),
                new Vector2(localMin.x, localMax.y), new Vector2(localMax.x, localMax.y)
            };
            var cornerNames = new[] { "bottom-left", "bottom-right", "top-left", "top-right" };
            for (int i = 0; i < corners.Length; i++)
            {
                Move(body, map.transform.TransformPoint(corners[i]));
                facing.SetFacing(i % 2 == 0 ? -1 : 1);
                follow.Snap();
                yield return null; yield return null;
                Check(ViewportInsideMap(camera, map, localMin, localMax),
                    "The complete camera viewport stays inside the map at the " + cornerNames[i] + " edge");
            }

            Move(body, center);
            facing.SetFacing(1);
            follow.Snap();
            yield return null; yield return null;
            GameServices2D.Instance.SetPaused(true);
            Vector3 pausedCamera = camera.transform.position;
            facing.SetFacing(-1);
            yield return new WaitForSecondsRealtime(.3f);
            Check(Vector3.Distance(camera.transform.position, pausedCamera) < .01f,
                "Pausing freezes camera damping even when the facing input changes");
            GameServices2D.Instance.SetPaused(false);

            // Camera checks travel through unloaded parts of the map. Reload the original
            // ground before restoring physics and continuing the existing integration checks.
            Move(body, savedPosition);
            facing.SetFacing(savedDirection);
            follow.Snap();
            var originalCell = map.WorldToCell(savedPosition);
            while (!map.IsCellLoaded(originalCell.x, originalCell.y)) yield return null;
        }
        finally
        {
            GameServices2D.Instance.SetPaused(savedPaused);
            Move(body, savedPosition);
            body.velocity = savedVelocity;
            body.simulated = savedSimulated;
            input.enabled = savedInputEnabled;
            facing.SetFacing(savedDirection);
            facing.FollowSprite = savedFollowSprite;
            if (facing.Sprite) facing.Sprite.flipX = savedFlip;
            follow.Snap();
        }
    }

    private static bool ViewportInsideMap(Camera camera, GridMapRenderer map, Vector2 localMin, Vector2 localMax)
    {
        float depth = Mathf.Abs(camera.transform.position.z - map.transform.position.z);
        const float tolerance = .035f;
        for (int y = 0; y <= 1; y++)
        for (int x = 0; x <= 1; x++)
        {
            var point = map.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(x, y, depth)));
            if (point.x < localMin.x - tolerance || point.x > localMax.x + tolerance ||
                point.y < localMin.y - tolerance || point.y > localMax.y + tolerance) return false;
        }
        return true;
    }

    private static void Move(Rigidbody2D body, Vector2 position)
    { body.simulated = false; body.velocity = Vector2.zero; body.position = position; body.transform.position = new Vector3(position.x, position.y, 0); Physics2D.SyncTransforms(); }
    private void Check(bool valid, string name)
    { if (!valid) throw new Exception(name); report.checks.Add(name); report.passed++; Debug.Log("2D CHECK PASSED: " + name); }
    private void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message + "\n" + stack; }
    private void Capture(Camera camera)
    {
        if (!camera || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var render = new RenderTexture(1280, 720, 24); var old = camera.targetTexture; var active = RenderTexture.active;
        var follow = camera.GetComponent<CameraFollow2D>();
        Texture2D texture = null;
        try
        {
            camera.targetTexture = render;
            // Rendering to another aspect ratio must update Cinemachine before capturing the viewport.
            if (follow)
            {
                follow.Snap();
                var map = follow.Map;
                var min = map.Map.Origin;
                var max = min + new Vector2(map.Map.Width, map.Map.Height) * map.Map.CellSize;
                Check(ViewportInsideMap(camera, map, min, max), "Camera bounds refresh for the 16:9 capture aspect ratio");
            }
            camera.Render(); RenderTexture.active = render;
            texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, "demo-camera.png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = old;
            if (follow) follow.Snap();
            RenderTexture.active = active; render.Release(); Destroy(render); if (texture) Destroy(texture);
        }
    }
    private void Finish(string error)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        report.unityVersion = Application.unityVersion; report.failure = error;
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "play-checks.json"), JsonUtility.ToJson(report, true));
        Time.timeScale = 1;
        if (error == null) Debug.Log("BIGWORLD_FRAMEWORK2D_VALIDATION_PASSED"); else Debug.LogError(error);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(error == null ? 0 : 1);
#endif
    }
}

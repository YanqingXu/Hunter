using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using YouYou;
using YouYou.DataTable;

public enum YouYouValidationStage { Core, Content, Bundles, Scenes, Network }

/// <summary>Exercises the original manager implementations and reports only completed checks.</summary>
[DefaultExecutionOrder(-2000)]
public sealed class YouYouFullValidationProbe : MonoBehaviour
{
    public GameEntry Entry;
    public YouYouValidationStage Stage;
    public const string PayloadPath = "Assets/YouYouFullValidation/Fixtures/MigrationPayload.txt";
    public string BundleFixtureDirectory, BundleRelativePath;
    public string[] OriginalBuildPaths;
    public bool[] OriginalBuildEnabled;
    private readonly List<string> checks = new List<string>();
    private readonly List<string> warnings = new List<string>();
    private string failure;
    private bool finished;
    private float deadline;
    private int timerTicks;
    private bool timerComplete;
    private readonly object tableLock = new object();
    private readonly HashSet<string> loadedTables = new HashSet<string>();
    private Stack<IEnumerator> running;
    private string expectedOfflineError;

    private void Awake()
    {
        deadline = UnityEngine.Time.realtimeSinceStartup + 180;
        Application.logMessageReceived += OnLog;
    }

    private void OnDestroy() { DisposeRunning(); Application.logMessageReceived -= OnLog; }
    private void OnLog(string message, string stack, LogType type)
    {
        if (finished) return;
        if (type == LogType.Error && expectedOfflineError != null &&
            (message == expectedOfflineError || message == "[youyou]" + expectedOfflineError)) return;
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            failure = failure ?? message + "\n" + stack;
        else if (type == LogType.Warning) warnings.Add(message);
    }

    private IEnumerator Start()
    {
        var stack = running = new Stack<IEnumerator>();
        stack.Push(Checks());
        while (stack.Count > 0 && !finished)
        {
            if (failure != null) { Finish(false); yield break; }
            if (UnityEngine.Time.realtimeSinceStartup > deadline)
            { failure = "Validation exceeded 180 seconds."; Finish(false); yield break; }
            object next = null;
            bool moved = false;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception exception) { failure = exception.ToString(); }
            if (failure != null) { Finish(false); yield break; }
            if (!moved)
            {
                var ended = stack.Pop();
                try { (ended as IDisposable)?.Dispose(); } catch (Exception exception) { failure = exception.ToString(); }
                continue;
            }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        if (!finished) Finish(failure == null);
    }

    private IEnumerator Checks()
    {
        yield return null;
        Require(Entry && Entry.IsInitialized && GameEntry.Instance == Entry, "Original GameEntry initializes independently");
        string[] managers = { "Logger", "Event", "Time", "Fsm", "Procedure", "DataTable", "Socket", "Http", "Data",
            "Localization", "Pool", "Scene", "Resource", "Download", "UI", "Audio", "Input", "Task" };
        var firstManagers = new Dictionary<string, object>();
        foreach (string name in managers)
        {
            var property = typeof(GameEntry).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            object manager = property?.GetValue(null);
            Require(manager != null && manager.GetType().Namespace == "YouYou", "Original manager present: " + name);
            firstManagers.Add(name, manager);
        }
        Require(!Entry.AutoLaunchProcedure && GameEntry.Procedure.CurrFsm.CurrStateType == -1,
            "Original account/update flow remains unstarted");
        Require(GameEntry.ParamsSettings == null ? false : GameEntry.UI.UIPoolMaxCount == 10 && GameEntry.Audio != null,
            "Original high-grade ParamsSettings initializes UI manager");
        for (byte i = 1; i <= 4; i++) Require(GameEntry.UI.GetUIGroup(i)?.Group != null, "Original UI group " + i);
        CheckLogger();

        int eventCount = 0;
        object payload = new object();
        CommonEvent.OnActionHandler handler = value => { if (ReferenceEquals(value, payload)) eventCount++; };
        GameEntry.Event.CommonEvent.AddEventListener(65000, handler);
        GameEntry.Event.CommonEvent.Dispatch(65000, payload);
        GameEntry.Event.CommonEvent.RemoveEventListener(65000, handler);
        GameEntry.Event.CommonEvent.Dispatch(65000, payload);
        Require(eventCount == 1, "Original event dispatch preserves payload and unsubscribe");
        CheckEventDisposal();

        var pooled = GameEntry.Pool.DequeueClassObject<YouYouValidationPooledObject>();
        pooled.Value = 37;
        GameEntry.Pool.EnqueueClassObject(pooled);
        var reused = GameEntry.Pool.DequeueClassObject<YouYouValidationPooledObject>();
        Require(ReferenceEquals(pooled, reused) && reused.Value == 37, "Original class pool reuses the real instance");
        GameEntry.Pool.EnqueueClassObject(reused);

        var owner = new YouYouValidationFsmOwner();
        var first = new YouYouValidationFsmState(owner);
        var second = new YouYouValidationFsmState(owner);
        var fsm = GameEntry.Fsm.Create(owner, new FsmState<YouYouValidationFsmOwner>[] { first, second });
        fsm.SetData("probe", 73);
        fsm.ChangeState(0); fsm.OnUpate(); fsm.ChangeState(1);
        Require(owner.Entered == 2 && owner.Left == 1 && owner.Updated == 1 && fsm.GetData<int>("probe") == 73,
            "Original FSM executes states and typed parameter storage");
        GameEntry.Fsm.DestroyFsm(fsm.FsmId);
        Require(owner.Destroyed == 2, "Original FSM destroys both registered states");

        GameEntry.Time.CreateTimeAction().Init("migration-timer", .02f, .03f, 2,
            onUpdate: OnTimerTick, onComplete: OnTimerComplete).Run();
        yield return Until(() => timerComplete, 4, "Original TimeAction callbacks");
        Require(timerTicks == 2, "Original fixed-update timer completes two ticks");

        int sequence = 0;
        bool taskComplete = false;
        var group = GameEntry.Task.CreateTaskGroup();
        var task1 = GameEntry.Task.CreateTaskRoutine();
        var task2 = GameEntry.Task.CreateTaskRoutine();
        task1.CurrTask = () => { sequence = 1; task1.Leave(); };
        task2.CurrTask = () => { if (sequence == 1) sequence = 2; task2.Leave(); };
        group.AddTask(task1); group.AddTask(task2);
        group.OnComplete = () => taskComplete = true;
        group.Run();
        yield return Until(() => taskComplete, 4, "Original sequential task group");
        Require(sequence == 2, "Original task group runs in order and completes");
        int concurrentStarted = 0;
        bool concurrentComplete = false;
        var parallel = GameEntry.Task.CreateTaskGroup();
        for (int i = 0; i < 2; i++)
        {
            var task = GameEntry.Task.CreateTaskRoutine();
            task.CurrTask = () => { concurrentStarted++; task.Leave(); };
            parallel.AddTask(task);
        }
        parallel.OnComplete = () => concurrentComplete = true;
        parallel.Run(true);
        Require(concurrentStarted == 2, "Original concurrent task group starts both routines");
        yield return Until(() => concurrentComplete, 4, "Original concurrent task completion");
        Require(concurrentComplete, "Original concurrent task group completes");
        Require(!GameEntry.Input.IsPointerOverGameObject(new Vector2(-10, -10)), "Original input UI raycast works with EventSystem");

        if (Stage == YouYouValidationStage.Content) yield return ContentChecks();
        if (Stage == YouYouValidationStage.Bundles) yield return BundleChecks();
        if (Stage == YouYouValidationStage.Scenes) yield return YouYouSceneValidation.Run(Require);
        if (Stage == YouYouValidationStage.Network) yield return YouYouNetworkValidation.Run(Require);
        Require(GameEntry.Procedure.CurrFsm.CurrStateType == -1, "Validation never entered original network flow");
        int staleEvents = 0;
        GameEntry.Event.CommonEvent.AddEventListener(65002, value => staleEvents++);
        var retainedList = GameEntry.Event.CommonEvent.dic[65002];
        var retainedNode = retainedList.First;
        int shutdownFrame = UnityEngine.Time.frameCount;
        var shutdown = Entry.ShutdownAsync();
        Require(ReferenceEquals(shutdown, Entry.ShutdownAsync()) && !shutdown.IsCompleted && !Entry.IsInitialized,
            "Async shutdown stops the update loop immediately and shares one pending task");
        Entry.Shutdown(); // Synchronous/OnDestroy path must not overtake the pending async phase.
        Require(!shutdown.IsCompleted, "Synchronous shutdown does not overtake a pending async shutdown");
        yield return Until(() => shutdown.IsCompleted, 10, "First async shutdown");
        Require(UnityEngine.Time.frameCount > shutdownFrame, "Async resource disposal crosses a real Unity frame boundary");
        if (shutdown.IsFaulted) throw shutdown.Exception.GetBaseException();
        Require(!shutdown.IsCanceled, "First async lifecycle completes after all callback owners release their delegates");
        Require(retainedList.Count == 0 && retainedNode.Value == null,
            "Shutdown releases C# callbacks even when callers retain event lists and nodes");
        Require(!Entry.IsInitialized && GameEntry.Instance == null, "Original GameEntry shuts down cleanly");
        Require(default(DTSys_UIFormList).GetList().Count == 0 && default(DTSys_SceneList).GetList().Count == 0,
            "Original static table caches are empty after shutdown");

        Entry.Initialize();
        yield return null;
        bool freshManagers = Entry.IsInitialized && GameEntry.Instance == Entry;
        foreach (string name in managers)
        {
            object manager = typeof(GameEntry).GetProperty(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            freshManagers &= manager != null && !ReferenceEquals(manager, firstManagers[name]);
        }
        Require(freshManagers, "Same GameEntry reinitializes all 18 original managers as fresh instances");
        GameEntry.Event.CommonEvent.Dispatch(65002);
        Require(staleEvents == 0 && GameEntry.Procedure.CurrFsm.CurrStateType == -1,
            "Same-process restart retains no old event subscriptions or running launch flow");
        timerComplete = false; timerTicks = 0;
        GameEntry.Time.CreateTimeAction().Init("migration-restart-timer", .02f, .03f, 2,
            onUpdate: OnTimerTick, onComplete: OnTimerComplete).Run();
        yield return Until(() => timerComplete, 4, "Original timer after reinitialization");
        Require(timerTicks == 2, "Original manager update loop works after same-process restart");
        if (Stage == YouYouValidationStage.Content) yield return ContentChecks();
        int secondShutdownFrame = UnityEngine.Time.frameCount;
        var secondShutdown = Entry.ShutdownAsync();
        yield return Until(() => secondShutdown.IsCompleted, 10, "Second async shutdown");
        if (secondShutdown.IsFaulted) throw secondShutdown.Exception.GetBaseException();
        Require(!secondShutdown.IsCanceled && UnityEngine.Time.frameCount > secondShutdownFrame,
            "Second async shutdown also completes beyond the callback release frame");
        Require(!Entry.IsInitialized && GameEntry.Instance == null &&
            default(DTSys_UIFormList).GetList().Count == 0, "Second lifecycle releases managers and static tables cleanly");

        Entry.Initialize();
        Require(Entry.IsInitialized, "A third lifecycle can start after async shutdown completes");
        var destroyedOwnerShutdown = Entry.ShutdownAsync();
        Destroy(Entry.gameObject);
        yield return Until(() => destroyedOwnerShutdown.IsCompleted, 10, "Pending shutdown after destroying the owner");
        if (destroyedOwnerShutdown.IsFaulted) throw destroyedOwnerShutdown.Exception.GetBaseException();
        Require(!destroyedOwnerShutdown.IsCanceled && !Entry && ReferenceEquals(GameEntry.Instance, null) &&
            GameEntry.Pool == null,
            "Destroying the owner during async shutdown preserves the pending task and completes remaining cleanup");
    }

    private void OnTimerTick(int remaining) { timerTicks++; }
    private void OnTimerComplete() { timerComplete = true; }

    private void CheckEventDisposal()
    {
        var common = new CommonEvent();
        int commonCalls = 0;
        common.AddEventListener(1, value => { commonCalls++; common.Dispose(); });
        common.AddEventListener(1, value => commonCalls++);
        var retainedList = common.dic[1];
        var firstNode = retainedList.First;
        var secondNode = firstNode.Next;
        common.Dispatch(1);
        Require(commonCalls == 1, "CommonEvent disposal inside dispatch stops remaining callbacks");
        Require(common.dic.Count == 0 && retainedList.Count == 0 && firstNode.Value == null && secondNode.Value == null,
            "CommonEvent disposal releases callback values held by external nodes");

        var socket = new SocketEvent();
        int socketCalls = 0;
        socket.AddEventListener(1, value => { socketCalls++; socket.Dispose(); });
        socket.AddEventListener(1, value => socketCalls++);
        socket.Dispatch(1);
        Require(socketCalls == 1, "SocketEvent disposal inside dispatch stops remaining callbacks");
    }

    private void CheckLogger()
    {
        string ownedPath = Path.GetFullPath(GameEntry.Logger.LogPath);
        string logRoot = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!ownedPath.StartsWith(logRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Validation logger path is outside persistentDataPath.");
        string token = "youyou-validation-log-" + Guid.NewGuid().ToString("N");
        try
        {
            GameEntry.Logger.Write(token, LogType.Log);
            GameEntry.Logger.SyncLog();
            Require(File.Exists(ownedPath) && File.ReadAllText(ownedPath).Contains(token),
                "Original Logger.Write and SyncLog persist the actual message to its unique session file");
        }
        finally { if (File.Exists(ownedPath)) File.Delete(ownedPath); }
    }

    private IEnumerator ContentChecks()
    {
#if !DISABLE_ASSETBUNDLE
        throw new InvalidOperationException("Content stage requires the original DISABLE_ASSETBUNDLE editor mode. AssetBundle validation is a separate stage.");
#else
        lock (tableLock) loadedTables.Clear();
        GameEntry.Event.CommonEvent.AddEventListener(SysEventId.LoadOneDataTableComplete, OnTableLoaded);
        GameEntry.DataTable.LoadDataAllTable();
        yield return Until(() => { lock (tableLock) return loadedTables.Count >= GameEntry.DataTable.TotalTableCount && loadedTables.Count > 0; },
            30, "All original compressed FlatBuffers tables");
        GameEntry.Event.CommonEvent.RemoveEventListener(SysEventId.LoadOneDataTableComplete, OnTableLoaded);
        Require(GameEntry.DataTable.Sys_UIFormList.GetEntity(UIFormId.UI_Dialog).HasValue &&
            GameEntry.DataTable.Sys_AudioList.GetEntity(ConstDefine.Aduio_ButtonClick).HasValue &&
            GameEntry.DataTable.Sys_SceneList.GetList().Count > 0,
            "Original UI/audio/scene FlatBuffers tables contain real entries");
        int uiRows = GameEntry.DataTable.Sys_UIFormList.GetList().Count;
        bool uiReloaded = false;
        CommonEvent.OnActionHandler reloaded = value => { if (value?.ToString() == DataTableDefine.DTSys_UIFormName) uiReloaded = true; };
        GameEntry.Event.CommonEvent.AddEventListener(SysEventId.LoadOneDataTableComplete, reloaded);
        try
        {
            byte[] uiBytes = ZlibHelper.DeCompressBytes(File.ReadAllBytes(Path.Combine(Application.dataPath, "Download/DataTable/DTSys_UIForm.bytes")));
            DTSys_UIFormListExt.Init(DTSys_UIFormList.GetRootAsDTSys_UIFormList(new FlatBuffers.ByteBuffer(uiBytes)));
            yield return Until(() => uiReloaded, 10, "Original UI FlatBuffers table reload");
            Require(GameEntry.DataTable.Sys_UIFormList.GetList().Count == uiRows &&
                GameEntry.DataTable.Sys_UIFormList.GetEntity(UIFormId.UI_Dialog).HasValue,
                "Original generated table reload replaces cache without duplicate rows");
        }
        finally { GameEntry.Event.CommonEvent.RemoveEventListener(SysEventId.LoadOneDataTableComplete, reloaded); }

        bool banksReady = false;
        GameEntry.Audio.LoadBanks(() => banksReady = true);
        yield return Until(() => banksReady, 20, "Original FMOD bank loader");
        var sound = GameEntry.DataTable.Sys_AudioList.GetEntity(ConstDefine.Aduio_ButtonClick).Value;
        Require(FMODUnity.RuntimeManager.GetEventDescription(sound.AssetPath).isValid(), "Native FMOD resolves the original UI sound event");
        int serial = GameEntry.Audio.PlayAudio(sound.AssetPath, 0);
        Require(serial >= 0 && GameEntry.Audio.PausedAudio(serial) && GameEntry.Audio.StopAudio(serial),
            "Original FMOD string API creates, pauses, and stops an event instance");
        serial = GameEntry.Audio.PlayAudio(ConstDefine.Aduio_ButtonClick);
        Require(serial >= 0 && GameEntry.Audio.StopAudio(serial), "Original table-driven FMOD ID API works in initialized offline mode");

        UIFormBase opened = null;
        var userData = GameEntry.Pool.DequeueClassObject<BaseParams>();
        userData.Reset(); userData.IntParam1 = 0; userData.StringParam1 = "Original framework migration validation";
        GameEntry.UI.OpenUIForm(UIFormId.UI_Dialog, userData, form => opened = form);
        yield return Until(() => opened, 10, "Original table-driven dialog prefab");
        yield return null;
        Require(opened.CurrCanvas && opened.GroupId > 0 && ReferenceEquals(opened.UserData, userData),
            "Original UI loads real prefab with original lifecycle and group");
        int formInstance = opened.GetInstanceID();
        GameEntry.UI.CloseUIForm(opened);
        opened = null;
        GameEntry.UI.OpenUIForm(UIFormId.UI_Dialog, userData, form => opened = form);
        yield return Until(() => opened, 10, "Original UI cache reopen");
        Require(opened.GetInstanceID() == formInstance, "Original UI cache reuses dialog instance");
        GameEntry.UI.CloseUIForm(opened);

        yield return PureCSharpChecks.Run(Require);
        GameEntry.Pool.EnqueueClassObject(userData);
#endif
    }

    private IEnumerator BundleChecks()
    {
#if DISABLE_ASSETBUNDLE
        throw new InvalidOperationException("RunBundles requires the real AssetBundle compilation path.");
#else
        string destination = Path.GetFullPath(Path.Combine(Application.persistentDataPath, BundleRelativePath));
        string ownedRoot = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "youyou-validation")) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid validation cache path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        foreach (string version in new[] { "first", "replacement" })
        {
            string directory = Path.Combine(BundleFixtureDirectory, version);
            File.Copy(Path.Combine(directory, "probe.assetbundle"), destination, true);
            GameEntry.Resource.ResourceManager.InitializeLocalManifest(File.ReadAllBytes(Path.Combine(directory, "version.bytes")));
            GameEntry.Resource.ResourceLoaderManager.InitializeLocalAssetInfo(File.ReadAllBytes(Path.Combine(directory, "assetinfo.bytes")));
            Require(GameEntry.Resource.ResourceManager.CDNVersion == version &&
                GameEntry.Resource.ResourceLoaderManager.GetAssetEntity(AssetCategory.DataTable, PayloadPath) != null,
                "Original compressed version and asset-info parsers: " + version);
            ResourceEntity asset = null;
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.DataTable, PayloadPath, result => asset = result);
            Require(asset == null, "Uncached main asset uses original asynchronous task/bundle loader: " + version);
            yield return Until(() => asset != null, 20, "Original main AssetBundle resource: " + version);
            Require(asset.Target is TextAsset text && text.text == "original-youyou-" + version,
                "Original AB chain loads the actual bundled payload: " + version);
            ResourceEntity cached = null;
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.DataTable, PayloadPath, result => cached = result);
            yield return Until(() => cached != null, 4, "Original main asset cache");
            Require(ReferenceEquals(asset, cached) && asset.ReferenceCount == 2,
                "Original asset pool reuses resource and counts references: " + version);
            AssetBundle bundle = null;
            GameEntry.Resource.ResourceLoaderManager.LoadAssetBundle(BundleRelativePath, onComplete: value => bundle = value);
            Require(bundle != null, "Original AssetBundle pool returns the loaded bundle: " + version);
            GameEntry.Pool.AssetPool[AssetCategory.DataTable].Unspawn(PayloadPath);
            GameEntry.Pool.AssetPool[AssetCategory.DataTable].Unspawn(PayloadPath);
            Require(asset.ReferenceCount == 0, "Original asset references return to zero: " + version);
            GameEntry.Pool.AssetPool[AssetCategory.DataTable].ReleaseAll();
            GameEntry.Pool.AssetBundlePool.ReleaseAll();
            yield return null;
        }
        Require(true, "Released original bundle path reloads changed bytes without stale cached data");
        yield return OfflineBundleChecks(destination);
#endif
    }

    private IEnumerator OfflineBundleChecks(string persistentDestination)
    {
        string[] parts = BundleRelativePath.Replace('\\', '/').Split('/');
        if (parts.Length != 3 || parts[0] != "youyou-validation" || !Guid.TryParseExact(parts[1], "N", out _) || parts[2] != "probe.assetbundle")
            throw new InvalidOperationException("Offline fixture must use its own generated GUID directory.");
        string streamingRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "youyou-validation")) + Path.DirectorySeparatorChar;
        string ownedDirectory = Path.GetFullPath(Path.Combine(streamingRoot, parts[1]));
        if (!ownedDirectory.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(ownedDirectory))
            throw new InvalidOperationException("Offline fixture cannot replace an existing StreamingAssets directory.");
        string streamingDestination = Path.Combine(ownedDirectory, "probe.assetbundle");
        string replacement = Path.Combine(BundleFixtureDirectory, "replacement");
        byte[] manifest = File.ReadAllBytes(Path.Combine(replacement, "version.bytes"));
        var resources = GameEntry.Resource.ResourceManager;
        Directory.CreateDirectory(ownedDirectory);
        try
        {
            File.Copy(Path.Combine(BundleFixtureDirectory, "first/probe.assetbundle"), persistentDestination, true);
            File.Copy(Path.Combine(replacement, "probe.assetbundle"), streamingDestination);
            resources.InitializeLocalManifest(manifest, true);
            var info = resources.GetAssetBundleInfo(BundleRelativePath);
            byte[] shipped = File.ReadAllBytes(streamingDestination);
            string shippedHash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                shippedHash = BitConverter.ToString(md5.ComputeHash(shipped)).Replace("-", "").ToLowerInvariant();
            Require(resources.StreamingAssetsOnly && (ulong)shipped.Length == info.Size && shippedHash == info.MD5,
                "Packaged offline mode uses the original manifest size and actual shipped bundle MD5");
            ResourceEntity loaded = null;
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.DataTable, PayloadPath, value => loaded = value);
            yield return Until(() => loaded != null, 20, "Original offline main loader with stale persistent cache");
            Require(loaded.Target is TextAsset text && text.text == "original-youyou-replacement" && resources.OfflineLoadError == null,
                "Original main loader uses shipped replacement bytes instead of stale persistentDataPath bundle");
            GameEntry.Pool.AssetPool[AssetCategory.DataTable].Unspawn(PayloadPath);
            GameEntry.Pool.AssetPool[AssetCategory.DataTable].ReleaseAll();
            GameEntry.Pool.AssetBundlePool.ReleaseAll();
            yield return null;

            // Keep the length identical: only a real content hash comparison can reject this fixture.
            shipped[shipped.Length - 1] ^= 1;
            File.WriteAllBytes(streamingDestination, shipped);
            resources.InitializeLocalManifest(manifest, true);
            expectedOfflineError = "Offline AssetBundle missing or does not match VersionFile: " + BundleRelativePath;
            ResourceEntity rejected = null;
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.DataTable, PayloadPath, value => rejected = value);
            yield return Until(() => resources.OfflineLoadError != null, 10, "Offline bundle hash rejection");
            Require(resources.OfflineLoadError == expectedOfflineError && rejected == null,
                "Original offline loader rejects an equal-size tampered bundle by MD5 without returning stale cache");
        }
        finally
        {
            expectedOfflineError = null;
            resources.InitializeLocalManifest(manifest);
            File.Copy(Path.Combine(replacement, "probe.assetbundle"), persistentDestination, true);
            // The exact absolute directory was checked above and did not exist before this test.
            if (Directory.Exists(ownedDirectory)) Directory.Delete(ownedDirectory, true);
        }
        Require(!resources.StreamingAssetsOnly && resources.OfflineLoadError == null && !Directory.Exists(ownedDirectory),
            "Offline fixture restores the original loader mode and removes only its owned GUID directory");
    }

    private void OnTableLoaded(object value) { lock (tableLock) loadedTables.Add(value?.ToString() ?? ""); }
    private IEnumerator Until(Func<bool> predicate, float seconds, string label)
    {
        float until = UnityEngine.Time.realtimeSinceStartup + seconds;
        while (!predicate())
        { if (UnityEngine.Time.realtimeSinceStartup > until) throw new TimeoutException(label); yield return null; }
    }
    private void Require(bool valid, string description)
    {
        if (!valid) throw new InvalidOperationException(description);
        checks.Add(description);
        Debug.Log("YOUYOU_CHECK_PASS: " + description);
    }
    private void Finish(bool passed)
    {
        finished = true;
        DisposeRunning();
        passed = passed && failure == null;
#if UNITY_EDITOR
        if (OriginalBuildPaths != null && OriginalBuildEnabled != null && OriginalBuildPaths.Length == OriginalBuildEnabled.Length)
        {
            var scenes = new UnityEditor.EditorBuildSettingsScene[OriginalBuildPaths.Length];
            for (int i = 0; i < scenes.Length; i++) scenes[i] = new UnityEditor.EditorBuildSettingsScene(OriginalBuildPaths[i], OriginalBuildEnabled[i]);
            UnityEditor.EditorBuildSettings.scenes = scenes;
        }
#endif
        var report = new Report { stage = Stage.ToString(), passed = passed, unityVersion = Application.unityVersion,
            utc = DateTime.UtcNow.ToString("o"), checks = checks, warnings = warnings, failure = failure };
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/YouYouFull"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Stage.ToString().ToLowerInvariant() + "-checks.json"), JsonUtility.ToJson(report, true));
        if (passed) Debug.Log("YOUYOU_FULL_VALIDATION_PASSED: " + checks.Count);
        else Debug.LogError("YOUYOU_FULL_VALIDATION_FAILED: " + failure);
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
    [Serializable] private sealed class Report
    { public string stage, unityVersion, utc, failure; public bool passed; public List<string> checks, warnings; }

    private void DisposeRunning()
    {
        if (running == null) return;
        while (running.Count > 0)
        {
            try { (running.Pop() as IDisposable)?.Dispose(); }
            catch (Exception exception) { failure = failure ?? exception.ToString(); }
        }
    }
}

public sealed class YouYouValidationPooledObject { public int Value; }
public sealed class YouYouValidationFsmOwner { public int Entered, Left, Updated, Destroyed; }
public sealed class YouYouValidationFsmState : FsmState<YouYouValidationFsmOwner>
{
    private readonly YouYouValidationFsmOwner owner;
    public YouYouValidationFsmState(YouYouValidationFsmOwner owner) { this.owner = owner; }
    public override void OnEnter() { owner.Entered++; }
    public override void OnLeave() { owner.Left++; }
    public override void OnUpdate() { owner.Updated++; }
    public override void OnDestroy() { owner.Destroyed++; }
}

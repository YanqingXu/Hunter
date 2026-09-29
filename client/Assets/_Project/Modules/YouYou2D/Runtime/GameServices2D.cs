using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BigWorld.Pooling;
using BigWorld.Pooling.Unity;
using FlatBuffers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YouYou;
using YouYou.DataTable;

namespace BigWorld.YouYou2D
{
    /// <summary>Offline 2D application host for the complete original YouYou GameEntry.</summary>
    [DefaultExecutionOrder(-2000), DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/2D Framework/Game Services")]
    public sealed class GameServices2D : MonoBehaviour
    {
        public GameAssetCatalog Assets;
        public static GameServices2D Instance { get; private set; }
        public GameEntry Framework { get; private set; }
        public PoolDriver Pools { get; private set; }
        public bool IsReady { get; private set; }
        public string InitializationError { get; private set; }
        public bool IsPaused { get; private set; }
        private bool ownsFramework, ownsPools;
        private float previousTimeScale = 1;
        private Task shutdown;
        private bool quitHandlerRegistered, quitRequested, quitApproved;
        private Stack<IEnumerator> startup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            if (transform.parent) throw new InvalidOperationException("GameServices2D 必须位于场景根节点。");
            Instance = this;
            DontDestroyOnLoad(gameObject);
            try
            {
                if (!Assets || !Assets.FrameworkSettings)
                    throw new InvalidOperationException("请执行 Tools/BigWorld/2D 框架/生成原框架运行内容，绑定原 ParamsSettings 和资源索引。");
                Assets.ValidateEntries();
                if (GameEntry.Instance)
                    throw new InvalidOperationException("请只保留 GameServices2D 入口，它会创建完整原版 GameEntry。");
                CreateOriginalEntry();
                foreach (var candidate in FindObjectsOfType<PoolDriver>())
                    if (candidate.isActiveAndEnabled && candidate.Service != null && candidate.Service.State == PoolState.Running)
                    {
                        if (Pools) throw new InvalidOperationException("当前场景有多个活动 PoolDriver，请保留一个共享对象池驱动。");
                        Pools = candidate;
                    }
                if (!Pools) { Pools = new GameObject("BigWorld Shared Pool Driver").AddComponent<PoolDriver>(); ownsPools = true; }
                if (!Application.isEditor)
                {
                    Application.wantsToQuit += WantsToQuit;
                    quitHandlerRegistered = true;
                }
            }
            catch (Exception exception) { FailInitialization(exception); }
        }

        private void CreateOriginalEntry()
        {
            var root = new GameObject("YouYou Framework / Original GameEntry");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            Framework = root.AddComponent<GameEntry>();
            ownsFramework = true;
            Framework.Configure(Assets.FrameworkSettings, ParamsSettings.DeviceGrade.High, YouYouLanguage.Chinese, false);
            Framework.PoolParent = Child("Original GameObject Pools", root.transform, false);
            Framework.GameObjectPoolGroups = new[] { new GameObjectPoolEntity { PoolId = 1, PoolName = "Role" } };
            Framework.LockedAssetBundle = new[] { ConstDefine.DataTableAssetBundlePath, ConstDefine.AudioAssetBundlePath };
            Framework.UICamera = Camera.main;
            var ui = Child("Original UI Root", root.transform, true).gameObject;
            Framework.UIRootCanvas = ui.AddComponent<Canvas>();
            Framework.UIRootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Framework.UIRootRectTransform = (RectTransform)ui.transform;
            Framework.UIRootCanvasScaler = ui.AddComponent<CanvasScaler>();
            Framework.UIRootCanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Framework.UIRootCanvasScaler.referenceResolution = new Vector2(1280, 720);
            ui.AddComponent<GraphicRaycaster>();
            Framework.UIGroups = new UIGroup[4];
            ushort[] sorting = { 100, 1000, 6000, 0 };
            for (byte i = 0; i < 4; i++) Framework.UIGroups[i] = new UIGroup
            { Id = (byte)(i + 1), BaseOrder = sorting[i], Group = Child("UI Group " + (i + 1), ui.transform, true) };
            if (!EventSystem.current)
            {
                var events = new GameObject("YouYou Event System", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(root.transform, false);
            }
            root.SetActive(true);
            if (!Framework.IsInitialized) throw new InvalidOperationException("原 GameEntry 初始化失败。");
        }

        private static Transform Child(string name, Transform parent, bool rect)
        {
            var child = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            child.transform.SetParent(parent, false);
            if (child.transform is RectTransform rt)
            { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
            return child.transform;
        }

        private IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(InitializationError)) yield break;
            startup = new Stack<IEnumerator>();
            startup.Push(InitializeContent());
            while (startup.Count > 0)
            {
                bool moved = false;
                object next = null;
                Exception error = null;
                try { moved = startup.Peek().MoveNext(); if (moved) next = startup.Peek().Current; }
                catch (Exception exception) { error = exception; }
                if (error != null) { DisposeStartup(); FailInitialization(error); yield break; }
                if (!moved) { (startup.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) { startup.Push(nested); continue; }
                yield return next;
            }
            startup = null;
            IsReady = true;
            Debug.Log("BIGWORLD_ORIGINAL_YOUYOU_READY: 18 managers, C# bundles/tables/FMOD");
        }

        private IEnumerator InitializeContent()
        {
            byte[] version = null, index = null;
            bool versionRead = false, indexRead = false;
            var streaming = GameEntry.Resource.ResourceManager.StreamingAssetsManager;
            streaming.ContentRoot = BigWorld.HotUpdate.HotUpdateRuntime.ContentRoot;
            streaming.ReadAssetBundle(ConstDefine.VersionFileName, bytes => { version = bytes; versionRead = true; });
            streaming.ReadAssetBundle(ConstDefine.AssetInfoName, bytes => { index = bytes; indexRead = true; });
            yield return Until(() => versionRead && indexRead, "原资源版本与索引");
            if (version == null || index == null)
                throw new InvalidOperationException("缺少 StreamingAssets 的 VersionFile/AssetInfo，请先生成原框架运行内容。");
            GameEntry.Resource.ResourceManager.InitializeLocalManifest(version, true);
            GameEntry.Resource.ResourceLoaderManager.InitializeLocalAssetInfo(index);
            GameEntry.DataTable.LoadDataAllTable();
            yield return Until(() => GameEntry.DataTable.AlreadyLoadTable.Count >= 17, "原始配置表");

            // Add project form rows with the original generated FlatBuffers schema; preserve every course row.
            TextAsset forms = null;
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.DataTable, GameAssetCatalog.NativeUIFormsPath,
                resource => forms = resource.Target as TextAsset);
            yield return Until(() => forms, "2D UI 配置表");
            GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_UIFormName, forms.bytes,
                bytes => DTSys_UIFormListExt.Init(DTSys_UIFormList.GetRootAsDTSys_UIFormList(new ByteBuffer(bytes))));
            yield return Until(() => GameEntry.DataTable.Sys_UIFormList.GetEntity(9001).HasValue &&
                GameEntry.DataTable.Sys_UIFormList.GetEntity(9002).HasValue, "2D UI 表提交");

            bool audioReady = false;
            GameEntry.Audio.LoadBanks(() => audioReady = true);
            yield return Until(() => audioReady, "原 FMOD Banks");
            if (Camera.main && !Camera.main.GetComponent<FMODUnity.StudioListener>())
                Camera.main.gameObject.AddComponent<FMODUnity.StudioListener>();

        }

        private static IEnumerator Until(Func<bool> ready, string step)
        {
            float timeout = Time.realtimeSinceStartup + 60;
            while (!ready())
            {
                if (!string.IsNullOrEmpty(GameEntry.Resource?.ResourceManager.OfflineLoadError))
                    throw new InvalidOperationException(GameEntry.Resource.ResourceManager.OfflineLoadError);
                if (Time.realtimeSinceStartup > timeout) throw new TimeoutException("初始化超时：" + step);
                yield return null;
            }
        }

        private void FailInitialization(Exception exception)
        {
            InitializationError = exception.Message;
            IsReady = false;
            Debug.LogException(exception, this);
            if (ownsFramework && Framework) Framework.Shutdown();
        }

        private void DisposeStartup()
        {
            if (startup == null) return;
            while (startup.Count > 0) (startup.Pop() as IDisposable)?.Dispose();
            startup = null;
        }

        public void SetPaused(bool paused)
        {
            if (paused == IsPaused) return;
            if (paused) { previousTimeScale = Time.timeScale; Time.timeScale = 0; }
            else Time.timeScale = previousTimeScale;
            IsPaused = paused;
            Publish(GameEvents2D.PauseChanged, paused);
        }

        public static void Publish(ushort id, object payload)
        {
            if (Instance && Instance.Framework && Instance.Framework.IsInitialized)
                GameEntry.Event.CommonEvent.Dispatch(id, payload);
        }

        public Task ShutdownAsync()
        {
            if (shutdown == null || shutdown.IsFaulted || shutdown.IsCanceled)
                shutdown = ShutdownInternalAsync();
            return shutdown;
        }

        private async Task ShutdownInternalAsync()
        {
            SetPaused(false);
            StopAllCoroutines();
            DisposeStartup();
            IsReady = false;
            foreach (var world in FindObjectsOfType<WorldSession2D>()) await world.StopAsync();
            if (ownsPools && Pools) await Pools.ShutdownAsync();
            if (ownsFramework && Framework)
            {
                // Managers release callbacks before pooled resources are closed on a fresh frame.
                await Framework.ShutdownAsync();
                if (Framework) Destroy(Framework.gameObject);
            }
            if (Instance == this) Instance = null;
            if (this) Destroy(gameObject);
        }

        private bool WantsToQuit()
        {
            if (Application.isEditor || quitApproved) return true;
            if (!quitRequested) { quitRequested = true; _ = ShutdownForQuitAsync(); }
            return false;
        }

        private async Task ShutdownForQuitAsync()
        {
            try
            {
                await Task.Yield();
                try { await ShutdownAsync(); }
                catch (Exception exception) { Debug.LogException(exception); }
                await Task.Yield();
                quitApproved = true;
                UnregisterQuitHandler();
                Application.Quit();
            }
            catch (Exception exception)
            {
                quitApproved = true;
                UnregisterQuitHandler();
                Debug.LogException(exception);
            }
        }

        private void UnregisterQuitHandler()
        {
            if (!quitHandlerRegistered) return;
            Application.wantsToQuit -= WantsToQuit;
            quitHandlerRegistered = false;
        }

        private void OnDestroy()
        {
            UnregisterQuitHandler();
            DisposeStartup();
            if (Instance != this) return;
            if (IsPaused) Time.timeScale = previousTimeScale;
            Instance = null;
            if (ownsFramework && Framework) { Framework.Shutdown(); Destroy(Framework.gameObject); }
        }
    }
}


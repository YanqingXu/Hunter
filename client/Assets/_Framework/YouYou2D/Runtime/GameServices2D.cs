using System;
using System.Threading.Tasks;
using BigWorld.Pooling;
using BigWorld.Pooling.Unity;
using UnityEngine;
using YouYou.Framework;

namespace BigWorld.YouYou2D
{
    [DefaultExecutionOrder(-2000), DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/2D Framework/Game Services")]
    public sealed class GameServices2D : MonoBehaviour
    {
        public GameAssetCatalog Assets;
        public static GameServices2D Instance { get; private set; }
        public FrameworkEntry Framework { get; private set; }
        public PoolDriver Pools { get; private set; }
        public bool IsPaused { get; private set; }
        private bool ownsFramework, ownsPools;
        private float previousTimeScale = 1;
        private Task shutdown;
        private bool quitHandlerRegistered, quitRequested, quitApproved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            if (transform.parent) throw new InvalidOperationException("GameServices2D 必须位于场景根节点。");
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Framework = FrameworkEntry.Instance;
            if (Framework && Assets)
                throw new InvalidOperationException("请只保留 GameServices2D 入口；它需要在初始化框架前注入资源表。");
            if (!Framework)
            {
                var root = new GameObject("YouYou Framework (BigWorld)");
                root.SetActive(false);
                try
                {
                    Framework = root.AddComponent<FrameworkEntry>();
                    Framework.Initialize(Assets ? Assets.CreateProvider() : null);
                    ownsFramework = true;
                    root.SetActive(true);
                }
                catch { Destroy(root); Instance = null; throw; }
            }
            foreach (var candidate in FindObjectsOfType<PoolDriver>())
                if (candidate.isActiveAndEnabled && candidate.Service != null && candidate.Service.State == PoolState.Running)
                {
                    if (Pools) throw new InvalidOperationException("当前场景有多个活动 PoolDriver，请保留一个共享对象池驱动。");
                    Pools = candidate;
                }
            if (!Pools) { Pools = new GameObject("BigWorld Shared Pool Driver").AddComponent<PoolDriver>(); ownsPools = true; }
            Framework.Context.Data.Set(Pools);
            // Register only after successful bootstrap; duplicates and failed initialization never own a handler.
            if (!Application.isEditor)
            {
                Application.wantsToQuit += WantsToQuit;
                quitHandlerRegistered = true;
            }
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
            var entry = Instance ? Instance.Framework : null;
            entry?.Context?.Event.CommonEvent.Dispatch(id, payload);
        }

        /// <summary>Stop worlds before draining the driver; it must continue ticking during cleanup.</summary>
        public Task ShutdownAsync() => shutdown ?? (shutdown = ShutdownInternalAsync());

        private async Task ShutdownInternalAsync()
        {
            SetPaused(false);
            foreach (var world in FindObjectsOfType<WorldSession2D>()) await world.StopAsync();
            if (ownsPools && Pools) await Pools.ShutdownAsync();
            if (ownsFramework && Framework) { Framework.Shutdown(); Destroy(Framework.gameObject); }
            if (Instance == this) Instance = null;
            if (this) Destroy(gameObject);
        }

        private bool WantsToQuit()
        {
            if (Application.isEditor || quitApproved) return true;
            if (!quitRequested)
            {
                quitRequested = true;
                _ = ShutdownForQuitAsync();
            }
            return false;
        }

        private async Task ShutdownForQuitAsync()
        {
            try
            {
                // Let the initial wantsToQuit callback return false before requesting another quit.
                await Task.Yield();
                try { await ShutdownAsync(); }
                catch (Exception exception) { Debug.LogException(exception); }
                // Destroy scheduled by shutdown may already remove this component. The task's managed
                // state remains valid: do not guard this continuation with Unity's destroyed-object check.
                await Task.Yield();
                quitApproved = true;
                UnregisterQuitHandler();
                Application.Quit();
            }
            catch (Exception exception)
            {
                // Keep fire-and-forget exceptions observed and do not trap subsequent OS quit requests.
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
            if (Instance != this) return;
            if (IsPaused) Time.timeScale = previousTimeScale;
            Instance = null;
            if (ownsFramework && Framework) Destroy(Framework.gameObject);
        }
    }
}

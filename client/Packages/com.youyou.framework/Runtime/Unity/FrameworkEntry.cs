using System;
using UnityEngine;
using UnityEngine.UI;

namespace YouYou.Framework
{
    /// <summary>Add to a root object. Custom asset/audio adapters can be supplied before first Initialize.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class FrameworkEntry : MonoBehaviour
    {
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private bool useUnscaledTime;
        [SerializeField] private RectTransform uiRoot;
        private bool shuttingDown;
        public static FrameworkEntry Instance { get; private set; }
        public ClientContext Context { get; private set; }
        public IAssetProvider Resource { get; private set; }
        public UIManager UI { get; private set; }
        public IAudioService Audio { get; private set; }
        public HttpService Http { get; private set; }
        public SceneService Scene { get; private set; }

        private void Awake() { Initialize(); }

        public void Initialize(IAssetProvider assets = null, IAudioService audio = null)
        {
            if (shuttingDown) throw new InvalidOperationException("Framework is shutting down.");
            if (Context != null)
            {
                if (assets != null || audio != null) throw new InvalidOperationException("Configure adapters before initialization (use an inactive root).");
                return;
            }
            if (Instance && Instance != this)
            {
                enabled = false;
                if (Application.isPlaying) Destroy(gameObject);
                return;
            }
            if (persistAcrossScenes && Application.isPlaying && transform.parent)
                throw new InvalidOperationException("Persistent FrameworkEntry must be on a root GameObject.");
            if (persistAcrossScenes && uiRoot && !uiRoot.IsChildOf(transform))
                throw new InvalidOperationException("Persistent UI root must be a child of FrameworkEntry.");

            Instance = this;
            Context = new ClientContext();
            try
            {
                Resource = assets ?? new ResourcesAssetProvider();
                if (!uiRoot)
                {
                    var canvasObject = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                    uiRoot = canvasObject.GetComponent<RectTransform>();
                    uiRoot.SetParent(transform, false);
                    canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    var scaler = canvasObject.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1280, 720);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                UI = new UIManager(Resource, uiRoot);
                Audio = audio ?? new AudioService(transform);
                Http = new HttpService();
                Scene = new SceneService();
                if (persistAcrossScenes && Application.isPlaying) DontDestroyOnLoad(gameObject);
            }
            catch { Shutdown(); throw; }
        }

        private void Update()
        {
            Context?.Tick(useUnscaledTime ? UnityEngine.Time.unscaledDeltaTime : UnityEngine.Time.deltaTime);
        }

        private void OnDestroy() { Shutdown(); }

        public void Shutdown()
        {
            if (shuttingDown || Context == null) return;
            shuttingDown = true;
            // Close UI before tearing down the services used by OnClose callbacks.
            foreach (var service in new IDisposable[] { Http, UI, Audio, Context, Resource as IDisposable })
                try { service?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            StopAllCoroutines();
            Context = null; UI = null; Audio = null; Http = null; Resource = null; Scene = null;
            if (Instance == this) Instance = null;
        }
    }
}

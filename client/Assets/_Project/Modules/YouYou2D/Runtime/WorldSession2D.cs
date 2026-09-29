using System.Collections;
using System.Threading.Tasks;
using BigWorld.Map2D;
using UnityEngine;
using YouYou;

namespace BigWorld.YouYou2D
{
    [DefaultExecutionOrder(-1500), DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/2D Framework/World Session")]
    public sealed class WorldSession2D : MonoBehaviour
    {
        public GridMapRenderer Map;
        public Transform Player;
        public string HudResourceKey;
        [Tooltip("原 Sys_UIForm 表编号；0 时由 ui.game / ui.demo 旧资源键兼容解析。")]
        public int HudFormId;
        public bool IsReady { get; private set; }
        private Rigidbody2D body;
        private bool wasSimulated;
        private GameServices2D services;
        private UIFormBase hud;
        private YouYouUIManager hudOwner;
        private GameEntry hudEntry;
        private int lifecycleVersion;
        private PendingHud pendingHud;

        private sealed class PendingHud
        {
            public GameEntry Entry;
            public YouYouUIManager UI;
            public int Version;
        }

        private void Awake()
        {
            if (Player) { body = Player.GetComponent<Rigidbody2D>(); if (body) wasSimulated = body.simulated; }
            if (Map && Player) Map.LoadingTarget = Player;
        }

        private void OnEnable()
        {
            IsReady = false;
            if (body) body.simulated = false;
            StartCoroutine(Prepare(++lifecycleVersion));
        }

        private IEnumerator Prepare(int version)
        {
            services = GameServices2D.Instance;
            if (!Map || !Map.Map || !Player || !services)
            { Debug.LogError("WorldSession2D 需要 GameServices2D、地图和玩家引用。", this); yield break; }
            float deadline = Time.realtimeSinceStartup + 60;
            while (!services.IsReady)
            {
                if (!string.IsNullOrEmpty(services.InitializationError))
                { Debug.LogError("原版框架初始化失败：" + services.InitializationError, this); yield break; }
                if (Time.realtimeSinceStartup > deadline)
                { Debug.LogError("等待原版框架初始化超时。", this); yield break; }
                yield return null;
                if (!services) yield break;
            }
            Map.LoadingTarget = Player;
            deadline = Time.realtimeSinceStartup + 10;
            while (!TerrainReady())
            {
                if (Time.realtimeSinceStartup > deadline)
                { Debug.LogError("玩家出生区域未就绪，请检查地图范围和分区加载设置。", this); yield break; }
                yield return null;
            }
            int formId = ResolveHudFormId();
            if (formId < 0) yield break;
            if (formId > 0)
            {
                if (!GameEntry.DataTable.Sys_UIFormList.GetEntity(formId).HasValue)
                { Debug.LogError("原 Sys_UIForm 表缺少 HUD 记录：" + formId, this); yield break; }
                // A disabled session may still have an original asynchronous UI load in flight.
                deadline = Time.realtimeSinceStartup + 30;
                while (pendingHud != null)
                {
                    if (!pendingHud.Entry || !pendingHud.Entry.IsInitialized || pendingHud.Entry != services.Framework)
                    { pendingHud = null; break; }
                    if (Time.realtimeSinceStartup > deadline)
                    { Debug.LogError("等待上一轮 HUD 加载结束超时。", this); yield break; }
                    yield return null;
                }
                var request = pendingHud = new PendingHud { Entry = services.Framework, UI = GameEntry.UI, Version = version };
                request.UI.OpenUIForm(formId, this, form =>
                {
                    // Original UIFormBase.Start invokes its callback before protected OnOpen.
                    // Finish on the following frame so close/reopen never interrupts that lifecycle.
                    if (request.Entry && request.Entry.IsInitialized && GameEntry.Instance == request.Entry)
                        GameEntry.Time.Yield(() => CompleteHud(request, form));
                    else if (ReferenceEquals(pendingHud, request)) pendingHud = null;
                });
                deadline = Time.realtimeSinceStartup + 30;
                while (!hud)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        lifecycleVersion++;
                        Debug.LogError("原版 UI 管理器加载 HUD 超时：" + formId, this);
                        yield break;
                    }
                    yield return null;
                }
            }
            if (version != lifecycleVersion || !isActiveAndEnabled) yield break;
            // Tilemap colliders and the original HUD lifecycle have both completed.
            Physics2D.SyncTransforms();
            if (body) body.simulated = wasSimulated;
            IsReady = true;
            GameServices2D.Publish(GameEvents2D.WorldReady, this);
            BigWorld.HotUpdate.HotUpdateRuntime.ConfirmReady();
        }

        private int ResolveHudFormId()
        {
            if (HudFormId > 0) return HudFormId;
            if (string.IsNullOrWhiteSpace(HudResourceKey)) return 0;
            if (HudResourceKey == "ui.game") return 9001;
            if (HudResourceKey == "ui.demo") return 9002;
            Debug.LogError("HUD 资源键没有原表编号，请设置 HudFormId：" + HudResourceKey, this);
            return -1;
        }

        private void CompleteHud(PendingHud request, UIFormBase form)
        {
            if (ReferenceEquals(pendingHud, request)) pendingHud = null;
            if (!request.Entry || !request.Entry.IsInitialized || GameEntry.Instance != request.Entry || !form) return;
            if (this && isActiveAndEnabled && lifecycleVersion == request.Version && services && services.Framework == request.Entry)
            { hud = form; hudOwner = request.UI; hudEntry = request.Entry; }
            else if (ReferenceEquals(form.UserData, this)) request.UI.CloseUIForm(form);
        }

        private bool TerrainReady()
        {
            var cell = Map.WorldToCell(Player.position);
            if (!Map.IsCellLoaded(cell.x, cell.y)) return false;
            var feet = Map.WorldToCell(Player.position + Vector3.down * .1f);
            return Map.IsCellLoaded(feet.x, feet.y);
        }

        public async Task StopAsync()
        {
            enabled = false;
            if (body) body.simulated = false;
            if (!Map) return;
            Map.enabled = false;
            var streamer = Map.GetComponent<GridMapEntityStreamer>();
            if (streamer) await streamer.CleanupTask;
        }

        private void OnDisable()
        {
            lifecycleVersion++;
            StopAllCoroutines();
            IsReady = false;
            if (body) body.simulated = false;
            if (hud && hudEntry && hudEntry.IsInitialized && GameEntry.Instance == hudEntry) hudOwner?.CloseUIForm(hud);
            hud = null;
            hudOwner = null; hudEntry = null;
        }
    }
}

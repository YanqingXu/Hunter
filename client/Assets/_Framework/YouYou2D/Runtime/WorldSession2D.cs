using System.Collections;
using System.Threading.Tasks;
using BigWorld.Map2D;
using UnityEngine;

namespace BigWorld.YouYou2D
{
    [DefaultExecutionOrder(-1500), DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/2D Framework/World Session")]
    public sealed class WorldSession2D : MonoBehaviour
    {
        public GridMapRenderer Map;
        public Transform Player;
        public string HudResourceKey;
        public bool IsReady { get; private set; }
        private Rigidbody2D body;
        private bool wasSimulated;
        private GameServices2D services;
        private const string HudId = "BigWorld.WorldHud";
        private YouYou.Framework.UIFormBase hud;

        private void Awake()
        {
            if (Player) { body = Player.GetComponent<Rigidbody2D>(); if (body) wasSimulated = body.simulated; }
            if (Map && Player) Map.LoadingTarget = Player;
        }

        private void OnEnable() { if (body) body.simulated = false; StartCoroutine(Prepare()); }

        private IEnumerator Prepare()
        {
            services = GameServices2D.Instance;
            if (!Map || !Map.Map || !Player || !services)
            { Debug.LogError("WorldSession2D 需要 GameServices2D、地图和玩家引用。", this); yield break; }
            Map.LoadingTarget = Player;
            float deadline = Time.realtimeSinceStartup + 10;
            while (!TerrainReady())
            {
                if (Time.realtimeSinceStartup > deadline)
                { Debug.LogError("玩家出生区域未就绪，请检查地图范围和分区加载设置。", this); yield break; }
                yield return null;
            }
            // Tilemap colliders are flushed by the map before its cells report loaded.
            Physics2D.SyncTransforms();
            if (body) body.simulated = wasSimulated;
            IsReady = true;
            if (!string.IsNullOrWhiteSpace(HudResourceKey))
            {
                services.Framework.UI.CloseUIForm(HudId);
                services.Framework.UI.Register(HudId, HudResourceKey);
                hud = services.Framework.UI.OpenUIForm(HudId, this);
            }
            GameServices2D.Publish(GameEvents2D.WorldReady, this);
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
            StopAllCoroutines();
            IsReady = false;
            if (body) body.simulated = false;
            if (hud && services && services.Framework) services.Framework.UI?.CloseUIForm(HudId);
            hud = null;
        }
    }
}

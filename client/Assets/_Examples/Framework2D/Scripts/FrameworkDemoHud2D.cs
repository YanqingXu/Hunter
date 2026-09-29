using BigWorld.Map2D;
using UnityEngine;
using YouYou.Framework;

namespace BigWorld.YouYou2D
{
    public sealed class FrameworkDemoHud2D : UIFormBase
    {
        private WorldSession2D world;
        private ClientContext context;
        private TimeAction timer;
        public int HitCount { get; private set; }
        public int Seconds { get; private set; }
        private string lastHit = "Shoot a red target to verify skill > map health > framework event.";

        public override void OnOpen(object userData)
        {
            world = userData as WorldSession2D;
            context = GameServices2D.Instance.Framework.Context;
            context.Event.CommonEvent.AddEventListener(GameEvents2D.ActorDamaged, OnHit);
            timer = context.Time.CreateTimeAction().Init(delayTime: 1, interval: 1, loop: -1, onUpdate: _ => Seconds++);
            timer.Run();
        }
        private void OnHit(object payload)
        {
            if (!(payload is ActorDamage2D hit)) return;
            HitCount++;
            lastHit = "Target HP: " + hit.Health.ToString("0") + "   Damage: " + hit.Damage.ToString("0");
        }
        public override void OnClose()
        {
            timer?.Stop(); timer = null;
            context?.Event.CommonEvent.RemoveEventListener(GameEvents2D.ActorDamaged, OnHit);
            context = null;
        }
        private void OnDestroy() { OnClose(); }
        private void OnGUI()
        {
            if (!world || !world.Map) return;
            var streamer = world.Map.GetComponent<GridMapEntityStreamer>();
            GUI.Box(new Rect(16, 16, 630, 145), "BigWorld 2D / YouYou Framework");
            GUI.Label(new Rect(32, 44, 600, 24), "A/D or arrows: move   Space: jump   J: shoot   K: melee   Esc: pause");
            GUI.Label(new Rect(32, 70, 600, 24), "Time: " + Seconds + "s   Hits: " + HitCount + "   Chunks: " + world.Map.LoadedChunkCount + "   Pooled actors: " + (streamer ? streamer.ActiveCount : 0));
            GUI.Label(new Rect(32, 96, 600, 24), lastHit);
            GUI.Label(new Rect(32, 122, 600, 24), GameServices2D.Instance && GameServices2D.Instance.IsPaused ? "PAUSED" : "Red targets retain damage after streaming out; defeated targets respawn after 5s.");
        }
    }
}

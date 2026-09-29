using UnityEngine;
using YouYou.Framework;

namespace YouYou.Framework.Samples
{
    /// <summary>Attach to an empty scene object and press Play. No prefabs or server required.</summary>
    public sealed class QuickStart : MonoBehaviour
    {
        private const ushort TickEvent = 1;
        private FrameworkEntry entry;
        private TimeAction timer;
        private int ticks;

        private void Start()
        {
            entry = FrameworkEntry.Instance;
            if (!entry) entry = new GameObject("YouYou Framework").AddComponent<FrameworkEntry>();
            entry.Context.Event.CommonEvent.AddEventListener(TickEvent, OnTick);
            entry.Context.Procedure.Start(new ProcedureBase[] { new StartupProcedure() });
            timer = entry.Context.Time.CreateTimeAction().Init("Example", interval: 1, loop: -1,
                onUpdate: _ => entry.Context.Event.CommonEvent.Dispatch(TickEvent));
            timer.Run();
        }
        private void OnTick(object data) { ticks++; }
        private void OnGUI()
        {
            GUI.Box(new Rect(20, 20, 380, 145), "YouYou Framework / Quick Start");
            GUI.Label(new Rect(40, 55, 340, 30), "Event + timer ticks: " + ticks);
            GUI.Label(new Rect(40, 80, 340, 30), "Core services are independent of the original game.");
            if (GUI.Button(new Rect(40, 115, 140, 30), "Dispatch event") && entry && entry.Context != null)
                entry.Context.Event.CommonEvent.Dispatch(TickEvent);
        }
        private void OnDestroy()
        {
            timer?.Stop();
            if (entry && entry.Context != null) entry.Context.Event.CommonEvent.RemoveEventListener(TickEvent, OnTick);
        }
        private sealed class StartupProcedure : ProcedureBase
        {
            public override void OnEnter() { Debug.Log("YouYou Framework started. Replace this procedure with your project flow."); }
        }
    }
}

using System;

namespace BigWorld.Gameplay
{
    /// <summary>One scene interaction attempt. The caller supplies simulation time and intent, not physical velocity.</summary>
    public sealed class HunterInteractionTimer
    {
        public bool IsRunning { get; private set; }
        public bool HasCompleted { get; private set; }
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public float Progress { get { return HasCompleted ? 1 : Duration > 0 ? Elapsed / Duration : 0; } }

        public bool TryStart(float seconds)
        {
            if (IsRunning || HasCompleted || !Finite(seconds) || seconds < 0) return false;
            Duration = seconds; Elapsed = 0; IsRunning = true; return true;
        }

        /// <summary>Returns true exactly on completion. Paused input is discarded; active input cancels before completion.</summary>
        public bool Tick(float delta, bool paused, bool interrupted)
        {
            if (!IsRunning || paused) return false;
            if (interrupted) { Cancel(); return false; }
            if (!Finite(delta) || delta < 0) return false;
            Elapsed = (float)Math.Min(Duration, (double)Elapsed + delta);
            if (Elapsed < Duration) return false;
            IsRunning = false; HasCompleted = true; return true;
        }

        public bool Cancel()
        {
            if (!IsRunning) return false;
            Reset(); return true;
        }
        /// <summary>Used for a new run, a reusable interaction, or an explicitly retried failed effect.</summary>
        public void Reset() { IsRunning = HasCompleted = false; Duration = Elapsed = 0; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

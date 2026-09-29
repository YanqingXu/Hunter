// Adapted from Assets/YouYouFramework/Managers/Time/TimeAction.cs.
using System;

namespace YouYou.Framework
{
    public sealed class TimeAction
    {
        private readonly TimeManager owner;
        private float delay, interval, remaining;
        private int loop, fired, generation;
        private bool started, paused;
        private Action onStart, onComplete;
        private Action<int> onUpdate;
        public string TimeName { get; private set; }
        public bool IsRuning { get; private set; }
        public bool IsPaused => paused;

        internal TimeAction(TimeManager owner) { this.owner = owner; }

        /// <summary>loop=0 or 1 runs once; -1 repeats forever. First callback fires after delay.</summary>
        public TimeAction Init(string timeName = null, float delayTime = 0, float interval = 1, int loop = 0,
            Action onStar = null, Action<int> onUpdate = null, Action onComplete = null)
        {
            if (IsRuning) throw new InvalidOperationException("Stop a timer before configuring it.");
            if (delayTime < 0 || float.IsNaN(delayTime) || float.IsInfinity(delayTime)) throw new ArgumentOutOfRangeException(nameof(delayTime));
            if (interval < 0 || float.IsNaN(interval) || float.IsInfinity(interval)) throw new ArgumentOutOfRangeException(nameof(interval));
            if (loop < -1) throw new ArgumentOutOfRangeException(nameof(loop));
            TimeName = timeName; delay = delayTime; this.interval = interval; this.loop = loop;
            onStart = onStar; this.onUpdate = onUpdate; this.onComplete = onComplete;
            return this;
        }

        public void Run()
        {
            if (IsRuning) return;
            generation++; fired = 0; started = false; paused = false; remaining = delay;
            IsRuning = true;
            owner.Register(this);
        }

        /// <summary>Cancellation does not invoke completion. Natural completion invokes it once.</summary>
        public void Stop() { generation++; IsRuning = false; owner.Remove(this); }
        public void Pause() { paused = true; }
        public void Resume() { paused = false; }

        internal void Tick(float deltaTime)
        {
            if (!IsRuning || paused) return;
            remaining -= deltaTime;
            if (remaining > 0) return;
            var version = generation;
            if (!started)
            {
                started = true;
                onStart?.Invoke();
                if (!IsRuning || paused || version != generation) return;
            }
            // At most once per frame, including interval=0. Missed intervals are not replayed.
            remaining = interval;
            fired++;
            try { onUpdate?.Invoke(loop < 0 ? -1 : Math.Max(0, Math.Max(1, loop) - fired)); }
            catch { Stop(); throw; }
            if (!IsRuning || version != generation) return;
            if (loop >= 0 && fired >= Math.Max(1, loop))
            {
                Stop();
                onComplete?.Invoke();
            }
        }
    }
}

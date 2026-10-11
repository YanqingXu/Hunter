using System;

namespace BigWorld.Gameplay
{
    public enum HunterBarrelHit { Bullet, Explosion }

    /// <summary>Each true result owns one detonation. Commit terminal state before propagating explosion callbacks.</summary>
    public sealed class HunterBarrelFuse
    {
        public float Delay { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsArmed { get; private set; }
        public bool HasExploded { get; private set; }
        public float Progress { get { return HasExploded ? 1 : IsArmed && Delay > 0 ? Elapsed / Delay : 0; } }

        public HunterBarrelFuse(float delay)
        {
            if (!Finite(delay) || delay < 0) throw new ArgumentOutOfRangeException("delay");
            Delay = delay;
        }

        public bool Hit(HunterBarrelHit kind, bool paused)
        {
            if (paused || HasExploded || (kind != HunterBarrelHit.Bullet && kind != HunterBarrelHit.Explosion)) return false;
            if (kind == HunterBarrelHit.Explosion) return Detonate();
            if (!IsArmed) { IsArmed = true; Elapsed = 0; }
            return Delay == 0 && Detonate();
        }

        public bool Tick(float delta, bool paused)
        {
            if (!IsArmed || HasExploded || paused || !Finite(delta) || delta < 0) return false;
            Elapsed = (float)Math.Min(Delay, (double)Elapsed + delta);
            return Elapsed >= Delay && Detonate();
        }

        private bool Detonate()
        { HasExploded = true; IsArmed = false; Elapsed = Delay; return true; }

        /// <summary>Explicit new-run reset; chunk visibility must never call this.</summary>
        public void Reset() { Elapsed = 0; IsArmed = HasExploded = false; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

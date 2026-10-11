using System;

namespace BigWorld.Gameplay
{
    /// <summary>Simulation-time stamina rules, independent of input and Unity objects.</summary>
    public sealed class HunterStamina
    {
        public float Maximum { get; private set; }
        public float Current { get; private set; }
        private float recoveryDelay, recoveryRate, remainingDelay;

        public HunterStamina(float maximum, float delay, float rate)
        {
            if (!Finite(maximum) || maximum <= 0 || !Finite(delay) || delay < 0 || !Finite(rate) || rate < 0)
                throw new ArgumentOutOfRangeException("Invalid stamina configuration.");
            Maximum = maximum; recoveryDelay = delay; recoveryRate = rate;
            Reset();
        }

        public void Reset() { Current = Maximum; remainingDelay = 0; }
        public void InterruptRecovery() { remainingDelay = recoveryDelay; }
        public bool CanSpend(float cost) { return Finite(cost) && cost >= 0 && Current >= cost; }
        public bool TrySpend(float cost)
        {
            if (!CanSpend(cost)) return false;
            Current -= cost; InterruptRecovery(); return true;
        }

        public void Tick(float delta, bool blocked)
        {
            if (!Finite(delta) || delta <= 0) return;
            if (blocked) { InterruptRecovery(); return; }
            float waiting = Math.Min(delta, remainingDelay);
            remainingDelay -= waiting;
            Current = Math.Min(Maximum, Current + (delta - waiting) * recoveryRate);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    public static class HunterHealthSegments
    {
        public static bool IsValid(float maximum, float[] segments)
        {
            if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0 || segments == null || segments.Length == 0) return false;
            float sum = 0;
            foreach (float segment in segments)
            {
                if (float.IsNaN(segment) || float.IsInfinity(segment) || segment <= 0) return false;
                sum += segment;
            }
            return Math.Abs(sum - maximum) <= .001f;
        }

        /// <summary>Segments are ordered from zero health upwards. Empty segments never regenerate.</summary>
        public static float RecoveryCeiling(float health, float maximum, float[] segments)
        {
            if (!IsValid(maximum, segments)) throw new ArgumentException("Health segments must sum to maximum health.");
            if (float.IsNaN(health) || float.IsInfinity(health)) throw new ArgumentOutOfRangeException("health");
            if (health <= 0) return 0;
            float boundary = 0;
            foreach (float segment in segments)
            {
                boundary += segment;
                if (health <= boundary) return boundary;
            }
            return maximum;
        }
    }
}

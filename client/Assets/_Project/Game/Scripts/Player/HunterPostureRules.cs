using System;

namespace BigWorld.Gameplay
{
    /// <summary>Provisional prone balance and aim limits. Collision admission belongs to the player controller.</summary>
    public static class HunterPostureRules
    {
        public const float ProneMoveMultiplier = .5f;
        public const float ProneHeightMultiplier = .5f;

        /// <summary>Keep local forward/up components nonnegative, then return a world-space unit direction.</summary>
        public static void ProjectProneAim(float horizontal, float vertical, int facing, out float x, out float y)
        {
            if (!Finite(horizontal) || !Finite(vertical)) throw new ArgumentOutOfRangeException("Prone aim must be finite.");
            int sign = facing < 0 ? -1 : 1;
            double forward = Math.Max(0, (double)horizontal * sign), up = Math.Max(0, (double)vertical);
            double length = Math.Sqrt(forward * forward + up * up);
            // Looking straight down or entirely backwards clamps to the current forward horizon.
            if (length == 0) { x = sign; y = 0; return; }
            x = (float)(sign * forward / length); y = (float)(up / length);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

using System;

namespace BigWorld.Gameplay
{
    /// <summary>Last valid two-dimensional aim input. Independent of movement, casts and Unity objects.</summary>
    public sealed class HunterAim
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public int Facing { get; private set; }
        public float DeadZone { get; private set; }

        public HunterAim(float deadZone = .15f)
        {
            if (!Finite(deadZone) || deadZone < 0 || deadZone >= 1)
                throw new ArgumentOutOfRangeException("deadZone");
            DeadZone = deadZone; Reset();
        }

        public void Reset(int facing = 1)
        {
            Facing = facing < 0 ? -1 : 1; X = Facing; Y = 0;
        }

        /// <summary>Neutral or invalid input preserves the last aim; vertical input preserves the sprite's facing.</summary>
        public bool TrySet(float horizontal, float vertical)
        {
            if (!Finite(horizontal) || !Finite(vertical)) return false;
            // Double intermediates prevent finite large inputs from overflowing during normalization.
            double magnitude = Math.Sqrt((double)horizontal * horizontal + (double)vertical * vertical);
            if (magnitude <= DeadZone || magnitude == 0) return false;
            X = (float)(horizontal / magnitude); Y = (float)(vertical / magnitude);
            if (horizontal != 0) Facing = horizontal < 0 ? -1 : 1;
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

using System;

namespace BigWorld.Gameplay
{
    /// <summary>Immutable aim/range snapshot for one throw; the arc bends upward in world space.</summary>
    public sealed class HunterThrowPath
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Range { get; private set; }
        public float ArcHeight { get; private set; }

        public HunterThrowPath(float horizontal, float vertical, float range, float arcHeight)
        {
            if (!Finite(horizontal) || !Finite(vertical)) throw new ArgumentOutOfRangeException("direction");
            double length = Math.Sqrt((double)horizontal * horizontal + (double)vertical * vertical);
            if (length == 0) throw new ArgumentException("A throw needs a nonzero aim direction.");
            if (!Finite(range) || range <= 0) throw new ArgumentOutOfRangeException("range");
            if (!Finite(arcHeight) || arcHeight < 0 || (double)range + arcHeight > float.MaxValue)
                throw new ArgumentOutOfRangeException("arcHeight");
            X = (float)(horizontal / length); Y = (float)(vertical / length);
            Range = range; ArcHeight = arcHeight;
        }

        /// <summary>Distance is progress along the chosen direction, not the curved path's total length.</summary>
        public void Sample(float distance, out float x, out float y)
        {
            if (!Finite(distance)) throw new ArgumentOutOfRangeException("distance");
            double progress = Math.Max(0, Math.Min(Range, distance));
            double lift = progress == 0 || progress == Range ? 0 : Math.Sin(progress / Range * Math.PI) * ArcHeight;
            x = (float)(X * progress); y = (float)(Y * progress + lift);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

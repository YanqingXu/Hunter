using System;

namespace BigWorld.Gameplay
{
    /// <summary>One pointer owns a stick until release/cancel; inputs are relative to its visual radius.</summary>
    public sealed class HunterPointerStick
    {
        public int? PointerId { get; private set; }
        public bool HasPointer { get { return PointerId.HasValue; } }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float DeadZone { get; private set; }

        public HunterPointerStick(float deadZone = .15f)
        {
            if (!Finite(deadZone) || deadZone < 0 || deadZone >= 1) throw new ArgumentOutOfRangeException("deadZone");
            DeadZone = deadZone;
        }

        public bool TryPress(int pointerId, float x, float y)
        {
            if (HasPointer || !SetValue(x, y)) return false;
            PointerId = pointerId; return true;
        }

        public bool TryDrag(int pointerId, float x, float y)
        { return PointerId == pointerId && SetValue(x, y); }

        public bool TryRelease(int pointerId)
        {
            if (PointerId != pointerId) return false;
            Cancel(); return true;
        }

        public void Cancel() { PointerId = null; X = 0; Y = 0; }

        private bool SetValue(float x, float y)
        {
            if (!Finite(x) || !Finite(y)) return false;
            double length = Math.Sqrt((double)x * x + (double)y * y);
            if (length <= DeadZone) { X = 0; Y = 0; return true; }
            double divisor = Math.Max(1, length);
            X = (float)(x / divisor); Y = (float)(y / divisor);
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

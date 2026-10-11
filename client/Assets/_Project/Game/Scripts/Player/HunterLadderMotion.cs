using System;

namespace BigWorld.Gameplay
{
    public enum HunterLadderBoundary { None, Bottom, Top }

    /// <summary>Plans vertical motion from the actual body position. Physics and safe exits remain the caller's responsibility.</summary>
    public sealed class HunterLadderMotion
    {
        public float Bottom { get; private set; }
        public float Top { get; private set; }
        public float Speed { get; private set; }

        public HunterLadderMotion(float bottom, float top, float speed)
        {
            if (!Finite(bottom) || !Finite(top) || top <= bottom) throw new ArgumentOutOfRangeException("top");
            if (!Finite(speed) || speed <= 0) throw new ArgumentOutOfRangeException("speed");
            Bottom = bottom; Top = top; Speed = speed;
        }

        /// <summary>Input adapters apply their dead zone before this call. Reaching an end requests an exit; it does not detach.</summary>
        public bool TryStep(float position, float vertical, float delta, bool paused, out float target, out HunterLadderBoundary boundary)
        {
            target = position; boundary = HunterLadderBoundary.None;
            if (!Finite(position) || position < Bottom || position > Top || !Finite(vertical) || !Finite(delta) || delta < 0) return false;
            if (paused || delta == 0 || vertical == 0) return true;
            double value = (double)position + Math.Max(-1, Math.Min(1, (double)vertical)) * Speed * delta;
            target = (float)Math.Max(Bottom, Math.Min(Top, value));
            if (value <= Bottom) boundary = HunterLadderBoundary.Bottom;
            else if (value >= Top) boundary = HunterLadderBoundary.Top;
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

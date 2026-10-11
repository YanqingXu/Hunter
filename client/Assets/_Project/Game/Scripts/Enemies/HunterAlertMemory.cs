using System;

namespace BigWorld.Gameplay
{
    /// <summary>Retains alert state briefly after lost sight. Retained alert never grants permission to attack an unseen target.</summary>
    public sealed class HunterAlertMemory
    {
        public float ForgetDelay { get; private set; }
        public bool IsAlerted { get; private set; }
        public float UnseenSeconds { get; private set; }
        public HunterAlertMemory(float forgetDelay)
        {
            if (float.IsNaN(forgetDelay) || float.IsInfinity(forgetDelay) || forgetDelay < 0)
                throw new ArgumentOutOfRangeException("forgetDelay");
            ForgetDelay = forgetDelay;
        }
        public bool Tick(bool visible, float delta, bool paused)
        {
            if (paused || float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) return IsAlerted;
            if (visible) { IsAlerted = true; UnseenSeconds = 0; }
            else if (IsAlerted)
            {
                UnseenSeconds = Math.Min(ForgetDelay, UnseenSeconds + delta);
                if (UnseenSeconds >= ForgetDelay) Forget();
            }
            return IsAlerted;
        }
        /// <summary>Explicit suppression, death and pool reuse discard retained alert immediately.</summary>
        public void Forget() { IsAlerted = false; UnseenSeconds = 0; }
    }
}

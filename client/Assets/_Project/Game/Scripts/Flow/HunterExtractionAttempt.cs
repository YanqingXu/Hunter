using System;

namespace BigWorld.Gameplay
{
    /// <summary>A continuous exit countdown independent of the boss objective. Only the final carried bounty earns currency.</summary>
    public sealed class HunterExtractionAttempt
    {
        private double elapsed;
        public float Duration { get; private set; }
        public int BountyValue { get; private set; }
        public float Elapsed { get { return (float)elapsed; } }
        public float Remaining { get { return (float)Math.Max(0, Duration - elapsed); } }
        public bool Completed { get; private set; }
        public bool CarriedBountyAtExit { get; private set; }
        public int Reward { get { return Completed && CarriedBountyAtExit ? BountyValue : 0; } }

        public HunterExtractionAttempt(float duration, int bountyValue)
        {
            if (!Finite(duration) || duration <= 0 || bountyValue < 0) throw new ArgumentException("Invalid exit countdown configuration.");
            Duration = duration; BountyValue = bountyValue;
        }
        // Call only while the deployed hunter is alive and gameplay is active. Zero simulation time freezes the attempt.
        // True is emitted once, so the host can capture settlement and retry persistence without another reward.
        public bool Tick(float delta, bool inExit, bool contested, bool carryingBounty)
        {
            if (Completed || !Finite(delta) || delta <= 0) return false;
            if (!inExit || contested) { Interrupt(); return false; }
            elapsed = Math.Min(Duration, elapsed + delta);
            if (elapsed < Duration) return false;
            Completed = true; CarriedBountyAtExit = carryingBounty; return true;
        }
        public void Interrupt() { if (!Completed) elapsed = 0; }
        public void Reset() { elapsed = 0; Completed = CarriedBountyAtExit = false; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

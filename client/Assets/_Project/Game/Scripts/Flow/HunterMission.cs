using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    public enum HuntStage { Tracking, Hunting, BountyAvailable, Carrying, Extracted }

    /// <summary>Run-owned objective state. Time advances only when the host supplies active gameplay time.</summary>
    public sealed class HunterMission
    {
        public int RequiredClues { get; private set; }
        public float ExtractionSeconds { get; private set; }
        public int BountyValue { get; private set; }
        public int ClueCount { get { return clues.Count; } }
        public HuntStage Stage { get; private set; }
        public bool SecondWaveStarted { get; private set; }
        public bool BossDefeated { get; private set; }
        public bool CarriedBountyAtExit { get { return extraction.CarriedBountyAtExit; } }
        public float ExtractionElapsed { get { return extraction.Elapsed; } }
        public int Reward { get { return extraction.Reward; } }
        private readonly HashSet<string> clues = new HashSet<string>();
        private readonly HunterExtractionAttempt extraction;

        public HunterMission(int requiredClues, float extractionSeconds, int bountyValue)
        {
            if (requiredClues < 1 || !Finite(extractionSeconds) || extractionSeconds <= 0 || bountyValue < 0)
                throw new ArgumentException("Invalid mission configuration.");
            RequiredClues = requiredClues; ExtractionSeconds = extractionSeconds; BountyValue = bountyValue;
            extraction = new HunterExtractionAttempt(extractionSeconds, bountyValue);
            Reset();
        }
        public void Reset()
        { clues.Clear(); Stage = HuntStage.Tracking; BossDefeated = SecondWaveStarted = false; extraction.Reset(); }
        public bool HasClue(string id) { return id != null && clues.Contains(id); }
        public bool Investigate(string id)
        {
            if (Stage != HuntStage.Tracking || string.IsNullOrWhiteSpace(id) || !clues.Add(id)) return false;
            if (ClueCount >= RequiredClues) Stage = HuntStage.Hunting;
            return true;
        }
        public bool DefeatBoss()
        {
            // Finding the boss before completing all clues is allowed.
            if (Stage != HuntStage.Tracking && Stage != HuntStage.Hunting) return false;
            BossDefeated = true; Stage = HuntStage.BountyAvailable; return true;
        }
        public bool PickUpBounty()
        {
            if (Stage != HuntStage.BountyAvailable) return false;
            Stage = HuntStage.Carrying; SecondWaveStarted = true; return true;
        }
        public bool DropBounty()
        {
            InterruptExtraction();
            if (Stage != HuntStage.Carrying) return false;
            Stage = HuntStage.BountyAvailable; return true;
        }
        public void InterruptExtraction() { extraction.Interrupt(); }
        public bool TickExtraction(float delta, bool inExit, bool contested)
        {
            if (!extraction.Tick(delta, inExit, contested, Stage == HuntStage.Carrying)) return false;
            Stage = HuntStage.Extracted; return true;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

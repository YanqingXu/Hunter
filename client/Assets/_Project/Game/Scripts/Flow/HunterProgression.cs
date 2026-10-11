using System;

namespace BigWorld.Gameplay
{
    /// <summary>Prototype progression balance; experience is banked only after a successful extraction.</summary>
    public static class HunterProgression
    {
        public const int MaximumLevel = 10;
        public const int ExperiencePerLevel = 100;
        public const int MaximumExperience = (MaximumLevel - 1) * ExperiencePerLevel;
        public const int ClueExperience = 25;
        public const int GuardExperience = 20;
        public const int BossExperience = 100;
        public const int ExtractionExperience = 100;
        public const int RetirementExperience = 500;

        public static int Level(int experience) { return 1 + experience / ExperiencePerLevel; }
        public static int ExtractionReward(int clues, int guards, bool bossDefeated)
        {
            if (clues < 0 || guards < 0) throw new ArgumentException("Invalid experience counters.");
            return checked(clues * ClueExperience + guards * GuardExperience +
                (bossDefeated ? BossExperience : 0) + ExtractionExperience);
        }
    }
}

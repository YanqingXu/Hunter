using System;

namespace BigWorld.Gameplay
{
    /// <summary>Provisional account curve. Derived from durable account experience; never changes hunter stats or grants unlocks.</summary>
    public static class HunterAccountProgression
    {
        public const int MaximumLevel = 100;
        public const int ExperiencePerLevel = 1000;
        public const int MaximumLevelExperience = (MaximumLevel - 1) * ExperiencePerLevel;

        public static int Level(int experience)
        {
            Validate(experience);
            return 1 + Math.Min(MaximumLevel - 1, experience / ExperiencePerLevel);
        }
        public static int LevelExperience(int experience)
        { return Level(experience) == MaximumLevel ? 0 : experience % ExperiencePerLevel; }
        public static int ExperienceToNextLevel(int experience)
        { return Level(experience) == MaximumLevel ? 0 : ExperiencePerLevel - LevelExperience(experience); }
        private static void Validate(int experience)
        { if (experience < 0) throw new ArgumentOutOfRangeException("experience"); }
    }
}

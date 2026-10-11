using System;
using BigWorld.Gameplay;

public static class HunterAccountProgressionChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(HunterAccountProgression.Level(0) == 1 && HunterAccountProgression.LevelExperience(0) == 0 &&
            HunterAccountProgression.ExperienceToNextLevel(0) == 1000, "An account starts at level one with a full first threshold remaining");
        check(HunterAccountProgression.Level(500) == 1 && HunterAccountProgression.LevelExperience(500) == 500 &&
            HunterAccountProgression.ExperienceToNextLevel(500) == 500, "One existing retirement reward fills half of the provisional account level");
        check(HunterAccountProgression.Level(999) == 1 && HunterAccountProgression.LevelExperience(999) == 999 &&
            HunterAccountProgression.ExperienceToNextLevel(999) == 1, "Being one point below the first threshold grants no early level");
        check(HunterAccountProgression.Level(1000) == 2 && HunterAccountProgression.LevelExperience(1000) == 0 &&
            HunterAccountProgression.ExperienceToNextLevel(1000) == 1000, "An exact threshold grants the next account level and starts its full progress interval");
        check(HunterAccountProgression.Level(15700) == 16 && HunterAccountProgression.LevelExperience(15700) == 700 &&
            HunterAccountProgression.ExperienceToNextLevel(15700) == 300, "Existing accumulated experience derives the correct level and partial interval immediately");
        bool boundaries = true;
        for (int level = 2; level <= HunterAccountProgression.MaximumLevel; level++)
        {
            int threshold = (level - 1) * HunterAccountProgression.ExperiencePerLevel;
            boundaries &= HunterAccountProgression.Level(threshold - 1) == level - 1 && HunterAccountProgression.Level(threshold) == level &&
                HunterAccountProgression.Level(threshold + 1) == level;
        }
        check(boundaries, "Every account-level boundary is exact and monotonic immediately before and after crossing");
        check(HunterAccountProgression.Level(98999) == 99 && HunterAccountProgression.LevelExperience(98999) == 999 &&
            HunterAccountProgression.ExperienceToNextLevel(98999) == 1, "The last uncapped level keeps its final experience point");
        check(HunterAccountProgression.Level(99000) == 100 && HunterAccountProgression.LevelExperience(99000) == 0 &&
            HunterAccountProgression.ExperienceToNextLevel(99000) == 0, "The account level cap has no fictitious next-level progress");
        check(HunterAccountProgression.Level(99500) == 100 && HunterAccountProgression.LevelExperience(99500) == 0 &&
            HunterAccountProgression.ExperienceToNextLevel(99500) == 0, "Further retirement experience does not make a capped account exceed its level cap");
        check(HunterAccountProgression.Level(int.MaxValue) == 100 && HunterAccountProgression.ExperienceToNextLevel(int.MaxValue) == 0,
            "Very large cumulative totals cannot overflow account-level arithmetic");
        check(Throws(() => HunterAccountProgression.Level(-1)) && Throws(() => HunterAccountProgression.LevelExperience(-1)) &&
            Throws(() => HunterAccountProgression.ExperienceToNextLevel(-1)), "Negative cumulative experience is rejected by every account-growth query");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

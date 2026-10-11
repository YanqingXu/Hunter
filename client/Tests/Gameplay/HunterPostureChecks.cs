using System;
using BigWorld.Gameplay;

public static class HunterPostureChecks
{
    public static void Run(Action<bool, string> check)
    {
        AssertDirection(check, .6f, .8f, 1, .6f, .8f, "Prone right-up aim preserves its legal angle");
        AssertDirection(check, -.6f, .8f, -1, -.6f, .8f, "Turning left mirrors the legal forward-up quadrant");
        AssertDirection(check, 1, -1, 1, 1, 0, "Right-facing prone aim cannot point below the horizon");
        AssertDirection(check, -1, -1, -1, -1, 0, "Left-facing prone aim cannot point below the horizon");
        AssertDirection(check, -1, 1, 1, 0, 1, "A backwards horizontal component is removed relative to the selected facing");
        AssertDirection(check, -1, 0, 1, 1, 0, "Entirely backwards input falls back to the forward horizon");
        AssertDirection(check, 0, 1, -1, 0, 1, "Straight-up aim is legal for either facing");
        AssertDirection(check, 0, -1, -1, -1, 0, "Straight-down input keeps the existing left-facing horizon");
        AssertDirection(check, 0, 0, 1, 1, 0, "A zero vector never produces invalid normalized aim");
        AssertDirection(check, float.MaxValue, float.MaxValue, 1, .7071068f, .7071068f, "Extreme finite components normalize without overflow");
        AssertDirection(check, float.Epsilon, 0, 1, 1, 0, "A small positive forward component remains finite");
        AssertDirection(check, 1, -1, 0, 1, 0, "A neutral facing parameter follows the existing right-facing convention");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            float x, y;
            check(Throws(() => HunterPostureRules.ProjectProneAim(invalid, 1, 1, out x, out y)) &&
                Throws(() => HunterPostureRules.ProjectProneAim(1, invalid, 1, out x, out y)),
                "Invalid prone aim is rejected instead of corrupting a later shot: " + invalid);
        }
    }
    private static void AssertDirection(Action<bool, string> check, float inputX, float inputY, int facing, float expectedX, float expectedY, string name)
    {
        float x, y; HunterPostureRules.ProjectProneAim(inputX, inputY, facing, out x, out y);
        check(Math.Abs(x - expectedX) < .00001f && Math.Abs(y - expectedY) < .00001f &&
            Math.Abs(x * x + y * y - 1) < .00001f && x * (facing < 0 ? -1 : 1) >= 0 && y >= 0, name);
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

using System;
using BigWorld.Gameplay;

public static class HunterLadderChecks
{
    public static void Run(Action<bool, string> check)
    {
        var ladder = new HunterLadderMotion(2, 4, 2.5f);
        float target; HunterLadderBoundary boundary;
        check(ladder.TryStep(2, 1, .2f, false, out target, out boundary) && target == 2.5f && boundary == HunterLadderBoundary.None,
            "Climbing uses configured speed and simulation time from the body's current height");
        check(ladder.TryStep(3, -.5f, .4f, false, out target, out boundary) && target == 2.5f,
            "Analog downward input preserves its magnitude and direction");
        check(ladder.TryStep(3, float.MaxValue, .2f, false, out target, out boundary) && target == 3.5f,
            "Out-of-range vertical input cannot exceed configured climbing speed");
        check(ladder.TryStep(3, -float.MaxValue, .2f, false, out target, out boundary) && target == 2.5f,
            "Negative vertical input is also clamped before multiplying frame time");
        check(ladder.TryStep(3, 1, 100, true, out target, out boundary) && target == 3 && boundary == HunterLadderBoundary.None,
            "Paused movement cannot advance to a ladder exit");
        check(ladder.TryStep(4, 0, 1, false, out target, out boundary) && target == 4 && boundary == HunterLadderBoundary.None,
            "Neutral input at the top does not automatically detach the player");
        check(ladder.TryStep(2, -1, 0, false, out target, out boundary) && target == 2 && boundary == HunterLadderBoundary.None,
            "A zero-time frame does not request an exit");
        check(ladder.TryStep(3, 1, .4f, false, out target, out boundary) && target == 4 && boundary == HunterLadderBoundary.Top,
            "Reaching the exact top clamps the plan and requests a checked upper exit");
        check(ladder.TryStep(3, -1, .4f, false, out target, out boundary) && target == 2 && boundary == HunterLadderBoundary.Bottom,
            "Reaching the exact bottom requests a checked lower exit");
        check(ladder.TryStep(3, 1, float.MaxValue, false, out target, out boundary) && target == 4 && boundary == HunterLadderBoundary.Top,
            "An oversized finite frame cannot move above the ladder");
        check(ladder.TryStep(3, -1, float.MaxValue, false, out target, out boundary) && target == 2 && boundary == HunterLadderBoundary.Bottom,
            "An oversized finite frame cannot move below the ladder");
        check(ladder.TryStep(4, -1, .2f, false, out target, out boundary) && target == 3.5f && boundary == HunterLadderBoundary.None,
            "A player whose upper exit is blocked can climb back down");
        check(ladder.TryStep(2, 1, .2f, false, out target, out boundary) && target == 2.5f && boundary == HunterLadderBoundary.None,
            "A player at the lower endpoint can climb back up");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!ladder.TryStep(3, 1, invalid, false, out target, out boundary) && target == 3 && boundary == HunterLadderBoundary.None,
                "Invalid frame time leaves the body's requested position unchanged: " + invalid);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!ladder.TryStep(3, invalid, .1f, false, out target, out boundary) && target == 3,
                "Invalid directional input cannot poison the motion plan: " + invalid);
        check(!ladder.TryStep(1, 1, .1f, false, out target, out boundary) && target == 1 &&
            !ladder.TryStep(5, -1, .1f, false, out target, out boundary) && target == 5,
            "A body outside the ladder is rejected rather than teleported onto it");
        check(!ladder.TryStep(float.NaN, 1, .1f, false, out target, out boundary) && float.IsNaN(target),
            "An invalid body position is rejected before any physics command is planned");
        float whole, half;
        ladder.TryStep(2.5f, .5f, .4f, false, out whole, out boundary);
        ladder.TryStep(2.5f, .5f, .2f, false, out half, out boundary);
        ladder.TryStep(half, .5f, .2f, false, out target, out boundary);
        check(Math.Abs(whole - target) < .00001f, "Splitting a frame preserves climbing distance away from endpoints");
        var huge = new HunterLadderMotion(-float.MaxValue, float.MaxValue, float.MaxValue);
        check(huge.TryStep(0, 1, float.MaxValue, false, out target, out boundary) && target == float.MaxValue && boundary == HunterLadderBoundary.Top,
            "Extreme finite configuration is calculated without overflowing the output");
        float[,] invalidSettings = { { 4, 2, 1 }, { 2, 2, 1 }, { float.NaN, 4, 1 }, { 2, float.PositiveInfinity, 1 },
            { 2, 4, 0 }, { 2, 4, -1 }, { 2, 4, float.NaN }, { 2, 4, float.PositiveInfinity } };
        for (int i = 0; i < invalidSettings.GetLength(0); i++)
        {
            bool rejected = false;
            try { new HunterLadderMotion(invalidSettings[i, 0], invalidSettings[i, 1], invalidSettings[i, 2]); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Invalid ladder geometry or speed is rejected at configuration: " + i);
        }
    }
}

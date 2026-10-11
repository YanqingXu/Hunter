using System;
using BigWorld.Gameplay;

public static class HunterExtractionAttemptChecks
{
    public static void Run(Action<bool, string> check)
    {
        var exit = new HunterExtractionAttempt(10, 250);
        check(exit.Elapsed == 0 && exit.Remaining == 10 && exit.Reward == 0 && !exit.Completed,
            "A new exit attempt has no elapsed time, completion or unearned reward");
        check(!exit.Tick(9, true, false, false) && exit.Elapsed == 9 && exit.Remaining == 1,
            "A hunter without a bounty can begin and sustain the full exit countdown");
        check(exit.Tick(1, true, false, false) && exit.Completed && exit.Remaining == 0 && exit.Reward == 0 && !exit.CarriedBountyAtExit,
            "A no-bounty exit completes at the exact deadline without inventing bounty currency");
        check(!exit.Tick(20, true, false, true) && exit.Reward == 0 && !exit.CarriedBountyAtExit,
            "Late bounty state changes cannot upgrade an already completed no-bounty settlement");
        exit.Reset();
        check(!exit.Completed && exit.Elapsed == 0 && exit.Remaining == 10 && !exit.CarriedBountyAtExit,
            "A new run resets all prior extraction and carried-bounty state");
        check(!exit.Tick(5, true, false, true) && exit.Reward == 0 && !exit.CarriedBountyAtExit,
            "Carrying a bounty during preparation does not grant its reward before successful departure");
        check(!exit.Tick(1, false, false, true) && exit.Elapsed == 0 && !exit.Completed,
            "Leaving the exit resets the entire continuous countdown");
        exit.Tick(6, true, false, true);
        check(!exit.Tick(1, true, true, true) && exit.Elapsed == 0,
            "An enemy contest resets progress even when the hunter carries a bounty");
        check(exit.Tick(10, true, false, true) && exit.Reward == 250 && exit.CarriedBountyAtExit,
            "An uncontested full bounty departure captures exactly the configured bounty reward");
        exit.Interrupt();
        check(exit.Completed && exit.Elapsed == 10 && exit.Reward == 250 && !exit.Tick(10, false, true, false),
            "After completion, later interruptions cannot erase the captured reward or emit a second settlement");
        exit.Reset(); exit.Tick(7, true, false, false);
        check(!exit.Tick(0, false, true, true) && exit.Elapsed == 7 && !exit.Completed,
            "Zero simulation time freezes both progress and spatial contest changes during pause");
        bool invalidTime = true;
        foreach (float delta in new[] { -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            invalidTime &= !exit.Tick(delta, false, true, true) && exit.Elapsed == 7;
        check(invalidTime, "Invalid elapsed time cannot finish or reset an ongoing attempt");
        exit.Interrupt();
        check(exit.Elapsed == 0 && !exit.Completed && exit.Reward == 0, "An explicit active interruption discards unfinished progress");
        exit.Tick(9, true, false, false);
        check(exit.Tick(1, true, false, true) && exit.Reward == 250,
            "A bounty acquired before departure is included when the successful exit is finally captured");
        exit.Reset(); exit.Tick(9, true, false, true);
        check(exit.Tick(1, true, false, false) && exit.Reward == 0,
            "A bounty no longer carried at departure is never rewarded from earlier countdown history");
        var second = new HunterExtractionAttempt(10, 250); second.Tick(3, true, false, true);
        check(exit.Completed && exit.Reward == 0 && second.Elapsed == 3 && !second.Completed,
            "Separate attempts keep progress and reward state independent");
        check(second.Tick(float.MaxValue, true, false, true) && second.Elapsed == 10 && second.Remaining == 0 && second.Reward == 250,
            "A large finite simulation step clamps to the deadline without overflow or duplicate rewards");
        var free = new HunterExtractionAttempt(1, 0);
        check(free.Tick(1, true, false, true) && free.CarriedBountyAtExit && free.Reward == 0,
            "A zero-valued bounty remains distinguishable from an unclaimed bounty without granting currency");
        bool invalid = true;
        foreach (float duration in new[] { 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            invalid &= Throws(() => new HunterExtractionAttempt(duration, 250));
        check(invalid && Throws(() => new HunterExtractionAttempt(10, -1)), "Invalid duration or negative bounty configuration is rejected");
        check(!new HunterExtractionAttempt(10, 250).Tick(20, false, false, false),
            "A no-bounty hunter still must physically reach the exit before any countdown can complete");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

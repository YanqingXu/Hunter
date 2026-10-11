using System;
using BigWorld.Gameplay;

public static class HunterInteractionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var timer = new HunterInteractionTimer();
        check(!timer.IsRunning && !timer.HasCompleted && timer.Progress == 0 && !timer.Tick(10, false, false),
            "Idle interactions cannot fire without an accepted start");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!timer.TryStart(invalid) && !timer.IsRunning && timer.Duration == 0,
                "Invalid interaction duration leaves the idle attempt unchanged: " + invalid);
        check(timer.TryStart(3) && timer.IsRunning && timer.Duration == 3 && !timer.HasCompleted,
            "A delayed scene interaction begins with zero elapsed simulation time");
        check(!timer.Tick(1, false, false) && timer.Elapsed == 1 && Math.Abs(timer.Progress - 1f / 3) < .0001f,
            "The progress display follows accumulated simulation time before completion");
        check(!timer.TryStart(0) && timer.Elapsed == 1 && timer.Duration == 3,
            "Repeated interaction requests cannot replace a running delay with an immediate completion");
        check(!timer.Tick(100, true, true) && timer.IsRunning && timer.Elapsed == 1,
            "Paused frames neither advance time nor replay blocked gameplay input as cancellation");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!timer.Tick(invalid, false, false) && timer.Elapsed == 1 && timer.IsRunning,
                "Invalid frame time cannot advance or corrupt the active delay: " + invalid);
        check(!timer.Tick(2, false, true) && !timer.IsRunning && !timer.HasCompleted && timer.Progress == 0,
            "Movement or another action cancels before a completion due in the same frame");
        check(!timer.Tick(10, false, false) && !timer.Cancel(), "A cancelled attempt cannot complete later or cancel twice");
        check(timer.TryStart(3) && timer.Elapsed == 0 && !timer.Tick(2, false, false),
            "Restarting an interrupted interaction must wait the full new duration");
        check(timer.Tick(1, false, false) && timer.HasCompleted && !timer.IsRunning && timer.Progress == 1,
            "Reaching the exact deadline emits one completion and clamps progress to one");
        check(!timer.Tick(10, false, false) && !timer.Tick(0, false, true) && !timer.TryStart(0) && !timer.Cancel() && timer.HasCompleted,
            "A completed attempt cannot emit twice or be reopened by repeated input without an explicit reset");
        timer.Reset();
        check(timer.TryStart(0) && timer.Tick(0, false, false) && !timer.Tick(0, false, false),
            "An immediate interaction completes exactly once on its start frame without waiting for positive delta time");
        timer.Reset(); timer.TryStart(0);
        check(!timer.Tick(0, true, false) && timer.IsRunning && timer.Tick(0, false, false),
            "Even a zero-duration interaction respects the paused session gate");
        timer.Reset(); timer.TryStart(2); timer.Tick(1, false, false);
        check(timer.Cancel() && timer.Duration == 0 && timer.Elapsed == 0 && timer.Progress == 0 && !timer.HasCompleted,
            "Explicit range, damage, death or unload cancellation discards an unfinished countdown");
        timer.TryStart(2);
        check(timer.Tick(float.MaxValue, false, false) && timer.Elapsed == 2,
            "A long finite frame completes once without progress overshoot");
        timer.Reset(); timer.TryStart(float.MaxValue); timer.Tick(float.MaxValue * .75f, false, false);
        check(timer.Tick(float.MaxValue, false, false) && timer.Elapsed == float.MaxValue,
            "Large finite accumulated times clamp without float overflow");
        timer.Reset(); timer.TryStart(float.Epsilon);
        check(timer.Tick(float.Epsilon, false, false), "A tiny positive configured delay is not mistaken for invalid input");
        timer.Reset(); timer.TryStart(1);
        check(!timer.Tick(float.NaN, false, true) && !timer.IsRunning,
            "An explicit interruption remains authoritative even when the frame clock is invalid");
        timer.TryStart(5); timer.Tick(2, false, false); timer.Reset();
        check(!timer.IsRunning && !timer.HasCompleted && timer.Elapsed == 0 && timer.Duration == 0,
            "Starting a new run clears both a pending delay and previous completion state");
    }
}

using System;
using BigWorld.Gameplay;

public static class HunterPointerStickChecks
{
    public static void Run(Action<bool, string> check)
    {
        var stick = new HunterPointerStick();
        check(!stick.HasPointer && stick.X == 0 && stick.Y == 0, "A fresh stick is neutral and has no captured pointer");
        check(!stick.TryDrag(0, 1, 0) && !stick.TryRelease(0), "An uncaptured finger cannot move or release the stick");
        check(!stick.TryPress(1, float.NaN, 1) && !stick.HasPointer, "Invalid press coordinates cannot capture the stick");
        check(stick.TryPress(-1, .4f, .3f) && stick.PointerId == -1 && Near(stick.X, .4f) && Near(stick.Y, .3f),
            "Mouse pointer IDs are valid and analog input inside the radius keeps its strength");
        check(!stick.TryPress(8, -1, 0) && stick.PointerId == -1 && Near(stick.X, .4f),
            "A second finger cannot steal an active stick");
        check(!stick.TryDrag(8, -1, 0) && !stick.TryRelease(8) && Near(stick.X, .4f) && Near(stick.Y, .3f),
            "Dragging or lifting another finger does not change the owner's movement");
        check(!stick.TryPress(-1, 1, 1) && Near(stick.X, .4f), "Duplicate press events from the owner do not reset its current input");
        check(stick.TryDrag(-1, -3, 4) && Near(stick.X, -.6f) && Near(stick.Y, .8f),
            "Dragging beyond the radius clamps to a unit circle while preserving direction");
        check(stick.TryDrag(-1, float.MaxValue, float.MaxValue) && Near(stick.X, .7071068f) && Near(stick.Y, .7071068f),
            "Extreme finite drag coordinates normalize without overflow");
        check(stick.TryDrag(-1, .1f, .1f) && stick.X == 0 && stick.Y == 0 && stick.HasPointer,
            "Radial dead-zone motion is neutral without releasing pointer ownership");
        check(stick.TryDrag(-1, .12f, .12f) && Near(stick.X, .12f) && Near(stick.Y, .12f),
            "Diagonal motion just outside the radial dead zone retains analog strength");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!stick.TryDrag(-1, invalid, 1) && !stick.TryDrag(-1, 1, invalid) && Near(stick.X, .12f) && Near(stick.Y, .12f),
                "Invalid owner motion preserves the previous finite input: " + invalid);
        check(stick.TryRelease(-1) && !stick.HasPointer && stick.X == 0 && stick.Y == 0 && !stick.TryDrag(-1, 1, 0),
            "The matching release clears movement and later stale drags are ignored");
        stick.TryPress(7, -1, 0); stick.Cancel();
        check(!stick.HasPointer && stick.X == 0 && stick.Y == 0 && !stick.TryRelease(7),
            "Explicit cancellation clears ownership and movement for pause, focus loss or teardown adapters");
        check(stick.TryPress(9, 1, 0) && !stick.TryRelease(7) && stick.PointerId == 9 && stick.X == 1,
            "A stale release after cancellation cannot end a new finger's gesture");
        check(Throws(() => new HunterPointerStick(-.1f)) && Throws(() => new HunterPointerStick(1)) &&
            Throws(() => new HunterPointerStick(float.NaN)) && Throws(() => new HunterPointerStick(float.PositiveInfinity)),
            "Invalid dead-zone configuration is rejected");
        var direct = new HunterPointerStick(0);
        check(direct.TryPress(int.MinValue, float.Epsilon, 0) && direct.X == float.Epsilon && direct.PointerId == int.MinValue,
            "Zero dead zone preserves tiny finite input and does not reserve a sentinel pointer ID");
        stick.Cancel();
        check(stick.TryPress(10, 0, 0) && stick.HasPointer && !stick.TryPress(11, 1, 0),
            "Pressing the center still owns the stick until the original finger releases it");
    }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .00001f; }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

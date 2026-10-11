using System;
using BigWorld.Gameplay;

public static class HunterThrowPathChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (int facing in new[] { -1, 1 })
        {
            var knife = new HunterThrowPath(facing, 0, 8, .25f);
            bool legacy = true;
            foreach (float distance in new[] { 0f, 1f, 2f, 4f, 6f, 8f })
            {
                float x, y; knife.Sample(distance, out x, out y);
                legacy &= Near(x, facing * distance) && Near(y, (float)Math.Sin(distance / 8 * Math.PI) * .25f);
            }
            check(legacy, "Horizontal knife trajectory retains the original upward arc for facing " + facing);
        }
        var rightUp = new HunterThrowPath(3, 4, 10, 1.2f);
        check(At(rightUp, 0, 0, 0) && At(rightUp, 5, 3, 5.2f) && At(rightUp, 10, 6, 8),
            "Diagonal direction is normalized and the arc returns to the intended directional endpoint");
        var leftUp = new HunterThrowPath(-3, 4, 10, 1.2f);
        check(At(leftUp, 5, -3, 5.2f) && At(leftUp, 10, -6, 8), "Turning left mirrors horizontal travel without flipping the upward arc");
        var up = new HunterThrowPath(0, 2, 6, 1.2f);
        check(At(up, 3, 0, 4.2f) && At(up, 6, 0, 6), "A vertical throw follows the upward aim and ends at its configured range");
        var down = new HunterThrowPath(0, -1, 6, 1.2f);
        check(At(down, 3, 0, -1.8f) && At(down, 6, 0, -6), "Standing downward aim throws below the origin while retaining the same arc formula");
        var downLeft = new HunterThrowPath(-1, -1, 8, 0);
        check(At(downLeft, 4, -2.8284271f, -2.8284271f), "A zero-height arc supports straight travel into every aim quadrant");
        check(At(rightUp, -1, 0, 0) && At(rightUp, 30, 6, 8), "Sampling clamps progress to the start and endpoint");
        var ordinary = new HunterThrowPath(3, 4, 8, .25f);
        var skilled = new HunterThrowPath(3, 4, 12, .25f);
        check(At(ordinary, 8, 4.8f, 6.4f) && At(skilled, 12, 7.2f, 9.6f) && ordinary.Range == 8 && ordinary.ArcHeight == skilled.ArcHeight,
            "A later range bonus has its own snapshot and extends only the new throw's directional distance");
        check(At(new HunterThrowPath(float.MaxValue, float.MaxValue, 8, .25f), 8, 5.6568542f, 5.6568542f),
            "Large finite aim inputs normalize without overflow");
        check(At(new HunterThrowPath(float.Epsilon, 0, 8, .25f), 8, 8, 0), "Tiny nonzero directions remain valid path snapshots");
        check(Throws(() => new HunterThrowPath(0, 0, 8, .25f)) && Throws(() => new HunterThrowPath(1, 0, 0, .25f)) &&
            Throws(() => new HunterThrowPath(1, 0, -1, .25f)) && Throws(() => new HunterThrowPath(1, 0, 8, -1)),
            "Invalid zero direction, nonpositive range and negative arc cannot create a path");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            float x, y;
            check(Throws(() => new HunterThrowPath(invalid, 1, 8, .25f)) && Throws(() => new HunterThrowPath(1, invalid, 8, .25f)) &&
                Throws(() => new HunterThrowPath(1, 0, invalid, .25f)) && Throws(() => new HunterThrowPath(1, 0, 8, invalid)) &&
                Throws(() => ordinary.Sample(invalid, out x, out y)), "Nonfinite path parameters and progress are rejected: " + invalid);
        }
        check(Throws(() => new HunterThrowPath(0, 1, float.MaxValue, float.MaxValue)),
            "Unrepresentable range-plus-arc bounds are rejected before producing an infinite position");
    }
    private static bool At(HunterThrowPath path, float distance, float expectedX, float expectedY)
    { float x, y; path.Sample(distance, out x, out y); return Near(x, expectedX) && Near(y, expectedY); }
    private static bool Near(float a, float b) { return Math.Abs(a - b) < .00001f; }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

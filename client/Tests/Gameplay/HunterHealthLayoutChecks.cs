using System;
using System.Collections.Generic;
using BigWorld.Gameplay;

public static class HunterHealthLayoutChecks
{
    public static void Run(Action<bool, string> check)
    {
        var input = new float[] { 50, 50, 25, 25 };
        var layout = new HunterHealthLayout(150, input);
        check(layout.Maximum == 150 && layout.Count == 4 && Same(layout.Segments, input), "The authored example preserves ordered segments and fixed total health");
        input[0] = 25;
        check(layout.Segments[0] == 50, "Changing the constructor input cannot mutate a health layout");
        var output = layout.Segments; output[1] = 25;
        check(layout.Segments[1] == 50, "Reading segments returns an independent copy for save and UI adapters");
        check(HunterHealthLayout.IsAllowed(100, new float[] { 25, 25, 25, 25 }), "Existing 100-health hunters remain a supported configuration");
        bool invalid = true;
        foreach (var value in new[] { new float[0], null, new float[] { 100 }, new float[] { 25, 50 }, new float[] { 25, 25, 25, 24.999f },
            new float[] { 25, 25, 0, 50 }, new float[] { -25, 25, 50, 50 }, new float[] { float.NaN, 50 }, new float[] { float.PositiveInfinity, 50 } })
            invalid &= !HunterHealthLayout.IsAllowed(100, value);
        check(invalid, "Null, empty, unsupported, nonfinite and wrong-total segment arrays are rejected");
        invalid = true;
        foreach (float maximum in new[] { 0, -100, 99, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { new HunterHealthLayout(maximum, new float[] { 50, 50 }); } catch (ArgumentException) { rejected = true; }
            invalid &= rejected;
        }
        check(invalid, "Invalid maximum health fails before a mutable configuration is created");
        check(layout.CanSplit(1) && layout.TrySplit(1) && Same(layout.Segments, new float[] { 50, 25, 25, 25, 25 }),
            "Splitting a middle 50 segment preserves the order of every other threshold");
        check(layout.Maximum == 150 && HunterHealthSegments.IsValid(layout.Maximum, layout.Segments), "Splitting cannot raise or lower maximum health");
        check(HunterHealthSegments.RecoveryCeiling(60, 150, layout.Segments) == 75,
            "The new split threshold changes recovery from 60 to 75 without changing health itself");
        check(HunterHealthSegments.RecoveryCeiling(75, 150, layout.Segments) == 75 && HunterHealthSegments.RecoveryCeiling(0, 150, layout.Segments) == 0,
            "An exact edited boundary stops regeneration and zero health never revives");
        check(layout.CanMerge(1) && layout.TryMerge(1) && Same(layout.Segments, new float[] { 50, 50, 25, 25 }),
            "Merging adjacent 25 segments restores the original ordered threshold");
        check(HunterHealthSegments.RecoveryCeiling(60, 150, layout.Segments) == 100 && layout.Maximum == 150,
            "Merged segments feed the existing recovery rules while keeping the same maximum");
        var before = layout.Segments;
        check(!layout.TrySplit(2) && Same(layout.Segments, before), "A 25 segment cannot be split below the minimum allowed size");
        check(!layout.TryMerge(0) && !layout.TryMerge(1) && Same(layout.Segments, before), "A 50 segment cannot merge with either size or cross an existing threshold");
        invalid = true;
        foreach (int index in new[] { -1, layout.Count, int.MinValue, int.MaxValue })
            invalid &= !layout.CanSplit(index) && !layout.CanMerge(index) && !layout.TrySplit(index) && !layout.TryMerge(index);
        check(invalid && Same(layout.Segments, before), "Invalid edit indices fail without partial changes or integer overflow");
        check(!layout.TryMerge(layout.Count - 1) && Same(layout.Segments, before), "The last segment cannot merge beyond the end of the bar");
        check(!layout.TrySet(new float[] { 50, 50 }) && !layout.TrySet(null) && Same(layout.Segments, before),
            "Replacing segments rejects wrong totals and null values atomically");
        check(!layout.TrySet(before), "An identical layout is a no-op for future camp save transactions");
        input = new float[] { 25, 25, 50, 50 };
        check(layout.TrySet(input) && Same(layout.Segments, input), "A valid full replacement allows a different ordered composition");
        input[0] = 50;
        check(layout.Segments[0] == 25, "A replacement also copies its input instead of retaining external ownership");
        check(Reachable(100, 4) == 5 && Reachable(150, 6) == 13,
            "Split and merge can reach all five 100-health and thirteen 150-health ordered compositions");
    }
    private static int Reachable(float maximum, int units)
    {
        var initial = new float[units]; for (int i = 0; i < units; i++) initial[i] = 25;
        var visited = new HashSet<string>(); var pending = new Queue<float[]>(); pending.Enqueue(initial);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue(); string key = string.Join(",", Array.ConvertAll(current, x => x.ToString()));
            if (!visited.Add(key)) continue;
            if (!HunterHealthLayout.IsAllowed(maximum, current)) throw new Exception("Reachable layout changed maximum health.");
            for (int i = 0; i < current.Length; i++)
            {
                var split = new HunterHealthLayout(maximum, current); if (split.TrySplit(i)) pending.Enqueue(split.Segments);
                var merge = new HunterHealthLayout(maximum, current); if (merge.TryMerge(i)) pending.Enqueue(merge.Segments);
            }
        }
        return visited.Count;
    }
    private static bool Same(float[] left, float[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }
}

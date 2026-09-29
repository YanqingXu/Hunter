// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Pixel-based magnetic candidates, ranked before frame quantization.</summary>
public static class SkillTimelineSnap
{
    public const float DistancePixels = 12f;

    public struct Target
    {
        public int Frame, Priority;
        public Target(int frame, int priority) { Frame = frame; Priority = priority; }
    }

    public struct Candidate
    {
        public int Delta, Frame, Priority;
        public double Distance;
    }

    public static IEnumerable<Candidate> Candidates(float rawDelta, IEnumerable<int> movingEdges,
        IEnumerable<Target> targets, float frameWidth)
    {
        if (frameWidth <= 0 || float.IsNaN(rawDelta) || float.IsInfinity(rawDelta))
            return Enumerable.Empty<Candidate>();
        var candidates = new List<Candidate>();
        var distinctTargets = targets.GroupBy(t => t.Frame)
            .Select(group => group.OrderBy(t => t.Priority).First()).ToArray();
        foreach (int edge in movingEdges.Distinct())
            foreach (var target in distinctTargets)
            {
                long delta = (long)target.Frame - edge;
                double distance = Math.Abs(delta - (double)rawDelta) * frameWidth;
                if (distance > DistancePixels || delta < int.MinValue || delta > int.MaxValue) continue;
                candidates.Add(new Candidate { Delta = (int)delta, Frame = target.Frame,
                    Priority = target.Priority, Distance = distance });
            }
        return candidates.OrderBy(c => c.Priority).ThenBy(c => c.Distance).ThenBy(c => c.Frame).ThenBy(c => c.Delta);
    }
}

}

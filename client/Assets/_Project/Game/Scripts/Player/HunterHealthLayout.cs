using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    /// <summary>Camp-editable 25/50 health segments. Editing never changes the fixed maximum.</summary>
    public sealed class HunterHealthLayout
    {
        private float[] segments;
        public float Maximum { get; private set; }
        public float[] Segments { get { return (float[])segments.Clone(); } }
        public int Count { get { return segments.Length; } }

        public HunterHealthLayout(float maximum, float[] value)
        {
            if (!IsAllowed(maximum, value)) throw new ArgumentException("Health layout requires 25/50 segments summing to the fixed maximum.");
            Maximum = maximum; segments = (float[])value.Clone();
        }
        public static bool IsAllowed(float maximum, float[] value)
        {
            if (!HunterHealthSegments.IsValid(maximum, value)) return false;
            foreach (float segment in value) if (segment != 25 && segment != 50) return false;
            return true;
        }
        public bool CanSplit(int index) { return index >= 0 && index < segments.Length && segments[index] == 50; }
        public bool CanMerge(int index)
        { return index >= 0 && index < segments.Length - 1 && segments[index] == 25 && segments[index + 1] == 25; }
        public bool TrySplit(int index)
        {
            if (!CanSplit(index)) return false;
            var next = new List<float>(segments); next[index] = 25; next.Insert(index + 1, 25);
            segments = next.ToArray(); return true;
        }
        public bool TryMerge(int index)
        {
            if (!CanMerge(index)) return false;
            var next = new List<float>(segments); next[index] = 50; next.RemoveAt(index + 1);
            segments = next.ToArray(); return true;
        }
        public bool TrySet(float[] value)
        {
            if (!IsAllowed(Maximum, value)) return false;
            bool same = value.Length == segments.Length;
            if (same) for (int i = 0; i < segments.Length; i++) same &= value[i] == segments[i];
            if (same) return false;
            segments = (float[])value.Clone(); return true;
        }
    }
}

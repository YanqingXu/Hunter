using System;

namespace BigWorld.Gameplay
{
    /// <summary>Run-owned clue sockets and candidate regions, independent of world coordinates and UI.</summary>
    public sealed class HunterSearchLayout
    {
        public int RegionCount { get; private set; }
        public int BossRegion { get; private set; }
        public int RequiredClues { get { return RegionCount - 1; } }
        public int CollectedClues { get; private set; }
        public int ActiveClueCount { get; private set; }
        public int SocketCount { get { return active.Length; } }
        public int CandidateCount { get { return RegionCount - CollectedClues; } }
        public bool IsLocated { get { return CollectedClues == RequiredClues; } }
        private readonly bool[] active, collected, candidates;
        private readonly int[] exclusionOrder;

        public HunterSearchLayout(int regionCount, int bossRegion, int seed)
        {
            // The current map uses four regions; bound scene configuration before allocating socket arrays.
            if (regionCount < 2 || regionCount > 64) throw new ArgumentOutOfRangeException("regionCount");
            if (bossRegion < 0 || bossRegion >= regionCount) throw new ArgumentOutOfRangeException("bossRegion");
            RegionCount = regionCount; BossRegion = bossRegion;
            active = new bool[regionCount * 2]; collected = new bool[active.Length]; candidates = new bool[regionCount];
            exclusionOrder = new int[regionCount - 1]; var random = new Random(seed);
            for (int region = 0, excluded = 0; region < regionCount; region++)
            {
                candidates[region] = true;
                int first = region * 2 + random.Next(2); active[first] = true; ActiveClueCount++;
                if (random.Next(2) == 1) { active[region * 2 + (1 - first % 2)] = true; ActiveClueCount++; }
                if (region != bossRegion) exclusionOrder[excluded++] = region;
            }
            for (int i = exclusionOrder.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1), value = exclusionOrder[i];
                exclusionOrder[i] = exclusionOrder[other]; exclusionOrder[other] = value;
            }
        }
        public bool IsCandidate(int region)
        { ValidateRegion(region); return candidates[region]; }
        public int ClueCountInRegion(int region)
        { ValidateRegion(region); return (active[region * 2] ? 1 : 0) + (active[region * 2 + 1] ? 1 : 0); }
        public bool HasClueSocket(int socket)
        { return socket >= 0 && socket < active.Length && active[socket]; }
        public bool HasCollected(int socket)
        { return socket >= 0 && socket < collected.Length && collected[socket]; }
        public bool IsClueAvailable(int socket)
        { return !IsLocated && HasClueSocket(socket) && !collected[socket]; }
        public bool TryCollect(int socket, out int excludedRegion)
        {
            excludedRegion = -1;
            if (!IsClueAvailable(socket)) return false;
            collected[socket] = true; excludedRegion = exclusionOrder[CollectedClues];
            candidates[excludedRegion] = false; CollectedClues++; return true;
        }
        private void ValidateRegion(int region)
        { if (region < 0 || region >= RegionCount) throw new ArgumentOutOfRangeException("region"); }
    }
}

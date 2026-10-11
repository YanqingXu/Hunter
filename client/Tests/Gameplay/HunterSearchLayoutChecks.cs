using System;
using System.Collections.Generic;
using BigWorld.Gameplay;

public static class HunterSearchLayoutChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (int invalid in new[] { -1, 0, 1, 65, int.MaxValue })
        {
            bool rejected = false;
            try { new HunterSearchLayout(invalid, 0, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Unsupported search-region counts are rejected before allocating sockets: " + invalid);
        }
        foreach (int invalid in new[] { -1, 4, int.MaxValue })
        {
            bool rejected = false;
            try { new HunterSearchLayout(4, invalid, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "The boss must belong to an authored search region: " + invalid);
        }
        var layouts = new HashSet<string>(); bool bounded = true, repeatable = true, canUseEitherSocket = false, canHaveTwo = false;
        bool keepsBoss = true, excludesOnce = true, completes = true, countsStayAligned = true;
        for (int boss = 0; boss < 4; boss++)
            for (int seed = 0; seed < 32; seed++)
            {
                var layout = new HunterSearchLayout(4, boss, seed); var same = new HunterSearchLayout(4, boss, seed);
                var mission = new HunterMission(layout.RequiredClues, 10, 250); string signature = "";
                for (int region = 0; region < 4; region++)
                {
                    int count = layout.ClueCountInRegion(region); bounded &= count >= 1 && count <= 2;
                    canHaveTwo |= count == 2;
                    canUseEitherSocket |= !layout.HasClueSocket(region * 2) && layout.HasClueSocket(region * 2 + 1);
                }
                bounded &= layout.ActiveClueCount >= 4 && layout.ActiveClueCount <= 8;
                var removed = new HashSet<int>();
                for (int socket = 0; socket < layout.SocketCount; socket++)
                {
                    signature += layout.HasClueSocket(socket) ? "1" : "0";
                    repeatable &= layout.HasClueSocket(socket) == same.HasClueSocket(socket);
                    int excluded, repeatExcluded;
                    bool accepted = layout.TryCollect(socket, out excluded), repeatAccepted = same.TryCollect(socket, out repeatExcluded);
                    repeatable &= accepted == repeatAccepted && excluded == repeatExcluded;
                    if (accepted)
                    {
                        excludesOnce &= excluded != boss && removed.Add(excluded) && !layout.IsCandidate(excluded);
                        countsStayAligned &= mission.Investigate(socket.ToString()) && mission.ClueCount == layout.CollectedClues;
                    }
                    keepsBoss &= layout.IsCandidate(boss);
                }
                layouts.Add(signature);
                completes &= layout.IsLocated && layout.CollectedClues == 3 && layout.CandidateCount == 1 && removed.Count == 3 && mission.Stage == HuntStage.Hunting;
            }
        check(bounded, "Across 128 seeded runs every region receives one or two clues and the map receives four to eight");
        check(repeatable, "A repeated seed and boss region reproduce both clue sockets and region exclusion order");
        check(layouts.Count > 1 && canUseEitherSocket && canHaveTwo, "Seeds vary the layout, permit either single socket and sometimes select both");
        check(keepsBoss, "No successful clue can gray out the actual boss region");
        check(excludesOnce, "Each accepted clue excludes exactly one distinct non-boss candidate");
        check(countsStayAligned && completes, "Three distinct clues locate the boss and stay aligned with the existing mission count and hunting stage");

        var probe = new HunterSearchLayout(4, 2, 7); int ignored;
        check(!probe.IsLocated && probe.CandidateCount == 4 && probe.CollectedClues == 0, "A fresh run starts with all regions possible");
        check(!probe.TryCollect(-1, out ignored) && ignored == -1 && !probe.TryCollect(probe.SocketCount, out ignored), "Out-of-range clue IDs cannot advance the search");
        int first = -1, inactive = -1;
        for (int slot = 0; slot < probe.SocketCount; slot++) { if (first < 0 && probe.HasClueSocket(slot)) first = slot; if (!probe.HasClueSocket(slot)) inactive = slot; }
        check(inactive >= 0 && !probe.TryCollect(inactive, out ignored) && probe.CollectedClues == 0, "An authored but unselected socket cannot be collected");
        check(probe.TryCollect(first, out ignored) && probe.HasCollected(first) && !probe.IsClueAvailable(first), "A successful investigation consumes its specific live clue");
        int candidates = probe.CandidateCount;
        check(!probe.TryCollect(first, out ignored) && ignored == -1 && probe.CandidateCount == candidates && probe.CollectedClues == 1,
            "Repeated investigation of one clue cannot shrink the map or add experience twice");
        for (int slot = 0; slot < probe.SocketCount; slot++) probe.TryCollect(slot, out ignored);
        bool available = false; for (int slot = 0; slot < probe.SocketCount; slot++) available |= probe.IsClueAvailable(slot);
        check(probe.IsLocated && !available && probe.CollectedClues == 3, "Surplus clue interactions stop after the boss region has been located");
        var next = new HunterSearchLayout(4, 2, 7);
        check(next.CollectedClues == 0 && next.CandidateCount == 4 && next.IsClueAvailable(first), "A new run has no collected flags or exclusions from an earlier run");
        foreach (int regions in new[] { 2, 8, 64 })
        {
            var map = new HunterSearchLayout(regions, regions - 1, 3);
            for (int slot = map.SocketCount - 1; slot >= 0; slot--) map.TryCollect(slot, out ignored);
            check(map.IsLocated && map.CollectedClues == regions - 1 && map.CandidateCount == 1 && map.IsCandidate(regions - 1),
                "A map with " + regions + " regions remains solvable even when clues are visited in reverse order");
        }
        bool regionRejected = false; try { probe.IsCandidate(-1); } catch (ArgumentOutOfRangeException) { regionRejected = true; }
        check(regionRejected, "Region queries reject an invalid map index");
    }
}

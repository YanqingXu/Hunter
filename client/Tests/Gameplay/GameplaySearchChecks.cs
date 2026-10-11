using System.Collections;
using System.Linq;
using BigWorld.Gameplay;
using SkillEditorKit;
using UnityEngine;

public sealed partial class GameplayPlayChecks
{
    private IEnumerator SearchChecks()
    {
        bool itemLoot = level.ItemLoot.enabled, skillLoot = level.SkillLoot.enabled, viewport = level.World.Map.UseCameraViewport;
        level.ItemLoot.enabled = level.SkillLoot.enabled = false; level.Elites.enabled = false; isolateEliteNpcs = true;
        var hunt = level.Hunt; hunt.UseRegionClues = true; hunt.RandomizeBossRegion = true;
        level.ClaimRelief(); level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        var layout = hunt.SearchLayout; var map = hud.RegionMap;
        Check(layout != null && layout.RegionCount == 4 && layout.ActiveClueCount >= 4 && layout.ActiveClueCount <= 8 &&
            hunt.State.RequiredClues == 3, "Production deployment selects four to eight regional clues but requires only three investigations");
        Check(hunt.BossPosition == hunt.BossRegions[layout.BossRegion] && hunt.BossRegion == layout.BossRegion &&
            Enumerable.Range(0, 4).All(i => layout.ClueCountInRegion(i) >= 1 && layout.ClueCountInRegion(i) <= 2),
            "Production boss selection and every region's one-or-two clue selection share the same search layout");
        Check(map && map.RegionCount == 4 && !map.BossMarkerVisible && !map.ExitMarkerVisible &&
            Enumerable.Range(0, 4).All(i => !map.IsRegionDimmed(i)), "The actual map begins with four candidates and no boss or exit location");
        Check(!map.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("Clue")), "The map UI contains no clue location icons");
        bool socketsReachable = true, markersCorrect = true;
        for (int i = 0; i < hunt.ClueSocketCount; i++)
        {
            Vector2 point = hunt.CluePosition(i);
            yield return EliteGround(new Vector2(point.x, 2.05f));
            socketsReachable &= player.IsGrounded && Vector2.Distance(body.position, point) < 2;
            markersCorrect &= !hunt.IsClueVisible(i);
            hunt.SetTouchInvestigation(true, Vector2.zero); yield return null;
            markersCorrect &= hunt.IsClueVisible(i) == layout.HasClueSocket(i);
            hunt.SetTouchInvestigation(false, Vector2.zero); yield return null;
        }
        Check(socketsReachable, "All eight authored clue sockets have loaded reachable ground in the real map");
        Check(markersCorrect, "Only selected clue sockets render in investigation view; normal view hides all clue sprites");
        int first = Enumerable.Range(0, hunt.ClueSocketCount).First(i => layout.HasClueSocket(i));
        Vector2 clue = hunt.CluePosition(first);
        yield return EliteGround(new Vector2(clue.x - 1, 2.05f));
        hunt.SetTouchInvestigation(true, Vector2.zero); yield return null;
        Check(hunt.IsSpecialView && !player.TryInteract() && !player.TryShoot() && !player.TryMelee() && hunt.State.ClueCount == 0,
            "Investigation view displays a random clue while rejecting interaction and attacks");
        hunt.SetTouchInvestigation(false, Vector2.zero); yield return null;
        level.TogglePause(); Check(!hunt.TryInteract() && hunt.State.ClueCount == 0, "Pause cannot consume a regional clue or gray a region");
        level.TogglePause(); yield return null;
        var wall = new GameObject("Search Clue Cover"); wall.transform.position = new Vector2(clue.x - .45f, 3);
        var shape = wall.AddComponent<BoxCollider2D>(); shape.size = new Vector2(.1f, 2); Physics2D.SyncTransforms();
        Check(!hunt.TryInteract() && layout.CollectedClues == 0, "A solid wall prevents collecting a random clue through terrain");
        Destroy(wall); yield return null; yield return EliteGround(new Vector2(clue.x, 2.05f));
        Check(player.TryInteract() && hunt.State.HasClue(first.ToString()) && layout.CollectedClues == 1,
            "A real player interaction collects the selected socket and advances mission and search atomically");
        yield return null;
        Check(Enumerable.Range(0, 4).Count(i => map.IsRegionDimmed(i)) == 1 && !map.IsRegionDimmed(hunt.BossRegion) &&
            !map.BossMarkerVisible && !map.ExitMarkerVisible, "The first random investigation visibly grays one non-boss region without revealing destinations");
        Check(!player.TryInteract() && layout.CollectedClues == 1 && hunt.State.ClueCount == 1,
            "Repeating interaction at the same socket cannot consume or exclude another region");
        yield return Capture("search-region-exclusion");
        level.World.Map.UseCameraViewport = false; yield return EliteGround(new Vector2(77, 2.05f));
        yield return Until(() => !level.World.Map.IsCellLoaded(Mathf.FloorToInt(clue.x), 2), 4, "The first clue's real terrain streams out");
        yield return EliteGround(new Vector2(clue.x, 2.05f));
        Check(ReferenceEquals(layout, hunt.SearchLayout) && layout.HasCollected(first) && hunt.State.ClueCount == 1 && !hunt.IsClueAvailable(first),
            "Terrain unload and reload preserve the selected layout and consumed clue identity");
        for (int count = 2; count <= 3; count++)
        {
            int next = Enumerable.Range(0, hunt.ClueSocketCount).Last(i => hunt.IsClueAvailable(i));
            yield return EliteGround(new Vector2(hunt.CluePosition(next).x, 2.05f));
            Check(player.TryInteract() && layout.CollectedClues == count && hunt.State.ClueCount == count,
                "Regional clue " + count + " synchronizes the actual player interaction and mission count");
            yield return null;
        }
        Check(hunt.State.Stage == HuntStage.Hunting && layout.CandidateCount == 1 && map.BossMarkerVisible && !map.ExitMarkerVisible &&
            Enumerable.Range(0, 4).All(i => map.IsRegionDimmed(i) == (i != hunt.BossRegion)),
            "Three investigations leave exactly the boss region colored and reveal its map marker");
        Check(!Enumerable.Range(0, hunt.ClueSocketCount).Any(i => hunt.IsClueAvailable(i)) && !hunt.TryInteract(),
            "Surplus generated clues stop accepting interactions after localization");
        yield return Capture("search-boss-located");
        yield return EliteGround(hunt.BossPosition); yield return Until(() => hunt.Boss, 3, "The randomly located real boss loads");
        SetAlertActor(hunt.Boss.transform, hunt.BossPosition);
        hunt.Boss.Actor.BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        Check(hunt.State.Stage == HuntStage.BountyAvailable && !map.ExitMarkerVisible,
            "Killing the real random boss leaves the exit hidden until the bounty is collected");
        yield return EliteGround(hunt.BountyPosition - Vector2.up * .5f);
        Check(player.TryInteract() && hunt.State.SecondWaveStarted, "The random search route reaches a genuine bounty pickup"); yield return null;
        Check(map.ExitMarkerVisible && !map.BossMarkerVisible, "Picking up the real bounty replaces the boss marker with the authored edge exit");
        hunt.OnPlayerDied(); yield return null;
        Check(map.ExitMarkerVisible && hunt.State.Stage == HuntStage.BountyAvailable, "A previously discovered exit remains mapped when its bounty is dropped");
        Check(hunt.TryInteract(), "The dropped bounty remains collectable in the random route"); yield return null;
        float markerX = map.PlayerMarker.anchoredPosition.x;
        yield return EliteGround(new Vector2(6, 2.05f)); yield return null;
        Check(map.PlayerMarker.anchoredPosition.x < markerX, "The regional map tracks the real player's position across the world");
        hud.ForceTouchControls = true; yield return null; yield return null;
        Check(map.gameObject.activeInHierarchy && map.transform.IsChildOf(hud.TouchControls.SafeContent),
            "Touch mode retains the regional map inside the safe-area layout");
        yield return Capture("search-touch-map", 1280, 720);
        yield return Capture("search-touch-map-portrait", 720, 1280, new Rect(0, 40, 720, 1200));
        hud.ForceTouchControls = false; yield return null; yield return null;
        Check(map.gameObject.activeInHierarchy && !map.transform.IsChildOf(hud.TouchControls.SafeContent),
            "Returning to desktop restores the map panel and its original placement");
        yield return SkillExtraction();
        level.StartRun(); yield return null; yield return EliteGround(new Vector2(6, 2.05f));
        Check(!ReferenceEquals(layout, hunt.SearchLayout) && hunt.State.ClueCount == 0 && hunt.SearchLayout.CollectedClues == 0 &&
            hunt.SearchLayout.CandidateCount == 4 && !map.BossMarkerVisible && !map.ExitMarkerVisible,
            "The next production deployment creates fresh clues, candidates and hidden destinations");
        yield return EliteGround(hunt.BossPosition); yield return Until(() => hunt.Boss, 3, "The next random boss is found without clues");
        SetAlertActor(hunt.Boss.transform, hunt.BossPosition);
        hunt.Boss.Actor.BeHit(new SkillHitData { attackValue = 1000 }); yield return null;
        Check(hunt.State.ClueCount == 0 && hunt.State.Stage == HuntStage.BountyAvailable && map.BossMarkerVisible && !map.ExitMarkerVisible,
            "Direct discovery and death of a random boss still work without collecting any clues");
        Check(!Enumerable.Range(0, hunt.ClueSocketCount).Any(i => hunt.IsClueAvailable(i)), "Early boss defeat closes leftover clue interactions");
        yield return SkillExtraction();
        hunt.UseRegionClues = false; hunt.RandomizeBossRegion = false; hunt.ResetMission();
        level.World.Map.UseCameraViewport = viewport; isolateEliteNpcs = false; RestoreFrozen();
        level.ItemLoot.enabled = itemLoot; level.SkillLoot.enabled = skillLoot;
    }
}

using System;
using BigWorld.Gameplay;

public static class HunterMissionExtractionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var mission = new HunterMission(3, 10, 250);
        check(!mission.BossDefeated && !mission.CarriedBountyAtExit, "New missions have neither a defeated boss nor a carried bounty result");
        check(!mission.TickExtraction(9, true, false) && mission.Stage == HuntStage.Tracking && mission.ExtractionElapsed == 9,
            "Tracking hunters may wait at the exit before investigating any clue");
        check(mission.TickExtraction(1, true, false) && mission.Stage == HuntStage.Extracted && mission.Reward == 0 &&
            !mission.BossDefeated && !mission.SecondWaveStarted && !mission.CarriedBountyAtExit,
            "Zero-clue extraction retains an explicit unkilled boss, no wave and zero bounty");
        check(!mission.DefeatBoss() && !mission.Investigate("late") && !mission.PickUpBounty() && mission.ClueCount == 0,
            "Completed departures cannot acquire late objective credit or a bounty");
        mission.DropBounty(); mission.InterruptExtraction();
        check(!mission.TickExtraction(100, true, false) && mission.Reward == 0 && mission.ExtractionElapsed == 10,
            "Completed zero-bounty results stay frozen for settlement retries");
        mission.Reset(); mission.Investigate("one");
        check(mission.TickExtraction(10, true, false) && mission.ClueCount == 1 && !mission.BossDefeated && mission.Reward == 0,
            "A partially investigated run may leave with its actual clue count and no boss credit");
        mission.Reset(); mission.Investigate("one"); mission.Investigate("two"); mission.Investigate("three");
        check(mission.Stage == HuntStage.Hunting && mission.TickExtraction(10, true, false) && !mission.BossDefeated,
            "Locating a boss without killing it never turns into boss experience after extraction");
        mission.Reset(); mission.DefeatBoss();
        check(mission.BossDefeated && !mission.SecondWaveStarted && mission.TickExtraction(10, true, false) && mission.Reward == 0 &&
            mission.BossDefeated && !mission.CarriedBountyAtExit, "An unclaimed boss drop gives true kill credit but no bounty currency or reinforcements");
        mission.Reset(); mission.DefeatBoss(); mission.PickUpBounty();
        check(mission.TickExtraction(10, true, false) && mission.BossDefeated && mission.CarriedBountyAtExit && mission.Reward == 250 && mission.SecondWaveStarted,
            "The original carried-bounty route still captures boss credit, reinforcements and the full reward");
        mission.Reset();
        check(mission.Stage == HuntStage.Tracking && mission.ClueCount == 0 && !mission.BossDefeated && !mission.CarriedBountyAtExit &&
            mission.Reward == 0 && mission.ExtractionElapsed == 0 && !mission.SecondWaveStarted, "Reset clears every previous objective and extraction result");
        mission.TickExtraction(4, true, false); mission.TickExtraction(1, false, false);
        check(mission.ExtractionElapsed == 0, "Leaving the exit clears progress for hunters without bounty too");
        mission.TickExtraction(4, true, false); mission.TickExtraction(1, true, true);
        check(mission.ExtractionElapsed == 0, "Enemy contest clears no-bounty extraction progress");
        mission.TickExtraction(4, true, false); mission.TickExtraction(0, false, true);
        check(mission.ExtractionElapsed == 4, "Paused mission time freezes both progress and interruption state");
        check(!mission.DropBounty() && mission.ExtractionElapsed == 0, "Player death interrupts a no-bounty attempt even with nothing to drop");
        mission.DefeatBoss(); mission.PickUpBounty(); mission.TickExtraction(4, true, false); mission.DropBounty();
        check(mission.ExtractionElapsed == 0 && mission.BossDefeated && mission.SecondWaveStarted && mission.Stage == HuntStage.BountyAvailable,
            "Dropping a carried bounty resets only the countdown, preserving boss credit and existing wave identity");
        check(mission.TickExtraction(10, true, false) && mission.Reward == 0 && !mission.CarriedBountyAtExit && mission.BossDefeated,
            "A survivor may leave without recovering the dropped token, earning no token reward");
        mission.Reset(); mission.DefeatBoss(); mission.TickExtraction(9, true, false); mission.PickUpBounty();
        check(mission.TickExtraction(1, true, false) && mission.Reward == 250, "A last-second pickup is captured at departure rather than at countdown start");
        var zeroBounty = new HunterMission(1, 1, 0); zeroBounty.DefeatBoss(); zeroBounty.PickUpBounty(); zeroBounty.TickExtraction(1, true, false);
        check(zeroBounty.CarriedBountyAtExit && zeroBounty.BossDefeated && zeroBounty.Reward == 0,
            "A zero-priced token remains a real carried bounty and does not erase the boss kill record");
    }
}

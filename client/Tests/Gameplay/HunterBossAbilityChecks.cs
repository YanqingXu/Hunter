using System;
using BigWorld.Gameplay;

public static class HunterBossAbilityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new HunterBossAbilityClock();
        check(!clock.IsWinding && clock.CooldownRemaining == 0 && clock.Tick(10, false, false) == HunterBossAbility.None,
            "An idle boss ability clock never invents an attack");
        check(!clock.TryBegin(HunterBossAbility.Slash, false, true) && !clock.TriggerRagePulse(true),
            "Paused normal and threshold requests cannot begin an ability");
        check(!clock.TryBegin(HunterBossAbility.None, false, false) && !clock.TryBegin((HunterBossAbility)9, false, false),
            "Unknown or empty abilities cannot enter a windup");
        foreach (HunterBossAbility ability in new[] { HunterBossAbility.Slash, HunterBossAbility.Bolt, HunterBossAbility.RagePulse, HunterBossAbility.Rush })
        {
            clock.Reset(); bool rage = HunterBossAbilityClock.IsRageAbility(ability);
            check(!clock.TryBegin(ability, !rage, false) && clock.TryBegin(ability, rage, false),
                "An ability can start only in its matching alert or rage group: " + ability);
            check(clock.IsWinding && clock.Active == ability && !clock.TryBegin(ability, rage, false),
                "A pending windup cannot be restarted or overlapped: " + ability);
            float windup = clock.WindupRemaining;
            check(clock.Tick(windup * .5f, rage, false) == HunterBossAbility.None && clock.WindupRemaining == windup * .5f,
                "A telegraph waits for the complete configured windup: " + ability);
            check(clock.Tick(10, rage, true) == HunterBossAbility.None && clock.WindupRemaining == windup * .5f,
                "Pause freezes a pending telegraph: " + ability);
            check(clock.Tick(windup * .5f, rage, false) == ability && !clock.IsWinding && clock.CooldownRemaining == HunterBossAbilityClock.Recovery(ability),
                "The exact windup deadline claims one effect and begins a full recovery: " + ability);
            check(clock.Tick(0, rage, false) == HunterBossAbility.None && !clock.TryBegin(ability, rage, false),
                "A completed attack cannot repeat or bypass recovery: " + ability);
            float recovery = clock.CooldownRemaining;
            clock.Tick(100, rage, true);
            check(clock.CooldownRemaining == recovery, "Pause freezes recovery as well as windup: " + ability);
            clock.Tick(recovery, rage, false);
            check(clock.CooldownRemaining == 0 && clock.TryBegin(ability, rage, false),
                "The next cast is admitted at the exact recovery deadline: " + ability);
        }
        clock.Reset(); clock.TryBegin(HunterBossAbility.Bolt, false, false); clock.Tick(.25f, false, false);
        check(clock.TriggerRagePulse(false) && clock.Active == HunterBossAbility.RagePulse && clock.WindupRemaining == .65f,
            "A newly crossed rage threshold replaces an in-progress normal projectile telegraph");
        check(clock.Tick(.65f, true, false) == HunterBossAbility.RagePulse,
            "The interrupted normal ability cannot leak out with the threshold pulse");
        check(clock.TriggerRagePulse(false) && clock.CooldownRemaining == 0,
            "A second newly crossed threshold also interrupts an existing recovery");
        check(clock.Tick(.65f, false, false) == HunterBossAbility.None && !clock.IsWinding,
            "Rage expiry on the same frame as a pulse deadline cancels that rage effect");
        clock.TryBegin(HunterBossAbility.Slash, false, false);
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(clock.Tick(invalid, false, false) == HunterBossAbility.None && clock.Active == HunterBossAbility.Slash && clock.WindupRemaining == .5f,
                "Invalid simulation time cannot complete or corrupt an ability: " + invalid);
        check(clock.Tick(.5f, true, false) == HunterBossAbility.None && !clock.IsWinding,
            "Entering the rage group cannot finish an old normal attack on the transition frame");
        clock.TryBegin(HunterBossAbility.Rush, true, false); clock.Cancel();
        check(clock.Tick(10, true, false) == HunterBossAbility.None && !clock.IsWinding,
            "Death, unloading or lost-target cancellation discards the old telegraph");
        clock.TryBegin(HunterBossAbility.Slash, false, false); clock.Tick(.5f, false, false); clock.Cancel();
        check(clock.CooldownRemaining == 1.2f, "Cancelling input cannot erase a completed ability's recovery");
        clock.Reset(); check(clock.CooldownRemaining == 0 && !clock.IsWinding, "An explicit new-run reset removes old timers");
        clock.TryBegin(HunterBossAbility.Bolt, false, false);
        check(clock.Tick(float.MaxValue, false, false) == HunterBossAbility.Bolt && clock.CooldownRemaining == 1.6f,
            "A huge finite frame emits one effect without also consuming its new recovery");
        bool invalidConfig = false;
        try { HunterBossAbilityClock.Windup((HunterBossAbility)9); } catch (ArgumentOutOfRangeException) { invalidConfig = true; }
        check(invalidConfig, "Unknown ability timing is rejected instead of treated as instant");
    }
}

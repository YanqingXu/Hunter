using System;
using BigWorld.Gameplay;

public static class HunterEliteChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(HunterEliteRules.CanBasicAttack(HunterEliteKind.Armored) && !HunterEliteRules.CanBasicAttack(HunterEliteKind.Summoner),
            "The two elite archetypes have distinct basic-attack permission");
        check(HunterEliteRules.IncomingDamage(HunterEliteKind.Armored, 25) == 12.5f && HunterEliteRules.IncomingDamage(HunterEliteKind.Armored, 60) == 30,
            "Armor halves both fractional bullet damage and explosive damage without integer rounding");
        check(HunterEliteRules.IncomingDamage(HunterEliteKind.Summoner, 25) == 25,
            "The summoning archetype does not inherit the armored archetype's protection");
        foreach (float invalid in new[] { 0f, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(HunterEliteRules.IncomingDamage(HunterEliteKind.Armored, invalid) == 0 && HunterEliteRules.IncomingDamage(HunterEliteKind.Summoner, invalid) == 0,
                "Invalid incoming damage is rejected rather than healed or amplified: " + invalid);
        check(HunterEliteRules.IncomingDamage(HunterEliteKind.Armored, float.MaxValue) == float.MaxValue * .5f,
            "Large finite armor damage remains finite");
        bool invalidKind = false;
        try { HunterEliteRules.CanBasicAttack((HunterEliteKind)9); } catch (ArgumentOutOfRangeException) { invalidKind = true; }
        check(invalidKind, "An unknown elite kind cannot silently acquire basic attacks");
        invalidKind = false;
        try { HunterEliteRules.IncomingDamage((HunterEliteKind)9, 30); } catch (ArgumentOutOfRangeException) { invalidKind = true; }
        check(invalidKind, "An unknown elite kind cannot silently select damage mitigation");

        var cycle = new HunterSummonCycle(3, 6);
        check(cycle.State == HunterSummonState.Ready && !cycle.Tick(100, false, false, false),
            "An idle summoner never creates a summon from timer updates alone");
        check(!cycle.TrySummon(true) && cycle.State == HunterSummonState.Ready, "Pause rejects an attempted summon");
        check(cycle.TrySummon(false) && !cycle.TrySummon(false) && cycle.State == HunterSummonState.Active,
            "One summoner can own only one active summon at a time");
        check(!cycle.Tick(2, false, false, false) && cycle.UnseenSeconds == 2, "Leaving both detection zones begins the abandonment delay");
        check(!cycle.Tick(50, false, false, true) && cycle.UnseenSeconds == 2, "Pause or unloaded terrain freezes abandonment time");
        check(!cycle.Tick(1, true, false, true) && cycle.UnseenSeconds == 2, "Paused detection changes cannot reset the retained delay");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!cycle.Tick(invalid, false, false, false) && cycle.UnseenSeconds == 2, "Invalid frame time preserves summon state: " + invalid);
        check(!cycle.Tick(.5f, true, false, false) && cycle.UnseenSeconds == 0, "Returning to the owner's zone cancels abandonment");
        cycle.Tick(2, false, false, false);
        check(!cycle.Tick(.5f, false, true, false) && cycle.UnseenSeconds == 0, "Returning only to the summon's zone also cancels abandonment");
        check(!cycle.Tick(2.5f, false, false, false) && cycle.State == HunterSummonState.Active, "Leaving again requires a fresh full abandonment interval");
        check(cycle.Tick(.5f, false, false, false) && cycle.State == HunterSummonState.Cooldown && cycle.CooldownRemaining == 6,
            "The exact abandonment deadline emits one removal and starts the full skill cooldown");
        check(!cycle.SummonDied() && !cycle.TrySummon(false) && !cycle.Tick(0, false, false, false),
            "Delayed death callbacks cannot repeat removal, restart cooldown or create another summon");
        check(!cycle.Tick(100, false, false, true) && cycle.CooldownRemaining == 6, "Pause also freezes the skill cooldown");
        cycle.Tick(5, true, true, false);
        check(cycle.CooldownRemaining == 1 && !cycle.TrySummon(false), "Enemy detection does not bypass a running cooldown");
        cycle.Tick(1, false, false, false);
        check(cycle.State == HunterSummonState.Ready && cycle.TrySummon(false), "A new summon can be explicitly created at the exact cooldown deadline");
        check(cycle.SummonDied() && cycle.State == HunterSummonState.Cooldown && cycle.CooldownRemaining == 6,
            "Killing the independent summon starts cooldown without waiting for abandonment");
        cycle.Tick(1, false, false, false);
        check(!cycle.SummonDied() && cycle.CooldownRemaining == 5, "Duplicate summon-death callbacks cannot extend cooldown");
        check(!cycle.OwnerDied() && cycle.State == HunterSummonState.Disabled && cycle.CooldownRemaining == 0,
            "Owner death while cooling down permanently disables the cycle");
        check(!cycle.TrySummon(false) && !cycle.Tick(100, false, false, false) && !cycle.SummonDied(),
            "A dead owner cannot be reactivated by time, detection or old summon callbacks");
        cycle.Reset(); cycle.TrySummon(false);
        check(cycle.OwnerDied() && !cycle.OwnerDied() && cycle.State == HunterSummonState.Disabled,
            "Owner death requests cleanup of an active summon exactly once");
        cycle.Reset(); check(cycle.State == HunterSummonState.Ready && cycle.UnseenSeconds == 0 && cycle.CooldownRemaining == 0,
            "Only an explicit new-run reset clears the disabled state and timers");
        var immediate = new HunterSummonCycle(0, 0); immediate.TrySummon(false);
        check(immediate.Tick(0, false, false, false) && immediate.State == HunterSummonState.Ready && !immediate.Tick(0, false, false, false),
            "Zero configured durations still emit only one removal per summon");
        var huge = new HunterSummonCycle(float.MaxValue, float.MaxValue); huge.TrySummon(false); huge.Tick(float.MaxValue * .75f, false, false, false);
        check(huge.Tick(float.MaxValue, false, false, false) && huge.CooldownRemaining == float.MaxValue,
            "A large finite abandonment frame cannot overflow or consume the new cooldown in the same tick");
        huge.Tick(float.MaxValue, false, false, false); check(huge.State == HunterSummonState.Ready, "A large cooldown completes without underflow");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool abandonRejected = false, cooldownRejected = false;
            try { new HunterSummonCycle(invalid, 6); } catch (ArgumentOutOfRangeException) { abandonRejected = true; }
            try { new HunterSummonCycle(3, invalid); } catch (ArgumentOutOfRangeException) { cooldownRejected = true; }
            check(abandonRejected && cooldownRejected, "Invalid configured summon durations are rejected: " + invalid);
        }
    }
}

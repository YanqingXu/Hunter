using System;
using BigWorld.Gameplay;

public static class HunterAlertMemoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { new HunterAlertMemory(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "An invalid alert forgetting delay is rejected: " + invalid);
        }
        var alert = new HunterAlertMemory(2);
        check(!alert.IsAlerted && alert.UnseenSeconds == 0 && alert.ForgetDelay == 2, "An alert memory starts idle with its validated delay");
        check(!alert.Tick(false, 1000, false) && alert.UnseenSeconds == 0, "Unseen idle time cannot invent a remembered target");
        check(!alert.Tick(true, 1, true), "A paused memory does not acquire a new target");
        check(alert.Tick(true, 0, false) && alert.UnseenSeconds == 0, "Visible acquisition does not need positive elapsed time");
        check(alert.Tick(false, .25f, false) && alert.UnseenSeconds == .25f, "Losing visibility briefly retains alert state");
        check(alert.Tick(false, 3, true) && alert.UnseenSeconds == .25f, "Pause or unload freezes the retained alert countdown");
        check(alert.Tick(true, 3, true) && alert.UnseenSeconds == .25f, "Paused input cannot reset the retained timer either");
        check(alert.Tick(true, 0, false) && alert.UnseenSeconds == 0, "Seeing the target again cancels elapsed forgetting time");
        alert.Tick(false, .5f, false);
        foreach (float invalid in new[] { -.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(alert.Tick(false, invalid, false) && alert.UnseenSeconds == .5f, "Invalid elapsed time leaves an existing alert unchanged: " + invalid);
        check(alert.Tick(false, 0, false) && alert.UnseenSeconds == .5f, "A zero-time unseen update does not prematurely forget");
        check(!alert.Tick(false, 1.5f, false) && alert.UnseenSeconds == 0, "The exact forgetting boundary clears alert and its timer");
        check(!alert.Tick(false, .5f, false), "Further unseen updates keep an expired memory idle");
        check(alert.Tick(true, .1f, false) && alert.UnseenSeconds == 0, "A forgotten target can be acquired again normally");
        alert.Tick(false, .75f, false); alert.Tick(true, .1f, false);
        check(alert.Tick(false, 1.5f, false), "Reacquisition grants a complete new forgetting period");
        alert.Forget();
        check(!alert.IsAlerted && alert.UnseenSeconds == 0, "Revival suppression or death explicitly clears retained alert immediately");
        alert.Forget(); check(!alert.IsAlerted, "Repeated lifecycle cleanup cannot restore alert");
        alert.Tick(true, 0, false);
        check(!alert.Tick(false, float.MaxValue, false), "A large finite update expires alert once without overflow");
        var immediate = new HunterAlertMemory(0);
        check(immediate.Tick(true, 0, false), "A zero-delay configuration still detects a visible target");
        check(!immediate.Tick(false, 0, false), "A zero-delay configuration forgets at the first loss of sight");
    }
}

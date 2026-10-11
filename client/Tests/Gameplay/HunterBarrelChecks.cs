using System;
using BigWorld.Gameplay;

public static class HunterBarrelChecks
{
    public static void Run(Action<bool, string> check)
    {
        var fuse = new HunterBarrelFuse(2);
        check(!fuse.IsArmed && !fuse.HasExploded && fuse.Progress == 0 && !fuse.Tick(10, false),
            "An untouched barrel does not begin a fuse by receiving frame time");
        check(!fuse.Hit(HunterBarrelHit.Bullet, true) && !fuse.Hit(HunterBarrelHit.Explosion, true) && !fuse.IsArmed,
            "Paused hit requests neither arm nor detonate a barrel");
        check(!fuse.Hit((HunterBarrelHit)42, false) && !fuse.IsArmed, "Unknown hit types leave an untouched barrel unchanged");
        check(!fuse.Hit(HunterBarrelHit.Bullet, false) && fuse.IsArmed && !fuse.HasExploded && fuse.Elapsed == 0,
            "A bullet starts the configured delay instead of exploding immediately");
        check(!fuse.Tick(.5f, false) && fuse.Elapsed == .5f && fuse.Progress == .25f,
            "The fuse exposes partial progress before the delayed explosion");
        check(!fuse.Hit(HunterBarrelHit.Bullet, false) && fuse.Elapsed == .5f && fuse.IsArmed,
            "Repeated bullets cannot restart or extend an already burning fuse");
        check(!fuse.Tick(20, true) && fuse.Elapsed == .5f && !fuse.Hit(HunterBarrelHit.Explosion, true),
            "Pausing freezes an armed barrel and discards a blocked explosion request");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!fuse.Tick(invalid, false) && fuse.Elapsed == .5f && fuse.IsArmed,
                "Invalid frame time cannot detonate or corrupt the armed fuse: " + invalid);
        check(!fuse.Tick(0, false) && fuse.Elapsed == .5f, "A zero-time frame leaves a positive fuse unchanged");
        check(fuse.Tick(1.5f, false) && fuse.HasExploded && !fuse.IsArmed && fuse.Progress == 1,
            "The exact deadline commits the exploded state before returning one detonation");
        check(!fuse.Tick(10, false) && !fuse.Hit(HunterBarrelHit.Bullet, false) && !fuse.Hit(HunterBarrelHit.Explosion, false),
            "An exploded barrel cannot emit another blast through ticks, bullets or a returning chain reaction");
        fuse.Reset();
        check(!fuse.IsArmed && !fuse.HasExploded && fuse.Elapsed == 0 && fuse.Delay == 2,
            "An explicit new-run reset restores an untouched barrel while preserving its configured delay");
        check(fuse.Hit(HunterBarrelHit.Explosion, false) && fuse.HasExploded && !fuse.IsArmed,
            "An explosion detonates an untouched barrel immediately without waiting for its bullet delay");
        fuse.Reset(); fuse.Hit(HunterBarrelHit.Bullet, false); fuse.Tick(.25f, false);
        check(fuse.Hit(HunterBarrelHit.Explosion, false) && !fuse.Tick(2, false),
            "An explosion also interrupts a burning fuse immediately and suppresses its later scheduled blast");
        fuse.Reset(); fuse.Hit(HunterBarrelHit.Bullet, false); fuse.Tick(1, false); fuse.Reset();
        check(!fuse.Tick(5, false) && !fuse.IsArmed && !fuse.HasExploded, "Resetting a pending fuse cancels only that old run's schedule");
        var instant = new HunterBarrelFuse(0);
        check(instant.Hit(HunterBarrelHit.Bullet, false) && instant.HasExploded && !instant.Tick(0, false),
            "An explicitly zero configured delay still emits exactly one bullet-triggered blast");
        var huge = new HunterBarrelFuse(float.MaxValue);
        huge.Hit(HunterBarrelHit.Bullet, false); huge.Tick(float.MaxValue * .75f, false);
        check(huge.Tick(float.MaxValue, false) && huge.Elapsed == float.MaxValue,
            "A large finite accumulated frame cannot overflow the fuse or leave it armed forever");
        var tiny = new HunterBarrelFuse(float.Epsilon); tiny.Hit(HunterBarrelHit.Bullet, false);
        check(tiny.Tick(float.Epsilon, false), "A tiny positive fuse remains valid and reaches its deadline");
        var a = new HunterBarrelFuse(2); var b = new HunterBarrelFuse(2); var c = new HunterBarrelFuse(2);
        int detonations = 0;
        if (a.Hit(HunterBarrelHit.Explosion, false))
        {
            detonations++;
            if (b.Hit(HunterBarrelHit.Explosion, false))
            {
                detonations++;
                if (a.Hit(HunterBarrelHit.Explosion, false)) detonations++;
                if (c.Hit(HunterBarrelHit.Explosion, false)) detonations++;
            }
            if (c.Hit(HunterBarrelHit.Explosion, false)) detonations++;
        }
        check(detonations == 3 && a.HasExploded && b.HasExploded && c.HasExploded,
            "A cyclic and branching callback graph claims each barrel once before propagating its neighbors");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { new HunterBarrelFuse(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Invalid configured fuse delay is rejected: " + invalid);
        }
    }
}

using System;
using BigWorld.Gameplay;

public static class HunterRoundChecks
{
    public static void Run(Action<bool, string> check)
    {
        var round = new HunterRoundDefinition { Pellets = 5, Damage = 8, SpreadAngle = 20, Distance = 9,
            PenetrationPower = 2, DamageRetainedPerProp = .5f, PenetrablePropKinds = new[] { 1 } };
        var rifle = new HunterGunDefinition { DefaultRound = round };
        var pistol = new HunterGunDefinition { Name = "Pistol", Weight = 1, Damage = 15 };
        var weapons = new HunterWeapons(new[] { rifle, pistol }, new[] { new HunterAmmoDefinition() }, 3);
        check(weapons.Round.Pellets == 5 && weapons.Damage == 8 && weapons.Round.Distance == 9 && weapons.Round.SpreadAngle == 20,
            "A default cartridge supplies per-pellet damage, pellet count, spread and range independently of legacy gun damage");
        round.Damage = 100; round.PenetrablePropKinds[0] = 9;
        check(weapons.Damage == 8 && weapons.Round.PenetrablePropKinds[0] == 1,
            "A live loadout snapshots nested cartridge settings and its prop whitelist");
        var exposed = weapons.Round; exposed.Pellets = 64; exposed.PenetrablePropKinds[0] = 99;
        check(weapons.Round.Pellets == 5 && weapons.Round.PenetrablePropKinds[0] == 1,
            "Reading a cartridge returns an independent copy that cannot change later shots");
        var shot = weapons.Round; int total = weapons.Total(HunterAmmoType.Standard);
        check(weapons.TryFire() && weapons.Total(HunterAmmoType.Standard) == total - 1 && shot.Pellets == 5,
            "One multi-pellet trigger pull consumes exactly one cartridge from the inclusive ammunition total");
        var modified = weapons.WithTimingMultipliers(.8f, .75f);
        check(modified.Round.Pellets == 5 && modified.Damage == 8 && modified.Round.PenetrationPower == 2 && modified.Magazine == weapons.Magazine,
            "Recomputing skill timings preserves cartridge configuration and spent ammunition");
        weapons.TrySwitch(1);
        check(weapons.Damage == 15 && weapons.Round.Pellets == 1 && weapons.Round.PenetrationPower == 0 && shot.Damage == 8,
            "A legacy gun gets its own nonpenetrating single-pellet default while an earlier shot keeps its snapshot");
        check(weapons.Round.Distance == 12 && weapons.Round.SpreadAngle == 0 && weapons.Round.PenetrablePropKinds.Length == 0,
            "Missing legacy cartridge settings preserve the existing 12-unit straight shot");
        var penetration = new HunterPenetration(shot.CreatePenetration()); penetration.TryPass("wood", 1);
        check(penetration.Damage == 4 && shot.Damage == 8 && weapons.Damage == 15,
            "A pellet's prop traversal changes only its own damage, not the cartridge or another weapon");
        var defaultRifle = HunterRoundDefinition.Rifle(); var rifleState = new HunterPenetration(defaultRifle.CreatePenetration());
        rifleState.TryPass("a", 1); rifleState.TryPass("b", 1);
        check(defaultRifle.Pellets == 1 && defaultRifle.Damage == 25 && rifleState.Damage == 14.0625f && !rifleState.TryPass("c", 1),
            "The authored rifle profile permits two wood props with successive 25 percent damage reduction");
        bool invalid = true;
        foreach (int value in new[] { -1, 0, 65, int.MaxValue })
            invalid &= Throws(() => new HunterRoundDefinition { Pellets = value }.Snapshot());
        check(invalid, "Cartridge pellet count respects the existing engine's one-to-64 launch bound");
        invalid = true;
        foreach (float value in new[] { -.1f, 180.1f, float.NaN, float.PositiveInfinity })
            invalid &= Throws(() => new HunterRoundDefinition { SpreadAngle = value }.Snapshot());
        check(invalid, "Invalid and nonfinite spread angles cannot enter a loadout");
        invalid = true;
        foreach (float value in new[] { -1, 0, float.NaN, float.PositiveInfinity })
            invalid &= Throws(() => new HunterRoundDefinition { Distance = value }.Snapshot());
        check(invalid, "Invalid and nonfinite cartridge ranges cannot enter a loadout");
        var bad = new HunterGunDefinition { DefaultRound = new HunterRoundDefinition { Damage = 0 } };
        check(Throws(() => new HunterWeapons(new[] { bad }, new[] { new HunterAmmoDefinition() }, 3)),
            "A malformed default cartridge is rejected during loadout construction before any shot can spend ammo");
        var edge = new HunterRoundDefinition { Pellets = 64, SpreadAngle = 180 }.Snapshot();
        check(edge.Pellets == 64 && edge.SpreadAngle == 180, "The supported maximum pellet count and spread are accepted without clamping");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

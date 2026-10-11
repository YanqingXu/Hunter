using System;
using BigWorld.Gameplay;

public static class HunterGunModifierChecks
{
    public static void Run(Action<bool, string> check)
    {
        var plain = new HunterGunModifiers();
        check(plain.Price(150) == 150 && plain.Weight(2) == 2 && plain.AmmoCapacity(72, 14) == 72 &&
            plain.MeleeDamage(25) == 25 && plain.FireInterval(.8f) == .8f && plain.ReloadSeconds(2) == 2,
            "Identity modifiers preserve all six supported authored gun attributes");
        var modified = new HunterGunModifiers(.8m, .5m, 1.25m, 1.2f, .8f, .75f);
        check(modified.Price(150) == 120 && modified.Price(75) == 60, "Discounts use exact decimal arithmetic instead of floating-point overcharging");
        check(new HunterGunModifiers(price: .5m).Price(75) == 38, "Fractional prices round upward to a whole currency unit");
        check(new HunterGunModifiers(price: 0m).Price(150) == 0 && plain.Price(0) == 0, "An explicit free-price configuration and an authored free price remain representable");
        check(modified.Weight(2) == 1 && modified.Weight(1) == 1, "Weight reductions preserve a minimum positive whole unit for every gun");
        check(new HunterGunModifiers(weight: 1.25m).Weight(2) == 3, "Fractional weapon weight rounds upward rather than bypassing carrying limits");
        check(modified.AmmoCapacity(72, 14) == 90 && modified.AmmoCapacity(73, 14) == 92, "Ammo-capacity modifiers produce deterministic whole totals per pool");
        check(Math.Abs(modified.MeleeDamage(25) - 30) < .0001f && Math.Abs(modified.FireInterval(.8f) - .64f) < .0001f &&
            modified.ReloadSeconds(2) == 1.5f, "Melee damage, firing interval and reload duration receive independent validated multipliers");
        check(modified.Price(150) == 120 && modified.Weight(2) == 1 && modified.AmmoCapacity(72, 14) == 90,
            "Repeated queries from authored values do not compound the previous modification");
        check(plain.Price(150) == 150 && plain.AmmoCapacity(72, 14) == 72,
            "A different hunter's unmodified profile never inherits another profile's changes");
        check(Throws(() => modified.Price(-1)) && Throws(() => modified.Weight(0)) && Throws(() => modified.Weight(-1)),
            "Invalid authored price and weapon weight are rejected");
        check(Throws(() => modified.AmmoCapacity(0, 0)) && Throws(() => modified.AmmoCapacity(-1, 0)) && Throws(() => modified.AmmoCapacity(72, -1)),
            "Invalid ammo totals and negative fixed-magazine capacity are rejected");
        check(plain.AmmoCapacity(14, 14) == 14 && Throws(() => new HunterGunModifiers(ammoCapacity: .1m).AmmoCapacity(72, 14)),
            "An exact magazine-only capacity is legal but a reduced capacity cannot discard fixed magazine space");
        check(Throws(() => new HunterGunModifiers(price: -1m)) && Throws(() => new HunterGunModifiers(weight: 0m)) &&
            Throws(() => new HunterGunModifiers(weight: -1m)) && Throws(() => new HunterGunModifiers(ammoCapacity: 0m)),
            "Negative prices or nonpositive integral-capacity multipliers are rejected");
        bool invalid = true;
        foreach (float value in new[] { 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            invalid &= Throws(() => new HunterGunModifiers(meleeDamage: value)) && Throws(() => new HunterGunModifiers(fireInterval: value)) &&
                Throws(() => new HunterGunModifiers(reload: value)) && Throws(() => plain.MeleeDamage(value)) &&
                Throws(() => plain.FireInterval(value)) && Throws(() => plain.ReloadSeconds(value));
        check(invalid, "All invalid authored scalar values and multipliers are rejected before use");
        check(plain.Price(int.MaxValue) == int.MaxValue && plain.Weight(int.MaxValue) == int.MaxValue &&
            plain.AmmoCapacity(int.MaxValue, int.MaxValue) == int.MaxValue, "Largest valid integral values do not overflow under identity modification");
        check(Throws(() => new HunterGunModifiers(price: 2m).Price(int.MaxValue)) && Throws(() => new HunterGunModifiers(weight: 2m).Weight(int.MaxValue)) &&
            Throws(() => new HunterGunModifiers(ammoCapacity: 2m).AmmoCapacity(int.MaxValue, 0)), "Integral overflow is rejected instead of wrapping to cheap or weightless guns");
        check(Throws(() => new HunterGunModifiers(price: decimal.MaxValue).Price(2)) &&
            Throws(() => new HunterGunModifiers(weight: decimal.MaxValue).Weight(2)), "Decimal multiplication overflow is normalized to a rejected configuration");
        check(Throws(() => new HunterGunModifiers(meleeDamage: float.MaxValue).MeleeDamage(float.MaxValue)) &&
            Throws(() => new HunterGunModifiers(reload: float.MaxValue).ReloadSeconds(float.MaxValue)), "Floating overflow cannot create infinite damage or a permanently unfinished reload");
        check(Throws(() => new HunterGunModifiers(fireInterval: float.Epsilon).FireInterval(float.Epsilon)),
            "Floating underflow cannot create a zero-interval fire-rate bypass");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

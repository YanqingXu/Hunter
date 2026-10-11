using System;
using BigWorld.Gameplay;

public static class HunterPenetrationChecks
{
    public static void Run(Action<bool, string> check)
    {
        int[] kinds = { 1, 7 };
        var definition = new HunterPenetrationDefinition(80, 2, .75f, kinds);
        kinds[0] = 99;
        check(definition.Allows(1) && definition.Allows(7) && !definition.Allows(99) && !definition.Allows(0),
            "Ammunition snapshots its explicit prop whitelist without granting ordinary terrain penetration");
        var pellet = new HunterPenetration(definition);
        check(pellet.PassedCount == 0 && pellet.Remaining == 2 && pellet.Damage == 80, "Each pellet begins with its full damage and penetration budget");
        check(!pellet.TryPass("wall", 0) && !pellet.TryPass("other-prop", 2) && pellet.PassedCount == 0 && pellet.Damage == 80,
            "Unlisted props and ordinary walls cannot consume penetration budget or reduce damage");
        check(!pellet.TryPass(null, 1) && !pellet.TryPass("", 1) && !pellet.TryPass("  ", 1) && pellet.PassedCount == 0,
            "A collision without a logical prop identity cannot enter the penetration ledger");
        check(pellet.TryPass("cover-a", 1) && pellet.PassedCount == 1 && pellet.Remaining == 1 && pellet.Damage == 60,
            "The first eligible prop consumes one penetration and reduces damage exactly once");
        check(pellet.TryPass("cover-a", 1) && pellet.TryPass("cover-a", 1) && pellet.PassedCount == 1 && pellet.Damage == 60,
            "Compound colliders and later overlap sweeps of the same prop do not consume power or repeatedly reduce damage");
        check(!pellet.TryPass("cover-a", 7) && pellet.PassedCount == 1 && pellet.Damage == 60,
            "Reusing one identity with a different prop kind is rejected without modifying prior traversal");
        check(pellet.TryPass("cover-b", 7) && pellet.Remaining == 0 && pellet.Damage == 45,
            "A second distinct eligible prop applies the second multiplicative reduction");
        check(!pellet.TryPass("cover-c", 1) && pellet.PassedCount == 2 && pellet.Damage == 45,
            "The next prop beyond configured power blocks without a third damage reduction");
        check(pellet.TryPass("cover-b", 7) && pellet.TryPass("cover-a", 1) && pellet.Remaining == 0 && pellet.Damage == 45,
            "Exiting already traversed compound props remains possible after the budget reaches zero");
        var second = new HunterPenetration(definition);
        check(second.Damage == 80 && second.TryPass("cover-a", 1) && second.Damage == 60 && pellet.Damage == 45,
            "Separate pellets sharing ammunition settings have independent prop ledgers and damage");
        var fresh = new HunterPenetration(definition);
        check(fresh.TryPass("cover-a:new-generation", 1) && fresh.TryPass("cover-a", 1) && fresh.PassedCount == 2,
            "A recycled scene object with a new logical prop identity is counted separately");
        var zero = new HunterPenetration(new HunterPenetrationDefinition(15, 0, .5f, new[] { 1 }));
        check(!zero.TryPass("cover", 1) && zero.Damage == 15, "Zero-power default ammunition cannot pass even a listed prop");
        var empty = new HunterPenetration(new HunterPenetrationDefinition(15, 2, .5f, new int[0]));
        check(!empty.TryPass("cover", 1) && empty.Remaining == 2, "An empty whitelist does not silently allow all props");
        var full = new HunterPenetration(new HunterPenetrationDefinition(15, 1, 1, new[] { 1 }));
        check(full.TryPass("cover", 1) && full.Damage == 15 && full.Remaining == 0, "Full retention consumes power without inventing damage loss");
        var absorbed = new HunterPenetration(new HunterPenetrationDefinition(15, 1, 0, new[] { 1 }));
        check(absorbed.TryPass("cover", 1) && absorbed.Damage == 0 && absorbed.Remaining == 0, "Zero retention is supported without negative damage");
        bool invalidDamage = true;
        foreach (float value in new[] { -1, 0, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            invalidDamage &= Throws(() => new HunterPenetrationDefinition(value, 1, .5f, new[] { 1 }));
        check(invalidDamage, "Nonpositive and nonfinite initial damage is rejected");
        bool invalidRetention = true;
        foreach (float value in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            invalidRetention &= Throws(() => new HunterPenetrationDefinition(10, 1, value, new[] { 1 }));
        check(invalidRetention, "Damage retention rejects increases, negatives and nonfinite values");
        check(Throws(() => new HunterPenetrationDefinition(10, -1, .5f, new[] { 1 })) &&
            Throws(() => new HunterPenetrationDefinition(10, 1001, .5f, new[] { 1 })), "Out-of-range penetration budgets are rejected");
        check(Throws(() => new HunterPenetrationDefinition(10, 1, .5f, null)) &&
            Throws(() => new HunterPenetrationDefinition(10, 1, .5f, new[] { 0 })) &&
            Throws(() => new HunterPenetrationDefinition(10, 1, .5f, new[] { -1 })) &&
            Throws(() => new HunterPenetrationDefinition(10, 1, .5f, new[] { 1, 1 })), "Missing or ambiguous prop whitelists are rejected");
        check(Throws(() => new HunterPenetration(null)), "A projectile cannot be initialized without a valid ammunition definition");
        var many = new HunterPenetration(new HunterPenetrationDefinition(float.MaxValue, 1000, .5f, new[] { 1 }));
        bool monotonic = true; float previous = many.Damage;
        for (int i = 0; i < 1000; i++)
        {
            monotonic &= many.TryPass("prop-" + i, 1) && many.Damage >= 0 && many.Damage <= previous && !float.IsNaN(many.Damage);
            previous = many.Damage;
        }
        check(monotonic && many.PassedCount == 1000 && !many.TryPass("last", 1),
            "The configured maximum remains bounded and monotonic even through float underflow");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
}

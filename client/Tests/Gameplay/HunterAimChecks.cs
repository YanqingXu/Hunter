using System;
using BigWorld.Gameplay;

public static class HunterAimChecks
{
    public static void Run(Action<bool, string> check)
    {
        var aim = new HunterAim();
        check(aim.X == 1 && aim.Y == 0 && aim.Facing == 1, "Aim starts facing right without changing movement");
        foreach (var input in new[] { new[] { 1f, 0f }, new[] { 1f, 1f }, new[] { 0f, 1f }, new[] { -1f, 1f },
            new[] { -1f, 0f }, new[] { -1f, -1f }, new[] { 0f, -1f }, new[] { 1f, -1f } })
        {
            int facing = aim.Facing;
            check(aim.TrySet(input[0], input[1]) && Math.Abs(aim.X * aim.X + aim.Y * aim.Y - 1) < .00001f &&
                Math.Sign(aim.X) == Math.Sign(input[0]) && Math.Sign(aim.Y) == Math.Sign(input[1]) &&
                aim.Facing == (input[0] == 0 ? facing : input[0] < 0 ? -1 : 1),
                "Standing aim normalizes direction and retains facing on vertical input: " + input[0] + "," + input[1]);
        }
        aim.TrySet(-3, 4); float x = aim.X, y = aim.Y;
        check(Math.Abs(x + .6f) < .00001f && Math.Abs(y - .8f) < .00001f && aim.Facing == -1,
            "Analog aim preserves the input angle without restricting input to eight directions");
        check(!aim.TrySet(0, 0) && !aim.TrySet(.1f, .1f) && !aim.TrySet(.15f, 0) &&
            aim.X == x && aim.Y == y && aim.Facing == -1, "Releasing the stick or staying inside its radial dead zone preserves the last aim");
        check(aim.TrySet(.151f, 0) && aim.X == 1 && aim.Y == 0 && aim.Facing == 1,
            "Input immediately outside the dead zone turns and normalizes normally");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(!aim.TrySet(invalid, 1) && !aim.TrySet(-1, invalid) && aim.X == 1 && aim.Y == 0 && aim.Facing == 1,
                "Non-finite aim input cannot corrupt or rotate the last valid direction: " + invalid);
        check(aim.TrySet(float.MaxValue, float.MaxValue) && Math.Abs(aim.X - .7071068f) < .00001f &&
            Math.Abs(aim.Y - .7071068f) < .00001f, "Large finite inputs normalize without float overflow");
        aim.Reset(-1);
        check(aim.X == -1 && aim.Y == 0 && aim.Facing == -1, "Deployment reset can restore a horizontal left-facing baseline");
        check(aim.TrySet(0, -1) && aim.Facing == -1 && aim.X == 0 && aim.Y == -1,
            "Standing aim supports a downward shot without changing left-facing orientation");
        aim.Reset();
        check(aim.X == 1 && aim.Y == 0 && aim.Facing == 1, "A new run clears the previous aim direction");
        check(Throws(() => new HunterAim(-1)) && Throws(() => new HunterAim(1)) && Throws(() => new HunterAim(float.NaN)) &&
            Throws(() => new HunterAim(float.PositiveInfinity)), "Invalid dead-zone configurations are rejected");
        var precise = new HunterAim(0);
        check(!precise.TrySet(0, 0) && precise.TrySet(float.Epsilon, 0) && precise.X == 1 && precise.Y == 0,
            "An explicitly zero dead zone accepts small finite input but never normalizes a zero vector");
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
}

using System;

namespace BigWorld.Gameplay
{
    public enum HunterEliteKind { Armored, Summoner }
    public enum HunterSummonState { Ready, Active, Cooldown, Disabled }

    public static class HunterEliteRules
    {
        public static bool CanBasicAttack(HunterEliteKind kind)
        { Validate(kind); return kind == HunterEliteKind.Armored; }
        public static float IncomingDamage(HunterEliteKind kind, float damage)
        {
            Validate(kind);
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0) return 0;
            return kind == HunterEliteKind.Armored ? damage * .5f : damage;
        }
        private static void Validate(HunterEliteKind kind)
        { if (kind != HunterEliteKind.Armored && kind != HunterEliteKind.Summoner) throw new ArgumentOutOfRangeException("kind"); }
    }

    /// <summary>One living summon per owner. Detection and damage remain the scene's responsibility.</summary>
    public sealed class HunterSummonCycle
    {
        public float AbandonSeconds { get; private set; }
        public float CooldownSeconds { get; private set; }
        public float UnseenSeconds { get; private set; }
        public float CooldownRemaining { get; private set; }
        public HunterSummonState State { get; private set; }
        public HunterSummonCycle(float abandonSeconds, float cooldownSeconds)
        {
            if (!Finite(abandonSeconds) || abandonSeconds < 0 || !Finite(cooldownSeconds) || cooldownSeconds < 0)
                throw new ArgumentOutOfRangeException();
            AbandonSeconds = abandonSeconds; CooldownSeconds = cooldownSeconds; Reset();
        }
        public bool TrySummon(bool paused)
        {
            if (paused || State != HunterSummonState.Ready) return false;
            State = HunterSummonState.Active; UnseenSeconds = 0; return true;
        }
        /// <summary>True requests removal of the active summon, exactly once, after both detection zones are clear.</summary>
        public bool Tick(float delta, bool ownerDetectsPlayer, bool summonDetectsPlayer, bool paused)
        {
            if (paused || !Finite(delta) || delta < 0) return false;
            if (State == HunterSummonState.Active)
            {
                if (ownerDetectsPlayer || summonDetectsPlayer) { UnseenSeconds = 0; return false; }
                UnseenSeconds = (float)Math.Min(AbandonSeconds, (double)UnseenSeconds + delta);
                if (UnseenSeconds >= AbandonSeconds) { StartCooldown(); return true; }
            }
            else if (State == HunterSummonState.Cooldown)
            {
                CooldownRemaining = Math.Max(0, CooldownRemaining - delta);
                if (CooldownRemaining == 0) State = HunterSummonState.Ready;
            }
            return false;
        }
        public bool SummonDied()
        {
            if (State != HunterSummonState.Active) return false;
            StartCooldown(); return true;
        }
        /// <summary>The scene removes an owned summon if true; no resurrection or automatic resummoning is allowed.</summary>
        public bool OwnerDied()
        {
            bool remove = State == HunterSummonState.Active;
            State = HunterSummonState.Disabled; UnseenSeconds = CooldownRemaining = 0; return remove;
        }
        private void StartCooldown()
        {
            UnseenSeconds = 0; CooldownRemaining = CooldownSeconds;
            State = CooldownSeconds > 0 ? HunterSummonState.Cooldown : HunterSummonState.Ready;
        }
        public void Reset() { State = HunterSummonState.Ready; UnseenSeconds = CooldownRemaining = 0; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

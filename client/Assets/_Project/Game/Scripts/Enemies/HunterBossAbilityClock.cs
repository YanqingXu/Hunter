using System;

namespace BigWorld.Gameplay
{
    public enum HunterBossAbility { None, Slash, Bolt, RagePulse, Rush }

    /// <summary>One telegraphed ability at a time. The scene owns threshold detection, aiming and physical effects.</summary>
    public sealed class HunterBossAbilityClock
    {
        public HunterBossAbility Active { get; private set; }
        public float WindupRemaining { get; private set; }
        public float CooldownRemaining { get; private set; }
        public bool IsWinding { get { return Active != HunterBossAbility.None; } }
        public static bool IsRageAbility(HunterBossAbility ability)
        { return ability == HunterBossAbility.RagePulse || ability == HunterBossAbility.Rush; }
        public static bool IsValid(HunterBossAbility ability)
        { return ability >= HunterBossAbility.Slash && ability <= HunterBossAbility.Rush; }
        public static float Windup(HunterBossAbility ability)
        {
            switch (ability)
            {
                case HunterBossAbility.Slash: return .5f;
                case HunterBossAbility.Bolt: return .75f;
                case HunterBossAbility.RagePulse: return .65f;
                case HunterBossAbility.Rush: return .5f;
                default: throw new ArgumentOutOfRangeException("ability");
            }
        }
        public static float Recovery(HunterBossAbility ability)
        {
            switch (ability)
            {
                case HunterBossAbility.Slash: return 1.2f;
                case HunterBossAbility.Bolt: return 1.6f;
                case HunterBossAbility.RagePulse: return .8f;
                case HunterBossAbility.Rush: return 1;
                default: throw new ArgumentOutOfRangeException("ability");
            }
        }
        public bool TryBegin(HunterBossAbility ability, bool enraged, bool paused)
        {
            if (paused || !IsValid(ability) || IsWinding || CooldownRemaining > 0 || IsRageAbility(ability) != enraged) return false;
            Begin(ability); return true;
        }
        /// <summary>Call once per newly crossed health threshold. Preempts both normal attacks and recovery.</summary>
        public bool TriggerRagePulse(bool paused)
        {
            if (paused) return false;
            CooldownRemaining = 0; Begin(HunterBossAbility.RagePulse); return true;
        }
        public HunterBossAbility Tick(float delta, bool enraged, bool paused)
        {
            if (paused || float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) return HunterBossAbility.None;
            // Switching groups wins over a windup reaching its deadline in this same frame.
            if (IsWinding && IsRageAbility(Active) != enraged) Cancel();
            if (!IsWinding) { CooldownRemaining = Math.Max(0, CooldownRemaining - delta); return HunterBossAbility.None; }
            WindupRemaining = Math.Max(0, WindupRemaining - delta);
            if (WindupRemaining > 0) return HunterBossAbility.None;
            var result = Active; Active = HunterBossAbility.None; CooldownRemaining = Recovery(result); return result;
        }
        private void Begin(HunterBossAbility ability) { Active = ability; WindupRemaining = Windup(ability); }
        public void Cancel() { Active = HunterBossAbility.None; WindupRemaining = 0; }
        public void Reset() { Cancel(); CooldownRemaining = 0; }
    }
}

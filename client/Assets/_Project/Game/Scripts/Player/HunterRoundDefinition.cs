using System;

namespace BigWorld.Gameplay
{
    /// <summary>A gun's default cartridge. Ammunition supply pools are still keyed separately by HunterAmmoType.</summary>
    [Serializable]
    public sealed class HunterRoundDefinition
    {
        public int Pellets = 1;
        public float Damage = 25;
        public float SpreadAngle;
        public float Distance = 12;
        public int PenetrationPower;
        public float DamageRetainedPerProp = .75f;
        public int[] PenetrablePropKinds = new int[0];

        public HunterRoundDefinition Snapshot()
        {
            if (Pellets < 1 || Pellets > 64 || !Finite(SpreadAngle) || SpreadAngle < 0 || SpreadAngle > 180 ||
                !Finite(Distance) || Distance <= 0) throw new ArgumentException("Invalid cartridge pellet count, spread or range.");
            // Shared validation also rejects nonfinite damage, invalid power, retention and whitelist entries.
            CreatePenetration();
            var copy = (HunterRoundDefinition)MemberwiseClone();
            copy.PenetrablePropKinds = (int[])PenetrablePropKinds.Clone(); return copy;
        }
        public HunterPenetrationDefinition CreatePenetration()
        { return new HunterPenetrationDefinition(Damage, PenetrationPower, DamageRetainedPerProp, PenetrablePropKinds); }
        public static HunterRoundDefinition Legacy(float damage) { return new HunterRoundDefinition { Damage = damage }; }
        public static HunterRoundDefinition Rifle()
        { return new HunterRoundDefinition { PenetrationPower = 2, PenetrablePropKinds = new[] { 1 } }; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

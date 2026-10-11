using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    /// <summary>Immutable ammunition settings. Prop kinds are authored positive IDs; zero denotes ordinary solid terrain.</summary>
    public sealed class HunterPenetrationDefinition
    {
        private readonly HashSet<int> allowedKinds;
        public float InitialDamage { get; private set; }
        public int Power { get; private set; }
        public float DamageRetainedPerProp { get; private set; }

        public HunterPenetrationDefinition(float damage, int power, float damageRetainedPerProp, int[] penetrablePropKinds)
        {
            if (!Finite(damage) || damage <= 0 || power < 0 || power > 1000 || !Finite(damageRetainedPerProp) ||
                damageRetainedPerProp < 0 || damageRetainedPerProp > 1 || penetrablePropKinds == null)
                throw new ArgumentException("Invalid ammunition penetration settings.");
            allowedKinds = new HashSet<int>();
            foreach (int kind in penetrablePropKinds)
                if (kind <= 0 || !allowedKinds.Add(kind)) throw new ArgumentException("Prop kinds must be positive and unique.");
            InitialDamage = damage; Power = power; DamageRetainedPerProp = damageRetainedPerProp;
        }
        public bool Allows(int propKind) { return allowedKinds.Contains(propKind); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    /// <summary>One pellet's traversal state. Actor hits use a separate ledger and never consume prop penetration power.</summary>
    public sealed class HunterPenetration
    {
        private readonly HunterPenetrationDefinition definition;
        private readonly Dictionary<string, int> passedProps = new Dictionary<string, int>(StringComparer.Ordinal);
        public int PassedCount { get { return passedProps.Count; } }
        public int Remaining { get { return definition.Power - PassedCount; } }
        public float Damage { get; private set; }

        public HunterPenetration(HunterPenetrationDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException("definition");
            this.definition = definition; Damage = definition.InitialDamage;
        }

        // The caller must stop the projectile on false. Use one stable logical prop ID across all of its colliders,
        // including repeated sweep frames; a recycled object representing a different prop needs a new logical ID.
        public bool TryPass(string propId, int propKind)
        {
            if (string.IsNullOrWhiteSpace(propId) || !definition.Allows(propKind)) return false;
            int previousKind;
            if (passedProps.TryGetValue(propId, out previousKind)) return previousKind == propKind;
            if (Remaining == 0) return false;
            passedProps.Add(propId, propKind);
            Damage *= definition.DamageRetainedPerProp;
            return true;
        }
    }
}

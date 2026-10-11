using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Immutable cartridge snapshot for one accepted trigger pull, independent of later weapon/config changes.</summary>
    public sealed class HunterShotSettings : ISkillProjectileSettings
    {
        private readonly HunterRoundDefinition round;
        private readonly HunterPenetrationDefinition penetration;
        public float Damage => round.Damage;
        public HunterShotSettings(HunterRoundDefinition source)
        { round = source.Snapshot(); penetration = round.CreatePenetration(); }
        public SkillProjectileEvent Configure(SkillProjectileEvent authored, int frameRate)
        {
            var value = authored.Copy(); value.Count = round.Pellets; value.SpreadAngle = round.SpreadAngle;
            // Keep launch speed while cartridge range determines travel time.
            value.Speed = (float)(authored.Distance / authored.FlightSeconds(frameRate));
            value.Timing = SkillProjectileTiming.SpeedAndDistance; value.Distance = round.Distance;
            value.MaxLifetime = Mathf.Max(value.MaxLifetime, value.Distance / value.Speed);
            value.StopAtWalls = true; return value;
        }
        public ISkillProjectileTraversal CreateTraversal() { return new Traversal(penetration); }
        private sealed class Traversal : ISkillProjectileTraversal
        {
            private readonly HunterPenetration state;
            public float Damage => state.Damage;
            public Traversal(HunterPenetrationDefinition value) { state = new HunterPenetration(value); }
            public bool TryPass(Component obstacle)
            {
                var prop = obstacle ? obstacle.GetComponentInParent<HunterPenetrableProp2D>() : null;
                return prop && prop.isActiveAndEnabled && state.TryPass(prop.LogicalId, prop.Kind);
            }
        }
    }
}

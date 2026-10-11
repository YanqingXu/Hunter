using System;
using System.Collections.Generic;

namespace BigWorld.Gameplay
{
    public enum HunterAmmoType { Standard, Shotgun }

    [Serializable]
    public sealed class HunterGunDefinition
    {
        public string Name = "步枪";
        public HunterAmmoType AmmoType;
        public int Weight = 2;
        public int MagazineCapacity = 6;
        public float ReloadSeconds = 2;
        public float FireInterval = .8f;
        public float Damage = 25;
        public float MeleeDamage = 25;
        // Old authored guns retain their existing damage when this new cartridge field is absent.
        public HunterRoundDefinition DefaultRound;
        internal HunterGunDefinition Snapshot()
        {
            var copy = (HunterGunDefinition)MemberwiseClone();
            copy.DefaultRound = (DefaultRound ?? HunterRoundDefinition.Legacy(Damage)).Snapshot(); return copy;
        }
    }

    [Serializable]
    public sealed class HunterAmmoDefinition
    {
        public HunterAmmoType Type;
        public int MaximumTotal = 72;
        public int RefillAmount = 12;
    }

    /// <summary>Independent magazines and one shared reserve per ammunition type. Uses simulation time.</summary>
    public sealed class HunterWeapons
    {
        private sealed class AmmoPool
        {
            internal int Maximum, Refill, Reserve;
        }

        private readonly HunterGunDefinition[] guns;
        private readonly HunterGunDefinition[] baseGuns;
        private readonly float fireTimingMultiplier = 1, reloadTimingMultiplier = 1;
        private readonly int[] magazines;
        private readonly Dictionary<HunterAmmoType, AmmoPool> pools = new Dictionary<HunterAmmoType, AmmoPool>();
        private float reloadRemaining, shotCooldown;
        public int ActiveIndex { get; private set; }
        public int WeaponCount { get { return guns.Length; } }
        public string Name { get { return guns[ActiveIndex].Name; } }
        public int Magazine { get { return magazines[ActiveIndex]; } }
        public int MagazineCapacity { get { return guns[ActiveIndex].MagazineCapacity; } }
        public int Reserve { get { return pools[guns[ActiveIndex].AmmoType].Reserve; } }
        public float Damage { get { return guns[ActiveIndex].DefaultRound.Damage; } }
        public HunterRoundDefinition Round { get { return guns[ActiveIndex].DefaultRound.Snapshot(); } }
        public float MeleeDamage { get { return guns[ActiveIndex].MeleeDamage; } }
        public float FireInterval { get { return guns[ActiveIndex].FireInterval; } }
        public bool IsReloading { get { return reloadRemaining > 0; } }
        public float ReloadProgress { get { return IsReloading ? 1 - reloadRemaining / guns[ActiveIndex].ReloadSeconds : 0; } }
        public bool CanShoot { get { return !IsReloading && Magazine > 0 && shotCooldown <= 0; } }
        public bool CanRefill
        {
            get
            {
                foreach (var pair in pools) if (Total(pair.Key) < pair.Value.Maximum) return true;
                return false;
            }
        }

        public HunterWeapons(HunterGunDefinition[] definitions, HunterAmmoDefinition[] ammo, int weightLimit)
        {
            if (definitions == null || definitions.Length == 0 || ammo == null || weightLimit <= 0)
                throw new ArgumentException("Missing weapons, ammo or weight limit.");
            guns = new HunterGunDefinition[definitions.Length];
            baseGuns = new HunterGunDefinition[definitions.Length];
            magazines = new int[definitions.Length];
            foreach (var entry in ammo)
            {
                if (entry == null || entry.MaximumTotal <= 0 || entry.RefillAmount <= 0 || pools.ContainsKey(entry.Type))
                    throw new ArgumentException("Invalid or duplicate ammunition definition.");
                pools.Add(entry.Type, new AmmoPool { Maximum = entry.MaximumTotal, Refill = entry.RefillAmount });
            }
            long weight = 0;
            for (int i = 0; i < definitions.Length; i++)
            {
                var gun = definitions[i];
                if (gun == null || string.IsNullOrEmpty(gun.Name) || gun.Weight <= 0 || gun.MagazineCapacity <= 0 ||
                    !Positive(gun.ReloadSeconds) || !Positive(gun.FireInterval) || !Positive(gun.Damage) || !Positive(gun.MeleeDamage) ||
                    !pools.ContainsKey(gun.AmmoType)) throw new ArgumentException("Invalid weapon definition.");
                baseGuns[i] = gun.Snapshot(); guns[i] = gun.Snapshot(); weight += gun.Weight;
            }
            if (weight > weightLimit) throw new ArgumentException("Weapons exceed the hunter's weight limit.");
            // Do not grant ammo for types the loadout does not carry.
            foreach (var type in new List<HunterAmmoType>(pools.Keys))
            {
                long loaded = 0;
                foreach (var gun in guns) if (gun.AmmoType == type) loaded += gun.MagazineCapacity;
                if (loaded == 0) { pools.Remove(type); continue; }
                if (loaded > pools[type].Maximum) throw new ArgumentException("Magazine capacities exceed ammunition maximum.");
            }
            Reset();
        }

        /// <summary>Recompute from authored timings, retaining ammo, selection and relative timer progress.</summary>
        public HunterWeapons WithTimingMultipliers(float fireInterval, float reload)
        {
            if (!Positive(fireInterval) || !Positive(reload)) throw new ArgumentException("Invalid weapon timing multiplier.");
            return new HunterWeapons(this, fireInterval, reload);
        }

        private HunterWeapons(HunterWeapons source, float fireInterval, float reload)
        {
            baseGuns = source.baseGuns; // Private immutable snapshots, never exposed or modified.
            guns = new HunterGunDefinition[baseGuns.Length];
            for (int i = 0; i < guns.Length; i++)
            {
                guns[i] = baseGuns[i].Snapshot();
                guns[i].FireInterval *= fireInterval; guns[i].ReloadSeconds *= reload;
                if (!Positive(guns[i].FireInterval) || !Positive(guns[i].ReloadSeconds))
                    throw new ArgumentException("Modified weapon timing is out of range.");
            }
            magazines = (int[])source.magazines.Clone(); ActiveIndex = source.ActiveIndex;
            foreach (var pair in source.pools)
                pools.Add(pair.Key, new AmmoPool { Maximum = pair.Value.Maximum, Refill = pair.Value.Refill, Reserve = pair.Value.Reserve });
            shotCooldown = (float)((double)source.shotCooldown / source.fireTimingMultiplier * fireInterval);
            reloadRemaining = (float)((double)source.reloadRemaining / source.reloadTimingMultiplier * reload);
            fireTimingMultiplier = fireInterval; reloadTimingMultiplier = reload;
        }

        public void Reset()
        {
            ActiveIndex = 0; reloadRemaining = shotCooldown = 0;
            foreach (var pool in pools.Values) pool.Reserve = pool.Maximum;
            for (int i = 0; i < guns.Length; i++)
            {
                magazines[i] = guns[i].MagazineCapacity;
                pools[guns[i].AmmoType].Reserve -= magazines[i];
            }
        }

        public bool TryFire()
        {
            if (!CanShoot) return false;
            magazines[ActiveIndex]--; shotCooldown = FireInterval; return true;
        }

        public bool TryReload()
        {
            if (IsReloading || Magazine >= MagazineCapacity || Reserve <= 0) return false;
            reloadRemaining = guns[ActiveIndex].ReloadSeconds; return true;
        }

        public void CancelReload() { reloadRemaining = 0; }

        public bool TrySwitch(int index)
        {
            if (index < 0 || index >= guns.Length || index == ActiveIndex) return false;
            CancelReload(); ActiveIndex = index;
            // Preserve fire cadence across switches, preventing switch-to-bypass exploits.
            return true;
        }

        public void Tick(float delta)
        {
            if (!Positive(delta)) return;
            shotCooldown = Math.Max(0, shotCooldown - delta);
            if (!IsReloading) return;
            reloadRemaining = Math.Max(0, reloadRemaining - delta);
            if (IsReloading) return;
            int amount = Math.Min(MagazineCapacity - Magazine, Reserve);
            magazines[ActiveIndex] += amount;
            pools[guns[ActiveIndex].AmmoType].Reserve -= amount;
        }

        public int Total(HunterAmmoType type)
        {
            AmmoPool pool;
            if (!pools.TryGetValue(type, out pool)) return 0;
            int total = pool.Reserve;
            for (int i = 0; i < guns.Length; i++) if (guns[i].AmmoType == type) total += magazines[i];
            return total;
        }

        /// <summary>One refill per carried ammo type, not per weapon; rounds go into reserve.</summary>
        public int Refill()
        {
            int added = 0;
            foreach (var pair in pools)
            {
                int amount = Math.Min(pair.Value.Refill, pair.Value.Maximum - Total(pair.Key));
                pair.Value.Reserve += amount; added += amount;
            }
            return added;
        }

        private static bool Positive(float value) { return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0; }
    }
}

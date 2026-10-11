using System;

namespace BigWorld.Gameplay
{
    /// <summary>Validated, immutable skill modifiers. Always apply to authored values, never to the previous result.</summary>
    public sealed class HunterGunModifiers
    {
        // Decimal ratios keep prices and integral capacities exact (e.g. 75 * .8 is 60, never rounded up to 61).
        public decimal PriceMultiplier { get; private set; }
        public decimal WeightMultiplier { get; private set; }
        public decimal AmmoCapacityMultiplier { get; private set; }
        public float MeleeDamageMultiplier { get; private set; }
        public float FireIntervalMultiplier { get; private set; }
        public float ReloadMultiplier { get; private set; }

        public HunterGunModifiers(decimal price = 1m, decimal weight = 1m, decimal ammoCapacity = 1m,
            float meleeDamage = 1, float fireInterval = 1, float reload = 1)
        {
            if (price < 0 || weight <= 0 || ammoCapacity <= 0 || !Positive(meleeDamage) || !Positive(fireInterval) || !Positive(reload))
                throw new ArgumentException("Invalid gun skill multiplier.");
            PriceMultiplier = price; WeightMultiplier = weight; AmmoCapacityMultiplier = ammoCapacity;
            MeleeDamageMultiplier = meleeDamage; FireIntervalMultiplier = fireInterval; ReloadMultiplier = reload;
        }
        public int Price(int authored)
        {
            if (authored < 0) throw new ArgumentException("Invalid authored price.");
            return Integral(authored, PriceMultiplier);
        }
        public int Weight(int authored)
        {
            if (authored <= 0) throw new ArgumentException("Invalid authored weight.");
            return Integral(authored, WeightMultiplier);
        }
        // A capacity is not a refill: the host must separately preserve existing rounds and clamp only excess reserve.
        public int AmmoCapacity(int authored, int totalMagazineCapacity)
        {
            if (authored <= 0 || totalMagazineCapacity < 0) throw new ArgumentException("Invalid authored ammunition capacity.");
            int result = Integral(authored, AmmoCapacityMultiplier);
            if (result < totalMagazineCapacity) throw new ArgumentException("Modified ammo capacity cannot contain the fixed magazines.");
            return result;
        }
        public float MeleeDamage(float authored) { return Scalar(authored, MeleeDamageMultiplier); }
        public float FireInterval(float authored) { return Scalar(authored, FireIntervalMultiplier); }
        public float ReloadSeconds(float authored) { return Scalar(authored, ReloadMultiplier); }

        private static int Integral(int authored, decimal multiplier)
        {
            try
            {
                decimal value = decimal.Ceiling(authored * multiplier);
                if (value > int.MaxValue) throw new ArgumentException("Modified integral gun attribute is out of range.");
                return (int)value;
            }
            catch (OverflowException) { throw new ArgumentException("Modified integral gun attribute overflows."); }
        }
        private static float Scalar(float authored, float multiplier)
        {
            if (!Positive(authored)) throw new ArgumentException("Invalid authored gun attribute.");
            float value = (float)((double)authored * multiplier);
            if (!Positive(value)) throw new ArgumentException("Modified gun attribute is out of range.");
            return value;
        }
        private static bool Positive(float value) { return value > 0 && !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

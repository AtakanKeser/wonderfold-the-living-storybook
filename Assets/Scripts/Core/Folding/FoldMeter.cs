using Wonderfold.Core.Rules;

namespace Wonderfold.Core.Folding
{
    /// <summary>
    /// The charge that pays for a fold. Ordinary matching fills it; obstacles and — especially — matches
    /// that already span a seam fill it faster, so the mechanic feeds itself once the player engages
    /// with it.
    /// </summary>
    public sealed class FoldMeter
    {
        private readonly GameRules _rules;

        public int Value { get; private set; }
        public int Max { get; }

        public FoldMeter(GameRules rules, int max = 0)
        {
            _rules = rules;
            Max = max > 0 ? max : rules.FoldMeterMax;
        }

        public bool IsFull => Value >= Max;
        public float Normalised => Max <= 0 ? 0f : (float)Value / Max;

        /// <summary>Returns true when this charge is what tipped the meter over the line.</summary>
        public bool Add(int amount)
        {
            if (amount <= 0 || IsFull) return false;
            bool wasFull = IsFull;
            Value += amount;
            if (Value > Max) Value = Max;
            return !wasFull && IsFull;
        }

        public bool ChargeForTile() => Add(_rules.FoldChargePerTile);
        public bool ChargeForObstacleHit() => Add(_rules.FoldChargePerObstacleHit);
        public bool ChargeForSeamMatch() => Add(_rules.FoldChargePerSeamMatch);

        public bool CanPay(int cost) => Value >= cost;

        public void Pay(int cost)
        {
            Value -= cost;
            if (Value < 0) Value = 0;
        }

        public void Set(int value)
        {
            Value = value < 0 ? 0 : (value > Max ? Max : value);
        }
    }
}

namespace Wonderfold.Core.Live
{
    public enum StreakChange
    {
        /// <summary>The player already claimed today; nothing moved.</summary>
        AlreadyCounted = 0,
        /// <summary>First day ever, or the first day after a broken streak.</summary>
        Started = 1,
        /// <summary>Yesterday plus one.</summary>
        Continued = 2,
        /// <summary>A gap the ledger could not bridge; the count restarted at one.</summary>
        Broken = 3
    }

    /// <summary>What a day's visit was worth.</summary>
    public readonly struct StreakReward
    {
        public readonly int Coins;
        public readonly int Hammers;
        public readonly int Rockets;

        public StreakReward(int coins, int hammers, int rockets)
        {
            Coins = coins;
            Hammers = hammers;
            Rockets = rockets;
        }

        public bool IsEmpty => Coins == 0 && Hammers == 0 && Rockets == 0;
    }

    public readonly struct StreakUpdate
    {
        public readonly StreakChange Change;
        public readonly int Streak;
        public readonly int Best;
        public readonly StreakReward Reward;

        public StreakUpdate(StreakChange change, int streak, int best, StreakReward reward)
        {
            Change = change;
            Streak = streak;
            Best = best;
            Reward = reward;
        }
    }

    /// <summary>
    /// Counts consecutive days the player showed up, and — more importantly — lets them buy back exactly
    /// one missed day.
    ///
    /// <para>A streak is the cheapest retention mechanism there is, but a streak that snaps the first
    /// time someone has a busy Tuesday teaches them the counter is not worth caring about. The mend is
    /// the part that makes it stick: one missed day is recoverable for coins, two is not. That keeps the
    /// number meaningful, gives the coin economy its first real sink, and turns "I broke it, why bother"
    /// into a decision the player gets to make.</para>
    ///
    /// <para>Pure day-index arithmetic, so it is fully testable without a clock.</para>
    /// </summary>
    public sealed class StreakLedger
    {
        public const int NeverPlayed = int.MinValue;

        /// <summary>Coin ladder across a seven-day cycle; the seventh day is the one worth planning for.</summary>
        private static readonly int[] CoinLadder = { 25, 35, 50, 65, 85, 110, 200 };

        public int Current;
        public int Best;
        public int LastDayIndex = NeverPlayed;

        /// <summary>Mends bought in total. Kept for display and for pricing the next one.</summary>
        public int MendsUsed;

        public bool HasPlayedToday(int dayIndex) => LastDayIndex == dayIndex;

        /// <summary>
        /// Registers a visit. Idempotent within a day, so it is safe to call from several places on the
        /// same launch.
        /// </summary>
        public StreakUpdate Record(int dayIndex)
        {
            if (LastDayIndex == dayIndex)
                return new StreakUpdate(StreakChange.AlreadyCounted, Current, Best, default);

            StreakChange change;
            if (LastDayIndex == NeverPlayed)
            {
                Current = 1;
                change = StreakChange.Started;
            }
            else if (dayIndex == LastDayIndex + 1)
            {
                Current++;
                change = StreakChange.Continued;
            }
            else if (dayIndex < LastDayIndex)
            {
                // The device clock moved backwards. Do not punish and do not reward.
                return new StreakUpdate(StreakChange.AlreadyCounted, Current, Best, default);
            }
            else
            {
                Current = 1;
                change = StreakChange.Broken;
            }

            LastDayIndex = dayIndex;
            if (Current > Best) Best = Current;
            return new StreakUpdate(change, Current, Best, RewardFor(Current));
        }

        /// <summary>Exactly one missed day is mendable. Two is a broken streak, and it should feel like one.</summary>
        public bool CanMend(int dayIndex) =>
            LastDayIndex != NeverPlayed && Current > 0 && dayIndex == LastDayIndex + 2;

        /// <summary>Priced off the streak being saved: the longer it is, the more it is worth keeping.</summary>
        public int MendCost()
        {
            int cost = 60 + Current * 12 + MendsUsed * 25;
            return cost > 600 ? 600 : cost;
        }

        /// <summary>
        /// Bridges the missing day. The caller is responsible for taking the coins; this only moves the
        /// ledger, and only when the gap is exactly one day.
        /// </summary>
        public bool TryMend(int dayIndex)
        {
            if (!CanMend(dayIndex)) return false;
            LastDayIndex = dayIndex - 1;
            MendsUsed++;
            return true;
        }

        public static StreakReward RewardFor(int streak)
        {
            if (streak <= 0) return default;

            int dayInCycle = (streak - 1) % 7;
            int cycle = (streak - 1) / 7;
            int multiplierSteps = cycle > 3 ? 3 : cycle;

            int coins = CoinLadder[dayInCycle] * (4 + multiplierSteps) / 4;
            bool crest = dayInCycle == 6;
            return new StreakReward(coins, crest ? 1 : 0, crest ? 1 : 0);
        }
    }
}

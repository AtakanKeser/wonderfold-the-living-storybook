namespace Wonderfold.Core.Live
{
    /// <summary>
    /// The archive that never ends: page 1, page 2, page 3 … each one woven from its depth and measured
    /// before it is served.
    ///
    /// <para>Running out of levels is the genre's terminal churn event, and the industry's answer is to
    /// hand-author dozens of new pages every week for ever. That is a permanent staffing cost this game
    /// cannot pay. The Endless Archive answers it differently: depth is the seed, so page 4,318 exists
    /// already and is the same page 4,318 for every player — which keeps depth comparable between
    /// players and keeps a shared replay meaningful.</para>
    ///
    /// <para>The difficulty curve is not a straight line. It climbs, then drops for a breather every
    /// fifth page and spikes every tenth, because measured play on the authored chapter says a flat ramp
    /// is what makes people stop.</para>
    /// </summary>
    public static class EndlessArchive
    {
        public const int IdBase = 800000;

        public static string Key(int depth) => "endless-" + depth.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        public static bool IsEndlessKey(string key) =>
            key != null && key.StartsWith("endless-", System.StringComparison.Ordinal);

        public static bool TryParseKey(string key, out int depth)
        {
            depth = 0;
            if (!IsEndlessKey(key)) return false;
            return int.TryParse(key.Substring(8), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out depth);
        }

        public static float DifficultyFor(int depth)
        {
            if (depth < 1) depth = 1;

            float ramp = 0.26f + (depth - 1) * 0.013f;
            if (ramp > 0.86f) ramp = 0.86f;

            if (depth % 5 == 0) ramp -= 0.16f;      // breather
            if (depth % 10 == 0) ramp += 0.22f;     // the tenth page is the one worth bragging about
            if (depth % 25 == 0) ramp += 0.06f;

            return ramp < 0.15f ? 0.15f : ramp > 0.92f ? 0.92f : ramp;
        }

        public static WeaveRequest Request(int depth)
        {
            if (depth < 1) depth = 1;
            float difficulty = DifficultyFor(depth);

            return new WeaveRequest
            {
                Seed = SeedFor(depth),
                Key = Key(depth),
                Id = IdBase + depth,
                Name = null, // the weaver names it; a woven page deserves its own title
                Difficulty = difficulty,
                TargetWinRate = 0.80f - difficulty * 0.42f,
                AllowFold = depth >= 3,
                Book = "endless-archive"
            };
        }

        public static int SeedFor(int depth)
        {
            unchecked
            {
                uint h = 0x85EBCA6Bu ^ (uint)depth;
                h *= 2246822519u;
                h ^= h >> 15;
                h *= 2654435761u;
                h ^= h >> 13;
                return (int)h;
            }
        }

        /// <summary>
        /// Weaves and measures one page of the archive. No settings parameter for the same reason the
        /// Daily Fold has none: the audition parameters decide the move budget, so they are part of what
        /// makes page N the same page N for everyone.
        /// </summary>
        public static AuditionResult Page(int depth) =>
            PageAudition.Hold(Request(depth), AuditionSettings.Canonical);

        /// <summary>
        /// Every fifth page is a Bound Page: it hands over a real reward instead of just a depth number,
        /// which is what turns a long climb into a series of near-term goals.
        /// </summary>
        public static bool IsRewardDepth(int depth) => depth > 0 && depth % 5 == 0;

        public static int RewardCoins(int depth)
        {
            if (!IsRewardDepth(depth)) return 0;
            int coins = 40 + depth / 5 * 15;
            return coins > 400 ? 400 : coins;
        }
    }
}

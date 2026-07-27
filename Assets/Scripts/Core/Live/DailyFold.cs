using System;

namespace Wonderfold.Core.Live
{
    /// <summary>
    /// One page a day, woven from the date, identical for every player on Earth.
    ///
    /// <para>Match-3 games cannot normally run a fair daily. Their boards are refilled from whatever
    /// random source the client happens to have, so two players never see the same page and a shared
    /// score means nothing. Wonderfold's core was built the other way round — a contract-stable xorshift
    /// PRNG, no <c>System.Random</c> anywhere, every board reconstructible from a seed — precisely so
    /// that replays and the simulator would work. The Daily Fold is that property cashed in: the date
    /// picks the seed, the seed weaves the page, and the page is the same page for everyone.</para>
    ///
    /// <para>That makes score comparable, makes a shared replay verifiable, and gives the game the one
    /// retention hook the genre has never been able to run honestly: <i>come back tomorrow, because
    /// tomorrow's page is a real, single, shared thing.</i></para>
    /// </summary>
    public static class DailyFold
    {
        /// <summary>Ids for generated pages live well clear of the authored 1..N range.</summary>
        public const int IdBase = 900000;

        /// <summary>
        /// The week has a shape on purpose. A flat difficulty curve is the fastest way to lose a player:
        /// Monday is a warm welcome back, Saturday is the one people plan their day around, Sunday winds
        /// down. The same rhythm the authored chapter uses, spread over seven days instead of twenty
        /// pages.
        /// </summary>
        private static readonly float[] DifficultyByWeekday =
        {
            0.34f, // Monday    — breather, the "I'm back" page
            0.44f, // Tuesday
            0.56f, // Wednesday — midweek lift
            0.46f, // Thursday  — breather
            0.62f, // Friday
            0.74f, // Saturday  — the spike everyone talks about
            0.40f  // Sunday    — wind-down
        };

        public static string Key(int dayIndex) => "daily-" + dayIndex.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        public static bool IsDailyKey(string key) => key != null && key.StartsWith("daily-", StringComparison.Ordinal);

        public static bool TryParseKey(string key, out int dayIndex)
        {
            dayIndex = 0;
            if (!IsDailyKey(key)) return false;
            return int.TryParse(key.Substring(6), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out dayIndex);
        }

        public static float DifficultyFor(int dayIndex) => DifficultyByWeekday[LiveClock.WeekdayIndex(dayIndex)];

        public static WeaveRequest Request(int dayIndex)
        {
            float difficulty = DifficultyFor(dayIndex);
            return new WeaveRequest
            {
                // The date is the seed. Nothing about the player enters this number, which is the whole
                // point — a per-player salt here would quietly destroy the shared page.
                Seed = SeedFor(dayIndex),
                Key = Key(dayIndex),
                Id = IdBase + dayIndex,
                Name = "The Lost Page of " + LiveClock.DayLabel(dayIndex),
                Difficulty = difficulty,
                TargetWinRate = 0.78f - difficulty * 0.40f,
                AllowFold = true,
                Book = "lost-pages"
            };
        }

        public static int SeedFor(int dayIndex)
        {
            unchecked
            {
                uint h = 0x9E3779B9u ^ (uint)dayIndex;
                h *= 2654435761u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return (int)h;
            }
        }

        /// <summary>
        /// Weaves and measures a day's page. Same result on every device, every time — which is why this
        /// takes no settings: the audition parameters are part of the page's identity.
        /// </summary>
        public static AuditionResult Page(int dayIndex) =>
            PageAudition.Hold(Request(dayIndex), AuditionSettings.Canonical);
    }
}

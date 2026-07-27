using System;

namespace Wonderfold.Core.Live
{
    /// <summary>
    /// Turns a wall clock into the only two numbers the live layer needs: which UTC day it is and which
    /// UTC week it is.
    ///
    /// <para>Everything scheduled — the Daily Fold, quests, streaks — is keyed off these integers rather
    /// than off a <see cref="DateTime"/>. That is deliberate: an integer day index is trivial to store in
    /// a save, cannot carry a time zone by accident, and makes "did the player already play today?" a
    /// comparison rather than a date-arithmetic bug waiting to happen. UTC is the reference everywhere so
    /// two players in different time zones are always working on the same page.</para>
    /// </summary>
    public static class LiveClock
    {
        /// <summary>Day 0. Fixed forever — changing it would renumber every saved streak.</summary>
        public static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static int DayIndex(DateTime utc) => (int)Math.Floor((utc - Epoch).TotalDays);

        public static int WeekIndex(int dayIndex) => (int)Math.Floor(dayIndex / 7.0);

        public static DateTime StartOfDay(int dayIndex) => Epoch.AddDays(dayIndex);

        public static TimeSpan UntilNextDay(DateTime utc) => StartOfDay(DayIndex(utc) + 1) - utc;

        public static TimeSpan UntilNextWeek(DateTime utc)
        {
            int nextWeekStart = (WeekIndex(DayIndex(utc)) + 1) * 7;
            return StartOfDay(nextWeekStart) - utc;
        }

        /// <summary>Human label for a day index, e.g. <c>2026-07-27</c>. Used in share text and codes.</summary>
        public static string DayLabel(int dayIndex) =>
            StartOfDay(dayIndex).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>0 = Monday … 6 = Sunday. Drives the week's difficulty rhythm.</summary>
        public static int WeekdayIndex(int dayIndex)
        {
            int weekday = (int)StartOfDay(dayIndex).DayOfWeek; // 0 = Sunday
            return (weekday + 6) % 7;
        }
    }
}

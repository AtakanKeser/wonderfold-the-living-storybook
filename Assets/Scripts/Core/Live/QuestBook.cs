using System.Collections.Generic;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Live
{
    /// <summary>What a quest counts. Every one of these is already tracked by <see cref="LevelRunStats"/>.</summary>
    public enum QuestMetric
    {
        PagesCleared = 0,
        SeamMatches = 1,
        GoldenStitches = 2,
        Folds = 3,
        ObstaclesCleared = 4,
        TilesCleared = 5,
        BoostersCreated = 6,
        DailyFoldsCleared = 7,
        EndlessPagesCleared = 8,
        ThreeStarWins = 9
    }

    public sealed class Quest
    {
        public string Id;
        public QuestMetric Metric;
        public int Target;
        public int RewardCoins;
        public string Title;

        public override string ToString() => $"{Title} ({Target})";
    }

    /// <summary>Everything the live layer needs to know about one finished page.</summary>
    public readonly struct PageRunReport
    {
        public readonly string PageKey;
        public readonly LevelRunStats Stats;
        public readonly int Stars;
        public readonly bool Won;

        public PageRunReport(string pageKey, LevelRunStats stats, int stars)
        {
            PageKey = pageKey;
            Stats = stats;
            Stars = stars;
            Won = stats != null && stats.Outcome == LevelOutcome.Won;
        }
    }

    /// <summary>
    /// The errands the Storykeeper leaves out each day and each week.
    ///
    /// <para>Quests are generated from the day or week index rather than rolled per player, so everybody
    /// is working the same list — the same reason the Daily Fold is shared. It makes the errands
    /// something people can talk about, and it means a quest's difficulty can be reasoned about once
    /// rather than audited per save file.</para>
    ///
    /// <para>Every metric here is a counter the core already keeps for the balance simulator, so quests
    /// cost the gameplay layer nothing: no new events, no new hooks, no chance of a quest and the board
    /// disagreeing about what happened.</para>
    /// </summary>
    public static class QuestBook
    {
        private static readonly QuestMetric[] DailyPool =
        {
            QuestMetric.PagesCleared, QuestMetric.SeamMatches, QuestMetric.GoldenStitches,
            QuestMetric.Folds, QuestMetric.ObstaclesCleared, QuestMetric.TilesCleared,
            QuestMetric.BoostersCreated, QuestMetric.DailyFoldsCleared
        };

        private static readonly QuestMetric[] WeeklyPool =
        {
            QuestMetric.PagesCleared, QuestMetric.SeamMatches, QuestMetric.GoldenStitches,
            QuestMetric.ObstaclesCleared, QuestMetric.EndlessPagesCleared, QuestMetric.ThreeStarWins,
            QuestMetric.Folds
        };

        public static List<Quest> Daily(int dayIndex) =>
            Draw(new DeterministicRandom(Mix(0x0DA11, dayIndex)), DailyPool, 3, DailyPrefix(dayIndex), 1f);

        public static List<Quest> Weekly(int weekIndex) =>
            Draw(new DeterministicRandom(Mix(0x0FEED, weekIndex)), WeeklyPool, 3, WeeklyPrefix(weekIndex), 5.5f);

        /// <summary>Quest ids carry their period, which is how expired lists are swept out of a save.</summary>
        public static string DailyPrefix(int dayIndex) =>
            "d" + dayIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-";

        public static string WeeklyPrefix(int weekIndex) =>
            "w" + weekIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-";

        private static int Mix(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 2654435761u;
                h ^= (uint)b + 0x9E3779B9u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (int)h;
            }
        }

        private static List<Quest> Draw(DeterministicRandom rng, QuestMetric[] pool, int count, string prefix,
            float scale)
        {
            var available = new List<QuestMetric>(pool);
            rng.Shuffle(available);

            var quests = new List<Quest>(count);
            for (int i = 0; i < count && i < available.Count; i++)
            {
                var metric = available[i];
                int target = ScaleTarget(BaseTarget(metric, rng), scale);
                quests.Add(new Quest
                {
                    Id = prefix + (int)metric,
                    Metric = metric,
                    Target = target,
                    RewardCoins = RewardFor(metric, target, scale),
                    Title = Describe(metric, target)
                });
            }

            return quests;
        }

        private static int BaseTarget(QuestMetric metric, DeterministicRandom rng)
        {
            switch (metric)
            {
                case QuestMetric.PagesCleared: return rng.Range(2, 5);
                case QuestMetric.SeamMatches: return rng.Range(6, 13);
                case QuestMetric.GoldenStitches: return rng.Range(2, 6);
                case QuestMetric.Folds: return rng.Range(4, 10);
                case QuestMetric.ObstaclesCleared: return rng.Range(10, 25);
                case QuestMetric.TilesCleared: return rng.Range(150, 320);
                case QuestMetric.BoostersCreated: return rng.Range(8, 18);
                case QuestMetric.DailyFoldsCleared: return 1;
                case QuestMetric.EndlessPagesCleared: return rng.Range(2, 5);
                case QuestMetric.ThreeStarWins: return rng.Range(1, 4);
                default: return 3;
            }
        }

        private static int ScaleTarget(int baseTarget, float scale)
        {
            if (scale <= 1f) return baseTarget;
            int scaled = (int)(baseTarget * scale);
            // Round to something a player can hold in their head.
            int step = scaled >= 200 ? 25 : scaled >= 50 ? 5 : 1;
            int rounded = scaled / step * step;
            return rounded < baseTarget ? baseTarget : rounded;
        }

        private static int RewardFor(QuestMetric metric, int target, float scale)
        {
            int reward;
            switch (metric)
            {
                case QuestMetric.TilesCleared: reward = target / 4; break;
                case QuestMetric.GoldenStitches: reward = target * 22; break;
                case QuestMetric.DailyFoldsCleared: reward = 90; break;
                case QuestMetric.ThreeStarWins: reward = target * 40; break;
                default: reward = target * 12; break;
            }

            if (scale > 1f) reward += 120;
            return reward < 30 ? 30 : reward > 900 ? 900 : reward;
        }

        private static string Describe(QuestMetric metric, int target)
        {
            switch (metric)
            {
                case QuestMetric.PagesCleared: return $"Mend {target} pages";
                case QuestMetric.SeamMatches: return $"Match across the seam {target} times";
                case QuestMetric.GoldenStitches: return $"Sew {target} Golden Stitches";
                case QuestMetric.Folds: return $"Turn the page {target} times";
                case QuestMetric.ObstaclesCleared: return $"Clear {target} obstacles";
                case QuestMetric.TilesCleared: return $"Clear {target} tiles";
                case QuestMetric.BoostersCreated: return $"Make {target} boosters";
                case QuestMetric.DailyFoldsCleared: return "Finish today's Lost Page";
                case QuestMetric.EndlessPagesCleared: return $"Climb {target} pages of the Endless Archive";
                case QuestMetric.ThreeStarWins: return $"Win {target} pages with three stars";
                default: return "Keep the story going";
            }
        }

        /// <summary>How much one finished page moves a given metric.</summary>
        public static int Contribution(QuestMetric metric, PageRunReport report)
        {
            var stats = report.Stats;
            if (stats == null) return 0;

            switch (metric)
            {
                case QuestMetric.PagesCleared: return report.Won ? 1 : 0;
                case QuestMetric.SeamMatches: return stats.SeamMatches;
                case QuestMetric.GoldenStitches: return stats.GoldenStitches;
                case QuestMetric.Folds: return stats.Folds;
                case QuestMetric.ObstaclesCleared: return stats.ObstaclesCleared;
                case QuestMetric.TilesCleared: return stats.TilesCleared;
                case QuestMetric.BoostersCreated: return stats.BoostersCreated;
                case QuestMetric.DailyFoldsCleared:
                    return report.Won && DailyFold.IsDailyKey(report.PageKey) ? 1 : 0;
                case QuestMetric.EndlessPagesCleared:
                    return report.Won && EndlessArchive.IsEndlessKey(report.PageKey) ? 1 : 0;
                case QuestMetric.ThreeStarWins: return report.Won && report.Stars >= 3 ? 1 : 0;
                default: return 0;
            }
        }
    }

    /// <summary>Progress against a quest list, plus which rewards have already been taken.</summary>
    public sealed class QuestTracker
    {
        public readonly Dictionary<string, int> Progress = new Dictionary<string, int>();
        public readonly HashSet<string> Claimed = new HashSet<string>();

        public int ProgressOf(Quest quest)
        {
            if (quest == null) return 0;
            Progress.TryGetValue(quest.Id, out int value);
            return value;
        }

        public bool IsComplete(Quest quest) => quest != null && ProgressOf(quest) >= quest.Target;

        public bool IsClaimed(Quest quest) => quest != null && Claimed.Contains(quest.Id);

        /// <summary>Applies one finished page to a quest list. Returns the quests it just completed.</summary>
        public List<Quest> Record(IReadOnlyList<Quest> quests, PageRunReport report)
        {
            var completed = new List<Quest>();
            if (quests == null) return completed;

            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest == null) continue;

                bool wasComplete = IsComplete(quest);
                int gain = QuestBook.Contribution(quest.Metric, report);
                if (gain <= 0) continue;

                Progress.TryGetValue(quest.Id, out int current);
                int next = current + gain;
                if (next > quest.Target) next = quest.Target;
                Progress[quest.Id] = next;

                if (!wasComplete && next >= quest.Target) completed.Add(quest);
            }

            return completed;
        }

        /// <summary>Takes the reward once. Returns 0 if the quest is unfinished or already claimed.</summary>
        public int Claim(Quest quest)
        {
            if (quest == null || !IsComplete(quest) || !Claimed.Add(quest.Id)) return 0;
            return quest.RewardCoins;
        }

        /// <summary>Drops everything belonging to expired quest lists when the day or week rolls over.</summary>
        public void Forget(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;

            var doomed = new List<string>();
            foreach (var pair in Progress)
            {
                if (pair.Key.StartsWith(prefix, System.StringComparison.Ordinal)) doomed.Add(pair.Key);
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Progress.Remove(doomed[i]);
                Claimed.Remove(doomed[i]);
            }
        }
    }
}

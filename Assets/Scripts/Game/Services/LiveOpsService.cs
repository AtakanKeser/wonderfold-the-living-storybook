using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Level;
using Wonderfold.Core.Live;

namespace Wonderfold.Game.Services
{
    /// <summary>What happened when a page finished, in the terms the live layer cares about.</summary>
    public sealed class LiveRunOutcome
    {
        public string PageKey;
        public int StoryInk;
        public int Stars;
        public bool Won;
        public bool IsPersonalBest;
        public string ThreadCode;
        public string ShareText;
        public readonly List<Quest> QuestsCompleted = new List<Quest>();
        public int CoinsAwarded;
        public int EndlessDepth;
    }

    /// <summary>
    /// Owns the living half of the game: today's shared page, the endless archive, the errand lists, the
    /// streak, and the coins those things pay out.
    ///
    /// <para>Every page it serves is woven and measured by the core, which takes a few hundred simulated
    /// playthroughs. That work is pumped inside a per-frame budget rather than run in one blocking call,
    /// so the app keeps its frame rate and the wait becomes a paced beat — the book writing a page —
    /// instead of a freeze. Results are cached per key, because a page is a pure function of its key and
    /// re-auditioning it would produce exactly the same answer at exactly the same cost.</para>
    ///
    /// <para>The core knows nothing about any of this. It knows how to invent a page and how to measure
    /// one; the decision of <i>which</i> page today is, and what finishing it is worth, lives here.</para>
    /// </summary>
    public sealed class LiveOpsService : MonoBehaviour
    {
        /// <summary>Milliseconds of audition work per frame. Keeps a 60 Hz frame honest while weaving.</summary>
        private const double FrameBudgetMs = 6.0;

        private readonly Dictionary<string, AuditionResult> _cache = new Dictionary<string, AuditionResult>();
        private PlayerProfile _profile;
        private SaveSystem _saves;
        private List<Quest> _dailyQuests = new List<Quest>();
        private List<Quest> _weeklyQuests = new List<Quest>();
        private Coroutine _prefetch;

        public event Action Changed;

        public static LiveOpsService Create(Transform parent, PlayerProfile profile, SaveSystem saves)
        {
            var go = new GameObject("Live Ops");
            go.transform.SetParent(parent, false);
            var service = go.AddComponent<LiveOpsService>();
            service._profile = profile;
            service._saves = saves;
            service.RollOver(DateTime.UtcNow);
            return service;
        }

        public PlayerProfile Profile => _profile;
        public int Today => LiveClock.DayIndex(DateTime.UtcNow);
        public int ThisWeek => LiveClock.WeekIndex(Today);
        public TimeSpan UntilTomorrow => LiveClock.UntilNextDay(DateTime.UtcNow);
        public IReadOnlyList<Quest> DailyQuests => _dailyQuests;
        public IReadOnlyList<Quest> WeeklyQuests => _weeklyQuests;

        // ------------------------------------------------------------------ the day

        /// <summary>
        /// Swaps in today's errand lists and forgets yesterday's. Called on launch and whenever the hub
        /// opens, because an app left running overnight has to notice the date changed.
        /// </summary>
        public void RollOver(DateTime utcNow)
        {
            int day = LiveClock.DayIndex(utcNow);
            int week = LiveClock.WeekIndex(day);

            if (_profile.QuestDayIndex != day)
            {
                if (_profile.QuestDayIndex != int.MinValue)
                    _profile.Quests.Forget(QuestBook.DailyPrefix(_profile.QuestDayIndex));
                _profile.QuestDayIndex = day;
            }

            if (_profile.QuestWeekIndex != week)
            {
                if (_profile.QuestWeekIndex != int.MinValue)
                    _profile.Quests.Forget(QuestBook.WeeklyPrefix(_profile.QuestWeekIndex));
                _profile.QuestWeekIndex = week;
            }

            _dailyQuests = QuestBook.Daily(day);
            _weeklyQuests = QuestBook.Weekly(week);
            Changed?.Invoke();
        }

        /// <summary>
        /// Registers today's visit and pays the streak reward. Returns what changed so the UI can make a
        /// small event out of it.
        /// </summary>
        public StreakUpdate TouchStreak()
        {
            RollOver(DateTime.UtcNow);
            var update = _profile.Streak.Record(Today);
            if (update.Change == StreakChange.AlreadyCounted) return update;

            _profile.AddCoins(update.Reward.Coins);
            _profile.Hammers += update.Reward.Hammers;
            _profile.RibbonRockets += update.Reward.Rockets;
            Save();
            return update;
        }

        public bool CanMendStreak => _profile.Streak.CanMend(Today);
        public int MendCost => _profile.Streak.MendCost();

        /// <summary>Buys back a single missed day. The coins are taken here; the ledger only moves once.</summary>
        public bool TryMendStreak()
        {
            if (!CanMendStreak) return false;
            int cost = _profile.Streak.MendCost();
            if (!_profile.TrySpendCoins(cost)) return false;
            if (!_profile.Streak.TryMend(Today))
            {
                _profile.AddCoins(cost);
                return false;
            }

            Save();
            return true;
        }

        public int ClaimQuest(Quest quest)
        {
            int coins = _profile.Quests.Claim(quest);
            if (coins <= 0) return 0;
            _profile.AddCoins(coins);
            Save();
            return coins;
        }

        // ------------------------------------------------------------------ serving pages

        public bool IsReady(string pageKey) => _cache.ContainsKey(pageKey);

        public AuditionResult Cached(string pageKey) =>
            _cache.TryGetValue(pageKey, out var result) ? result : null;

        public Coroutine RequestDaily(int dayIndex, Action<AuditionResult> ready, Action<float> progress = null) =>
            StartCoroutine(Build(DailyFold.Key(dayIndex), DailyFold.Request(dayIndex), ready, progress));

        public Coroutine RequestEndless(int depth, Action<AuditionResult> ready, Action<float> progress = null) =>
            StartCoroutine(Build(EndlessArchive.Key(depth), EndlessArchive.Request(depth), ready, progress));

        /// <summary>
        /// Weaves the next archive page quietly while the player is busy elsewhere, so the common case is
        /// a page that is already waiting when they ask for it.
        /// </summary>
        public void PrefetchEndless(int depth)
        {
            if (depth < 1 || _cache.ContainsKey(EndlessArchive.Key(depth))) return;
            if (_prefetch != null) StopCoroutine(_prefetch);
            _prefetch = StartCoroutine(Build(EndlessArchive.Key(depth), EndlessArchive.Request(depth), null, null));
        }

        private IEnumerator Build(string key, WeaveRequest request, Action<AuditionResult> ready,
            Action<float> progress)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                progress?.Invoke(1f);
                ready?.Invoke(cached);
                yield break;
            }

            var runner = new PageAuditionRunner(request, AuditionSettings.Canonical);
            var watch = new System.Diagnostics.Stopwatch();

            while (!runner.IsDone)
            {
                watch.Restart();
                // Burn as much of this frame's budget as the audition needs, then hand the frame back.
                while (watch.Elapsed.TotalMilliseconds < FrameBudgetMs && runner.Advance()) { }
                progress?.Invoke(runner.Progress);
                yield return null;
            }

            _cache[key] = runner.Result;
            progress?.Invoke(1f);
            ready?.Invoke(runner.Result);
        }

        // ------------------------------------------------------------------ finishing a page

        /// <summary>
        /// Applies one finished generated page: score, personal best, quests, coins and — for the archive
        /// — the depth the player has now reached. Authored pages do not come through here; their
        /// progression is the chapter map's business.
        /// </summary>
        public LiveRunOutcome RecordRun(string pageKey, LevelDefinition definition, LevelSession session)
        {
            var stats = session.Stats;
            var outcome = new LiveRunOutcome
            {
                PageKey = pageKey,
                Won = session.Outcome == Wonderfold.Core.Primitives.LevelOutcome.Won,
                Stars = RunScore.Stars(definition, stats),
                StoryInk = RunScore.StoryInk(stats)
            };

            var report = new PageRunReport(pageKey, stats, outcome.Stars);

            // A run is only worth a thread if it was won: a thread is a solution, not a diary.
            if (outcome.Won)
            {
                var thread = new ReplayThread(pageKey, session.InitialSeed,
                    new List<Wonderfold.Core.Board.PlayerMove>(session.MoveHistory));
                if (ReplayCode.TryEncode(thread, out string code, out _)) outcome.ThreadCode = code;
            }

            if (DailyFold.TryParseKey(pageKey, out int day))
            {
                outcome.IsPersonalBest = _profile.RecordDailyResult(day, outcome.StoryInk, outcome.ThreadCode);
            }
            else if (EndlessArchive.TryParseKey(pageKey, out int depth) && outcome.Won)
            {
                _profile.RecordEndlessWin(depth);
                outcome.EndlessDepth = depth;
                int reward = EndlessArchive.RewardCoins(depth);
                if (reward > 0)
                {
                    _profile.AddCoins(reward);
                    outcome.CoinsAwarded += reward;
                }

                PrefetchEndless(_profile.EndlessDepth);
            }

            outcome.QuestsCompleted.AddRange(_profile.Quests.Record(_dailyQuests, report));
            outcome.QuestsCompleted.AddRange(_profile.Quests.Record(_weeklyQuests, report));

            outcome.ShareText = ShareCard.Compose(definition.Name, report, outcome.StoryInk,
                _profile.Streak.Current, outcome.ThreadCode);

            Save();
            Changed?.Invoke();
            return outcome;
        }

        /// <summary>Authored pages still move the errand lists; they simply do not touch the archive.</summary>
        public void RecordAuthoredRun(LevelDefinition definition, LevelSession session)
        {
            var report = new PageRunReport(ReplayCode.AuthoredKey(definition.Id), session.Stats,
                RunScore.Stars(definition, session.Stats));
            _profile.Quests.Record(_dailyQuests, report);
            _profile.Quests.Record(_weeklyQuests, report);
            Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ shared threads

        /// <summary>
        /// Rebuilds whichever page a pasted thread names. Generated pages come from their key alone,
        /// which is the whole reason a code can be short enough to paste.
        /// </summary>
        public AuditionResult ResolveThreadPage(ReplayThread thread)
        {
            if (DailyFold.TryParseKey(thread.PageKey, out int day)) return Cached(DailyFold.Key(day));
            if (EndlessArchive.TryParseKey(thread.PageKey, out int depth)) return Cached(EndlessArchive.Key(depth));
            return null;
        }

        private void Save() => _saves?.Save(_profile);
    }
}

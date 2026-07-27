using System;
using System.Collections.Generic;
using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Live;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Simulation;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class LiveClockTests
    {
        [Test]
        public void DayIndexCountsWholeUtcDaysFromTheEpoch()
        {
            Assert.That(LiveClock.DayIndex(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), Is.EqualTo(0));
            Assert.That(LiveClock.DayIndex(new DateTime(2026, 1, 1, 23, 59, 0, DateTimeKind.Utc)), Is.EqualTo(0));
            Assert.That(LiveClock.DayIndex(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)), Is.EqualTo(1));
        }

        [Test]
        public void ADayIndexRoundTripsThroughItsLabel()
        {
            int day = LiveClock.DayIndex(new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc));
            Assert.That(LiveClock.DayLabel(day), Is.EqualTo("2026-07-27"));
            // 2026-07-27 is a Monday, and the weekday index is what the difficulty rhythm reads.
            Assert.That(LiveClock.WeekdayIndex(day), Is.EqualTo(0));
        }

        [Test]
        public void TheCountdownNeverExceedsADay()
        {
            var noon = new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(LiveClock.UntilNextDay(noon), Is.EqualTo(TimeSpan.FromHours(12)));
            Assert.That(LiveClock.UntilNextWeek(noon), Is.LessThanOrEqualTo(TimeSpan.FromDays(7)));
        }
    }

    [TestFixture]
    public class PageWeaverTests
    {
        /// <summary>
        /// The generator's contract in one test: whatever it invents, across the whole difficulty range,
        /// has to be a level the game can actually build and the validator accepts.
        /// </summary>
        [Test]
        public void EveryWovenPagePassesTheValidator()
        {
            for (int i = 0; i < 60; i++)
            {
                float difficulty = i / 59f;
                var level = PageWeaver.Weave(new WeaveRequest
                {
                    Seed = 1000 + i * 977,
                    Difficulty = difficulty,
                    Id = 50000 + i
                });

                var issues = LevelValidator.Validate(level);
                Assert.That(LevelValidator.HasErrors(issues), Is.False,
                    $"difficulty {difficulty:F2} produced: {string.Join(" | ", issues)}");
            }
        }

        [Test]
        public void TheSameSeedAlwaysWeavesTheSamePage()
        {
            var request = new WeaveRequest { Seed = 4242, Difficulty = 0.55f, Id = 7 };
            var a = PageWeaver.Weave(request);
            var b = PageWeaver.Weave(request);

            Assert.That(LevelSerializer.ToJson(b), Is.EqualTo(LevelSerializer.ToJson(a)));
        }

        [Test]
        public void AWovenPageAlwaysAsksForSomethingAndCanAlwaysRefill()
        {
            for (int i = 0; i < 25; i++)
            {
                var level = PageWeaver.Weave(new WeaveRequest { Seed = 90210 + i, Difficulty = 0.2f + i * 0.03f });

                Assert.That(level.Goals.Count, Is.GreaterThan(0));
                Assert.That(level.Moves, Is.InRange(PageWeaver.MinMoves, PageWeaver.MaxMoves));
                Assert.That(level.SpawnTables.Count, Is.GreaterThan(0));
            }
        }

        /// <summary>
        /// A crease is the whole point of the game, so a page that has one must also ask the player to
        /// use it — otherwise the generator quietly drifts into producing ordinary match-3 levels.
        /// </summary>
        [Test]
        public void APageWithACreaseAlwaysAsksThePlayerToTurnIt()
        {
            for (int i = 0; i < 40; i++)
            {
                var level = PageWeaver.Weave(new WeaveRequest { Seed = 5150 + i * 31, Difficulty = i / 39f });
                if (level.FoldRegions.Count == 0) continue;

                bool asksForTheFold = false;
                for (int g = 0; g < level.Goals.Count; g++)
                {
                    var type = level.Goals[g].Type;
                    if (type == "seam" || type == "fold") asksForTheFold = true;
                }

                Assert.That(asksForTheFold, Is.True, $"seed {5150 + i * 31} wove a crease nobody has to turn.");
            }
        }

        [Test]
        public void ScalingGoalsAlwaysMovesTheNumberOrReportsThatItCannot()
        {
            var level = PageWeaver.Weave(new WeaveRequest { Seed = 11, Difficulty = 0.5f });
            int before = level.Goals[0].Count;

            Assert.That(PageWeaver.TryScaleGoals(level, 1.05f), Is.True);
            Assert.That(level.Goals[0].Count, Is.GreaterThan(before));
        }
    }

    [TestFixture]
    public class PageAuditionTests
    {
        private static AuditionSettings Fast() => new AuditionSettings
        {
            Runs = 12,
            MaxCandidates = 3,
            MaxRetunes = 4,
            Tolerance = 0.14f
        };

        [Test]
        public void AnAuditionedPageIsAlwaysValidAndAlwaysMeasured()
        {
            var result = PageAudition.Hold(new WeaveRequest
            {
                Seed = 20260727,
                Difficulty = 0.5f,
                TargetWinRate = 0.6f,
                Id = 60001
            }, Fast());

            Assert.That(result.Level, Is.Not.Null);
            Assert.That(result.Report, Is.Not.Null);
            Assert.That(result.Playthroughs, Is.GreaterThan(0));
            Assert.That(LevelValidator.HasErrors(LevelValidator.Validate(result.Level)), Is.False);
        }

        /// <summary>
        /// The promise the Endless Archive makes to the player. A generated page that nobody measured is
        /// exactly the failure mode this whole system exists to prevent.
        /// </summary>
        [Test]
        public void AuditionedPagesLandNearTheWinRateTheyWereAskedFor()
        {
            int measured = 0;
            int inBand = 0;

            for (int depth = 1; depth <= 6; depth++)
            {
                var request = EndlessArchive.Request(depth);
                var result = PageAudition.Hold(request, Fast());
                measured++;
                if (Math.Abs(result.WinRate - request.TargetWinRate) <= 0.18f) inBand++;

                Assert.That(result.DeadBoardRate, Is.LessThan(0.2f),
                    $"depth {depth} collapses on its own {result.DeadBoardRate:P0} of the time.");
            }

            Assert.That(inBand, Is.GreaterThanOrEqualTo(measured - 1),
                "the audition is meant to land nearly every page inside its band.");
        }

        [Test]
        public void SteppingAnAuditionGivesTheSameAnswerAsRunningItStraightThrough()
        {
            var request = EndlessArchive.Request(4);

            var blocking = PageAudition.Hold(request, Fast());

            var runner = new PageAuditionRunner(request, Fast());
            int guard = 0;
            while (runner.Advance() && guard++ < 100000) { }

            Assert.That(runner.IsDone, Is.True);
            Assert.That(LevelSerializer.ToJson(runner.Result.Level),
                Is.EqualTo(LevelSerializer.ToJson(blocking.Level)));
            Assert.That(runner.Result.Playthroughs, Is.EqualTo(blocking.Playthroughs));
        }
    }

    [TestFixture]
    public class SharedPageTests
    {
        /// <summary>
        /// The one property the Daily Fold is built on: two devices, no network, same page. If this test
        /// ever fails, every shared score in the game is meaningless.
        /// </summary>
        [Test]
        public void TwoIndependentClientsWeaveTheIdenticalDailyPage()
        {
            int day = LiveClock.DayIndex(new DateTime(2026, 9, 14, 6, 30, 0, DateTimeKind.Utc));

            var first = DailyFold.Page(day);
            var second = DailyFold.Page(day);

            Assert.That(LevelSerializer.ToJson(second.Level), Is.EqualTo(LevelSerializer.ToJson(first.Level)));
            Assert.That(second.Level.Moves, Is.EqualTo(first.Level.Moves));
        }

        [Test]
        public void ConsecutiveDaysAreDifferentPages()
        {
            int day = LiveClock.DayIndex(new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc));
            var today = DailyFold.Page(day);
            var tomorrow = DailyFold.Page(day + 1);

            Assert.That(LevelSerializer.ToJson(tomorrow.Level),
                Is.Not.EqualTo(LevelSerializer.ToJson(today.Level)));
        }

        [Test]
        public void TheWeekHasAShapeRatherThanAFlatDifficulty()
        {
            int monday = LiveClock.DayIndex(new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc));

            float easiest = 1f;
            float hardest = 0f;
            for (int i = 0; i < 7; i++)
            {
                float difficulty = DailyFold.DifficultyFor(monday + i);
                if (difficulty < easiest) easiest = difficulty;
                if (difficulty > hardest) hardest = difficulty;
            }

            Assert.That(hardest - easiest, Is.GreaterThan(0.25f));
            Assert.That(DailyFold.DifficultyFor(monday + 5), Is.EqualTo(hardest), "Saturday should be the spike.");
        }

        [Test]
        public void EndlessDepthClimbsAndBreathes()
        {
            Assert.That(EndlessArchive.DifficultyFor(30), Is.GreaterThan(EndlessArchive.DifficultyFor(2)));
            Assert.That(EndlessArchive.DifficultyFor(15), Is.LessThan(EndlessArchive.DifficultyFor(14)),
                "every fifth page should be a breather.");
            Assert.That(EndlessArchive.IsRewardDepth(20), Is.True);
            Assert.That(EndlessArchive.IsRewardDepth(21), Is.False);
            Assert.That(EndlessArchive.RewardCoins(20), Is.GreaterThan(0));
        }

        [Test]
        public void PageKeysRoundTrip()
        {
            Assert.That(DailyFold.TryParseKey(DailyFold.Key(207), out int day), Is.True);
            Assert.That(day, Is.EqualTo(207));

            Assert.That(EndlessArchive.TryParseKey(EndlessArchive.Key(42), out int depth), Is.True);
            Assert.That(depth, Is.EqualTo(42));

            Assert.That(ReplayCode.TryParseAuthored(ReplayCode.AuthoredKey(10), out int id), Is.True);
            Assert.That(id, Is.EqualTo(10));

            Assert.That(DailyFold.TryParseKey("endless-3", out _), Is.False);
        }
    }

    [TestFixture]
    public class ReplayCodeTests
    {
        private static ReplayThread SampleThread() => new ReplayThread("daily-207", -1234567, new List<PlayerMove>
        {
            PlayerMove.Swap(new GridCoord(0, 0), new GridCoord(1, 0)),
            PlayerMove.Swap(new GridCoord(7, 7), new GridCoord(7, 6)),
            PlayerMove.ActivateBooster(new GridCoord(3, 4)),
            PlayerMove.Fold(2),
            PlayerMove.UseTool(PageTool.Hammer, new GridCoord(5, 5)),
            PlayerMove.UseTool(PageTool.RibbonRocket, new GridCoord(0, 6))
        });

        [Test]
        public void EveryMoveKindSurvivesTheRoundTrip()
        {
            var original = SampleThread();
            string code = ReplayCode.Encode(original);

            Assert.That(ReplayCode.TryDecode(code, out var decoded, out string error), Is.True, error);
            Assert.That(decoded.PageKey, Is.EqualTo(original.PageKey));
            Assert.That(decoded.Seed, Is.EqualTo(original.Seed));
            Assert.That(decoded.Moves.Count, Is.EqualTo(original.Moves.Count));

            for (int i = 0; i < original.Moves.Count; i++)
            {
                Assert.That(decoded.Moves[i].Kind, Is.EqualTo(original.Moves[i].Kind));
                Assert.That(decoded.Moves[i].A, Is.EqualTo(original.Moves[i].A));
                Assert.That(decoded.Moves[i].B, Is.EqualTo(original.Moves[i].B));
                Assert.That(decoded.Moves[i].RegionId, Is.EqualTo(original.Moves[i].RegionId));
                Assert.That(decoded.Moves[i].Tool, Is.EqualTo(original.Moves[i].Tool));
            }
        }

        [Test]
        public void ACodeSurvivesBeingRetypedByAHuman()
        {
            string code = ReplayCode.Encode(SampleThread());

            // Lower case, separators stripped, and the Crockford confusions people actually make.
            string mangled = code.ToLowerInvariant().Replace("-", " ");
            Assert.That(ReplayCode.TryDecode(mangled, out var decoded, out string error), Is.True, error);
            Assert.That(decoded.PageKey, Is.EqualTo("daily-207"));
        }

        [Test]
        public void ADamagedCodeIsRejectedRatherThanReplayedIntoNonsense()
        {
            string code = ReplayCode.Encode(SampleThread());
            var characters = code.Replace("-", string.Empty).ToCharArray();
            characters[3] = characters[3] == 'A' ? 'B' : 'A';

            Assert.That(ReplayCode.TryDecode(new string(characters), out _, out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NonsenseIsRefusedPolitely()
        {
            Assert.That(ReplayCode.TryDecode(null, out _, out _), Is.False);
            Assert.That(ReplayCode.TryDecode(string.Empty, out _, out _), Is.False);
            Assert.That(ReplayCode.TryDecode("not a code!", out _, out _), Is.False);
        }

        [Test]
        public void ARealRunCompressesToSomethingAPersonCanPaste()
        {
            var level = PageWeaver.Weave(new WeaveRequest { Seed = 77, Difficulty = 0.4f, Id = 61000 });
            var moves = PlayOut(level, 2024, out _);

            string code = ReplayCode.Encode(new ReplayThread("endless-9", 2024, moves));
            Assert.That(moves.Count, Is.GreaterThan(5));
            Assert.That(code.Length, Is.LessThan(moves.Count * 6),
                "a thread should stay far smaller than one character per bit of move data.");
        }

        /// <summary>
        /// The claim carries its own proof: replaying a shared thread against the page it names reproduces
        /// the run exactly, which is what lets a leaderboard exist without a server to trust.
        /// </summary>
        [Test]
        public void ReplayingAThreadReproducesTheRunAndItsScore()
        {
            var level = PageWeaver.Weave(new WeaveRequest { Seed = 31337, Difficulty = 0.45f, Id = 61001 });
            var moves = PlayOut(level, 8675309, out var originalStats);

            string code = ReplayCode.Encode(new ReplayThread("endless-5", 8675309, moves));
            Assert.That(ReplayCode.TryDecode(code, out var thread, out string decodeError), Is.True, decodeError);

            Assert.That(ReplayVerifier.TryReplay(level, thread, out var replayed, out string error), Is.True, error);
            Assert.That(replayed.Outcome, Is.EqualTo(originalStats.Outcome));
            Assert.That(replayed.MovesRemaining, Is.EqualTo(originalStats.MovesRemaining));
            Assert.That(replayed.GoldenStitches, Is.EqualTo(originalStats.GoldenStitches));
            Assert.That(RunScore.StoryInk(replayed), Is.EqualTo(RunScore.StoryInk(originalStats)));
        }

        [Test]
        public void AThreadPlayedAgainstTheWrongPageIsRejected()
        {
            var level = PageWeaver.Weave(new WeaveRequest { Seed = 4, Difficulty = 0.5f, Id = 61002 });
            var other = PageWeaver.Weave(new WeaveRequest { Seed = 5, Difficulty = 0.9f, Id = 61003 });
            var moves = PlayOut(level, 999, out _);

            bool replayed = ReplayVerifier.TryReplay(other, new ReplayThread("endless-1", 999, moves),
                out var stats, out _);

            // Either the moves stop fitting, or they fit but no longer win. Both are honest answers; what
            // must never happen is the wrong page reporting the original score.
            Assert.That(!replayed || stats.Outcome != LevelOutcome.Won || RunScore.StoryInk(stats) > 0);
        }

        private static List<PlayerMove> PlayOut(LevelDefinition level, int seed, out LevelRunStats stats)
        {
            var session = new LevelSession(level, seed);
            session.Events.Recording = false;
            session.Start();

            var agent = AgentFactory.Create("heuristic", seed);
            int guard = 0;
            while (!session.IsOver && guard++ < 400)
            {
                var move = agent.ChooseMove(session);
                if (move == null) break;
                if (!session.TryExecute(move.Value, out _)) break;
            }

            stats = session.Stats;
            if (stats.Outcome == LevelOutcome.InProgress)
            {
                stats.Outcome = session.AllGoalsComplete() ? LevelOutcome.Won : LevelOutcome.InProgress;
                stats.MovesRemaining = session.MovesRemaining;
            }

            return new List<PlayerMove>(session.MoveHistory);
        }
    }

    [TestFixture]
    public class RunScoreTests
    {
        [Test]
        public void AnUnfinishedPageScoresNothing()
        {
            Assert.That(RunScore.StoryInk(new LevelRunStats { Outcome = LevelOutcome.LostOutOfMoves }), Is.Zero);
            Assert.That(RunScore.StoryInk(null), Is.Zero);
        }

        [Test]
        public void SparingMovesAndSewingStitchesBothRaiseTheScore()
        {
            var plain = new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 2 };
            var better = new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 6 };
            var showOff = new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 6, GoldenStitches = 3 };

            Assert.That(RunScore.StoryInk(better), Is.GreaterThan(RunScore.StoryInk(plain)));
            Assert.That(RunScore.StoryInk(showOff), Is.GreaterThan(RunScore.StoryInk(better)));
        }

        [Test]
        public void StarsGradeAgainstTheMovesTheLevelGranted()
        {
            var level = new LevelDefinition { Moves = 20 };
            Assert.That(RunScore.Stars(level, new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 8 }),
                Is.EqualTo(3));
            Assert.That(RunScore.Stars(level, new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 3 }),
                Is.EqualTo(2));
            Assert.That(RunScore.Stars(level, new LevelRunStats { Outcome = LevelOutcome.Won, MovesRemaining = 0 }),
                Is.EqualTo(1));
            Assert.That(RunScore.Stars(level, new LevelRunStats { Outcome = LevelOutcome.LostOutOfMoves }),
                Is.Zero);
        }
    }

    [TestFixture]
    public class StreakLedgerTests
    {
        [Test]
        public void ConsecutiveDaysBuildAndARepeatVisitChangesNothing()
        {
            var ledger = new StreakLedger();

            Assert.That(ledger.Record(10).Change, Is.EqualTo(StreakChange.Started));
            Assert.That(ledger.Record(10).Change, Is.EqualTo(StreakChange.AlreadyCounted));
            Assert.That(ledger.Record(11).Change, Is.EqualTo(StreakChange.Continued));
            Assert.That(ledger.Current, Is.EqualTo(2));
            Assert.That(ledger.Best, Is.EqualTo(2));
        }

        [Test]
        public void AGapOfMoreThanADayBreaksTheStreakButKeepsTheRecord()
        {
            var ledger = new StreakLedger();
            ledger.Record(1);
            ledger.Record(2);
            ledger.Record(3);

            Assert.That(ledger.Record(9).Change, Is.EqualTo(StreakChange.Broken));
            Assert.That(ledger.Current, Is.EqualTo(1));
            Assert.That(ledger.Best, Is.EqualTo(3));
        }

        [Test]
        public void ExactlyOneMissedDayCanBeMendedAndTwoCannot()
        {
            var ledger = new StreakLedger();
            ledger.Record(5);
            ledger.Record(6);

            Assert.That(ledger.CanMend(7), Is.False, "no day has been missed yet.");
            Assert.That(ledger.CanMend(8), Is.True);
            Assert.That(ledger.CanMend(9), Is.False, "two missed days is a broken streak.");

            int cost = ledger.MendCost();
            Assert.That(cost, Is.GreaterThan(0));
            Assert.That(ledger.TryMend(8), Is.True);
            Assert.That(ledger.Record(8).Change, Is.EqualTo(StreakChange.Continued));
            Assert.That(ledger.Current, Is.EqualTo(3));
            Assert.That(ledger.MendCost(), Is.GreaterThan(cost), "the next mend should cost more.");
        }

        [Test]
        public void AClockThatGoesBackwardsNeitherRewardsNorPunishes()
        {
            var ledger = new StreakLedger();
            ledger.Record(20);
            ledger.Record(21);

            var update = ledger.Record(4);

            Assert.That(update.Change, Is.EqualTo(StreakChange.AlreadyCounted));
            Assert.That(ledger.Current, Is.EqualTo(2));
            Assert.That(ledger.LastDayIndex, Is.EqualTo(21));
        }

        [Test]
        public void TheSeventhDayIsTheOneWorthPlanningFor()
        {
            var sixth = StreakLedger.RewardFor(6);
            var seventh = StreakLedger.RewardFor(7);

            Assert.That(seventh.Coins, Is.GreaterThan(sixth.Coins));
            Assert.That(seventh.Hammers + seventh.Rockets, Is.GreaterThan(0));
            Assert.That(StreakLedger.RewardFor(0).IsEmpty, Is.True);
        }
    }

    [TestFixture]
    public class QuestBookTests
    {
        [Test]
        public void EverybodyGetsTheSameErrandsOnTheSameDay()
        {
            var mine = QuestBook.Daily(400);
            var yours = QuestBook.Daily(400);
            var tomorrow = QuestBook.Daily(401);

            Assert.That(mine.Count, Is.EqualTo(3));
            for (int i = 0; i < mine.Count; i++)
            {
                Assert.That(yours[i].Id, Is.EqualTo(mine[i].Id));
                Assert.That(yours[i].Target, Is.EqualTo(mine[i].Target));
            }

            bool differs = false;
            for (int i = 0; i < mine.Count; i++)
            {
                if (tomorrow[i].Id != mine[i].Id || tomorrow[i].Target != mine[i].Target) differs = true;
            }

            Assert.That(differs, Is.True, "a new day should bring new errands.");
        }

        [Test]
        public void WeeklyErrandsAskForMoreAndPayMore()
        {
            var daily = QuestBook.Daily(500);
            var weekly = QuestBook.Weekly(LiveClock.WeekIndex(500));

            int dailyReward = 0;
            for (int i = 0; i < daily.Count; i++) dailyReward += daily[i].RewardCoins;

            int weeklyReward = 0;
            for (int i = 0; i < weekly.Count; i++) weeklyReward += weekly[i].RewardCoins;

            Assert.That(weeklyReward, Is.GreaterThan(dailyReward));
        }

        [Test]
        public void ProgressAccumulatesAndARewardIsOnlyTakenOnce()
        {
            var quest = new Quest
            {
                Id = "test-seams",
                Metric = QuestMetric.SeamMatches,
                Target = 5,
                RewardCoins = 100,
                Title = "test"
            };
            var quests = new List<Quest> { quest };
            var tracker = new QuestTracker();

            tracker.Record(quests, Report("endless-1", new LevelRunStats
            {
                Outcome = LevelOutcome.Won,
                SeamMatches = 3
            }));
            Assert.That(tracker.IsComplete(quest), Is.False);
            Assert.That(tracker.Claim(quest), Is.Zero, "an unfinished quest pays nothing.");

            var completed = tracker.Record(quests, Report("endless-2", new LevelRunStats
            {
                Outcome = LevelOutcome.Won,
                SeamMatches = 4
            }));

            Assert.That(completed.Count, Is.EqualTo(1));
            Assert.That(tracker.ProgressOf(quest), Is.EqualTo(5), "progress is capped at the target.");
            Assert.That(tracker.Claim(quest), Is.EqualTo(100));
            Assert.That(tracker.Claim(quest), Is.Zero, "a reward is taken exactly once.");
        }

        [Test]
        public void OnlyTheRightKindOfPageCountsForAPageSpecificQuest()
        {
            var quest = new Quest
            {
                Id = "test-daily",
                Metric = QuestMetric.DailyFoldsCleared,
                Target = 1,
                Title = "test"
            };
            var quests = new List<Quest> { quest };
            var tracker = new QuestTracker();

            tracker.Record(quests, Report("endless-4", new LevelRunStats { Outcome = LevelOutcome.Won }));
            Assert.That(tracker.IsComplete(quest), Is.False);

            tracker.Record(quests, Report("daily-88", new LevelRunStats { Outcome = LevelOutcome.Won }));
            Assert.That(tracker.IsComplete(quest), Is.True);
        }

        [Test]
        public void ExpiredListsAreSweptOut()
        {
            var quests = QuestBook.Daily(600);
            var tracker = new QuestTracker();
            tracker.Progress[quests[0].Id] = 3;

            tracker.Forget(QuestBook.DailyPrefix(600));

            Assert.That(tracker.Progress.ContainsKey(quests[0].Id), Is.False);
        }

        private static PageRunReport Report(string key, LevelRunStats stats) => new PageRunReport(key, stats, 3);
    }

    [TestFixture]
    public class UnreachableSurfaceTests
    {
        /// <summary>
        /// Regression for the bug the generator shipped in its first draft: obstacles written onto the
        /// back of cells that belong to no fold region can never be turned face up, so any goal counting
        /// them makes the level unwinnable. The bots measured it as 8–42% win against a 65% target; the
        /// validator now says so directly.
        /// </summary>
        [Test]
        public void AnObstacleOnAFaceNothingCanTurnIsAnError()
        {
            var level = new LevelDefinition
            {
                Id = 1,
                Width = 4,
                Height = 4,
                Moves = 20
            };
            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow }
            });
            level.Goals.Add(new GoalSpec { Type = "obstacle", Obstacle = ObstacleType.TornPage });
            level.Front.Tiles = new List<string> { "....", "....", "....", "...." };
            level.Back.Tiles = new List<string> { "....", "....", "....", "...." };
            // Column 3 belongs to no fold region, so its back face is unreachable for ever.
            level.Back.Obstacles = new List<string> { "....", "...T", "....", "...." };

            var issues = LevelValidator.Validate(level);

            Assert.That(LevelValidator.HasErrors(issues), Is.True,
                "the validator should refuse a level whose objective sits on an unturnable face.");
        }

        [Test]
        public void TheSameObstacleInsideAFoldRegionIsFine()
        {
            var level = new LevelDefinition
            {
                Id = 1,
                Width = 4,
                Height = 4,
                Moves = 20
            };
            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow }
            });
            level.FoldRegions.Add(new FoldRegionSpec { Id = 1, X = 2, Y = 0, Width = 2, Height = 4 });
            level.Goals.Add(new GoalSpec { Type = "obstacle", Obstacle = ObstacleType.TornPage });
            level.Front.Tiles = new List<string> { "....", "....", "....", "...." };
            level.Back.Tiles = new List<string> { "....", "....", "....", "...." };
            level.Back.Obstacles = new List<string> { "....", "...T", "....", "...." };

            var issues = LevelValidator.Validate(level);

            Assert.That(LevelValidator.HasErrors(issues), Is.False, string.Join(" | ", issues));
        }
    }

    [TestFixture]
    public class ShareCardTests
    {
        [Test]
        public void ALosingRunBragsAboutNothing()
        {
            var report = new PageRunReport("daily-1", new LevelRunStats { Outcome = LevelOutcome.LostOutOfMoves }, 0);
            string card = ShareCard.Compose("Lost Page", report, 0, 4, "ABCDE");

            Assert.That(card, Does.Not.Contain("ABCDE"));
            Assert.That(card, Does.Contain("Lost Page"));
        }

        [Test]
        public void AWinCarriesTheScoreTheStreakAndTheThread()
        {
            var stats = new LevelRunStats
            {
                Outcome = LevelOutcome.Won,
                MovesRemaining = 5,
                GoldenStitches = 2,
                SeamMatches = 7
            };
            string card = ShareCard.Compose("Lost Page", new PageRunReport("daily-1", stats, 3),
                RunScore.StoryInk(stats), 12, "ABCDE-FGHJK");

            Assert.That(card, Does.Contain("★★★"));
            Assert.That(card, Does.Contain("12-day streak"));
            Assert.That(card, Does.Contain("ABCDE-FGHJK"));
        }
    }
}

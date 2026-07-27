using System.Collections.Generic;
using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Simulation;

namespace Wonderfold.Tests
{
    /// <summary>
    /// Invariants that must hold for <i>every</i> board, checked by driving many seeded games through
    /// the real engine rather than by asserting a handful of hand-picked cases.
    ///
    /// <para>These are the tests that catch what unit tests do not: a booster that quietly eats a tile,
    /// a cascade that never settles, a fold that leaves a coordinate belonging to two faces at once.</para>
    /// </summary>
    [TestFixture]
    public class PropertyTests
    {
        private const int Seeds = 40;
        private const int MovesPerRun = 25;

        private static LevelDefinition FoldingLevel()
        {
            var level = new LevelDefinition
            {
                Id = 999,
                Name = "Property Harness",
                Width = 8,
                Height = 8,
                Moves = 60
            };

            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow, TileColor.Amber, TileColor.Violet }
            });

            level.FoldRegions.Add(new FoldRegionSpec
            {
                Id = 1, X = 5, Y = 0, Width = 3, Height = 8,
                FrontGravity = Direction.Down, BackGravity = Direction.Right,
                MeterCost = 30, MoveCost = 0
            });

            level.FoldRegions.Add(new FoldRegionSpec
            {
                Id = 2, X = 0, Y = 5, Width = 4, Height = 3,
                FlipAxis = Axis.Horizontal, MeterCost = 30, MoveCost = 0
            });

            var rows = new List<string>();
            for (int i = 0; i < 8; i++) rows.Add("........");
            level.Front.Tiles = new List<string>(rows);
            level.Back.Tiles = new List<string>(rows);

            level.Front.Obstacles = new List<string>
            {
                "........", "..T..W..", "........", "...TT...",
                "........", "..W..T..", "........", "........"
            };

            level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Crimson, Count = 500 });
            return level;
        }

        [Test]
        public void EveryPlayableCellHoldsATileOnceTheBoardSettles()
        {
            RunMany((session, move) =>
            {
                foreach (var coord in session.Board.AllCoords())
                {
                    var cell = session.Board.ActiveCell(coord);
                    if (!cell.CanHoldTile) continue;
                    Assert.That(cell.Tile, Is.Not.Null,
                        $"{coord} settled empty after {move}\n{session.Board.ToAscii()}");
                }
            });
        }

        [Test]
        public void NoMatchIsLeftSittingOnASettledBoard()
        {
            var finder = new MatchFinder();
            RunMany((session, move) =>
                Assert.That(finder.HasAnyMatch(session.Board), Is.False,
                    $"a resolved match survived {move}\n{session.Board.ToAscii()}"));
        }

        [Test]
        public void EveryCoordinateBelongsToExactlyOneFace()
        {
            RunMany((session, move) =>
            {
                foreach (var coord in session.Board.AllCoords())
                {
                    var active = session.Board.ActiveCell(coord);
                    var hidden = session.Board.HiddenCell(coord);
                    Assert.That(active.Side, Is.Not.EqualTo(hidden.Side), $"{coord} after {move}");
                    Assert.That(active.Coord, Is.EqualTo(coord));
                    Assert.That(hidden.Coord, Is.EqualTo(coord));
                }
            });
        }

        [Test]
        public void TileIdentitiesAreNeverDuplicated()
        {
            var seen = new HashSet<int>();
            RunMany((session, move) =>
            {
                seen.Clear();
                foreach (var cell in session.Board.AllCells())
                {
                    if (cell.Tile == null) continue;
                    Assert.That(seen.Add(cell.Tile.Id), Is.True,
                        $"tile id {cell.Tile.Id} appears twice after {move}");
                }
            });
        }

        [Test]
        public void GoalProgressNeverRunsBackwards()
        {
            var previous = new Dictionary<string, int>();
            RunMany((session, move) =>
            {
                for (int i = 0; i < session.Goals.Count; i++)
                {
                    var goal = session.Goals[i];
                    previous.TryGetValue(goal.Id, out int before);
                    Assert.That(goal.Current, Is.GreaterThanOrEqualTo(before),
                        $"goal {goal.Id} went backwards after {move}");
                    previous[goal.Id] = goal.Current;
                }
            }, resetBetweenRuns: previous.Clear);
        }

        [Test]
        public void MovesOnlyEverDecrease()
        {
            int previous = int.MaxValue;
            RunMany((session, move) =>
            {
                Assert.That(session.MovesRemaining, Is.LessThanOrEqualTo(previous));
                previous = session.MovesRemaining;
            }, resetBetweenRuns: () => previous = int.MaxValue);
        }

        [Test]
        public void TheSameSeedAndTheSameMovesAlwaysProduceTheSameBoard()
        {
            var level = FoldingLevel();

            for (int seed = 1; seed <= 8; seed++)
            {
                var first = PlayScripted(level, seed);
                var second = PlayScripted(level, seed);

                Assert.That(second.Board, Is.EqualTo(first.Board), $"seed {seed} diverged");
                Assert.That(second.Events, Is.EqualTo(first.Events), $"seed {seed} produced a different event stream");
                Assert.That(second.Meter, Is.EqualTo(first.Meter));
            }
        }

        [Test]
        public void ARejectedMoveLeavesTheBoardExactlyAsItWas()
        {
            var level = FoldingLevel();
            var session = new LevelSession(level, 12);
            session.Start();

            var before = session.Board.ToAscii() + session.Board.ToAscii(true);
            int movesBefore = session.MovesRemaining;

            // Diagonal, so it can never be legal.
            bool accepted = session.TryExecute(PlayerMove.Swap(0, 0, 1, 1), out _);

            Assert.That(accepted, Is.False);
            Assert.That(session.Board.ToAscii() + session.Board.ToAscii(true), Is.EqualTo(before));
            Assert.That(session.MovesRemaining, Is.EqualTo(movesBefore));
        }

        [Test]
        public void CloningABoardProducesAnIndependentCopy()
        {
            var session = new LevelSession(FoldingLevel(), 5);
            session.Start();

            var clone = session.Board.Clone();
            Assert.That(clone.ToAscii(), Is.EqualTo(session.Board.ToAscii()));

            clone.ActiveCell(0, 0).Tile = null;
            Assert.That(session.Board.ActiveCell(0, 0).Tile, Is.Not.Null,
                "mutating the clone must not reach the original — bot look-ahead depends on it");
        }

        // ------------------------------------------------------------------ harness

        private readonly struct Snapshot
        {
            public readonly string Board;
            public readonly string Events;
            public readonly int Meter;

            public Snapshot(string board, string events, int meter)
            {
                Board = board;
                Events = events;
                Meter = meter;
            }
        }

        private static Snapshot PlayScripted(LevelDefinition level, int seed)
        {
            var session = new LevelSession(level, seed);
            session.Start();

            var agent = new HeuristicAgent(seed);
            var log = new System.Text.StringBuilder();

            for (int i = 0; i < MovesPerRun && !session.IsOver; i++)
            {
                var move = agent.ChooseMove(session);
                if (move == null) break;
                session.TryExecute(move.Value, out _);
            }

            var events = session.Events.Events;
            for (int i = 0; i < events.Count; i++) log.Append(events[i]).Append('\n');

            return new Snapshot(session.Board.ToAscii() + session.Board.ToAscii(true), log.ToString(),
                session.FoldMeter.Value);
        }

        private static void RunMany(System.Action<LevelSession, PlayerMove> check,
            System.Action resetBetweenRuns = null)
        {
            var level = FoldingLevel();

            for (int seed = 1; seed <= Seeds; seed++)
            {
                resetBetweenRuns?.Invoke();

                var session = new LevelSession(level, seed);
                session.Events.Recording = false;
                session.Start();

                var agent = new HeuristicAgent(seed);
                for (int i = 0; i < MovesPerRun && !session.IsOver; i++)
                {
                    var move = agent.ChooseMove(session);
                    if (move == null) break;
                    if (!session.TryExecute(move.Value, out _)) continue;
                    check(session, move.Value);
                }
            }
        }
    }
}

using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Boosters;
using Wonderfold.Core.Events;
using Wonderfold.Core.Obstacles;
using Wonderfold.Core.Primitives;
using Wonderfold.Core.Rules;
using Wonderfold.Core.Simulation;
using Wonderfold.Core.Level;
using System.Collections.Generic;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class BoosterTests
    {
        private ResolutionEngine _engine;
        private BoardEventLog _events;
        private CountingListener _counter;

        [SetUp]
        public void SetUp()
        {
            _engine = new ResolutionEngine(TestBoards.Rules());
            _events = new BoardEventLog();
            _counter = new CountingListener();
        }

        [Test]
        public void FourInALineMintsARocketAlongThatLine()
        {
            var rules = TestBoards.Rules();
            var group = new MatchGroup { Color = TileColor.Crimson, LongestHorizontal = 4 };
            group.Cells.Add(new GridCoord(0, 0));

            Assert.That(BoosterFactory.TryCreate(rules, group, out var type, out var orientation), Is.True);
            Assert.That(type, Is.EqualTo(BoosterType.RibbonRocket));
            Assert.That(orientation, Is.EqualTo(BoosterOrientation.Horizontal));
        }

        [Test]
        public void FiveInALineMintsAColourClear()
        {
            var group = new MatchGroup { Color = TileColor.Azure, LongestVertical = 5 };
            group.Cells.Add(new GridCoord(0, 0));

            BoosterFactory.TryCreate(TestBoards.Rules(), group, out var type, out _);

            Assert.That(type, Is.EqualTo(BoosterType.PrismBookmark));
        }

        [Test]
        public void ASeamCrossingMatchMintsTheGoldenStitchInsteadOfTheOrdinaryReward()
        {
            var group = new MatchGroup { Color = TileColor.Amber, LongestHorizontal = 4, CrossesSeam = true };
            for (int x = 0; x < 4; x++) group.Cells.Add(new GridCoord(x, 0));

            BoosterFactory.TryCreate(TestBoards.Rules(), group, out var type, out _);

            Assert.That(type, Is.EqualTo(BoosterType.GoldenStitch));
        }

        [Test]
        public void ShortSeamMatchesDoNotMintAStitch()
        {
            var rules = TestBoards.Rules();
            rules.SeamStitchMinLength = 4;
            var group = new MatchGroup { Color = TileColor.Amber, LongestHorizontal = 3, CrossesSeam = true };
            group.Cells.Add(new GridCoord(0, 0));
            group.Cells.Add(new GridCoord(1, 0));
            group.Cells.Add(new GridCoord(2, 0));

            BoosterFactory.TryCreate(rules, group, out var type, out _);

            Assert.That(type, Is.EqualTo(BoosterType.None));
        }

        [Test]
        public void ARocketClearsItsWholeRow()
        {
            var board = TestBoards.Build(
                "SSSSS",
                "rgbrg",
                "gb-rg",
                "brgbr");

            _engine.Boosters.Detonate(board, new GridCoord(2, 1), _events, _counter);

            Assert.That(_counter.TilesCleared, Is.GreaterThanOrEqualTo(5),
                "four neighbours in the row plus the rocket itself");
        }

        [Test]
        public void ChainedBoostersDoNotRecurseIntoTheStack()
        {
            // A row of rockets, each setting off the next. If chaining recursed this would overflow.
            var board = TestBoards.Build(
                "SSSSSSSS",
                "--------",
                "rgbrgbrg");

            Assert.DoesNotThrow(() =>
                _engine.Boosters.Detonate(board, new GridCoord(0, 1), _events, _counter));
        }

        [Test]
        public void WaxSealsIgnoreMatchesAndYieldToBlasts()
        {
            var obstacles = new ObstacleService();
            var board = TestBoards.Build(
                "SSS",
                "rWr",
                "ggg");

            var seal = new CellRef(new GridCoord(1, 1), SurfaceSide.Front);

            obstacles.Damage(board, seal, DamageSource.AdjacentMatch, _events, _counter);
            Assert.That(board.CellAt(seal).Obstacle, Is.Not.Null, "a match must not touch a seal");

            obstacles.Damage(board, seal, DamageSource.Blast, _events, _counter);
            Assert.That(board.CellAt(seal).Obstacle, Is.Null, "a blast must break it");
        }

        [Test]
        public void TornPagesTakeTwoAdjacentMatchesToClear()
        {
            var obstacles = new ObstacleService();
            var board = TestBoards.Build(
                "SSS",
                "rTr",
                "ggg");

            var page = new CellRef(new GridCoord(1, 1), SurfaceSide.Front);

            obstacles.Damage(board, page, DamageSource.AdjacentMatch, _events, _counter);
            Assert.That(board.CellAt(page).Obstacle, Is.Not.Null);

            obstacles.Damage(board, page, DamageSource.AdjacentMatch, _events, _counter);
            Assert.That(board.CellAt(page).Obstacle, Is.Null);
        }

        [Test]
        public void CuttingAStoryKnotOnOneFaceReleasesBoth()
        {
            var obstacles = new ObstacleService();
            var board = TestBoards.Build(
                "SSS",
                "rKr",
                "ggg");

            var front = new CellRef(new GridCoord(1, 1), SurfaceSide.Front);
            board.CellAt(front.Twin).Obstacle = new Obstacle(ObstacleType.StoryKnot);

            obstacles.Damage(board, front, DamageSource.Blast, _events, _counter);

            Assert.That(board.CellAt(front).Obstacle, Is.Null);
            Assert.That(board.CellAt(front.Twin).Obstacle, Is.Null,
                "one knot threaded through the page — cut it anywhere and both faces free");
        }

        [Test]
        public void APaperChainOnlyFallsWhenEveryLinkIsCut()
        {
            var obstacles = new ObstacleService();
            var board = TestBoards.Build(
                "SSS",
                "rgb",
                "ggg");

            var a = new CellRef(new GridCoord(0, 1), SurfaceSide.Front);
            var b = new CellRef(new GridCoord(1, 1), SurfaceSide.Front);
            board.CellAt(a).Obstacle = new Obstacle(ObstacleType.PaperChain, 1, 7);
            board.CellAt(b).Obstacle = new Obstacle(ObstacleType.PaperChain, 1, 7);

            obstacles.Damage(board, a, DamageSource.Blast, _events, _counter);
            Assert.That(board.CellAt(a).Obstacle, Is.Not.Null, "one cut link does not free the chain");

            obstacles.Damage(board, b, DamageSource.Blast, _events, _counter);
            Assert.That(board.CellAt(a).Obstacle, Is.Null);
            Assert.That(board.CellAt(b).Obstacle, Is.Null);
        }

        [Test]
        public void ChainFoldUpgradePathTopsOutAtTheGoldenStitch()
        {
            Assert.That(BoosterFactory.Upgrade(BoosterType.RibbonRocket), Is.EqualTo(BoosterType.InkBloom));
            Assert.That(BoosterFactory.Upgrade(BoosterType.InkBloom), Is.EqualTo(BoosterType.OrigamiBird));
            Assert.That(BoosterFactory.Upgrade(BoosterType.OrigamiBird), Is.EqualTo(BoosterType.PrismBookmark));
            Assert.That(BoosterFactory.Upgrade(BoosterType.PrismBookmark), Is.EqualTo(BoosterType.GoldenStitch));
            Assert.That(BoosterFactory.Upgrade(BoosterType.GoldenStitch), Is.EqualTo(BoosterType.GoldenStitch));
        }

        [Test]
        public void DraggingABoosterOntoAnOrdinaryTileIsLegalAndFiresIt()
        {
            var board = TestBoards.Build(
                "SSSSS",
                "rgbrg",
                "-grbg",
                "brgbr");

            var validator = new MoveValidator(new MatchFinder());
            Assert.That(validator.ValidateSwap(board, new GridCoord(0, 1), new GridCoord(1, 1)).IsValid, Is.True,
                "aiming a booster is the only way to reach blast-only obstacles");

            _engine.TrySwap(board, new GridCoord(0, 1), new GridCoord(1, 1), _events, _counter, out _);

            Assert.That(_counter.BoosterDetonations, Is.GreaterThan(0));
        }

        [Test]
        public void PageHammerIsAReplayableMoveThatDoesNotSpendBoardMoves()
        {
            var level = new LevelDefinition { Id = 99, Width = 3, Height = 3, Moves = 5 };
            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow }
            });
            level.Front.Tiles = new List<string> { "...", "...", "..." };
            level.Front.Obstacles = new List<string> { "...", ".W.", "..." };
            level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Amber, Count = 99 });

            var session = new LevelSession(level, 42);
            session.Start();
            session.Events.Drain();
            int movesBefore = session.MovesRemaining;
            var move = PlayerMove.UseTool(PageTool.Hammer, new GridCoord(1, 1));

            Assert.That(session.TryExecute(move, out var reason), Is.True, reason);

            Assert.That(move.Kind, Is.EqualTo(MoveKind.UseTool));
            Assert.That(move.Tool, Is.EqualTo(PageTool.Hammer));
            Assert.That(session.MovesRemaining, Is.EqualTo(movesBefore));
            Assert.That(session.Board.ActiveCell(new GridCoord(1, 1)).Obstacle, Is.Null,
                "the hammer is a precise blast, so it can open a wax seal");
        }

        [Test]
        public void PackedStartingRocketAppearsBeforeTheFirstPlayerTurn()
        {
            var level = new LevelDefinition { Id = 100, Width = 3, Height = 3, Moves = 5 };
            level.SpawnTables.Add(new SpawnTableSpec
            {
                Colors = { TileColor.Crimson, TileColor.Azure, TileColor.Meadow }
            });
            level.Front.Tiles = new List<string> { "...", "...", "..." };
            level.Goals.Add(new GoalSpec { Type = "collect", Color = TileColor.Amber, Count = 99 });

            var session = new LevelSession(level, 7);
            session.Start();
            int movesBefore = session.MovesRemaining;

            Assert.That(session.TryPlaceStartingRocket(), Is.True);
            Assert.That(session.MovesRemaining, Is.EqualTo(movesBefore));
            Assert.That(session.Board.ActiveCell(new GridCoord(1, 1)).Tile.Booster,
                Is.EqualTo(BoosterType.RibbonRocket));
        }
    }
}

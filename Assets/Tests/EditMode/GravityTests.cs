using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class GravityTests
    {
        private GravityService _gravity;
        private BoardEventLog _events;

        [SetUp]
        public void SetUp()
        {
            _gravity = new GravityService(TestBoards.Rules(), new MatchFinder());
            _events = new BoardEventLog();
        }

        [Test]
        public void TilesFallIntoHolesBeneathThem()
        {
            var board = TestBoards.Build(
                "S",
                "r",
                "_");

            _gravity.Settle(board, _events);

            Assert.That(board.ActiveCell(0, 0).Tile.Color, Is.EqualTo(TileColor.Crimson));
        }

        [Test]
        public void SpawnersRefillEveryEmptyCell()
        {
            var board = TestBoards.Build(
                "SSS",
                "___",
                "___");

            _gravity.Settle(board, _events);

            Assert.That(TestBoards.CountTilesOnActiveSurface(board), Is.EqualTo(9));
        }

        [Test]
        public void BlockingObstaclesAreNotFallenThrough()
        {
            var board = TestBoards.Build(
                "S",
                "W",
                "_");

            _gravity.Settle(board, _events);

            Assert.That(board.ActiveCell(0, 0).Tile, Is.Null, "the cell under a wax seal cannot be reached");
            Assert.That(board.ActiveCell(0, 1).Tile, Is.Null, "the seal itself holds no tile");
        }

        [Test]
        public void TilesSlideDiagonallyPastABlockedColumn()
        {
            // Nothing can fall straight into (1,0); it must topple off the shoulders of the seal.
            var board = TestBoards.Build(
                "SSS",
                "rWr",
                "_W_");

            _gravity.Settle(board, _events);

            Assert.That(board.ActiveCell(1, 0).Tile, Is.Null, "the seal still blocks its own cell");
            Assert.That(board.ActiveCell(0, 0).Tile, Is.Not.Null);
            Assert.That(board.ActiveCell(2, 0).Tile, Is.Not.Null);
        }

        [Test]
        public void SettlingIsIdempotent()
        {
            var board = TestBoards.Build(
                "SSS",
                "rgb",
                "___");

            _gravity.Settle(board, _events);
            var settled = board.ToAscii();

            bool movedAgain = _gravity.Settle(board, _events);

            Assert.That(movedAgain, Is.False);
            Assert.That(board.ToAscii(), Is.EqualTo(settled));
        }

        [Test]
        public void AFoldedRegionThatFallsSidewaysRefillsFromItsOwnUpstreamEdge()
        {
            // The regression that made an entire chapter unwinnable: at the upstream edge of a sideways
            // gravity zone, nothing can flow in and — before the fix — nothing would spawn either.
            var region = new FoldRegion(1, new GridRect(2, 0, 2, 3), Axis.Vertical,
                Direction.Down, Direction.Right);

            var board = TestBoards.Build(new[] { region }, 1,
                "SSSS",
                "rgbr",
                "gbrg");

            for (int y = 0; y < board.Height; y++)
            for (int x = 2; x < 4; x++)
            {
                var cell = board.CellAt(new GridCoord(x, y), SurfaceSide.Back);
                cell.Tile = null;
                cell.Role = x == 2 ? CellRole.Spawner : CellRole.Playable;
            }

            board.Folds.Flip(0);
            _gravity.Settle(board, _events);

            for (int y = 0; y < board.Height; y++)
            for (int x = 2; x < 4; x++)
            {
                Assert.That(board.ActiveCell(x, y).Tile, Is.Not.Null,
                    $"the sideways region left {new GridCoord(x, y)} empty");
            }
        }

        [Test]
        public void HiddenSurfacesAreFrozen()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 3));
            var board = TestBoards.Build(new[] { region }, 1,
                "SS",
                "rg",
                "gb");

            var hidden = board.CellAt(new GridCoord(0, 2), SurfaceSide.Back);
            hidden.Tile = null;
            hidden.Role = CellRole.Playable;

            _gravity.Settle(board, _events);

            Assert.That(hidden.Tile, Is.Null,
                "a face that is turned away is closed paper; it must not settle behind the player's back");
        }
    }
}

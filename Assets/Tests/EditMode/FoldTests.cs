using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Boosters;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class FoldTests
    {
        private ResolutionEngine _engine;
        private FoldService _folds;
        private BoardEventLog _events;

        [SetUp]
        public void SetUp()
        {
            _engine = new ResolutionEngine(TestBoards.Rules());
            _folds = new FoldService();
            _events = new BoardEventLog();
        }

        private static BoardModel FoldableBoard(params FoldRegion[] regions) =>
            TestBoards.Build(regions, 1,
                "SSSS",
                "rgbr",
                "gbrg",
                "brgb");

        [Test]
        public void FoldingSwapsWhichFaceIsInPlayWithoutMovingAnyCoordinate()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4));
            var board = FoldableBoard(region);

            var frontTile = board.CellAt(new GridCoord(0, 0), SurfaceSide.Front).Tile;
            var backTile = board.CellAt(new GridCoord(0, 0), SurfaceSide.Back).Tile;

            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);

            Assert.That(board.ActiveSide(new GridCoord(0, 0)), Is.EqualTo(SurfaceSide.Back));
            Assert.That(board.CellAt(new GridCoord(0, 0), SurfaceSide.Front).Tile, Is.SameAs(frontTile),
                "the front face keeps its tiles; it is merely turned away");
            Assert.That(board.ActiveCell(new GridCoord(0, 0)).Tile, Is.SameAs(backTile));
        }

        [Test]
        public void CellsOutsideTheRegionAreUnaffected()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4));
            var board = FoldableBoard(region);

            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);

            Assert.That(board.ActiveSide(new GridCoord(3, 0)), Is.EqualTo(SurfaceSide.Front));
        }

        [Test]
        public void FoldingTwiceReturnsToTheOriginalFace()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4));
            var board = FoldableBoard(region);

            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);
            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);

            Assert.That(board.ActiveSide(new GridCoord(0, 0)), Is.EqualTo(SurfaceSide.Front));
            Assert.That(board.Folds.FoldCountOfRegion(0), Is.EqualTo(2));
        }

        [Test]
        public void EveryCoordinateResolvesToExactlyOneFaceAtAllTimes()
        {
            var a = new FoldRegion(1, new GridRect(0, 0, 2, 4));
            var b = new FoldRegion(2, new GridRect(2, 0, 2, 4), Axis.Horizontal);
            var board = FoldableBoard(a, b);

            for (int step = 0; step < 6; step++)
            {
                _folds.Fold(board, step % 2, _engine, _events, NullResolutionListener.Instance);

                foreach (var coord in board.AllCoords())
                {
                    var side = board.ActiveSide(coord);
                    var active = board.ActiveCell(coord);
                    var hidden = board.HiddenCell(coord);

                    Assert.That(active.Side, Is.EqualTo(side));
                    Assert.That(hidden.Side, Is.Not.EqualTo(side));
                    Assert.That(active, Is.Not.SameAs(hidden));
                }
            }
        }

        [Test]
        public void OverlappingFoldRegionsAreRejectedAtConstruction()
        {
            var a = new FoldRegion(1, new GridRect(0, 0, 3, 3));
            var b = new FoldRegion(2, new GridRect(2, 0, 3, 3));

            Assert.Throws<System.InvalidOperationException>(
                () => new FoldTopology(6, 3, new[] { a, b }));
        }

        [Test]
        public void ChainFoldCarriesABoosterOnTheCreaseThroughAndUpgradesIt()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4));
            var board = FoldableBoard(region);

            // (1, 2) is inside the region and borders the cell outside it — the crease.
            var crease = new GridCoord(1, 2);
            Assert.That(board.Folds.IsFoldLineCell(crease), Is.True);
            board.ActiveCell(crease).Tile = board.CreateBooster(
                BoosterType.RibbonRocket, TileColor.Crimson, BoosterOrientation.Horizontal);

            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);

            var landed = board.ActiveCell(crease).Tile;
            Assert.That(landed, Is.Not.Null);
            Assert.That(landed.Booster, Is.EqualTo(BoosterType.InkBloom),
                "a rocket dragged through the page comes out one rung up the ladder");
        }

        [Test]
        public void ABoosterAwayFromTheCreaseStaysWhereItIs()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 3, 4));
            var board = TestBoards.Build(new[] { region }, 1,
                "SSSS",
                "rgbr",
                "gbrg",
                "brgb");

            var interior = new GridCoord(0, 2);
            Assert.That(board.Folds.IsFoldLineCell(interior), Is.False);
            board.ActiveCell(interior).Tile = board.CreateBooster(
                BoosterType.RibbonRocket, TileColor.Crimson, BoosterOrientation.Horizontal);

            _folds.Fold(board, 0, _engine, _events, NullResolutionListener.Instance);

            var frontCell = board.CellAt(interior, SurfaceSide.Front);
            Assert.That(frontCell.Tile.Booster, Is.EqualTo(BoosterType.RibbonRocket),
                "it is printed on the far face now, waiting to be found again");
        }

        [Test]
        public void FoldIsRefusedWhileAFoldLockSurvives()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4), Axis.Vertical,
                Direction.Down, Direction.Down, SurfaceSide.Front, lockGroup: 3);
            var board = FoldableBoard(region);
            board.CellAt(new GridCoord(3, 1), SurfaceSide.Front).Obstacle =
                new Obstacle(ObstacleType.FoldLock, 0, 3);

            var locked = _folds.Validate(board, 0, null, 10);
            Assert.That(locked.IsValid, Is.False);

            board.CellAt(new GridCoord(3, 1), SurfaceSide.Front).Obstacle = null;

            Assert.That(_folds.Validate(board, 0, null, 10).IsValid, Is.True);
        }

        [Test]
        public void FoldMeterMustBeChargedBeforeFolding()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4), Axis.Vertical,
                Direction.Down, Direction.Down, SurfaceSide.Front, 0, 0, meterCost: 50);
            var board = FoldableBoard(region);
            var meter = new FoldMeter(TestBoards.Rules());

            Assert.That(_folds.Validate(board, 0, meter, 10).IsValid, Is.False);

            meter.Add(50);
            Assert.That(_folds.Validate(board, 0, meter, 10).IsValid, Is.True);
        }

        [Test]
        public void GravityFollowsTheFaceThatIsShowing()
        {
            var region = new FoldRegion(1, new GridRect(0, 0, 2, 4), Axis.Vertical,
                Direction.Down, Direction.Right);
            var board = FoldableBoard(region);

            Assert.That(board.GravityAt(new GridCoord(0, 0)), Is.EqualTo(Direction.Down));

            board.Folds.Flip(0);

            Assert.That(board.GravityAt(new GridCoord(0, 0)), Is.EqualTo(Direction.Right));
            Assert.That(board.GravityAt(new GridCoord(3, 0)), Is.EqualTo(Direction.Down),
                "outside the region nothing changed");
        }
    }
}

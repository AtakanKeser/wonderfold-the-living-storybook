using NUnit.Framework;
using Wonderfold.Core.Board;
using Wonderfold.Core.Events;
using Wonderfold.Core.Folding;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Tests
{
    [TestFixture]
    public class MatchFinderTests
    {
        private MatchFinder _finder;

        [SetUp]
        public void SetUp() => _finder = new MatchFinder();

        [Test]
        public void FindsHorizontalThree()
        {
            var board = TestBoards.Build(
                "bgby",
                "rrry",
                "gybg");

            var groups = _finder.FindAll(board);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Color, Is.EqualTo(TileColor.Crimson));
            Assert.That(groups[0].Count, Is.EqualTo(3));
            Assert.That(groups[0].Shape, Is.EqualTo(MatchShape.Line3));
        }

        [Test]
        public void FindsVerticalFourAndReportsRocketOrientation()
        {
            var board = TestBoards.Build(
                "bgy",
                "bry",
                "bgr",
                "byg");

            var groups = _finder.FindAll(board);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Shape, Is.EqualTo(MatchShape.Line4));
            Assert.That(groups[0].LineOrientation, Is.EqualTo(BoosterOrientation.Vertical));
        }

        [Test]
        public void MergesOverlappingRunsIntoOneCornerGroup()
        {
            // An L: three across the bottom, three up the left.
            var board = TestBoards.Build(
                "rby",
                "rgy",
                "rrr");

            var groups = _finder.FindAll(board);

            Assert.That(groups, Has.Count.EqualTo(1), "an L must be one group, not two overlapping lines");
            Assert.That(groups[0].Count, Is.EqualTo(5));
            Assert.That(groups[0].Shape, Is.EqualTo(MatchShape.Corner));
        }

        [Test]
        public void PivotOfCornerIsTheIntersection()
        {
            var board = TestBoards.Build(
                "rby",
                "rgy",
                "rrr");

            var group = _finder.FindAll(board)[0];

            Assert.That(group.Pivot, Is.EqualTo(new GridCoord(0, 0)));
        }

        [Test]
        public void BoostersDoNotParticipateInColourMatching()
        {
            var board = TestBoards.Build(
                "bgy",
                "r-r",
                "gyb");

            Assert.That(_finder.HasAnyMatch(board), Is.False);
        }

        [Test]
        public void VoidCellsBreakRuns()
        {
            var board = TestBoards.Build(
                "bgyb",
                "r#rr",
                "gybg");

            Assert.That(_finder.HasAnyMatch(board), Is.False);
        }

        [Test]
        public void WouldMatchLeavesTheBoardUntouched()
        {
            var board = TestBoards.Build(
                "brb",
                "rbr",
                "brb");

            var before = board.ToAscii();
            _finder.WouldMatch(board, new GridCoord(0, 0), new GridCoord(1, 0));

            Assert.That(board.ToAscii(), Is.EqualTo(before));
        }

        [Test]
        public void MatchSpanningTwoFacesIsFlaggedAsCrossingTheSeam()
        {
            // Right two columns fold; flip them so the board shows two different faces.
            var region = new FoldRegion(1, new GridRect(2, 0, 2, 3));
            var board = TestBoards.Build(new[] { region }, 1,
                "bgby",
                "rrrr",
                "gybg");
            TestBoards.PaintBack(board,
                "yyyy",
                "rrrr",
                "gggg");

            var beforeFold = _finder.FindAll(board);
            Assert.That(beforeFold[0].CrossesSeam, Is.False, "nothing is folded yet, so there is no seam");

            board.Folds.Flip(0);
            var afterFold = _finder.FindAll(board);

            var crimson = afterFold.Find(g => g.Color == TileColor.Crimson);
            Assert.That(crimson, Is.Not.Null);
            Assert.That(crimson.CrossesSeam, Is.True,
                "the run spans a cell showing its front and a cell showing its back");
        }
    }
}
